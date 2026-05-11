using Bpm.Application.Common.Abstractions;

namespace Bpm.Application.Common.Services;

/// <summary>
/// Default no-op <see cref="ISandboxActorContext"/> for non-HTTP code paths
/// (background jobs, tests, CLI tools). Returns null/false so the audit
/// interceptor's sandbox-actor stamping cleanly skips when this is in scope.
/// The Api layer overrides registration with <c>HttpContextSandboxActor</c>.
/// </summary>
public sealed class SystemSandboxActor : ISandboxActorContext
{
    public Guid? ActualActorUserId => null;
    public bool IsSandboxActor => false;
}
