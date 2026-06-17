namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// Per-environment deploy configuration for the Publish→Deploy pipeline
/// (Task 5). One row per <see cref="EnvName"/>. Holds the Azure resource
/// NAMES the deploy worker needs to target an env's stack — NO secrets.
/// The worker uses its own logged-in <c>az</c> and fetches SWA deploy
/// tokens at deploy time (<c>az staticwebapp secrets list</c>); the DB
/// must never hold tokens or credentials.
/// </summary>
public sealed class DeployEnvConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Logical environment key, matches the chef agent's env name
    /// (e.g. "azure-poc"). Unique.</summary>
    public string EnvName { get; set; } = "";

    public string ResourceGroup { get; set; } = "";
    public string BpmSvcApp { get; set; } = "";
    public string AdminSvcApp { get; set; } = "";
    public string BpmUiSwa { get; set; } = "";
    public string AdminUiSwa { get; set; } = "";

    /// <summary>Whether this env is a live deploy target.</summary>
    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
