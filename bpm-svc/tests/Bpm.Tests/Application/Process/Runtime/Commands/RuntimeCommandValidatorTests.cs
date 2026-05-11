using System.Text.Json;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Domain.Entities.Process;
using Xunit;

namespace Bpm.Tests.Application.Process.Runtime.Commands;

public sealed class RuntimeCommandValidatorTests
{
    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void StartInstance_valid_passes()
    {
        var v = new StartInstanceCommandValidator();
        var cmd = new StartInstanceCommand("LEAVE", Empty(), Guid.NewGuid());
        var result = v.Validate(cmd);
        Assert.True(result.IsValid, string.Join(",", result.Errors));
    }

    [Fact]
    public void StartInstance_empty_specCode_fails()
    {
        var v = new StartInstanceCommandValidator();
        var cmd = new StartInstanceCommand("", Empty(), Guid.NewGuid());
        Assert.False(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void StartInstance_empty_initiator_fails()
    {
        var v = new StartInstanceCommandValidator();
        var cmd = new StartInstanceCommand("LEAVE", Empty(), Guid.Empty);
        Assert.False(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void SubmitTask_valid_passes()
    {
        var v = new SubmitTaskCommandValidator();
        var cmd = new SubmitTaskCommand(Guid.NewGuid(), Guid.NewGuid(), null, Decision.Approve, "ok");
        Assert.True(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void SubmitTask_long_comment_fails()
    {
        var v = new SubmitTaskCommandValidator();
        var cmd = new SubmitTaskCommand(Guid.NewGuid(), Guid.NewGuid(), null, Decision.Approve, new string('x', 2001));
        Assert.False(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void ReturnTask_requires_comment()
    {
        var v = new ReturnTaskCommandValidator();
        var cmd = new ReturnTaskCommand(Guid.NewGuid(), Guid.NewGuid(), "");
        Assert.False(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void ReturnTask_with_comment_passes()
    {
        var v = new ReturnTaskCommandValidator();
        var cmd = new ReturnTaskCommand(Guid.NewGuid(), Guid.NewGuid(), "please clarify");
        Assert.True(v.Validate(cmd).IsValid);
    }

    [Fact]
    public void ClaimTask_empty_ids_fail()
    {
        var v = new ClaimTaskCommandValidator();
        Assert.False(v.Validate(new ClaimTaskCommand(Guid.Empty, Guid.NewGuid())).IsValid);
        Assert.False(v.Validate(new ClaimTaskCommand(Guid.NewGuid(), Guid.Empty)).IsValid);
        Assert.True(v.Validate(new ClaimTaskCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void CancelInstance_requires_reason()
    {
        var v = new CancelInstanceCommandValidator();
        Assert.False(v.Validate(new CancelInstanceCommand(Guid.NewGuid(), Guid.NewGuid(), "")).IsValid);
        Assert.True(v.Validate(new CancelInstanceCommand(Guid.NewGuid(), Guid.NewGuid(), "duplicate request")).IsValid);
    }
}
