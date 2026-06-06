using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Bpm.Tests.Common;

namespace Bpm.Tests.Application.Spec.Bundle;

/// <summary>
/// Shared bundle-construction helpers for PR-I3+ test suites. The
/// LEAVE-v1 sample is the canonical fixture across the spec-bundle PR
/// chain — keeping the build wiring (org seed + test cases + builder
/// dependencies) here means each PR's tests stay focused on the
/// behaviour under test instead of re-building the same scaffolding.
/// </summary>
internal static class BundleTestFactory
{
    // Resolved next to the test assembly (copied from Fixtures/ via the csproj
    // Content item) — portable across machines / CI, unlike the old hardcoded
    // absolute path that was never committed.
    public static readonly string LeaveSpecPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "leave_v1.json");

    public static readonly Guid AliceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid BobId   = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid HrGroupId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static BundleBuilder NewBuilder() => new(
        new BundleBuildValidator(),
        new SpecMdRenderer(),
        new WalkthroughRenderer(),
        new ChangelogRenderer(),
        new StubClock());

    public static SampleOrgSnapshot DefaultOrg() => new(
        Users: new List<UserSnapshot>
        {
            // Alice = manager (no manager of her own)
            new(AliceId, "alice@acme.tld", "Alice", null, null),
            // Bob reports to Alice — exercises submitter.manager path.
            new(BobId, "bob@acme.tld", "Bob", AliceId, null),
        },
        Departments: new List<DepartmentSnapshot>(),
        Groups: new List<GroupSnapshot>
        {
            new(HrGroupId, "HR", "Human Resources", new List<Guid> { AliceId }),
        },
        // VP + HR roles are referenced by leave_v1's approver fallback +
        // notify recipients; both must have at least one assignee for the
        // validator to pass.
        RoleAssignments: new List<RoleAssignmentSnapshot>
        {
            new("VP", AliceId, "tenant", null),
            new("HR", AliceId, "tenant", null),
        });

    public static IReadOnlyList<TestCaseSnapshot> DefaultCases()
    {
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        return new List<TestCaseSnapshot>
        {
            new(
                Id: "tc_1",
                Name: "5 day vacation",
                Inputs: doc.RootElement.Clone(),
                ExpectedTrace: new[]
                {
                    "start_1", "task_apply", "approval_manager",
                    "gateway_days", "task_hr_archive", "end_1",
                }),
        };
    }

    public static async Task<JsonElement> LoadLeaveSpecAsync()
    {
        var json = await File.ReadAllTextAsync(LeaveSpecPath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Build the LEAVE bundle using the default org + test case fixtures.
    /// Returns the raw zip bytes ready to feed into the parser.
    /// </summary>
    public static async Task<byte[]> BuildLeaveBundleAsync(
        SampleOrgSnapshot? org = null,
        IReadOnlyList<TestCaseSnapshot>? cases = null)
    {
        var spec = await LoadLeaveSpecAsync();
        return await NewBuilder().BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn:definitions />",
            SampleOrg: org ?? DefaultOrg(),
            TestCases: cases ?? DefaultCases()));
    }
}
