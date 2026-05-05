using Bpm.Domain.Spec;

namespace Bpm.Application.Spec;

public sealed record ValidationIssue(string Path, string Message, bool IsWarning = false);

public sealed class ValidationReport
{
    public List<ValidationIssue> Errors { get; } = new();
    public List<ValidationIssue> Warnings { get; } = new();
    public bool Ok => Errors.Count == 0;
}

/// Schema-level validation for ActorRef. Does NOT touch the database — that
/// extra "lint" pass (referenced role/group/user exists) lives in the
/// `ActorRefLinter` service which composes this with `IOrgChartReader`.
public sealed class ActorRefValidator
{
    private const int MaxConditionalDepth = 3;
    private static readonly HashSet<string> AllowedOps = new(StringComparer.Ordinal)
    {
        "==", "!=", ">", ">=", "<", "<=", "in", "not_in",
    };

    public ValidationReport Validate(ActorRef root, string path = "$")
    {
        var report = new ValidationReport();
        Walk(root, path, conditionalDepth: 0, fallbackDepth: 0, report);
        return report;
    }

    private void Walk(ActorRef node, string path, int conditionalDepth, int fallbackDepth, ValidationReport report)
    {
        switch (node)
        {
            case ExprActorRef e:
                if (!ActorPathWhitelist.IsAllowed(e.Path))
                    report.Errors.Add(new(path, $"path '{e.Path}' is not on whitelist. Allowed: {string.Join(", ", ActorPathWhitelist.Paths)}"));
                break;

            case RoleActorRef r:
                if (string.IsNullOrWhiteSpace(r.Code))
                    report.Errors.Add(new(path, "role.code must be non-empty"));
                break;

            case GroupActorRef g:
                if (string.IsNullOrWhiteSpace(g.Id))
                    report.Errors.Add(new(path, "group.id must be non-empty"));
                break;

            case UserActorRef u:
                if (string.IsNullOrWhiteSpace(u.Id))
                    report.Errors.Add(new(path, "user.id must be non-empty"));
                report.Warnings.Add(new(path, "hardcoded user references should not be used in production specs", IsWarning: true));
                break;

            case ConditionalActorRef c:
                if (conditionalDepth >= MaxConditionalDepth)
                {
                    report.Errors.Add(new(path, $"conditional nesting capped at {MaxConditionalDepth} levels — restructure with collection or flatten"));
                    return;
                }
                if (!AllowedOps.Contains(c.Condition.Op))
                    report.Errors.Add(new(path + ".condition.op", $"op '{c.Condition.Op}' not in {string.Join(",", AllowedOps)}"));
                if (string.IsNullOrWhiteSpace(c.Condition.Field))
                    report.Errors.Add(new(path + ".condition.field", "field must be non-empty"));
                Walk(c.Then, path + ".then", conditionalDepth + 1, fallbackDepth, report);
                Walk(c.Else, path + ".else", conditionalDepth + 1, fallbackDepth, report);
                break;

            case CollectionActorRef col:
                if (col.Mode is not "any" and not "all")
                    report.Errors.Add(new(path + ".mode", $"mode must be 'any' or 'all', got '{col.Mode}'"));
                if (col.Actors is null || col.Actors.Count == 0)
                {
                    report.Errors.Add(new(path + ".actors", "actors must be a non-empty array"));
                }
                else
                {
                    if (col.Mode == "any")
                    {
                        var min = col.MinApprovals ?? 1;
                        if (min < 1)
                            report.Errors.Add(new(path + ".min_approvals", "min_approvals must be ≥ 1"));
                        if (min > col.Actors.Count)
                            report.Errors.Add(new(path + ".min_approvals", $"min_approvals ({min}) exceeds actors length ({col.Actors.Count})"));
                    }
                    for (var i = 0; i < col.Actors.Count; i++)
                        Walk(col.Actors[i], $"{path}.actors[{i}]", conditionalDepth, fallbackDepth, report);
                }
                break;

            default:
                report.Errors.Add(new(path, $"unknown ActorRef variant {node.GetType().Name}"));
                break;
        }

        if (node.Fallback is not null)
        {
            if (fallbackDepth >= 1)
                report.Errors.Add(new(path + ".fallback", "fallback chain limited to one level"));
            else
                Walk(node.Fallback, path + ".fallback", conditionalDepth, fallbackDepth + 1, report);
        }
    }
}
