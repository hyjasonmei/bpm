using Bpm.Domain.Spec;

namespace Bpm.Application.Spec;

public interface IActorResolutionAuditor
{
    Task WriteAsync(ActorRef actor, ResolutionContext ctx, ResolutionResult result, CancellationToken ct = default);
}
