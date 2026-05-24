namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_UserCredentials (one row per Principal of Type = User).
// PasswordHash format is ASP.NET Identity's PBKDF2 (Microsoft.AspNetCore.Identity
// PasswordHasher v3). The lifted bpm-svc PasswordHasher must produce
// interoperable output so a credential seeded by admin verifies cleanly
// against bpm-svc /api/auth/login.
public class SharedUserCredential
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
