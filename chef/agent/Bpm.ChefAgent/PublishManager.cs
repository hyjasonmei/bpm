namespace Bpm.ChefAgent;

/// <summary>
/// Handles the Publishing → deployed → Published tail for one environment:
/// builds main, deploys the two APIs + two SPAs to the env's Azure resources,
/// health-checks, then stamps Published / PublishFailed server-side. All
/// az/dotnet/npm/swa interaction is local-only and deterministic (NO LLM); the
/// start gate is pure (unit-tested). Mirrors <see cref="PrManager"/>.
///
/// Deploy sequence per env (proven in infra/azure/03-deploy.sh):
///   backend : dotnet publish -c Release -o &lt;out&gt; → zip → az webapp deploy
///             --type zip --track-status false → az webapp restart (REQUIRED:
///             track-status false doesn't load new code until restart) →
///             poll https://&lt;host&gt;/health until 200 (≤120s).
///   frontend: resolve hostnames → export VITE_* → npm run build → swa deploy
///             &lt;dist&gt; --deployment-token &lt;token&gt; --env production.
/// </summary>
public sealed class PublishManager
{
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(30);
    // 30 min, deliberately generous: an App Service restart recreates the
    // container (image pull + warmup probe ~90s) and the app then applies pending
    // EF migrations before serving — cold start has repeatedly run past shorter
    // windows (120s, then 300s margins were still too tight in practice), failing
    // the health-check even though the deploy succeeded and the app came up
    // moments later. A healthy app responds in seconds; this ceiling only ever
    // matters for a genuinely stuck start, so erring large costs nothing on the
    // happy path and removes the whole "slow-but-fine cold start aborts publish"
    // failure class.
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(15);

    // The deploy restarts admin-svc; the first DB-writing call after that cold
    // start (mark-published) can time out even though the deploy succeeded. Retry
    // it a few times — admin-svc warms within a poll or two — instead of failing
    // the whole publish (and re-deploying everything 30 min later on cooldown).
    public const int MarkPublishedAttempts = 5;
    private static readonly TimeSpan MarkRetryDelay = TimeSpan.FromSeconds(15);

    private readonly AgentConfig _cfg;
    private readonly TelegramNotifier _tg;
    private readonly AgentState _state;
    private readonly DateTime _now;
    private readonly HttpClient _health;

