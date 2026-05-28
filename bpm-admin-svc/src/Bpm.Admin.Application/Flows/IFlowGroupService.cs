using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

/// <summary>
/// Admin-managed launcher groups. CRUD lives here; assignment of a
/// flow to a group is done through <see cref="IFlowLifecycleService"/>.
/// </summary>
public interface IFlowGroupService
{
    Task<IReadOnlyList<FlowGroupDto>> ListAsync(CancellationToken ct = default);

    Task<FlowGroupDto> CreateAsync(CreateFlowGroupRequest req, Guid? actorUserId, CancellationToken ct = default);

    Task<FlowGroupDto> UpdateAsync(Guid id, UpdateFlowGroupRequest req, Guid? actorUserId, CancellationToken ct = default);

    Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default);
}

public record FlowGroupDto(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> DisplayName,
    int SortOrder,
    string? Icon,
    int FlowCount);

public record CreateFlowGroupRequest(
    string Code,
    IReadOnlyDictionary<string, string> DisplayName,
    int SortOrder,
    string? Icon);

public record UpdateFlowGroupRequest(
    string? Code,
    IReadOnlyDictionary<string, string>? DisplayName,
    int? SortOrder,
    string? Icon);

public record AssignFlowGroupRequest(Guid? GroupId);
