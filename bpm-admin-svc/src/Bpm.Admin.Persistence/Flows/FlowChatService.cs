using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class FlowChatService : IFlowChatService
{
    private readonly AdminDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public FlowChatService(AdminDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FlowChatMessage>> ListAsync(Guid flowId, DateTime? since, CancellationToken ct = default)
    {
        var query = _db.FlowChatMessages
            .AsNoTracking()
            .Where(m => m.FlowId == flowId);
        if (since.HasValue)
        {
            query = query.Where(m => m.CreatedAt > since.Value);
        }
        return await query
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<FlowChatMessage> AppendAsync(AppendChatMessageInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
            throw new FlowLifecycleException("chat message content required");

        // Light validation: the flow must exist (any state). Soft-deleted
        // rows are filtered by the global query filter.
        var flowExists = await _db.Flows.AnyAsync(f => f.Id == input.FlowId, ct);
        if (!flowExists) throw new FlowLifecycleException($"Flow {input.FlowId} not found");

        var row = new FlowChatMessage
        {
            Id = Guid.NewGuid(),
            FlowId = input.FlowId,
            Sender = input.Sender,
            Kind = input.Kind,
            Content = input.Content,
            ArtifactsJson = input.ArtifactsJson,
            Version = input.Version,
            AuthorUserId = input.AuthorUserId,
            CreatedAt = _clock.UtcNow,
        };
        _db.FlowChatMessages.Add(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_chat_appended",
            targetType: "flow_chat_message",
            targetId: row.Id.ToString(),
            actorUserId: input.AuthorUserId,
            actorPrincipalId: null,
            after: new { row.FlowId, Sender = row.Sender.ToString(), Kind = row.Kind.ToString(), HasArtifacts = row.ArtifactsJson != null },
            ct: ct);

        return row;
    }
}
