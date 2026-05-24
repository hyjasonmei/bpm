namespace Bpm.Persistence.SharedIdentity;

// Read/write mapping onto Admin_Principals owned by bpm-admin-svc. bpm-svc
// does not run migrations for this table — admin-svc is the schema owner.
// Soft delete is enforced by admin's DbContext query filter; queries from
// bpm-svc see all rows including DeletedAt != null, so callers must filter
// `Where(p => p.DeletedAt == null)` explicitly when "active only" is wanted.
public class SharedPrincipal
{
    public Guid Id { get; set; }
    public SharedPrincipalType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
