using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Spec;
using Bpm.Domain.Entities.Audit;
using Bpm.Domain.Spec;

namespace Bpm.Persistence.Spec;

public sealed class ActorResolutionAuditor(AppDbContext db, IClock clock) : IActorResolutionAuditor
{
    public async Task WriteAsync(ActorRef actor, ResolutionContext ctx, ResolutionResult result, CancellationToken ct = default)
    {
        var row = new ActorResolutionAudit
        {
            Timestamp = clock.UtcNow,
            RequestId = string.IsNullOrWhiteSpace(ctx.RequestId) ? Guid.NewGuid().ToString("N")[..16] : ctx.RequestId,
            ActorRefJson = JsonSerializer.Serialize<ActorRef>(actor),
            SubmitterUserId = ctx.SubmitterUserId,
            FlowCode = ctx.FlowCode,
            StepCode = ctx.StepCode,
        };

        switch (result)
        {
            case ResolutionResult.Success s:
                row.ResultKind = "Success";
                row.ResolvedUserIdsJson = JsonSerializer.Serialize(s.UserIds);
                break;
            case ResolutionResult.Failure f:
                row.ResultKind = "Failure";
                row.ErrorKind = f.Error.Kind.ToString();
                row.ErrorReason = f.Error.Reason;
                break;
        }

        db.Set<ActorResolutionAudit>().Add(row);
        await db.SaveChangesAsync(ct);
    }
}
