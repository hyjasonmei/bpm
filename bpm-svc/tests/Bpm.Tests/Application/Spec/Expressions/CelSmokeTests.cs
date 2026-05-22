using Bpm.Application.Spec.Expressions;
using Bpm.Tests.Common;
using Xunit;

namespace Bpm.Tests.Application.Spec.Expressions;

public sealed class CelSmokeTests
{
    [Fact]
    public void days_ge_7_long_value()
    {
        var ev = new CelNetExpressionEvaluator(new StubClock());
        var ctx = new ExpressionContext(
            FormData: new Dictionary<string, object?> { ["days"] = 8L },
            Submitter: new Dictionary<string, object?>());
        Assert.True(ev.EvaluateBoolean("days >= 7", ctx));
    }

    [Fact]
    public void days_ge_7_int_value()
    {
        var ev = new CelNetExpressionEvaluator(new StubClock());
        var ctx = new ExpressionContext(
            FormData: new Dictionary<string, object?> { ["days"] = (int)8 },
            Submitter: new Dictionary<string, object?>());
        Assert.True(ev.EvaluateBoolean("days >= 7", ctx));
    }

    [Fact]
    public void days_ge_7_with_extra_string_keys()
    {
        var ev = new CelNetExpressionEvaluator(new StubClock());
        var ctx = new ExpressionContext(
            FormData: new Dictionary<string, object?>
            {
                ["leave_type"] = "事假",
                ["reason"] = "trip",
                ["days"] = 8L,
            },
            Submitter: new Dictionary<string, object?> { ["userId"] = Guid.NewGuid().ToString() });
        Assert.True(ev.EvaluateBoolean("days >= 7", ctx));
    }
}
