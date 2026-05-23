using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Authz;
using Bpm.Persistence;
using Bpm.SeedCli.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.SeedCli.Fixtures;

/// <summary>
/// Iterates every <c>sample_specs/*.json</c>, snapshots the currently
/// seeded org into a <see cref="SampleOrgSnapshot"/>, lifts the spec's own
/// <c>testCases[]</c> (when present) into <see cref="TestCaseSnapshot"/>s,
/// then calls <see cref="BundleInstaller"/> to persist each bundle as a
/// pending <c>SpecBundle</c> row.
///
/// <para>
/// Idempotent: re-running yields zero new rows because
/// <see cref="BundleInstaller"/> short-circuits on
/// <c>ManifestChecksum</c>.
/// </para>
///
/// <para>
/// BPMN xml is a placeholder ("&lt;bpmn:definitions/&gt;") — the bundle's
/// rendered diagram is a Phase 2 concern (see
/// <c>add-form-runtime-rendering</c> proposal).
/// </para>
/// </summary>
public sealed class BundleSeedFixture(
    AppDbContext db,
    BundleInstaller installer,
    ILogger<BundleSeedFixture> logger)
{
    public sealed record FixtureResult(int Inserted, int AlreadyExisted, int Failed, IReadOnlyList<string> Errors);

    public async Task<FixtureResult> RunAsync(string sampleSpecsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(sampleSpecsDir))
            throw new DirectoryNotFoundException($"sample_specs directory not found: {sampleSpecsDir}");

        var sampleOrg = await BuildSampleOrgFromSeedAsync(ct);

        var specPaths = Directory.GetFiles(sampleSpecsDir, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var inserted = 0;
        var existed = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var specPath in specPaths)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(specPath);
            try
            {
                var (specJson, testCases) = await LoadSpecAndCasesAsync(specPath, ct);
                var req = new BundleBuildRequest(
                    DraftSpecJson: specJson,
                    BpmnXml: "<bpmn:definitions/>",
                    SampleOrg: sampleOrg,
                    TestCases: testCases,
                    IncludeAssets: false);

                var outcome = await installer.InstallAsync(tenantCode: "default", req, ct);
                if (outcome.AlreadyExisted)
                {
                    existed++;
                    logger.LogInformation("BundleSeedFixture: {File} already installed (id={Id}, checksum={Checksum})",
                        fileName, outcome.BundleId, outcome.ManifestChecksum[..Math.Min(12, outcome.ManifestChecksum.Length)]);
                }
                else
                {
                    inserted++;
                    logger.LogInformation("BundleSeedFixture: installed {File} (id={Id}, checksum={Checksum})",
                        fileName, outcome.BundleId, outcome.ManifestChecksum[..Math.Min(12, outcome.ManifestChecksum.Length)]);
                }
            }
            catch (Exception ex)
            {
                failed++;
                var msg = $"{fileName}: {ex.Message}";
                errors.Add(msg);
                logger.LogError(ex, "BundleSeedFixture: failed to install {File}", fileName);
            }
        }

        logger.LogInformation(
            "BundleSeedFixture: {Inserted} inserted, {Existed} already existed, {Failed} failed (of {Total} sample specs)",
            inserted, existed, failed, specPaths.Count);

        return new FixtureResult(inserted, existed, failed, errors);
    }

    /// <summary>
    /// Snapshot the seeded users / departments / groups / role-assignments
    /// from the live DB into the in-memory shape the BundleBuilder wants.
    /// </summary>
    private async Task<SampleOrgSnapshot> BuildSampleOrgFromSeedAsync(CancellationToken ct)
    {
        // After unify-user-store: identity comes from admin-svc's seed via
        // SharedX entity mappings on the shared db. The legacy
        // ManagerId / DepartmentId / Department.Code / Group.Code / Role.Code
        // columns no longer exist — translate through SharedUserManagers,
        // SharedUserDepts(IsPrimary), SharedDeptParents, SharedDeptHeads, etc.
        var principals = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .ToListAsync(ct);

        var primaryDeptByUser = await db.SharedUserDepts.AsNoTracking()
            .Where(ud => ud.IsPrimary)
            .ToDictionaryAsync(ud => ud.UserId, ud => ud.DeptId, ct);
        var managerByUser = await db.SharedUserManagers.AsNoTracking()
            .ToDictionaryAsync(m => m.UserId, m => (Guid?)m.ManagerUserId, ct);

        var users = principals
            .Where(p => p.Type == SharedIdentity.SharedPrincipalType.User)
            .Select(u => new UserSnapshot(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                managerByUser.GetValueOrDefault(u.Id),
                primaryDeptByUser.TryGetValue(u.Id, out var dId) ? dId : (Guid?)null))
            .ToList();

        var parentByDept = await db.SharedDeptParents.AsNoTracking()
            .ToDictionaryAsync(dp => dp.DeptId, dp => dp.ParentDeptId, ct);
        var headByDept = await db.SharedDeptHeads.AsNoTracking()
            .ToDictionaryAsync(dh => dh.DeptId, dh => (Guid?)dh.HeadUserId, ct);

        var depts = principals
            .Where(p => p.Type == SharedIdentity.SharedPrincipalType.Dept)
            .Select(d => new DepartmentSnapshot(
                d.Id,
                d.DisplayName,
                d.DisplayName,
                parentByDept.TryGetValue(d.Id, out var pId) ? pId : null,
                headByDept.TryGetValue(d.Id, out var hId) ? hId : null))
            .ToList();

        var memberRows = await db.SharedGroupMembers.AsNoTracking()
            .Select(m => new { m.GroupId, m.MemberPrincipalId })
            .ToListAsync(ct);
        var membersByGroup = memberRows
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.MemberPrincipalId).ToList());
        var groups = principals
            .Where(p => p.Type == SharedIdentity.SharedPrincipalType.Group)
            .Select(g => new GroupSnapshot(g.Id, g.DisplayName, g.DisplayName,
                membersByGroup.GetValueOrDefault(g.Id, Array.Empty<Guid>())))
            .ToList();

        var raRows = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            select new { RoleName = r.Name, pr.PrincipalId }
        ).ToListAsync(ct);
        var assignments = raRows
            .Select(x => new RoleAssignmentSnapshot(x.RoleName, x.PrincipalId, "tenant", null))
            .ToList();

        if (users.Count == 0)
            throw new InvalidOperationException(
                "BundleSeedFixture: no users seeded. Boot admin-svc once to populate the unified seed.");

        return new SampleOrgSnapshot(users, depts, groups, assignments);
    }

    /// <summary>
    /// Load a spec.json and pull out its <c>testCases[]</c> array (if any)
    /// as <see cref="TestCaseSnapshot"/>s. Falls back to a single dummy
    /// test-case when the spec has none — <see cref="BundleBuildValidator"/>
    /// requires at least one.
    /// </summary>
    private static async Task<(JsonElement Spec, IReadOnlyList<TestCaseSnapshot> Cases)> LoadSpecAndCasesAsync(
        string path, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        using var doc = JsonDocument.Parse(bytes);
        var spec = doc.RootElement.Clone();

        var cases = new List<TestCaseSnapshot>();
        if (spec.TryGetProperty("testCases", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in arr.EnumerateArray())
            {
                var id = tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString() ?? "tc"
                    : "tc";
                var name = tc.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? id
                    : id;
                var inputs = tc.TryGetProperty("inputs", out var inEl)
                    ? inEl.Clone()
                    : EmptyObject();

                // Spec uses both "expectedTrace" (newer) and "expectedPath"
                // (older LEAVE-v1 style); accept either.
                var trace = ExtractTrace(tc);
                var finalStatus = tc.TryGetProperty("expectedFinalStatus", out var fsEl) && fsEl.ValueKind == JsonValueKind.String
                    ? fsEl.GetString() ?? "Completed"
                    : "Completed";

                cases.Add(new TestCaseSnapshot(
                    Id: id,
                    Name: name,
                    Inputs: inputs,
                    ExpectedTrace: trace,
                    ExpectedFinalStatus: finalStatus));
            }
        }

        if (cases.Count == 0)
        {
            // Stub case so BundleBuildValidator passes. Repro will obviously
            // fail this — but SeedCli doesn't run repro, so the bundle just
            // sits in Pending until the user clicks "Repro Check".
            cases.Add(new TestCaseSnapshot(
                Id: "tc_stub",
                Name: "stub (no test cases in spec)",
                Inputs: EmptyObject(),
                ExpectedTrace: Array.Empty<string>(),
                ExpectedFinalStatus: "Completed"));
        }

        return (spec, cases);
    }

    private static IReadOnlyList<string> ExtractTrace(JsonElement tc)
    {
        if (tc.TryGetProperty("expectedTrace", out var t1) && t1.ValueKind == JsonValueKind.Array)
            return t1.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        if (tc.TryGetProperty("expectedPath", out var t2) && t2.ValueKind == JsonValueKind.Array)
            return t2.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        return Array.Empty<string>();
    }

    private static JsonElement EmptyObject()
    {
        using var d = JsonDocument.Parse("{}");
        return d.RootElement.Clone();
    }
}
