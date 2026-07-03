namespace Bpm.Persistence.SharedIdentity;

/// Read-model mirror of admin-svc's Dataset. Schema owned by admin (ExcludeFromMigrations).
public sealed class SharedDataset
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public bool IsActive { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SharedDatasetRow
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public string CellsJson { get; set; } = "{}";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTime? DeletedAt { get; set; }
}
