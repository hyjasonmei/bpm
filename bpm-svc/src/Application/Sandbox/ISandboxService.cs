using Bpm.Application.Sandbox.Dtos;

namespace Bpm.Application.Sandbox;

public interface ISandboxService
{
    Task<SandboxStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task<SandboxStatusDto> SetStatusAsync(UpdateSandboxRequest req, Guid actorUserId, CancellationToken ct = default);
}
