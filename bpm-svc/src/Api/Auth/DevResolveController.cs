using System.Text.Json;
using Bpm.Application.Spec;
using Bpm.Domain.Spec;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Auth;

/// Dev-only smoke endpoint: feed an ActorRef + a submitter persona, get
/// back the resolver's verdict. Useful for one-shot e2e checks without
/// having to run a full workflow. Disabled in prod mode.
[ApiController]
[Route("api/dev")]
[AllowAnonymous]
public sealed class DevResolveController(
    IActorResolver resolver,
    ActorRefValidator validator,
    PersonaLoginService personas) : ControllerBase
{
    public sealed record ResolveRequest(
        string PersonaCode,
        JsonElement Actor,
        Dictionary<string, JsonElement>? FormData = null,
        string FlowCode = "smoke");

    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest req, CancellationToken ct)
    {
        var authMode = (Environment.GetEnvironmentVariable("BPM_AUTH_MODE") ?? "dev").ToLowerInvariant();
        if (authMode == "prod") return NotFound();

        // Resolve the persona to a real submitter user.
        var login = await personas.LoginAsync(req.PersonaCode, ct);

        ActorRef actor;
        try
        {
            actor = JsonSerializer.Deserialize<ActorRef>(req.Actor.GetRawText())!;
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "actor_parse_failed", detail = ex.Message });
        }

        var report = validator.Validate(actor);
        if (!report.Ok)
            return BadRequest(new { error = "actor_invalid", errors = report.Errors });

        var ctx = new ResolutionContext(
            login.User.Id,
            (req.FormData ?? new()).AsReadOnly(),
            req.FlowCode);

        var result = await resolver.ResolveAsync(actor, ctx, ct);
        return result switch
        {
            ResolutionResult.Success s => Ok(new
            {
                kind = "success",
                userIds = s.UserIds,
                count = s.UserIds.Count,
                warnings = report.Warnings,
            }),
            ResolutionResult.Failure f => Ok(new
            {
                kind = "failure",
                errorKind = f.Error.Kind.ToString(),
                reason = f.Error.Reason,
                cyclePath = f.Error.CyclePath,
                warnings = report.Warnings,
            }),
            _ => StatusCode(500, new { error = "unknown_result_type" }),
        };
    }
}

internal static class DictExt
{
    public static IReadOnlyDictionary<TK, TV> AsReadOnly<TK, TV>(this Dictionary<TK, TV> d) where TK : notnull
        => d;
}
