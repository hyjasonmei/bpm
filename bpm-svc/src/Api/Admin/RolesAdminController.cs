using Bpm.Application.Admin;
using Bpm.Application.Admin.Dtos;
using Bpm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Admin;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin")]
public sealed class RolesAdminController(IRoleAdminService service) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<IReadOnlyList<RoleSummaryDto>> ListRoles(CancellationToken ct)
        => await service.ListRolesAsync(ct);

    [HttpGet("users")]
    public async Task<PagedResult<UserSummaryDto>> ListUsers(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? roleCode = null,
        CancellationToken ct = default)
        => await service.ListUsersAsync(q, page, pageSize, roleCode, ct);

    [HttpGet("users/{id:guid}")]
    public async Task<UserDetailDto> GetUser(Guid id, CancellationToken ct)
        => await service.GetUserDetailAsync(id, ct);

    [HttpPost("users/{userId:guid}/roles")]
    public async Task<IActionResult> Assign(Guid userId, [FromBody] AssignRoleRequest req, CancellationToken ct)
    {
        var dto = await service.AssignRoleAsync(RequireUserId(), userId, req, ct);
        return Created($"/api/admin/users/{userId}/roles/{dto.Id}", dto);
    }

    [HttpDelete("users/{userId:guid}/roles/{assignmentId:guid}")]
    public async Task<IActionResult> Revoke(Guid userId, Guid assignmentId, CancellationToken ct)
    {
        await service.RevokeAssignmentAsync(RequireUserId(), userId, assignmentId, ct);
        return NoContent();
    }

    private Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}
