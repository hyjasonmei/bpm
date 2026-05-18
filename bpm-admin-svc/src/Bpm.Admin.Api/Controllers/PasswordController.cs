using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Auth;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record SetPasswordRequest(string Password);

[ApiController]
[Route("api/principals/{userId:guid}/password")]
public class PasswordController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditLogger _audit;

    public PasswordController(AdminDbContext db, IPasswordHasher hasher, IAuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    [HttpPut]
    public async Task<IActionResult> Set(Guid userId, [FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest("Password must be at least 6 characters.");

        var user = await _db.Principals.FirstOrDefaultAsync(p => p.Id == userId, ct);
        if (user is null) return NotFound();
        if (user.Type != PrincipalType.User) return BadRequest("Only user principals can have credentials.");

        var existing = await _db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is null)
        {
            _db.UserCredentials.Add(new UserCredential
            {
                UserId = userId,
                PasswordHash = _hasher.Hash(req.Password),
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.PasswordHash = _hasher.Hash(req.Password);
            existing.PasswordChangedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: existing is null ? "password_set" : "password_changed",
            targetType: "user_credential",
            targetId: userId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            ct: ct);

        return NoContent();
    }
}
