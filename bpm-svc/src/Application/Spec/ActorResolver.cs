using System.Text.Json;
using Bpm.Application.Org;
using Bpm.Domain.Spec;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Spec;

/// Resolves an ActorRef + runtime context to a Set<UserId>, with cycle
/// detection on graph walks and structured failure modes. Top-level Resolve
/// calls also write one ActorResolutionAudit row via IActorResolutionAuditor.
public sealed class ActorResolver(
    IOrgChartReader org,
    IActorResolutionAuditor auditor,
    ILogger<ActorResolver> logger) : IActorResolver
{
    public async Task<ResolutionResult> ResolveAsync(ActorRef actor, ResolutionContext ctx, CancellationToken ct = default)
    {
        ResolutionResult result;
        try
        {
            result = await ResolveInner(actor, ctx, ct);
            if (NeedsFallback(result) && actor.Fallback is not null)
            {
                logger.LogInformation("ActorRef primary resolution {Result}, trying fallback", result);
                result = await ResolveInner(actor.Fallback, ctx, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ActorResolver crashed on {ActorType}", actor.Type);
            result = ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, "internal error: " + ex.Message);
        }
        await auditor.WriteAsync(actor, ctx, result, ct);
        return result;
    }

    private static bool NeedsFallback(ResolutionResult r) => r switch
    {
        ResolutionResult.Failure => true,
        ResolutionResult.Success s when s.UserIds.Count == 0 => true,
        _ => false,
    };

    private async Task<ResolutionResult> ResolveInner(ActorRef actor, ResolutionContext ctx, CancellationToken ct)
        => actor switch
        {
            ExprActorRef e => await ResolveExpr(e, ctx, ct),
            RoleActorRef r => await ResolveRole(r, ctx, ct),
            GroupActorRef g => await ResolveGroup(g, ctx, ct),
            UserActorRef u => ResolveUser(u),
            ConditionalActorRef c => await ResolveConditional(c, ctx, ct),
            CollectionActorRef col => await ResolveCollection(col, ctx, ct),
            _ => ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, $"unknown ActorRef type {actor.GetType().Name}"),
        };

    private async Task<ResolutionResult> ResolveExpr(ExprActorRef e, ResolutionContext ctx, CancellationToken ct)
    {
        if (!ActorPathWhitelist.IsAllowed(e.Path))
            return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, $"path '{e.Path}' off whitelist");

        // Walk the path token by token. State = current Guid + kind ("user" | "department").
        var segments = e.Path.Split('.');
        if (segments.Length == 0 || segments[0] != "submitter")
            return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, "path must start with submitter");

        Guid currentId = ctx.SubmitterUserId;
        string currentKind = "user";
        var visitedUsers = new HashSet<Guid> { currentId };
        var visitedDepts = new HashSet<Guid>();

        for (var i = 1; i < segments.Length; i++)
        {
            var seg = segments[i];
            switch ((currentKind, seg))
            {
                case ("user", "manager"):
                {
                    var manager = await org.GetManagerAsync(currentId, ct);
                    if (manager is null)
                        return ResolutionResult.Fail(ResolutionErrorKind.PathUnresolved,
                            $"user {currentId} has no manager (segment {i} of '{e.Path}')");
                    currentId = manager.Id;
                    if (!visitedUsers.Add(currentId))
                        return ResolutionResult.Fail(ResolutionErrorKind.Cycle,
                            $"manager chain cycle at user {currentId}", visitedUsers.ToList());
                    break;
                }
                case ("user", "department"):
                {
                    var dept = await org.GetDepartmentOfAsync(currentId, ct);
                    if (dept is null)
                        return ResolutionResult.Fail(ResolutionErrorKind.PathUnresolved,
                            $"user {currentId} has no department");
                    currentId = dept.Id;
                    currentKind = "department";
                    visitedDepts.Add(currentId);
                    break;
                }
                case ("department", "head"):
                {
                    var head = await org.GetDepartmentHeadAsync(currentId, ct);
                    if (head is null)
                        return ResolutionResult.Fail(ResolutionErrorKind.PathUnresolved,
                            $"department {currentId} has no head");
                    currentId = head.Id;
                    currentKind = "user";
                    if (!visitedUsers.Add(currentId))
                        return ResolutionResult.Fail(ResolutionErrorKind.Cycle,
                            $"manager/dept-head cycle at user {currentId}", visitedUsers.ToList());
                    break;
                }
                case ("department", "parent"):
                {
                    var parent = await org.GetDepartmentParentAsync(currentId, ct);
                    if (parent is null)
                        return ResolutionResult.Fail(ResolutionErrorKind.PathUnresolved,
                            $"department {currentId} has no parent");
                    currentId = parent.Id;
                    if (!visitedDepts.Add(currentId))
                        return ResolutionResult.Fail(ResolutionErrorKind.Cycle,
                            $"department parent cycle at {currentId}", visitedDepts.ToList());
                    break;
                }
                default:
                    return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed,
                        $"segment '{seg}' invalid on {currentKind}");
            }
        }

        // Path must terminate on a user; a path ending on department is invalid in our whitelist.
        if (currentKind != "user")
            return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed,
                $"path '{e.Path}' terminates on {currentKind}, expected user");

        return ResolutionResult.Ok(currentId);
    }

    private async Task<ResolutionResult> ResolveRole(RoleActorRef r, ResolutionContext ctx, CancellationToken ct)
    {
        var assignees = await org.GetRoleAssigneesAsync(r.Code, ctx.FlowCode, ct);
        if (assignees.Count == 0)
            return ResolutionResult.Fail(ResolutionErrorKind.RoleEmpty, $"no assignees for role '{r.Code}'");

        var users = new HashSet<Guid>();
        foreach (var (principalId, _) in assignees)
        {
            // Each principal could be a User, Group, or Department. Probe in order.
            var asUser = await org.GetUserAsync(principalId, ct);
            if (asUser is not null) { users.Add(principalId); continue; }
            var asDept = await org.GetDepartmentAsync(principalId, ct);
            if (asDept is not null)
            {
                // Department assigned a role → all users in dept inherit it
                // (use a dedicated query rather than per-user lookup)
                var dept = await org.GetDepartmentAsync(principalId, ct);
                // For now: pull users via a follow-up query; org reader doesn't expose dept→users yet
                // but we can lean on the GroupExpansion machinery isn't quite right either, so instead
                // surface this as a known limitation: dept-assigned roles cascade via a separate path.
                // (Resolved by ExpandGroup style enumeration in a future change; for POC we skip.)
                continue;
            }
            var groupExp = await org.ExpandGroupAsync(principalId, ct);
            if (groupExp.HasCycle)
                return ResolutionResult.Fail(ResolutionErrorKind.Cycle,
                    $"cycle expanding group {principalId} for role '{r.Code}'", groupExp.CyclePath);
            foreach (var uid in groupExp.UserIds) users.Add(uid);
        }

        if (users.Count == 0)
            return ResolutionResult.Fail(ResolutionErrorKind.RoleEmpty, $"role '{r.Code}' has assignees but none expand to users");

        return ResolutionResult.Ok(users);
    }

    private async Task<ResolutionResult> ResolveGroup(GroupActorRef g, ResolutionContext _, CancellationToken ct)
    {
        if (!Guid.TryParse(g.Id, out var gid))
            return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, $"group.id '{g.Id}' is not a Guid");
        var exp = await org.ExpandGroupAsync(gid, ct);
        if (exp.HasCycle)
            return ResolutionResult.Fail(ResolutionErrorKind.Cycle, $"group cycle starting at {gid}", exp.CyclePath);
        if (exp.UserIds.Count == 0)
            return ResolutionResult.Fail(ResolutionErrorKind.GroupEmpty, $"group {gid} has no resolvable users");
        return ResolutionResult.Ok(exp.UserIds);
    }

    private static ResolutionResult ResolveUser(UserActorRef u)
    {
        if (!Guid.TryParse(u.Id, out var uid))
            return ResolutionResult.Fail(ResolutionErrorKind.ValidationFailed, $"user.id '{u.Id}' is not a Guid");
        return ResolutionResult.Ok(uid);
    }

    private async Task<ResolutionResult> ResolveConditional(ConditionalActorRef c, ResolutionContext ctx, CancellationToken ct)
    {
        var branch = EvaluateCondition(c.Condition, ctx) ? c.Then : c.Else;
        return await ResolveInner(branch, ctx, ct);
    }

    private static bool EvaluateCondition(ActorRefCondition cond, ResolutionContext ctx)
    {
        if (!ctx.FormData.TryGetValue(cond.Field, out var lhs))
            return false;
        return cond.Op switch
        {
            "==" => JsonEquals(lhs, cond.Value),
            "!=" => !JsonEquals(lhs, cond.Value),
            ">"  => Compare(lhs, cond.Value) > 0,
            ">=" => Compare(lhs, cond.Value) >= 0,
            "<"  => Compare(lhs, cond.Value) < 0,
            "<=" => Compare(lhs, cond.Value) <= 0,
            "in" => InArray(lhs, cond.Value),
            "not_in" => !InArray(lhs, cond.Value),
            _ => false,
        };
    }

    private static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText(),
        };
    }

    private static int Compare(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return a.GetDecimal().CompareTo(b.GetDecimal());
        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String)
            return string.Compare(a.GetString(), b.GetString(), StringComparison.Ordinal);
        return 0;
    }

    private static bool InArray(JsonElement value, JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in array.EnumerateArray())
            if (JsonEquals(value, item)) return true;
        return false;
    }

    private async Task<ResolutionResult> ResolveCollection(CollectionActorRef col, ResolutionContext ctx, CancellationToken ct)
    {
        var unioned = new HashSet<Guid>();
        var failureReasons = new List<string>();
        foreach (var inner in col.Actors)
        {
            var r = await ResolveInner(inner, ctx, ct);
            if (r is ResolutionResult.Success s)
                foreach (var uid in s.UserIds) unioned.Add(uid);
            else if (r is ResolutionResult.Failure f)
                failureReasons.Add(f.Error.Reason);
        }

        if (col.Mode == "all" && failureReasons.Count > 0)
            return ResolutionResult.Fail(ResolutionErrorKind.ConditionalBranchEmpty,
                $"{failureReasons.Count} of {col.Actors.Count} collection entries failed: {string.Join(" ; ", failureReasons)}");

        if (unioned.Count == 0)
            return ResolutionResult.Fail(ResolutionErrorKind.ConditionalBranchEmpty,
                "all collection entries resolved empty");

        return ResolutionResult.Ok(unioned);
    }
}
