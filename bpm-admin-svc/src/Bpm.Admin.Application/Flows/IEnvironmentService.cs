namespace Bpm.Admin.Application.Flows;

/// <summary>
/// CRUD over deployment environments tracked by admin. Pure
/// bookkeeping for POC — actually deploying anything is out of scope.
/// </summary>
public interface IEnvironmentService
{
    Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default);
    Task<EnvironmentDto> CreateAsync(CreateEnvironmentRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task<EnvironmentDto> UpdateAsync(Guid id, UpdateEnvironmentRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default);
}

public record EnvironmentDto(Guid Id, string Code, string DisplayName, int SortOrder);
public record CreateEnvironmentRequest(string Code, string DisplayName, int SortOrder);
public record UpdateEnvironmentRequest(string? Code, string? DisplayName, int? SortOrder);
