using System.Text.Json;
using Bpm.Domain.Spec;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Cross-file consistency checks for a parsed bundle. Errors are
/// aggregated; the result's <see cref="BundleValidationResult.Valid"/> is
/// false iff any error was emitted. Stable error <c>Code</c>s let the UI
/// localize messages and route categorically (e.g. "form missing" vs
/// "test trace bad").
///
/// <para>
/// v1 checks (per change spec.md §"Bundle validation"):
/// </para>
///
/// <list type="number">
///   <item><description>Every ActorRef in spec.json resolves under
///   sample-org.json. <c>expr</c> paths are checked against
///   <see cref="ActorPathWhitelist"/> only — depth resolution against
///   the org tree is deferred to PR-I5 (the repro runner does it
///   end-to-end via <c>IActorResolver</c>).</description></item>
///   <item><description>Every userTask has a matching <c>forms/{id}.json</c>.</description></item>
///   <item><description>Every test-case's <c>expectedTrace</c> only mentions
///   node ids that exist in spec.json's flow.</description></item>
/// </list>
/// </summary>
public sealed class BundleValidator : IBundleValidator
{
    public BundleValidationResult Validate(ParsedBundle b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var errors = new List<BundleValidationError>();

        ValidateActorRefs(b, errors);
        ValidateUserTaskForms(b, errors);
        ValidateTestCaseTraces(b, errors);

        return errors.Count == 0
            ? BundleValidationResult.Ok()
            : new BundleValidationResult(false, errors);
    }

    /// <summary>
    /// Re-extract every distinct ActorRef from spec.json (using the same
    /// collector the builder used to populate actors.json — keeps export
    /// and validation in lockstep) and check each against sample-org.
    /// </summary>
    private static void ValidateActorRefs(ParsedBundle b, List<BundleValidationError> errors)
    {
        var refs = ActorRefCollector.Collect(b.SpecJson);
        var org = b.SampleOrg;

        var roleCodes = new HashSet<string>(org.RoleAssignments.Select(a => a.RoleCode), StringComparer.Ordinal);
        var groupIds = new HashSet<string>(org.Groups.Select(g => g.Id.ToString()), StringComparer.OrdinalIgnoreCase);
        var groupCodes = new HashSet<string>(org.Groups.Select(g => g.Code), StringComparer.Ordinal);
        var userIds = new HashSet<string>(org.Users.Select(u => u.Id.ToString()), StringComparer.OrdinalIgnoreCase);

        foreach (var r in refs)
        {
            // Skip pseudo-refs that resolve at runtime against the live
            // process context, not against the sample org.
            if (r is "submitter" or "current_approver")
                continue;

            var (kind, value) = SplitRef(r);
            switch (kind)
            {
                case "expr":
                    if (!ActorPathWhitelist.IsAllowed(value))
                    {
                        errors.Add(new BundleValidationError(
                            "EXPR_PATH_OFF_WHITELIST",
                            $"spec.json#actor:{r}",
                            $"Actor path '{value}' is not on the ActorRef whitelist."));
                    }
                    break;
                case "role":
                    if (!roleCodes.Contains(value))
                    {
                        errors.Add(new BundleValidationError(
                            "ROLE_NO_ASSIGNEE",
                            $"sample-org.json#roleAssignments",
                            $"Role '{value}' has no assignee in sample-org.json."));
                    }
                    break;
                case "group":
                    if (!groupIds.Contains(value) && !groupCodes.Contains(value))
                    {
                        errors.Add(new BundleValidationError(
                            "GROUP_NOT_FOUND",
                            $"sample-org.json#groups",
                            $"Group '{value}' not found in sample-org.json (checked id and code)."));
                    }
                    break;
                case "user":
                    if (!userIds.Contains(value))
                    {
                        errors.Add(new BundleValidationError(
                            "USER_NOT_FOUND",
                            $"sample-org.json#users",
                            $"User '{value}' not found in sample-org.json."));
                    }
                    break;
                // Other prefixes (none today) silently pass — additive
                // ActorRef kinds will get a dedicated case in a follow-up.
            }
        }
    }

    private static void ValidateUserTaskForms(ParsedBundle b, List<BundleValidationError> errors)
    {
        if (!b.SpecJson.TryGetProperty("userTasks", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var t in arr.EnumerateArray())
        {
            if (!t.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id))
                continue;
            if (!b.Forms.ContainsKey(id))
            {
                errors.Add(new BundleValidationError(
                    "MISSING_FORM",
                    $"spec.json#userTasks.{id}",
                    $"userTask '{id}' has no matching forms/{id}.json in the bundle."));
            }
        }
    }

    private static void ValidateTestCaseTraces(ParsedBundle b, List<BundleValidationError> errors)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (b.SpecJson.TryGetProperty("flow", out var flow)
            && flow.TryGetProperty("nodes", out var nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in nodes.EnumerateArray())
            {
                if (n.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    var s = idEl.GetString();
                    if (!string.IsNullOrEmpty(s))
                        nodeIds.Add(s);
                }
            }
        }

        foreach (var tc in b.TestCases)
        {
            for (var i = 0; i < tc.ExpectedTrace.Count; i++)
            {
                var nid = tc.ExpectedTrace[i];
                if (!nodeIds.Contains(nid))
                {
                    errors.Add(new BundleValidationError(
                        "UNKNOWN_NODE_IN_TRACE",
                        $"test-cases/{tc.Id}.json#expectedTrace[{i}]",
                        $"Test case '{tc.Id}' expectedTrace[{i}] references unknown node '{nid}'."));
                }
            }
        }
    }

    /// <summary>
    /// Split an ActorRefCollector entry like "expr:submitter.manager" into
    /// ("expr", "submitter.manager"). Pseudo-refs without a colon (e.g.
    /// "submitter") return ("", original) so callers can short-circuit.
    /// </summary>
    private static (string Kind, string Value) SplitRef(string s)
    {
        var idx = s.IndexOf(':');
        if (idx < 0) return (string.Empty, s);
        return (s[..idx], s[(idx + 1)..]);
    }
}
