using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Spec.Bundle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class BundleValidatorTests
{
    private static BundleParser NewParser() => new(NullLogger<BundleParser>.Instance);
    private static BundleValidator NewValidator() => new();

    [Fact]
    public async Task Valid_leave_bundle_passes()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        using var ms = new MemoryStream(bytes);
        var parsed = await NewParser().ParseAsync(ms);

        var result = NewValidator().Validate(parsed);
        Assert.True(result.Valid, "expected valid bundle, got errors: " +
            string.Join("; ", result.Errors.Select(e => $"{e.Code} @ {e.Location}: {e.Message}")));
        Assert.Empty(result.Errors);
    }

    /// <summary>3.7 — A spec referencing an off-whitelist actor path
    /// (4-deep manager chain) must be rejected with a stable code so
    /// the wizard can surface a fix-it hint.</summary>
    [Fact]
    public void Off_whitelist_expr_path_yields_EXPR_PATH_OFF_WHITELIST()
    {
        var parsed = ParsedBundleFromSpec("""
        {
          "meta": { "flowCode": "DEMO", "flowVersion": 1 },
          "flow": { "nodes": [], "edges": [] },
          "userTasks": [],
          "approvals": [
            { "id": "deep", "approver": { "type": "expr", "path": "submitter.manager.manager.manager.manager" } }
          ]
        }
        """);

        var result = NewValidator().Validate(parsed);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "EXPR_PATH_OFF_WHITELIST" &&
            e.Message.Contains("submitter.manager.manager.manager.manager"));
    }

    [Fact]
    public void Missing_form_for_userTask_yields_MISSING_FORM()
    {
        // A spec that declares two user tasks but we only provide ONE form file.
        var parsed = ParsedBundleFromSpec(
            """
            {
              "meta": { "flowCode": "DEMO", "flowVersion": 1 },
              "flow": { "nodes": [
                { "id": "task_a", "type": "userTask" },
                { "id": "task_b", "type": "userTask" }
              ], "edges": [] },
              "userTasks": [
                { "id": "task_a" },
                { "id": "task_b" }
              ]
            }
            """,
            forms: new Dictionary<string, JsonElement>
            {
                ["task_a"] = JsonDocument.Parse("{}").RootElement.Clone(),
                // task_b missing → MISSING_FORM
            });

        var result = NewValidator().Validate(parsed);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "MISSING_FORM" && e.Message.Contains("task_b"));
        Assert.DoesNotContain(result.Errors, e =>
            e.Code == "MISSING_FORM" && e.Message.Contains("task_a"));
    }

    [Fact]
    public void TestCase_referencing_unknown_node_yields_UNKNOWN_NODE_IN_TRACE()
    {
        var parsed = ParsedBundleFromSpec(
            """
            {
              "meta": { "flowCode": "DEMO", "flowVersion": 1 },
              "flow": { "nodes": [
                { "id": "start_1", "type": "startEvent" },
                { "id": "end_1", "type": "endEvent" }
              ], "edges": [] },
              "userTasks": []
            }
            """,
            testCases: new[]
            {
                new TestCaseSnapshot(
                    Id: "tc_bad",
                    Name: "trace mentions a ghost node",
                    Inputs: JsonDocument.Parse("{}").RootElement.Clone(),
                    ExpectedTrace: new[] { "start_1", "ghost_node", "end_1" }),
            });

        var result = NewValidator().Validate(parsed);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "UNKNOWN_NODE_IN_TRACE" &&
            e.Message.Contains("ghost_node") &&
            e.Location.Contains("expectedTrace[1]"));
    }

    [Fact]
    public void RoleActorRef_with_no_assignee_yields_ROLE_NO_ASSIGNEE()
    {
        // Spec references role:VP but sample-org has no role assignments.
        var parsed = ParsedBundleFromSpec(
            """
            {
              "meta": { "flowCode": "DEMO", "flowVersion": 1 },
              "flow": { "nodes": [], "edges": [] },
              "userTasks": [],
              "approvals": [
                { "id": "vp", "approver": { "type": "role", "code": "VP" } }
              ]
            }
            """,
            sampleOrg: new SampleOrgSnapshot(
                Users: new List<UserSnapshot> { new(BundleTestFactory.AliceId, "alice@x", "A", null, null) },
                Departments: Array.Empty<DepartmentSnapshot>(),
                Groups: Array.Empty<GroupSnapshot>(),
                RoleAssignments: Array.Empty<RoleAssignmentSnapshot>())); // no VP assignee

        var result = NewValidator().Validate(parsed);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "ROLE_NO_ASSIGNEE" && e.Message.Contains("VP"));
    }

    [Fact]
    public void GroupActorRef_with_unknown_id_yields_GROUP_NOT_FOUND()
    {
        var parsed = ParsedBundleFromSpec(
            """
            {
              "meta": { "flowCode": "DEMO", "flowVersion": 1 },
              "flow": { "nodes": [], "edges": [] },
              "userTasks": [],
              "approvals": [
                { "id": "g1", "approver": { "type": "group", "code": "nope-not-a-group" } }
              ]
            }
            """);

        var result = NewValidator().Validate(parsed);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "GROUP_NOT_FOUND" && e.Message.Contains("nope-not-a-group"));
    }

    [Fact]
    public void UserActorRef_with_unknown_id_yields_USER_NOT_FOUND()
    {
        var ghost = Guid.NewGuid().ToString();
        var parsed = ParsedBundleFromSpec(
            $$"""
            {
              "meta": { "flowCode": "DEMO", "flowVersion": 1 },
              "flow": { "nodes": [], "edges": [] },
              "userTasks": [],
              "approvals": [
                { "id": "u1", "approver": { "type": "user", "id": "{{ghost}}" } }
              ]
            }
            """);

        var result = NewValidator().Validate(parsed);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e =>
            e.Code == "USER_NOT_FOUND" && e.Message.Contains(ghost));
    }

    // ---- helpers -----------------------------------------------------

    /// <summary>
    /// Build a synthetic <see cref="ParsedBundle"/> straight from a spec
    /// JSON literal — saves us from round-tripping through builder+parser
    /// in every validator test, and lets each test target one rule
    /// in isolation. The caller can override forms/sampleOrg/testCases
    /// to flip the rule under test on or off.
    /// </summary>
    private static ParsedBundle ParsedBundleFromSpec(
        string specJson,
        IReadOnlyDictionary<string, JsonElement>? forms = null,
        SampleOrgSnapshot? sampleOrg = null,
        IReadOnlyList<TestCaseSnapshot>? testCases = null)
    {
        var spec = JsonDocument.Parse(specJson).RootElement.Clone();
        var manifest = new BundleManifest(
            BundleSchemaVersion: BundleSchemaVersion.Current,
            FlowCode: "DEMO",
            FlowVersion: 1,
            ExportedAt: DateTimeOffset.UtcNow,
            SourceInstanceId: "test",
            Parent: null,
            Files: Array.Empty<BundleFileEntry>());

        return new ParsedBundle(
            Manifest: manifest,
            ManifestChecksum: new string('0', 64),
            SpecJson: spec,
            BpmnXml: string.Empty,
            SpecMd: string.Empty,
            Readme: null,
            Walkthrough: null,
            Changelog: null,
            Forms: forms ?? new Dictionary<string, JsonElement>(),
            Notifications: new Dictionary<string, JsonElement>(),
            Sla: null,
            Actors: null,
            SampleOrg: sampleOrg ?? BundleTestFactory.DefaultOrg(),
            TestCases: testCases ?? Array.Empty<TestCaseSnapshot>(),
            Assets: new Dictionary<string, byte[]>(),
            UnknownEntries: Array.Empty<string>());
    }
}
