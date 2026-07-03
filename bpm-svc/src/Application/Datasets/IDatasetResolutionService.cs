namespace Bpm.Application.Datasets;

/// A declarative option query over a dataset (the form field's binding + the
/// parent's selected value). filterValue null + filterColumn set => empty (child
/// not ready). distinct dedupes by (value,label,group). sortColumn null => row SortOrder.
public record ResolveRequest(
    string DatasetKey, string ValueColumn, string? LabelColumn,
    string? FilterColumn, string? FilterValue,
    bool Distinct, string? GroupByColumn, string? SortByColumn);

public record DatasetOption(string Value, string Label, string? Group);

public interface IDatasetResolutionService
{
    Task<IReadOnlyList<DatasetOption>> ResolveAsync(ResolveRequest req, CancellationToken ct);
}
