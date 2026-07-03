using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Datasets;

/// A customer-maintained reference table. Columns are stored as a JSON TEXT
/// blob (repo convention: no EF Owned types). Rows live in DatasetRow.
public class Dataset : ISoftDeletable
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;          // stable slug, e.g. "tw-regions"
    public string Name { get; set; } = string.Empty;         // display label
    public string? Description { get; set; }
    public string ColumnsJson { get; set; } = "[]";          // [{"key":"city","label":"縣市","type":"text"}]
    public bool IsActive { get; set; } = true;               // dataset-level enable/disable
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }                 // soft delete (ISoftDeletable)
}

/// One row of a Dataset. Cells = columnKey -> value, stored as JSON TEXT.
public class DatasetRow : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public string CellsJson { get; set; } = "{}";            // {"city":"台北市","district":"大安區"}
    public bool IsActive { get; set; } = true;               // deactivate-not-delete for history
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
