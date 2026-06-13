using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class ConfigAndLockTests
{
    [Fact]
    public void Load_parses_example_shaped_config()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
        {
          "environments": [
            { "name": "local", "baseUrl": "http://localhost:5266", "chefToken": "dev-chef-token", "enabled": true },
            { "name": "azure-poc", "baseUrl": "https://x", "chefToken": "t", "enabled": false }
          ],
          "telegram": { "botToken": "b", "chatId": "c" },
          "repoPath": "/repo", "worktreeRoot": "/wt", "claudeBin": "claude",
          "maxTurns": 300, "maxSessionMinutes": 45, "maxCookSessions": 8, "maxNoProgressSessions": 2,
          "lockFilePath": "/wt/agent.lock", "stateFilePath": "/wt/state.json"
        }
        """);

        var cfg = AgentConfig.Load(path);

        Assert.Equal(2, cfg.Environments.Count);
        Assert.Single(cfg.EnabledEnvironments);
        Assert.Equal("local", cfg.EnabledEnvironments.Single().Name);
        Assert.Equal(300, cfg.MaxTurns);
        Assert.Equal(8, cfg.MaxCookSessions);
        Assert.Equal(2, cfg.MaxNoProgressSessions);
        Assert.Equal("c", cfg.Telegram!.ChatId);
        File.Delete(path);
    }

    [Fact]
    public void Load_throws_on_missing_file()
    {
        Assert.ThrowsAny<Exception>(() => AgentConfig.Load("/no/such/config.json"));
    }

    [Fact]
    public void Lock_is_exclusive_then_releases()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chef-agent-test-{Guid.NewGuid():N}.lock");

        var first = SingleInstanceLock.TryAcquire(path);
        Assert.NotNull(first);

        var second = SingleInstanceLock.TryAcquire(path);
        Assert.Null(second);   // already held → second caller backs off

        first!.Dispose();

        var third = SingleInstanceLock.TryAcquire(path);
        Assert.NotNull(third);   // released → acquirable again
        third!.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void State_roundtrips_and_tolerates_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chef-agent-state-{Guid.NewGuid():N}.json");

        var s = new AgentState();
        s.EnvFailures["azure"] = 3;
        s.Save(path);

        var loaded = AgentState.Load(path);
        Assert.Equal(3, loaded.EnvFailures["azure"]);

        File.WriteAllText(path, "{ not json");
        var fromGarbage = AgentState.Load(path);   // never throws
        Assert.Empty(fromGarbage.EnvFailures);

        File.Delete(path);
    }
}
