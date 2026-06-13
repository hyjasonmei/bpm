namespace Bpm.ChefAgent;

/// <summary>Mirror of admin-svc's ChefTaskDto (System.Text.Json, camelCase).</summary>
public sealed record ChefTask(
    Guid FlowId,
    string FlowCode,
    int Version,
    string DisplayName,
    string State,
    DateTime UpdatedAt,
    string? ChefWorkContextJson,
    string? PrUrl,
    DateTime? LastUserMessageAt);

/// <summary>Mirror of admin-svc's ChefTaskListDto.</summary>
public sealed record ChefTaskList(
    List<ChefTask> Submitted,
    List<ChefTask> AwaitingChef,
    List<ChefTask> ApprovedAwaitingMerge,
    List<ChefTask> Stalled)
{
    public static ChefTaskList Empty() => new([], [], [], []);
}

/// <summary>chef's workspace context (ChefWorkContextJson shape).</summary>
public sealed record ChefWorkContext(string? Branch, string? Notes, DateTime? SetAt);

/// <summary>What one poll decided to do for one environment.</summary>
public sealed record CookPlan(ChefTask? CookTask, bool IsResume, List<ChefTask> MergeChecks)
{
    public static CookPlan NothingToCook(List<ChefTask> mergeChecks) => new(null, false, mergeChecks);
}
