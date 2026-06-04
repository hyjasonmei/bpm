namespace Bpm.Admin.Domain.Roles;

public class Role
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable identifier (SCREAMING_SNAKE, like FlowCode) — the value used
    /// everywhere a role is referenced as a key: JWT roles claim, [Authorize],
    /// actor resolution (role:&lt;code&gt;), seeding. Unique.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name (zh-TW), e.g. 系統管理員 / 財務.</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}
