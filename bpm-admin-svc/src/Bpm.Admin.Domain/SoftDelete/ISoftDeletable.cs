namespace Bpm.Admin.Domain.SoftDelete;

/// <summary>
/// Marker for entities whose rows are hidden by setting <see cref="DeletedAt"/>
/// rather than physically removed. AdminDbContext applies a global query filter
/// so standard queries skip soft-deleted rows automatically.
/// </summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
