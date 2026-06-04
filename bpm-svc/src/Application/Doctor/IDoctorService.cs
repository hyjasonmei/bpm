namespace Bpm.Application.Doctor;

/// <summary>
/// Process Doctor: diagnoses operational health (stuck/abandoned cases + broken
/// org wiring) and remediates the case-level findings. Detection scans every
/// Model-B case table by reflection (open = CompletedAt is null) and resolves
/// "gone" / "empty" through the same <see cref="Bpm.Application.Org.IOrgChartReader"/>
/// the flows use at runtime, so the diagnosis matches what the runtime hits.
/// </summary>
public interface IDoctorService
{
    Task<DoctorReport> ScanAsync(int stalledDays = 14, CancellationToken ct = default);

    /// <summary>Active users for the reassign picker; if <paramref name="forUserId"/>
    /// is given, the first entry is the suggested target (their manager → dept head).</summary>
    Task<DoctorCandidates> GetCandidatesAsync(Guid? forUserId, string? q, CancellationToken ct = default);

    Task<DoctorActionResult> ReassignAsync(string flowCode, Guid caseId, Guid toUserId, Guid? operatorUserId, string? reason, CancellationToken ct = default);
    Task<DoctorActionResult> BatchReassignAsync(Guid fromUserId, Guid toUserId, Guid? operatorUserId, string? reason, CancellationToken ct = default);
    Task<DoctorActionResult> CancelAsync(string flowCode, Guid caseId, Guid? operatorUserId, string? reason, CancellationToken ct = default);
}

public sealed record DoctorReport(
    IReadOnlyList<CaseFinding> CaseFindings,
    IReadOnlyList<OrgFinding> OrgFindings,
    IReadOnlyList<DepartedPerson> DepartedWithCases);

/// <param name="Rule">resigned_approver | ownerless | stalled</param>
/// <param name="Severity">high | medium | info</param>
public sealed record CaseFinding(
    string Rule,
    string Severity,
    string FlowCode,
    Guid CaseId,
    string? StatusName,
    Guid? AssigneeUserId,
    string? AssigneeName,
    bool AssigneeGone,
    Guid? SubmitterUserId,
    string? SubmitterName,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    int DaysStuck,
    Guid? SuggestedUserId,
    string? SuggestedName,
    string? SuggestedVia);

/// <param name="Rule">no_manager | no_dept_head | empty_role | empty_group</param>
/// <param name="Kind">user | dept | role | group</param>
public sealed record OrgFinding(
    string Rule,
    string Severity,
    string Kind,
    Guid? PrincipalId,
    string Name,
    string Detail);

public sealed record DepartedPerson(Guid UserId, string Name, bool Active, bool Deleted, int OpenCaseCount);

public sealed record DoctorCandidates(DoctorCandidate? Suggested, IReadOnlyList<DoctorCandidate> Users);

public sealed record DoctorCandidate(Guid UserId, string Name, string? Email, string? Hint);

public sealed record DoctorActionResult(bool Ok, int Affected, string? Error = null);
