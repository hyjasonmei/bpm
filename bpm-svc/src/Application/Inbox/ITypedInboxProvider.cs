namespace Bpm.Application.Inbox;

/// <summary>
/// One implementation per chef-cooked flow. Lets the unified
/// <c>InboxController</c> ask each feature "what does this user own
/// (Mine) and what is waiting on this user (Pending) in your domain?"
/// without the controller knowing any flow-specific schema.
///
/// Registration is automatic: <c>Persistence.DependencyInjection</c>
/// scans the Persistence assembly for non-abstract implementations and
/// registers each as <c>AddScoped&lt;ITypedInboxProvider&gt;</c>, so chef
/// just drops a new <c>&lt;CODE&gt;_V&lt;N&gt;_InboxProvider.cs</c> into
/// <c>Persistence/Features/&lt;CODE&gt;/V&lt;N&gt;/</c> and ships.
/// </summary>
public interface ITypedInboxProvider
{
    string FlowCode { get; }
    int FlowVersion { get; }

    Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct);
}
