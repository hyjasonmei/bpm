using Bpm.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Bpm.Api.Common;

/// <summary>
/// PR-J4 §6: reads sandbox-persona claims from the current request's JWT.
/// The persona endpoint mints tokens with <c>actual_actor_id</c> +
/// <c>sandbox_actor=true</c>, and this class surfaces them so the audit
/// interceptor can stamp <c>SandboxActualActor</c> on every impersonable
/// entity created in the request scope.
/// </summary>
public sealed class HttpContextSandboxActor(IHttpContextAccessor accessor) : ISandboxActorContext
{
    public Guid? ActualActorUserId
    {
        get
        {
            var raw = accessor.HttpContext?.User?.FindFirst("actual_actor_id")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool IsSandboxActor =>
        string.Equals(
            accessor.HttpContext?.User?.FindFirst("sandbox_actor")?.Value,
            "true",
            StringComparison.OrdinalIgnoreCase);
}
