namespace Bpm.Admin.Domain.Auth;

public class UserCredential
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
