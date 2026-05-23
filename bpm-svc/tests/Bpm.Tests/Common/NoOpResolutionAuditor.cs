using Bpm.Application.Spec;
using Bpm.Domain.Spec;

namespace Bpm.Tests.Common;

/// <summary>
/// In-memory no-op auditor used by integration tests that don't care
/// about persisted ActorResolutionAudit rows. Previously lived in
/// BundleReproducibilityRunnerTests; relocated to Common when the
/// bundle reproducibility runner was retired in the unify-user-store
/// change.
/// </summary>
public sealed class NoOpResolutionAuditor : IActorResolutionAuditor
{
    public Task WriteAsync(ActorRef actor, ResolutionContext ctx, ResolutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}
