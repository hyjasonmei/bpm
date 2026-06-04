using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;

namespace Bpm.Application.Sandbox;

/// <summary>
/// PR-J4 §7: Mailbox API for browsing captured outbound messages (mail,
/// webhook, SMS). All listing endpoints filter by the active sandbox
/// tenant; reads are per-user (the <c>ReadByUserIdsJson</c> column tracks
/// per-persona read state so multiple testers don't trample each other).
/// </summary>
public interface IMailboxService
{
    /// <summary>List with channel / recipient / instance / flow / unread filters.</summary>
    Task<IReadOnlyList<CapturedMessageSummaryDto>> ListAsync(
        Guid currentUserId,
        SandboxChannel? channel,
        Guid? recipientUserIdHint,
        Guid? processInstanceId,
        string? flowCode,
        bool unreadOnly,
        int limit,
        CancellationToken ct = default);

    /// <summary>Single full payload for the modal view.</summary>
    Task<CapturedMessageDetailDto?> GetAsync(Guid id, Guid currentUserId, CancellationToken ct = default);

    /// <summary>Idempotent — appends current user id to ReadByUserIdsJson if absent.</summary>
    Task<bool> MarkReadAsync(Guid id, Guid currentUserId, CancellationToken ct = default);

    /// <summary>
    /// Counter for the SandboxBanner badge. Returns zero counts WITHOUT a DB
    /// hit when sandbox is off (per the §7 spec — minimises prod overhead).
    /// </summary>
    Task<UnreadCountDto> UnreadCountAsync(Guid currentUserId, CancellationToken ct = default);
}
