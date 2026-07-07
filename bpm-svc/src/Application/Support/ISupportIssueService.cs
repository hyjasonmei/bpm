using Bpm.Domain.Entities.Support;

namespace Bpm.Application.Support;

public sealed record SubmitIssueRequest(
    string Kind,          // bug | feature | question
    string Title,
    string Description,
    string? Contact,
    string? Page);

public sealed record IssueDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string Kind,
    string Title,
    string Description,
    string? Contact,
    string? Page,
    string? UserAgent,
    SupportIssueStatus Status,
    DateTime SubmittedAt);

public interface ISupportIssueService
{
    /// Store the report and notify SYSTEM_ADMIN users (in-app + email).
    Task<IssueDto> SubmitAsync(Guid userId, SubmitIssueRequest req, string? userAgent, CancellationToken ct = default);

    Task<IReadOnlyList<IssueDto>> ListAsync(SupportIssueStatus? status, CancellationToken ct = default);
    Task<IssueDto> SetStatusAsync(Guid issueId, SupportIssueStatus status, CancellationToken ct = default);
}
