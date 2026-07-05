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
    List<ChefTask> Stalled,
    List<ChefTask> Publishing)
{
    public static ChefTaskList Empty() => new([], [], [], [], []);
}

/// <summary>Mirror of admin-svc's DeployEnvConfigDto — Azure resource NAMES
/// only (no secrets); the deploy worker fetches secrets at deploy time via az.</summary>
public sealed record DeployEnvConfig(
    string EnvName,
    string ResourceGroup,
    string BpmSvcApp,
    string AdminSvcApp,
    string BpmUiSwa,
    string AdminUiSwa,
    bool Enabled);

/// <summary>chef's workspace context (ChefWorkContextJson shape).</summary>
public sealed record ChefWorkContext(string? Branch, string? Notes, DateTime? SetAt);

/// <summary>Mirror of admin-svc's RegistryCodeDto — one live Admin_Flows row
/// (non-archived, non-deleted) as seen by launcher resolution.</summary>
public sealed record RegistryCodeRow(string FlowCode, int Version, string State, string DisplayName, DateTime UpdatedAt);

/// <summary>Mirror of bpm-svc's /api/flow-codes item — a flow whose runtime
/// code is actually deployed (highest <c>_V&lt;N&gt;_Case</c> per code).</summary>
public sealed record DeployedFlowCode(string FlowCode, string DisplayName, int Version);

/// <summary>What one poll decided to do for one environment.</summary>
public sealed record CookPlan(ChefTask? CookTask, bool IsResume, List<ChefTask> MergeChecks)
{
    public static CookPlan NothingToCook(List<ChefTask> mergeChecks) => new(null, false, mergeChecks);
}
