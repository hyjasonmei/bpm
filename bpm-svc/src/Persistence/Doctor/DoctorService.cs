using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Doctor;
using Bpm.Application.Org;
using Bpm.Domain.Entities.Doctor;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Doctor;

/// <summary>
/// Process Doctor implementation. Detection reflection-scans every Model-B
/// case table (open = CompletedAt null) and resolves "gone" / "empty" via the
/// same <see cref="IOrgChartReader"/> the flows use, so the diagnosis matches
/// runtime. Remediation is a generic case override (parameterized UPDATE on the
/// table named by the finding) — reassign is state-neutral so it bypasses each
/// flow's state machine; cancel writes the flow's reflected <c>Cancelled</c>
/// enum value. Every action is logged to <c>DoctorActionLogs</c>.
/// </summary>
public sealed class DoctorService(
    AppDbContext db,
    IOrgChartReader org,
    IClock clock) : IDoctorService
{
    private const string DefaultTenant = "default";

    private static readonly Regex CaseTypeRe = new(@"^(?<code>.+)_V\d+_Case$", RegexOptions.Compiled);

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethods()
        .First(m => m.Name == "Set" && m.IsGenericMethod && m.GetParameters().Length == 0);
    private static readonly MethodInfo ToListAsyncMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
        .First(m => m.Name == "ToListAsync" && m.GetParameters().Length == 2);

    // ---- discovery ----

    private sealed record CaseTable(string Code, Type Clr, string Table);

    private IReadOnlyList<CaseTable> CaseTables()
        => db.Model.GetEntityTypes()
            .Select(e => new { e, m = CaseTypeRe.Match(e.ClrType.Name) })
            .Where(x => x.m.Success
                && x.e.ClrType.GetProperty("Status") is not null
                && x.e.ClrType.GetProperty("SubmittedAt") is not null
                && x.e.ClrType.GetProperty("CurrentAssigneeUserId") is not null
                && x.e.ClrType.GetProperty("CompletedAt") is not null
                && x.e.GetTableName() is not null)
            .Select(x => new CaseTable(x.m.Groups["code"].Value, x.e.ClrType, x.e.GetTableName()!))
            .ToList();

    private async Task<List<object>> MaterializeAsync(Type clr, CancellationToken ct)
    {
        var set = SetMethod.MakeGenericMethod(clr).Invoke(db, null)!;
        var task = (Task)ToListAsyncMethod.MakeGenericMethod(clr).Invoke(null, new[] { set, ct })!;
        await task.ConfigureAwait(false);
        var rows = (IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
        return rows.Cast<object>().ToList();
    }

    private static object? Prop(object row, string name) => row.GetType().GetProperty(name)?.GetValue(row);

    // ---- scan ----

    public async Task<DoctorReport> ScanAsync(int stalledDays = 14, CancellationToken ct = default)
    {
        if (stalledDays <= 0) stalledDays = 14;
        var now = clock.UtcNow;
        var stalledBefore = now.AddDays(-stalledDays);

        // 1) gather all open cases across every flow
        var open = new List<OpenCase>();
        foreach (var t in CaseTables())
        {
            var rows = await MaterializeAsync(t.Clr, ct);
            foreach (var r in rows)
            {
                if (Prop(r, "CompletedAt") is not null) continue; // closed
                open.Add(new OpenCase(
                    t.Code,
                    (Guid)Prop(r, "Id")!,
                    Prop(r, "Status")?.ToString(),
                    (Guid?)Prop(r, "CurrentAssigneeUserId"),
                    (Guid)Prop(r, "SubmitterUserId")!,
                    (DateTime)Prop(r, "SubmittedAt")!,
                    (DateTime)Prop(r, "LastActivityAt")!));
            }
        }

        // 2) load every principal once (id -> name/active/deleted)
        var principals = await db.SharedPrincipals.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p, ct);

        bool IsActive(Guid? id) => id is { } g && principals.TryGetValue(g, out var p) && p.Active && p.DeletedAt == null;
        string? Name(Guid? id) => id is { } g && principals.TryGetValue(g, out var p) ? p.DisplayName : null;

        // 3) classify each open case into one finding (severity priority)
        var caseFindings = new List<CaseFinding>();
        var departedCounts = new Dictionary<Guid, int>();
        foreach (var c in open)
        {
            var days = (int)Math.Floor((now - c.LastActivityAt).TotalDays);
            string? rule = null, severity = null;
            bool assigneeGone = false;

            if (c.AssigneeUserId is { } a && !IsActive(a))
            {
                rule = "resigned_approver"; severity = "high"; assigneeGone = true;
                departedCounts[a] = departedCounts.GetValueOrDefault(a) + 1;
            }
            else if (c.AssigneeUserId is null)
            {
                rule = "ownerless"; severity = "high";
            }
            else if (c.LastActivityAt < stalledBefore)
            {
                rule = "stalled"; severity = "medium";
            }
            if (rule is null) continue;

            (Guid? sId, string? sName, string? sVia) = assigneeGone
                ? await SuggestTargetAsync(c.AssigneeUserId!.Value, IsActive, ct)
                : (null, null, null);

            caseFindings.Add(new CaseFinding(
                rule, severity!, c.Code, c.CaseId, c.StatusName,
                c.AssigneeUserId, Name(c.AssigneeUserId), assigneeGone,
                c.SubmitterUserId, Name(c.SubmitterUserId),
                c.SubmittedAt, c.LastActivityAt, days,
                sId, sName, sVia));
        }

        var departed = departedCounts
            .Select(kv =>
            {
                principals.TryGetValue(kv.Key, out var p);
                return new DepartedPerson(
                    kv.Key,
                    p?.DisplayName ?? kv.Key.ToString(),
                    p?.Active ?? false,
                    p?.DeletedAt != null,
                    kv.Value);
            })
            .OrderByDescending(d => d.OpenCaseCount)
            .ToList();

        // 4) org health
        var orgFindings = await ScanOrgHealthAsync(principals, IsActive, ct);

        return new DoctorReport(caseFindings, orgFindings, departed);
    }

    private sealed record OpenCase(
        string Code, Guid CaseId, string? StatusName, Guid? AssigneeUserId,
        Guid SubmitterUserId, DateTime SubmittedAt, DateTime LastActivityAt);

    private async Task<(Guid?, string?, string?)> SuggestTargetAsync(Guid forUserId, Func<Guid?, bool> isActive, CancellationToken ct)
    {
        // manager → primary dept head; first active wins
        var mgr = await org.GetManagerIdAsync(forUserId, ct);
        if (isActive(mgr)) return (mgr, await NameAsync(mgr!.Value, ct), "主管");

        var dept = await org.GetPrimaryDepartmentIdAsync(forUserId, ct);
        if (dept is { } d)
        {
            var head = await org.GetDepartmentHeadIdAsync(d, ct);
            if (isActive(head) && head != forUserId) return (head, await NameAsync(head!.Value, ct), "部門主管");
        }
        return (null, null, null);
    }

    private async Task<string?> NameAsync(Guid id, CancellationToken ct)
        => await db.SharedPrincipals.AsNoTracking().Where(p => p.Id == id).Select(p => p.DisplayName).FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyList<OrgFinding>> ScanOrgHealthAsync(
        IReadOnlyDictionary<Guid, SharedPrincipal> principals, Func<Guid?, bool> isActive, CancellationToken ct)
    {
        var findings = new List<OrgFinding>();

        var userDepts = await db.SharedUserDepts.AsNoTracking().ToListAsync(ct);
        var deptMembers = userDepts.GroupBy(x => x.DeptId).ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());
        var heads = (await db.SharedDeptHeads.AsNoTracking().Select(h => h.DeptId).ToListAsync(ct)).ToHashSet();

        // dept_no_head: an active dept that has members but no head → dept_head resolution fails
        foreach (var p in principals.Values.Where(p => p.Type == SharedPrincipalType.Dept && p.Active && p.DeletedAt == null))
        {
            if (deptMembers.TryGetValue(p.Id, out var mem) && mem.Count > 0 && !heads.Contains(p.Id))
                findings.Add(new OrgFinding("no_dept_head", "info", "dept", p.Id, p.DisplayName, "部門有成員但沒有指定主管（dept_head 簽核會解析失敗）"));
        }

        // empty_role: a role that resolves to 0 active users
        foreach (var role in await db.SharedRoles.AsNoTracking().ToListAsync(ct))
        {
            try
            {
                var active = await ActiveMembersOfRoleAsync(role.Name, principals, deptMembers, isActive, ct);
                if (active == 0)
                    findings.Add(new OrgFinding("empty_role", "info", "role", role.Id, role.Name, "此角色目前沒有任何在職成員（路由到它的流程會卡）"));
            }
            catch { /* resolution error — skip this role */ }
        }

        // empty_group: an active group that expands to 0 active users
        foreach (var p in principals.Values.Where(p => p.Type == SharedPrincipalType.Group && p.Active && p.DeletedAt == null))
        {
            try
            {
                var exp = await org.ExpandGroupAsync(p.Id, ct);
                if (!exp.HasCycle && !exp.UserIds.Any(u => isActive(u)))
                    findings.Add(new OrgFinding("empty_group", "info", "group", p.Id, p.DisplayName, "此群組目前沒有任何在職成員"));
            }
            catch { /* skip */ }
        }

        return findings;
    }

    private async Task<int> ActiveMembersOfRoleAsync(
        string roleName, IReadOnlyDictionary<Guid, SharedPrincipal> principals,
        IReadOnlyDictionary<Guid, List<Guid>> deptMembers, Func<Guid?, bool> isActive, CancellationToken ct)
    {
        var assignees = await org.GetRoleAssigneesAsync(roleName, null, ct);
        var users = new HashSet<Guid>();
        foreach (var (pid, _) in assignees)
        {
            if (!principals.TryGetValue(pid, out var p)) continue;
            switch (p.Type)
            {
                case SharedPrincipalType.User:
                    users.Add(pid); break;
                case SharedPrincipalType.Group:
                    var exp = await org.ExpandGroupAsync(pid, ct);
                    foreach (var u in exp.UserIds) users.Add(u);
                    break;
                case SharedPrincipalType.Dept:
                    if (deptMembers.TryGetValue(pid, out var mem)) foreach (var u in mem) users.Add(u);
                    break;
            }
        }
        return users.Count(u => isActive(u));
    }

    // ---- candidates ----

    public async Task<DoctorCandidates> GetCandidatesAsync(Guid? forUserId, CancellationToken ct = default)
    {
        var users = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null)
            .OrderBy(p => p.DisplayName).Take(300)
            .Select(p => new DoctorCandidate(p.Id, p.DisplayName, p.Email, null))
            .ToListAsync(ct);

        DoctorCandidate? suggested = null;
        if (forUserId is { } f)
        {
            var activeIds = users.Select(u => u.UserId).ToHashSet();
            bool isActive(Guid? id) => id is { } g && activeIds.Contains(g);
            var (sid, sname, svia) = await SuggestTargetAsync(f, isActive, ct);
            if (sid is { } s) suggested = new DoctorCandidate(s, sname ?? s.ToString(), null, svia);
        }
        return new DoctorCandidates(suggested, users);
    }

    // ---- remediation ----

    public async Task<DoctorActionResult> ReassignAsync(string flowCode, Guid caseId, Guid toUserId, Guid? operatorUserId, string? reason, CancellationToken ct = default)
    {
        var t = CaseTables().FirstOrDefault(x => string.Equals(x.Code, flowCode, StringComparison.OrdinalIgnoreCase));
        if (t is null) return new DoctorActionResult(false, 0, "unknown_flow");

        var now = clock.UtcNow;
        var n = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{t.Table}\" SET \"CurrentAssigneeUserId\" = {{0}}, \"LastActivityAt\" = {{1}} WHERE \"Id\" = {{2}} AND \"CompletedAt\" IS NULL",
            new object[] { toUserId, now, caseId }, ct);

        await LogAsync("reassign", flowCode, caseId, null, toUserId, n, operatorUserId, reason, ct);
        return new DoctorActionResult(n > 0, n, n > 0 ? null : "not_found_or_closed");
    }

    public async Task<DoctorActionResult> BatchReassignAsync(Guid fromUserId, Guid toUserId, Guid? operatorUserId, string? reason, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var total = 0;
        foreach (var t in CaseTables())
        {
            total += await db.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{t.Table}\" SET \"CurrentAssigneeUserId\" = {{0}}, \"LastActivityAt\" = {{1}} WHERE \"CurrentAssigneeUserId\" = {{2}} AND \"CompletedAt\" IS NULL",
                new object[] { toUserId, now, fromUserId }, ct);
        }
        await LogAsync("batch_reassign", null, null, fromUserId, toUserId, total, operatorUserId, reason, ct);
        return new DoctorActionResult(true, total);
    }

    public async Task<DoctorActionResult> CancelAsync(string flowCode, Guid caseId, Guid? operatorUserId, string? reason, CancellationToken ct = default)
    {
        var t = CaseTables().FirstOrDefault(x => string.Equals(x.Code, flowCode, StringComparison.OrdinalIgnoreCase));
        if (t is null) return new DoctorActionResult(false, 0, "unknown_flow");

        var enumType = t.Clr.GetProperty("Status")!.PropertyType;
        enumType = Nullable.GetUnderlyingType(enumType) ?? enumType;
        if (!enumType.IsEnum || !Enum.IsDefined(enumType, "Cancelled"))
            return new DoctorActionResult(false, 0, "no_cancelled_status");
        var cancelledInt = Convert.ToInt32(Enum.Parse(enumType, "Cancelled"));

        var now = clock.UtcNow;
        var n = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{t.Table}\" SET \"Status\" = {{0}}, \"CompletedAt\" = {{1}}, \"CurrentAssigneeUserId\" = NULL, \"LastActivityAt\" = {{2}} WHERE \"Id\" = {{3}} AND \"CompletedAt\" IS NULL",
            new object[] { cancelledInt, now, now, caseId }, ct);

        await LogAsync("cancel", flowCode, caseId, null, null, n, operatorUserId, reason, ct);
        return new DoctorActionResult(n > 0, n, n > 0 ? null : "not_found_or_closed");
    }

    private async Task LogAsync(string action, string? flowCode, Guid? caseId, Guid? from, Guid? to, int affected, Guid? op, string? reason, CancellationToken ct)
    {
        db.Set<DoctorActionLog>().Add(new DoctorActionLog
        {
            Id = Guid.NewGuid(),
            Action = action,
            FlowCode = flowCode,
            CaseId = caseId,
            FromUserId = from,
            ToUserId = to,
            Affected = affected,
            OperatorUserId = op,
            Reason = reason,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
