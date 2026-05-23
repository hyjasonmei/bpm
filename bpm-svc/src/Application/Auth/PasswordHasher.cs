using Microsoft.AspNetCore.Identity;

namespace Bpm.Application.Auth;

/// <summary>
/// Wraps ASP.NET Identity's <see cref="PasswordHasher{TUser}"/> so the
/// Application layer does not take a hard dependency on Identity's
/// user-tied generic API. Hash format must match admin-svc's
/// PasswordHasher exactly so a credential seeded by admin verifies
/// cleanly against bpm-svc /api/auth/login.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string plaintext)
        => _inner.HashPassword(new object(), plaintext);

    public bool Verify(string plaintext, string hash)
    {
        var result = _inner.VerifyHashedPassword(new object(), hash, plaintext);
        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }
}
