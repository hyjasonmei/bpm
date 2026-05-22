using Bpm.Application.Common.Abstractions;

namespace Bpm.Tests.Common;

internal sealed class StubCurrentUser(string? id = "tester", Guid? impersonatedById = null, Guid? sessionId = null) : ICurrentUser
{
    public string? Id { get; } = id;
    public bool IsAuthenticated => Id is not null;
    public Guid? ImpersonatedById { get; } = impersonatedById;
    public Guid? ImpersonationSessionId { get; } = sessionId;
}
