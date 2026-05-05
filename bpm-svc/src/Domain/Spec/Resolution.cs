using System.Text.Json;

namespace Bpm.Domain.Spec;

public enum ResolutionErrorKind
{
    PathUnresolved,
    RoleEmpty,
    GroupEmpty,
    Cycle,
    ConditionalBranchEmpty,
    ValidationFailed,
}

public sealed record ResolutionError(
    ResolutionErrorKind Kind,
    string Reason,
    IReadOnlyList<Guid>? CyclePath = null);

public abstract record ResolutionResult
{
    public sealed record Success(IReadOnlySet<Guid> UserIds) : ResolutionResult;
    public sealed record Failure(ResolutionError Error) : ResolutionResult;

    public static Success Ok(IReadOnlySet<Guid> ids) => new(ids);
    public static Success Ok(params Guid[] ids) => new(new HashSet<Guid>(ids));
    public static Failure Fail(ResolutionErrorKind kind, string reason, IReadOnlyList<Guid>? path = null)
        => new(new ResolutionError(kind, reason, path));
}

/// Inputs the resolver needs at request time.
public sealed record ResolutionContext(
    Guid SubmitterUserId,
    IReadOnlyDictionary<string, JsonElement> FormData,
    string FlowCode,
    string? StepCode = null,
    string RequestId = "")
{
    public static ResolutionContext ForFlow(Guid submitter, string flowCode)
        => new(submitter, new Dictionary<string, JsonElement>(), flowCode);
}
