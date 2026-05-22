using Bpm.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Bpm.Api.Common;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? Id =>
        accessor.HttpContext?.User?.Identity?.IsAuthenticated == true
            ? accessor.HttpContext.User.Identity.Name
            : "system";

    public bool IsAuthenticated =>
        accessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public Guid? ImpersonatedById
    {
        get
        {
            var raw = accessor.HttpContext?.User?.FindFirst("impersonated_by")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid? ImpersonationSessionId
    {
        get
        {
            var raw = accessor.HttpContext?.User?.FindFirst("imp_session_id")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
