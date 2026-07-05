using System.Net.Http.Json;
using Bpm.ChefAgent;

// One-shot poll: launchd / Task Scheduler wakes this every 5 minutes. It does
// one sweep of all enabled environments and exits, so a crash self-heals on
// the next tick and overlapping wake-ups are blocked by the file lock.
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Bpm.ChefAgent <config.json>");
    return 2;
}

AgentConfig config;
try { config = AgentConfig.Load(args[0]); }
catch (Exception ex) { Console.Error.WriteLine($"config load failed: {ex.Message}"); return 2; }

using var heldLock = SingleInstanceLock.TryAcquire(config.LockFilePath);
if (heldLock is null)
{
    Console.WriteLine("another chef-agent instance holds the lock — exiting.");
    return 0;
}

var now = DateTime.UtcNow;
var state = AgentState.Load(config.StateFilePath);
var tg = new TelegramNotifier(config.Telegram);
var worktrees = new WorktreeManager(config);
var cookRunner = new CookRunner(config, worktrees);
var envDownCooldown = TimeSpan.FromMinutes(60);

foreach (var env in config.EnabledEnvironments)
{
    using var api = new AdminApiClient(env);

    ChefTaskList tasks;
    try
    {
        tasks = await api.GetTasksAsync();
        state.EnvFailures[env.Name] = 0;   // reachable → reset failure streak
    }
    catch (Exception ex)
    {
        var fails = state.EnvFailures.TryGetValue(env.Name, out var n) ? n + 1 : 1;
        state.EnvFailures[env.Name] = fails;
        // Only alert after 3 consecutive misses, then at most hourly (Azure POC
        // is normally stopped — don't nag every 5 minutes).
        if (fails >= 3 && Cooldown.ShouldSend(now, Look(state.LastEnvAlertAt, env.Name), envDownCooldown))
        {
            await tg.SendAsync($"🔌 {env.Name} unreachable ({fails} polls): {ex.Message}");
            state.LastEnvAlertAt[env.Name] = now;
        }
        continue;
    }

    // Merge checks are cheap (git/gh, no LLM) — run them all every poll.
    var prManager = new PrManager(config, worktrees, tg, state, now);
    foreach (var task in tasks.ApprovedAwaitingMerge)
    {
        try { await prManager.ProcessAsync(env, api, task); }
        catch (Exception ex) { Console.Error.WriteLine($"[pr-fail] {task.FlowCode}: {ex.Message}"); }
    }

    // Publishing sweep: deploy main to this env's Azure resources for any flow
    // in Publishing. One deploy per poll (serialize migrations + the cold-start
    // window); single-flight is enforced inside PublishManager via AgentState.
    // Null-safe: an env whose admin-svc predates the Publishing work returns
    // no `publishing` field, so this list deserializes to null — don't crash.
    if ((tasks.Publishing?.Count ?? 0) > 0)
    {
        List<DeployEnvConfig> deployCfgs;
        try { deployCfgs = await api.GetDeployConfigAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[deploy-config-fail] {env.Name}: {ex.Message}"); deployCfgs = []; }

        var cfg = deployCfgs.FirstOrDefault(c => string.Equals(c.EnvName, env.Name, StringComparison.OrdinalIgnoreCase));
        if (cfg is { Enabled: true })
        {
            var publishManager = new PublishManager(config, tg, state, now);
            foreach (var task in tasks.Publishing)
            {
                try { await publishManager.ProcessAsync(env, cfg, api, task); }
                catch (Exception ex) { Console.Error.WriteLine($"[publish-fail] {task.FlowCode}: {ex.Message}"); }
                break;   // one deploy per poll
            }
        }
    }

    // Registry reconciliation: flows whose runtime code is deployed on this
    // env's bpm-svc but that the registry doesn't cover — they entered the
    // stack outside the AI Kitchen pipeline. Detection + notify ONLY (no
    // auto-register): a human decides whether a hand-merged flow should be
    // customer-visible, so a test/carrier flow can never auto-launch itself.
    try
    {
        List<DeployEnvConfig> reconCfgs;
        try { reconCfgs = await api.GetDeployConfigAsync(); } catch { reconCfgs = []; }
        var reconCfg = reconCfgs.FirstOrDefault(c => string.Equals(c.EnvName, env.Name, StringComparison.OrdinalIgnoreCase));
        if (reconCfg is { Enabled: true })
        {
            using var plainHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // /api/flow-codes is anonymous by design (admin-ui reaches it the same way).
            var deployed = await plainHttp.GetFromJsonAsync<List<DeployedFlowCode>>(
                $"https://{reconCfg.BpmSvcApp}.azurewebsites.net/api/flow-codes") ?? [];
            var registry = await api.GetRegistryCodesAsync();
            var missing = RegistryReconciler.MissingRegistrations(deployed, registry);
            var missingKey = string.Join(",", missing);
            var lastSet = state.LastUnregisteredSet.TryGetValue(env.Name, out var s) ? s : "";
            if (missing.Count > 0 &&
                (missingKey != lastSet ||
                 Cooldown.ShouldSend(now, Look(state.LastUnregisteredAlertAt, env.Name), TimeSpan.FromHours(6))))
            {
                await tg.SendAsync(
                    $"📋 {env.Name}: {missing.Count} deployed flow(s) missing from the registry — {string.Join("、", missing)}. " +
                    "Register via admin-ui「Register shipped」to make them live, or ignore if they are test carriers.");
                state.LastUnregisteredAlertAt[env.Name] = now;
            }
            state.LastUnregisteredSet[env.Name] = missingKey;
        }
    }
    catch (Exception ex) { Console.Error.WriteLine($"[recon-fail] {env.Name}: {ex.Message}"); }

    var plan = TaskPlanner.Plan(tasks);
    if (plan.CookTask is not { } cook) continue;

    // Fresh submission → claim it (Submitted→Cooking) before spending a session.
    if (!plan.IsResume && !await api.TryClaimAsync(cook.FlowId))
    {
        Console.WriteLine($"{cook.FlowCode}: claim lost (already moved) — skipping this poll.");
        continue;
    }

    await tg.SendAsync($"👨‍🍳 Cooking {cook.FlowCode} v{cook.Version} ({env.Name}){(plan.IsResume ? " — resume" : "")}…");
    // RunAsync loops sessions internally until the flow is Committed / OnHold /
    // gone, or it gives up after no progress — so the outcome here is terminal.
    var outcome = await cookRunner.RunAsync(env, api, cook, plan.IsResume);

    switch (outcome)
    {
        case CookOutcome.Committed:
            await tg.SendAsync($"🍽️ {cook.FlowCode} v{cook.Version} committed — review & Approve in admin-ui.");
            break;
        case CookOutcome.OnHold:
            await tg.SendAsync($"⏸️ {cook.FlowCode} v{cook.Version} is on hold — chef asked a question, your reply needed.");
            break;
        case CookOutcome.GaveUp:
            await tg.SendAsync($"🛑 {cook.FlowCode} v{cook.Version} cook stalled (no progress) — parked on hold for a human.");
            break;
        case CookOutcome.FlowGone:
            Console.WriteLine($"{cook.FlowCode}: flow vanished mid-cook (deleted).");
            break;
    }

    // One cook per poll, across all environments — keep migrations serial and
    // the single session honoured.
    break;
}

state.Save(config.StateFilePath);
return 0;

static DateTime? Look(Dictionary<string, DateTime> map, string key)
    => map.TryGetValue(key, out var v) ? v : null;
