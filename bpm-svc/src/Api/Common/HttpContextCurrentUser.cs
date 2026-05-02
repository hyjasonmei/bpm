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
}
