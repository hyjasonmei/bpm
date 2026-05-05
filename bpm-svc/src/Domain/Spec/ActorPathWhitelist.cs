namespace Bpm.Domain.Spec;

/// The exact set of `path` values accepted by the `expr` ActorRef type.
/// Anything outside this set is rejected at validator time so the resolver
/// never has to reason about unknown segments.
public static class ActorPathWhitelist
{
    public static readonly IReadOnlySet<string> Paths = new HashSet<string>(StringComparer.Ordinal)
    {
        "submitter",
        "submitter.manager",
        "submitter.manager.manager",
        "submitter.manager.manager.manager",
        "submitter.department",
        "submitter.department.head",
        "submitter.department.parent",
        "submitter.department.parent.head",
        "submitter.department.parent.parent.head",
    };

    public static bool IsAllowed(string path) => Paths.Contains(path);
}
