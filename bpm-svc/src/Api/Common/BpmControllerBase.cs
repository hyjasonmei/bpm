using System.Security.Claims;
using Bpm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Common;

/// <summary>
/// Convenience base for new controllers. Existing controllers (HrFlows,
/// Sandbox, Impersonation, etc.) keep their inline <c>RequireUserId()</c>
/// helpers — this is just a place new controllers can DRY against.
/// </summary>
public abstract class BpmControllerBase : ControllerBase
{
    /// <summary>
    /// Read the authenticated user's id from the <c>sub</c> claim. Falls back
    /// to <see cref="ClaimTypes.NameIdentifier"/> and finally
    /// <see cref="System.Security.Principal.IIdentity.Name"/> for parity with
    /// the legacy <c>HrFlowsController</c> shape.
    /// </summary>
    protected Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}
