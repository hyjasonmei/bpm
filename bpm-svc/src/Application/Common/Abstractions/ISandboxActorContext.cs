namespace Bpm.Application.Common.Abstractions;

/// <summary>
/// PR-J4 §6: surfaces the "real tester behind the persona" id when a request
/// arrives carrying a sandbox-persona JWT (minted by
/// <c>POST /api/sandbox/persona</c>). The audit interceptor reads it to stamp
/// <c>SandboxActualActor</c> on impersonable entities so the audit trail
/// records both who-the-system-saw and who-actually-clicked.
///
/// Defaults to all-null when no sandbox claims are present — callers should
/// treat <see cref="ActualActorUserId"/> being null as "regular non-sandbox
/// request".
/// </summary>
public interface ISandboxActorContext
{
    Guid? ActualActorUserId { get; }
    bool IsSandboxActor { get; }
}
