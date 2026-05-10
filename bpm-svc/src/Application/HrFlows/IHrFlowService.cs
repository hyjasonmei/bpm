using System.Text.Json;
using Bpm.Application.HrFlows.Dtos;
using Bpm.Domain.Entities.HrFlows;

namespace Bpm.Application.HrFlows;

// Interim service. Sunset when add-process-runtime ships.
public interface IHrFlowService
{
    Task<HrFlowInstanceDto> StartAsync(HrFlowSpecCode specCode, JsonElement formData, Guid initiatorUserId, CancellationToken ct = default);
    Task<HrFlowInstanceDto> GetByIdAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<HrFlowSummaryDto>> GetMineAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<HrFlowSummaryDto>> GetMyTodoAsync(Guid userId, CancellationToken ct = default);
    Task<HrFlowInstanceDto> ApproveAsync(Guid instanceId, Guid actorUserId, string? comment, CancellationToken ct = default);
    Task<HrFlowInstanceDto> ReturnAsync(Guid instanceId, Guid actorUserId, string comment, CancellationToken ct = default);
    Task<HrFlowInstanceDto> ResubmitAsync(Guid instanceId, Guid actorUserId, JsonElement formData, CancellationToken ct = default);
    Task<HrFlowInstanceDto> CancelAsync(Guid instanceId, Guid actorUserId, CancellationToken ct = default);
}
