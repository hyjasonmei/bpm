using System.Text.RegularExpressions;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.FlowCodes;

/// <summary>
/// Inventory of the flow codes whose runtime code is actually deployed on this
/// bpm-svc — i.e. every <c>&lt;CODE&gt;_V1_Case</c> entity the EF model knows. Used
/// by admin's "register shipped flows" action to backfill the Admin_Flows
/// registry for flows that were merged as code but never went through the AI
/// Kitchen wizard. Auto-discovering, so a new chef-cooked flow appears here the
/// moment its case table is registered.
///
/// <para>POC: anonymous + read-only — reached by admin-ui via its /bpmsvc dev
/// proxy.</para>
/// </summary>
[ApiController]
[Route("api/flow-codes")]
[AllowAnonymous]
public sealed class FlowCodesController(AppDbContext db) : ControllerBase
{
    // Matches a flow case entity of ANY version: <CODE>_V<N>_Case. The code
    // group strips the version so APE_V1_Case and a future APE_V2_Case both
    // collapse to "APE".
    private static readonly Regex CaseTypeRe = new(@"^(?<code>.+)_V\d+_Case$", RegexOptions.Compiled);

    [HttpGet]
    public IReadOnlyList<DeployedFlowDto> Get()
        => db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Select(t => new { Type = t, Match = CaseTypeRe.Match(t.Name) })
            .Where(x => x.Match.Success
                        && x.Type.GetProperty("Status") is not null
                        && x.Type.GetProperty("SubmittedAt") is not null)
            .Select(x => x.Match.Groups["code"].Value)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .Select(c => new DeployedFlowDto(c, c))
            .ToList();
}

public sealed record DeployedFlowDto(string FlowCode, string DisplayName);
