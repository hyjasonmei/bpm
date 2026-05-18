using Bpm.Admin.Domain.Audit;
using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Principals;

public class Principal : ISoftDeletable, IAuditable
{
    public Guid Id { get; set; }

    public PrincipalType Type { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool Active { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