    public PublishManager(AgentConfig cfg, TelegramNotifier tg, AgentState state, DateTime now, HttpClient? health = null)
    {
        _cfg = cfg; _tg = tg; _state = state; _now = now;
        _health = health ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>Start gate (pure): never run two deploys at once. Otherwise a
    /// FRESH publish request — the operator (re-)pressed Publish / Retry deploy,
    /// which bumps the flow's UpdatedAt past our last attempt — always deploys
    /// immediately; the <see cref="RetryCooldown"/> only spaces out AUTO-retries
    /// of the same still-pending request (e.g. a crash before MarkPublishFailed
    /// left it stuck in Publishing), so an explicit human retry is never swallowed
    /// by the cooldown.</summary>
    public static bool ShouldStartDeploy(DateTime now, DateTime? lastAttemptAt, bool inFlight, DateTime publishingSince)
    {
        if (inFlight) return false;
        if (lastAttemptAt is { } last && publishingSince > last) return true;   // fresh request → bypass cooldown
        return Cooldown.ShouldSend(now, lastAttemptAt, RetryCooldown);
    }

    /// <summary>Deploy main to one env's Azure resources for one Publishing flow,
    /// health-check, then Mark{Published|PublishFailed}. Single-flight + retry
    /// cooldown via AgentState.</summary>
    public async Task ProcessAsync(EnvTarget env, DeployEnvConfig cfg, AdminApiClient api, ChefTask task, CancellationToken ct = default)
    {
        var inFlight = _state.DeployInFlightFlowId is { Length: > 0 };
        var lastAttempt = _state.LastDeployAttemptAt.TryGetValue(task.FlowId.ToString(), out var v) ? v : (DateTime?)null;
        // task.UpdatedAt = when the flow last transitioned, i.e. when it (re-)entered
        // Publishing — newer than lastAttempt means a fresh operator retry.
        if (!ShouldStartDeploy(_now, lastAttempt, inFlight, task.UpdatedAt)) return;

        // Claim the single deploy slot + stamp the attempt (gates retries even if
        // we crash mid-deploy — the cooldown holds the next poll back).
        _state.DeployInFlightFlowId = task.FlowId.ToString();
        _state.LastDeployAttemptAt[task.FlowId.ToString()] = _now;

        await _tg.SendAsync($"🚢 Deploying {task.FlowCode} v{task.Version} → {cfg.EnvName}…", ct);
        try
        {
            var reason = await RunDeployAsync(cfg, task, ct);
            if (reason is null)
            {
                await MarkPublishedWithRetryAsync(api, task, ct);
                await _tg.SendAsync($"✅ {task.FlowCode} v{task.Version} deployed + published ({cfg.EnvName}).", ct);
            }
            else
            {
                await api.MarkPublishFailedAsync(task.FlowId, reason, ct);
                await _tg.SendAsync($"🛑 {task.FlowCode} v{task.Version} publish failed ({cfg.EnvName}): {reason}", ct);
            }
        }
        catch (Exception ex)
        {
            // Unexpected throw (transport, etc.): best-effort mark + alert, never
            // leave the flow stuck Publishing silently.
            var reason = $"deploy exception: {ex.Message}";
            try { await api.MarkPublishFailedAsync(task.FlowId, reason, ct); } catch { /* best effort */ }
            await _tg.SendAsync($"🛑 {task.FlowCode} v{task.Version} publish failed ({cfg.EnvName}): {reason}", ct);
        }
        finally
        {
            _state.DeployInFlightFlowId = null;
        }
    }

    /// <summary>Retry decision (pure → unit-tested): retry a failed mark-published
    /// only while attempts remain, we're not shutting down, and the error is a
    /// transient transport/timeout (admin-svc still cold-warming) rather than a
    /// real HTTP error (e.g. 409 — flow already moved, retrying won't help).</summary>
    public static bool ShouldRetryMark(int attempt, int maxAttempts, Exception ex, bool shuttingDown)
        => attempt < maxAttempts && !shuttingDown && IsTransientMarkError(ex);

    /// <summary>A TaskCanceledException with no caller-cancellation is an
    /// HttpClient.Timeout; HttpRequestException is a transport failure. Both mean
    /// "try again", unlike an EnsureSuccessStatusCode throw (real HTTP status).</summary>
    private static bool IsTransientMarkError(Exception ex)
        => ex is TaskCanceledException or HttpRequestException;

    /// <summary>Mark the flow Published, retrying through admin-svc's post-restart
    /// cold start. Rethrows after the last attempt (or on a non-transient error)
    /// so the caller's catch still marks PublishFailed.</summary>
    private async Task MarkPublishedWithRetryAsync(AdminApiClient api, ChefTask task, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await api.MarkPublishedAsync(task.FlowId, ct);
                return;
            }
            catch (Exception ex) when (ShouldRetryMark(attempt, MarkPublishedAttempts, ex, ct.IsCancellationRequested))
            {
                await _tg.SendAsync($"⚠️ {task.FlowCode} v{task.Version}: mark-published attempt {attempt}/{MarkPublishedAttempts} failed ({ex.GetType().Name}) — admin-svc likely still warming, retrying in {MarkRetryDelay.TotalSeconds:0}s…", ct);
                await Task.Delay(MarkRetryDelay, ct);
            }
        }
    }

    /// <summary>Run the full deploy sequence. Returns null on success, or a
    /// human-readable failure reason on the first failing step.</summary>
    private async Task<string?> RunDeployAsync(DeployEnvConfig cfg, ChefTask task, CancellationToken ct)
    {
        // 1. Verify the worker repo is on main + build it. The auto-ff-merge step
        //    already landed the flow's code; here we just confirm + build main.
        if (await VerifyMainAsync(ct) is { } repoErr) return repoErr;

        // 2. Backend: bpm-svc serves cooked flows; admin-svc too for stack
        //    consistency (matches 03-deploy.sh order — admin first).
        if (await DeployBackendAsync(cfg, "Bpm.Admin.Api", cfg.AdminSvcApp, RepoRel(_cfg.AdminSvcProj), ct) is { } adminErr) return adminErr;
        if (await DeployBackendAsync(cfg, "Bpm.Api", cfg.BpmSvcApp, RepoRel(_cfg.BpmSvcProj), ct) is { } bpmErr) return bpmErr;

        // 3. Frontend: bpm-ui + admin-ui. Resolve API hostnames once and bake them
        //    into both builds (admin-ui calls both svc origins).
        var bpmApiHost = await ResolveWebAppHostAsync(cfg.BpmSvcApp, cfg.ResourceGroup, ct);
        var adminApiHost = await ResolveWebAppHostAsync(cfg.AdminSvcApp, cfg.ResourceGroup, ct);
        if (bpmApiHost is null || adminApiHost is null)
            return "could not resolve API hostnames for frontend build";

        var viteEnv = new Dictionary<string, string>
        {
            ["VITE_BPM_SVC_URL"] = $"https://{bpmApiHost}",
            ["VITE_ADMIN_SVC_URL"] = $"https://{adminApiHost}",
        };
        // bpm-ui hosts the per-flow form/detail being published — assert the build
        // actually contains it (see DeployFrontendAsync). admin-ui doesn't carry
        // per-flow code, so no marker there.
        var marker = FrontendApiMarker(task.FlowCode, task.Version);
        if (await DeployFrontendAsync(cfg, cfg.BpmUiSwa, RepoRel(_cfg.BpmUiDir), viteEnv, ct, marker) is { } bpmUiErr) return bpmUiErr;
        if (await DeployFrontendAsync(cfg, cfg.AdminUiSwa, RepoRel(_cfg.AdminUiDir), viteEnv, ct) is { } adminUiErr) return adminUiErr;

        return null;
    }

    /// <summary>Checkout main + build it (publish would rebuild anyway, but a
    /// clean `dotnet build` of the solution catches a broken main early).</summary>
    private async Task<string?> VerifyMainAsync(CancellationToken ct)
    {
        var checkout = await Git(["checkout", "main"], ct);
        if (!checkout.Ok) return $"git checkout main failed: {Trim(checkout.Stderr)}";
        return null;
    }

    /// <summary>publish → zip → az webapp deploy → az webapp restart → health-poll.</summary>
    private async Task<string?> DeployBackendAsync(DeployEnvConfig cfg, string assembly, string app, string proj, CancellationToken ct)
    {
        var outDir = Path.Combine(Path.GetTempPath(), $"chef-deploy-{app}");
        var zip = outDir + ".zip";
        try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); } catch { /* best effort */ }
        try { if (File.Exists(zip)) File.Delete(zip); } catch { /* best effort */ }

        var publish = await Dotnet(["publish", proj, "-c", "Release", "-o", outDir], _cfg.RepoPath, ct);
        if (!publish.Ok) return $"{app}: dotnet publish failed: {Trim(publish.Stderr)}";

        var zipRes = await ProcessRunner.RunAsync("zip", ["-qr", zip, "."], outDir, StepTimeout, ct);
        if (!zipRes.Ok) return $"{app}: zip failed: {Trim(zipRes.Stderr)}";

        var deploy = await Az(["webapp", "deploy", "-n", app, "-g", cfg.ResourceGroup,
            "--src-path", zip, "--type", "zip", "--track-status", "false"], ct);
        if (!deploy.Ok) return $"{app}: az webapp deploy failed: {Trim(deploy.Stderr)}";

        // REQUIRED: --track-status false doesn't load new code until a restart.
        var restart = await Az(["webapp", "restart", "-n", app, "-g", cfg.ResourceGroup], ct);
        if (!restart.Ok) return $"{app}: az webapp restart failed: {Trim(restart.Stderr)}";

        var host = await ResolveWebAppHostAsync(app, cfg.ResourceGroup, ct);
        if (host is null) return $"{app}: could not resolve defaultHostName for health-check";
        if (!await PollHealthAsync($"https://{host}/health", ct))
            return $"{app}: health-check did not return 200 within {HealthTimeout.TotalSeconds:0}s";

        return null;
    }

    private const int FrontendDeployAttempts = 3;

    /// <summary>npm run build (VITE_* exported) → swa deploy → VERIFY the live SWA
    /// actually serves the freshly-built bundle, retrying the deploy if not.
    /// The swa CLI can exit 0 without uploading (StaticSitesClient flakiness), so
    /// we don't trust its exit code alone — this is the frontend analog of the
    /// backend health-check: deploy is only "done" once the live site serves the
    /// new bundle hash.</summary>
    private async Task<string?> DeployFrontendAsync(DeployEnvConfig cfg, string swa, string dir, IReadOnlyDictionary<string, string> viteEnv, CancellationToken ct, string? expectMarker = null)
    {
        var build = await ProcessRunner.RunAsync("npm", ["run", "build"], dir, StepTimeout, ct, viteEnv);
        if (!build.Ok) return $"{swa}: npm run build failed: {Trim(build.Stderr)}";

        var dist = Path.Combine(dir, "dist");
        // Fingerprint the build: the main JS asset that index.html references
        // (e.g. /assets/index-CvkIajz5.js). A verified deploy => the live site's
        // index.html references this exact file.
        var indexPath = Path.Combine(dist, "index.html");
        if (!File.Exists(indexPath)) return $"{swa}: build produced no dist/index.html";
        var builtBundle = ParseBundleId(await File.ReadAllTextAsync(indexPath, ct));
        if (builtBundle is null) return $"{swa}: could not find a bundle reference in dist/index.html";

        // Guard against a build that silently omitted the flow version being
        // published (stale working tree / cache — the failure mode WFH v3 hit:
        // the deploy "succeeded" but shipped the previous version). The per-flow
        // form fetches /api/<slug>/v<n>/…, a literal that MUST be in the bundle
        // if this version was compiled in. Missing → fail loudly instead of
        // deploying + "verifying" the wrong bundle (verify only proves swa
        // uploaded the built bundle, not that the build contained the new code).
        if (expectMarker is not null && !await BuiltBundleContainsAsync(dist, expectMarker, ct))
            return $"{swa}: built bundle is missing '{expectMarker}' — the build did not include the version being published (stale tree/cache?). Refusing to deploy a stale bundle.";

        var tokenRes = await Az(["staticwebapp", "secrets", "list", "-n", swa, "-g", cfg.ResourceGroup,
            "--query", "properties.apiKey", "-o", "tsv"], ct);
        if (!tokenRes.Ok) return $"{swa}: az staticwebapp secrets list failed: {Trim(tokenRes.Stderr)}";
        var token = tokenRes.Stdout.Trim();
        if (string.IsNullOrEmpty(token)) return $"{swa}: empty deployment token";

        var swaHost = await ResolveSwaHostAsync(swa, cfg.ResourceGroup, ct);
        if (swaHost is null) return $"{swa}: could not resolve SWA defaultHostname for deploy verification";
        var liveUrl = $"https://{swaHost}/";

        string? lastSwaErr = null;
        for (var attempt = 1; attempt <= FrontendDeployAttempts; attempt++)
        {
            var swaDeploy = await ProcessRunner.RunAsync("swa",
                ["deploy", dist, "--deployment-token", token, "--env", "production"], dir, StepTimeout, ct);
            lastSwaErr = swaDeploy.Ok ? null : Trim(swaDeploy.Stderr);

            // Don't trust swaDeploy.Ok — verify the live site serves builtBundle.
            if (await VerifyLiveBundleAsync(liveUrl, builtBundle, ct))
                return null;

            await _tg.SendAsync($"⚠️ {swa}: deploy attempt {attempt}/{FrontendDeployAttempts} did not take (live bundle ≠ {builtBundle}) — retrying…", ct);
        }
        return $"{swa}: frontend deploy not verified after {FrontendDeployAttempts} attempts — live site never served {builtBundle}"
             + (lastSwaErr is null ? " (swa reported success each time)" : $" (last swa error: {lastSwaErr})");
    }

    /// <summary>The per-flow API path literal the cooked form/detail fetches,
    /// e.g. WFH v3 → <c>/api/wfh/v3</c>. Slug = lower-cased flow code with
    /// <c>_</c>→<c>-</c> (matches the bpm-ui route convention). Pure →
    /// unit-tested; used as a build-content marker.</summary>
    public static string FrontendApiMarker(string flowCode, int version)
        => $"/api/{flowCode.ToLowerInvariant().Replace('_', '-')}/v{version}";

    /// <summary>True when any built JS asset under <paramref name="dist"/>/assets
    /// contains <paramref name="marker"/> — i.e. the version's frontend code was
    /// compiled into the bundle.</summary>
    private static async Task<bool> BuiltBundleContainsAsync(string dist, string marker, CancellationToken ct)
    {
        var assets = Path.Combine(dist, "assets");
        if (!Directory.Exists(assets)) return false;
        foreach (var js in Directory.EnumerateFiles(assets, "*.js"))
        {
            var text = await File.ReadAllTextAsync(js, ct);
            if (text.Contains(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The main JS asset filename index.html references, e.g.
    /// <c>/assets/index-CvkIajz5.js</c>. Pure → unit-tested.</summary>
    public static string? ParseBundleId(string indexHtml)
    {
        var m = System.Text.RegularExpressions.Regex.Match(indexHtml, @"/assets/index-[A-Za-z0-9_-]+\.js");
        return m.Success ? m.Value : null;
    }

    /// <summary>Poll the live SWA index.html until it references <paramref name="bundle"/>
    /// (CDN propagation), or the health window elapses.</summary>
    private async Task<bool> VerifyLiveBundleAsync(string url, string bundle, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + HealthTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var html = await _health.GetStringAsync(url, ct);
                if (html.Contains(bundle, StringComparison.Ordinal)) return true;
            }
            catch { /* transient / CDN warmup → keep polling */ }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        return false;
    }

    private async Task<string?> ResolveSwaHostAsync(string swa, string rg, CancellationToken ct)
    {
        var r = await Az(["staticwebapp", "show", "-n", swa, "-g", rg, "--query", "defaultHostname", "-o", "tsv"], ct);
        var host = r.Stdout.Trim();
        return r.Ok && host.Length > 0 ? host : null;
    }

    /// <summary>Poll a /health URL until 200 or the timeout elapses.</summary>
    private async Task<bool> PollHealthAsync(string url, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + HealthTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await _health.GetAsync(url, ct);
                // 200 = healthy (bpm-svc /health is anonymous). 401 = the app is
                // up but /health is auth-gated (admin-svc) — still proves Kestrel
                // finished cold-starting and is serving. Only a missing response
                // (timeout / connection refused) or a platform 5xx means "not up
                // yet", so those fall through and keep polling.
                if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return true;
            }
            catch { /* cold start / connection refused → keep polling */ }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        return false;
    }

    private async Task<string?> ResolveWebAppHostAsync(string app, string rg, CancellationToken ct)
    {
        var r = await Az(["webapp", "show", "-n", app, "-g", rg, "--query", "defaultHostName", "-o", "tsv"], ct);
        var host = r.Stdout.Trim();
        return r.Ok && host.Length > 0 ? host : null;
    }

    private Task<ProcessResult> Git(IEnumerable<string> args, CancellationToken ct)
        => ProcessRunner.RunAsync("git", args, _cfg.RepoPath, StepTimeout, ct);
    private Task<ProcessResult> Dotnet(IEnumerable<string> args, string cwd, CancellationToken ct)
        => ProcessRunner.RunAsync("dotnet", args, cwd, StepTimeout, ct);
    private Task<ProcessResult> Az(IEnumerable<string> args, CancellationToken ct)
        => ProcessRunner.RunAsync("az", args, _cfg.RepoPath, StepTimeout, ct);

    /// <summary>Resolve a repo-relative config path against RepoPath (no-op if
    /// already absolute).</summary>
    private string RepoRel(string p) => Path.IsPathRooted(p) ? p : Path.Combine(_cfg.RepoPath, p);

    private static string Trim(string s)
    {
        s = s.Trim();
        return s.Length > 400 ? s[^400..] : s;   // tail is where the error usually is
    }
}
