using Bpm.Domain.Spec;

namespace Bpm.Application.Spec;

public interface IActorResolver
{
    Task<ResolutionResult> ResolveAsync(ActorRef actor, ResolutionContext ctx, CancellationToken ct = default);
}
