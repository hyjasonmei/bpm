using Bpm.Api.Common;
using Bpm.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Datasets;

[ApiController]
[Authorize]
[Route("api/datasets")]
public sealed class DatasetsController(IDatasetResolutionService resolver) : BpmControllerBase
{
    /// Resolve a form field's dataset binding (+ optional parent value) into options.
    [HttpPost("resolve")]
    public async Task<ActionResult<IReadOnlyList<DatasetOption>>> Resolve([FromBody] ResolveRequest req, CancellationToken ct)
        => Ok(await resolver.ResolveAsync(req, ct));
}
