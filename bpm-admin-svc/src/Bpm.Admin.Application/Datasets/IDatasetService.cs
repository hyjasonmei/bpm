namespace Bpm.Admin.Application.Datasets;

public sealed class DatasetException(string message) : Exception(message);

public record DatasetColumnDef(string Key, string Label, string Type);

public record DatasetDto(Guid Id, string Key, string Name, string? Description,
    IReadOnlyList<DatasetColumnDef> Columns, bool IsActive, int RowCount);

public record DatasetRowDto(Guid Id, Guid DatasetId,
    IReadOnlyDictionary<string, string> Cells, bool IsActive, int SortOrder);

public record CreateDatasetRequest(string Key, string Name, string? Description, IReadOnlyList<DatasetColumnDef> Columns);
public record UpdateDatasetRequest(string? Name, string? Description, IReadOnlyList<DatasetColumnDef>? Columns, bool? IsActive);
public record AddRowRequest(IReadOnlyDictionary<string, string> Cells);
public record UpdateRowRequest(IReadOnlyDictionary<string, string>? Cells, bool? IsActive, int? SortOrder);

public interface IDatasetService
{
    Task<IReadOnlyList<DatasetDto>> ListAsync(CancellationToken ct = default);
    Task<DatasetDto> CreateAsync(CreateDatasetRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<DatasetRowDto>> ListRowsAsync(Guid datasetId, CancellationToken ct = default);
    Task<DatasetRowDto> AddRowAsync(Guid datasetId, AddRowRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task<DatasetRowDto> UpdateRowAsync(Guid rowId, UpdateRowRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task DeleteRowAsync(Guid rowId, Guid? actorUserId, CancellationToken ct = default);
}
