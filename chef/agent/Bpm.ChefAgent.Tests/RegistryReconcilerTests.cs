using Bpm.ChefAgent;
using Xunit;

namespace Bpm.ChefAgent.Tests;

public class RegistryReconcilerTests
{
    private static RegistryCodeRow Row(string code, int version, string state, DateTime? updated = null)
        => new(code, version, state, code, updated ?? DateTime.UtcNow);

    // ── VisibilityIssue ──────────────────────────────────────────────────────

    [Fact]
    public void Visibility_Ok_When_Published_Row_Wins()
    {
        var rows = new[] { Row("LEAVE", 1, "Published") };
        Assert.Null(RegistryReconciler.VisibilityIssue(rows, "LEAVE", 1));
    }

    [Fact]
    public void Visibility_Shadow_Row_Scenario_Same_Version_Published_Still_Wins()
    {
        // The 2026-07-05 incident: retired demo carrier + re-cooked Published
        // flow share (code, v1). Published must win regardless of row order.
        var older = Row("COMMITTEE_REVIEW", 1, "Retired", DateTime.UtcNow.AddHours(-2));
        var live = Row("COMMITTEE_REVIEW", 1, "Published", DateTime.UtcNow);
        Assert.Null(RegistryReconciler.VisibilityIssue(new[] { older, live }, "COMMITTEE_REVIEW", 1));
        Assert.Null(RegistryReconciler.VisibilityIssue(new[] { live, older }, "COMMITTEE_REVIEW", 1));
    }

    [Fact]
    public void Visibility_Fails_When_Winner_Is_Not_Published()
    {
        // A newer Draft/Retired version shadows the published one — the launcher
        // resolves to the higher version, which is invisible.
        var rows = new[] { Row("WFH", 6, "Published"), Row("WFH", 7, "Retired") };
        var issue = RegistryReconciler.VisibilityIssue(rows, "WFH", 6);
        Assert.NotNull(issue);
        Assert.Contains("v7", issue);
    }

    [Fact]
    public void Visibility_Fails_When_No_Row_Exists()
    {
        Assert.NotNull(RegistryReconciler.VisibilityIssue(Array.Empty<RegistryCodeRow>(), "LEAVE", 1));
    }

    [Fact]
    public void Visibility_Fails_When_Another_Version_Wins()
    {
        var rows = new[] { Row("WFH", 6, "Published"), Row("WFH", 7, "Published") };
        var issue = RegistryReconciler.VisibilityIssue(rows, "WFH", 6);
        Assert.NotNull(issue);
        Assert.Contains("v7", issue);
    }

    // ── MissingRegistrations ─────────────────────────────────────────────────

    [Fact]
    public void Missing_Reports_Deployed_Code_With_No_Registry_Row()
    {
        var deployed = new[] { new DeployedFlowCode("CONTRACT_REVIEW", "合約審查", 1) };
        var missing = RegistryReconciler.MissingRegistrations(deployed, Array.Empty<RegistryCodeRow>());
        Assert.Equal(new[] { "CONTRACT_REVIEW v1" }, missing);
    }

    [Fact]
    public void Missing_Reports_Deployed_Version_Above_Registered_Max()
    {
        var deployed = new[] { new DeployedFlowCode("WFH", "WFH", 7) };
        var registry = new[] { Row("WFH", 6, "Published") };
        Assert.Equal(new[] { "WFH v7" }, RegistryReconciler.MissingRegistrations(deployed, registry));
    }

    [Fact]
    public void Missing_Empty_When_Registry_Covers_Deployed_Version_In_Any_State()
    {
        // Any row (even Retired/Draft) at ≥ deployed version means the registry
        // KNOWS about the flow — visibility is a human/state decision, not a
        // missing registration.
        var deployed = new[] { new DeployedFlowCode("LEAVE", "請假申請", 1) };
        var registry = new[] { Row("LEAVE", 1, "Retired") };
        Assert.Empty(RegistryReconciler.MissingRegistrations(deployed, registry));
    }
}
