using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class WorktreeAndCookTests
{
    [Fact]
    public void BranchName_carries_env_prefix_and_lowercases_code()
    {
        Assert.Equal("cook/local/leave-v1", WorktreeManager.BranchName("local", "LEAVE", 1));
        Assert.Equal("cook/azure-poc/ape-v2", WorktreeManager.BranchName("azure-poc", "APE", 2));
    }

    [Fact]
    public void Env_prefix_keeps_same_flow_distinct_across_environments()
    {
        Assert.NotEqual(
            WorktreeManager.BranchName("local", "LEAVE", 1),
            WorktreeManager.BranchName("azure-poc", "LEAVE", 1));
    }

    [Fact]
    public void BranchFromWorkContext_reads_branch_or_null()
    {
        Assert.Equal("cook/local/leave-v1",
            WorktreeManager.BranchFromWorkContext("""{"branch":"cook/local/leave-v1","notes":"x"}"""));
        Assert.Null(WorktreeManager.BranchFromWorkContext(null));
        Assert.Null(WorktreeManager.BranchFromWorkContext("not json"));
        Assert.Null(WorktreeManager.BranchFromWorkContext("""{"notes":"no branch key"}"""));
    }

    [Fact]
    public void Prompt_differs_for_resume_and_mentions_flow()
    {
        var task = new ChefTask(Guid.NewGuid(), "LEAVE", 1, "請假", "Submitted",
            DateTime.UtcNow, null, null, null);

        var fresh = CookRunner.BuildPrompt(task, isResume: false);
        var resume = CookRunner.BuildPrompt(task, isResume: true);

        Assert.Contains("LEAVE v1", fresh);
        Assert.Contains(task.FlowId.ToString(), fresh);
        Assert.Contains("from scratch", fresh);
        Assert.Contains("chef_get_messages", resume);     // resume must re-read the thread
        Assert.Contains("do NOT switch branches", fresh);
    }
}
