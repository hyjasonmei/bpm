# Chef 自動化 Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 這台 Mac 變成 chef worker：每 5 分鐘輪詢所有啟用環境的待辦（Submitted / OnHold+user回覆 / Approved 未merge），自動開乾淨 Claude session cook、卡住轉 OnHold 留言、完成轉 Committed；Approve 後開 PR、偵測 merge 後才開放 Publish。

**Architecture:** 三層 — ① admin-svc 加 chef tasks 查詢 endpoint + PR/merge 追蹤欄位 + Publish gating；② `Bpm.ChefAgent`（.NET 10 console，跨平台，未來 Windows chef 直接沿用）one-shot 輪詢器，由 launchd（mac）/ Task Scheduler（Windows）每 5 分鐘喚起，file-lock 保證全域單 session；③ chef Claude session 沿用既有 chef-token MCP 工具，但 chef skill / 測試床先對齊 Clean Arch 分層。

**Tech Stack:** .NET 10（admin-svc + agent）、EF Core/Postgres、xUnit、React/Vite（admin-ui）、claude CLI headless（`-p`）、gh CLI、launchd / Windows Task Scheduler。

---

## 已驗證的現況（2026-06-13 盤點，執行者不必重查）

| 事實 | 出處 |
|---|---|
| chef 狀態機：Accept（Submitted/OnHold→Cooking）、Resume（OnHold→Cooking）、OnHold（強制 question）、Commit（Cooking→Committed） | `FlowLifecycleService.cs` `ChefAcceptAsync` 等（~L414） |
| user 在 Committed/Approved 後可 Open issue → `ReopenForIssueAsync` 打回 OnHold，訊息 Kind=Issue | `FlowLifecycleService.cs` ~L474、`CookPanel.tsx` L110-118 |
| ⇒ task「OnHold+user回覆」與「issue」是同一條 query：state=OnHold 且最後一則非 System 訊息 Sender=User | 設計結論 |
| chef API 每個 call bump heartbeat；30 分無心跳 UI 顯示 stalled；admin 可 stall-reset 回 Submitted | `ChefFlowsController.cs`、`ChefStallResetAsync` |
| spec submit 後鎖死（UpdateSpecAsync 只允許 Draft）→ cook 中無 spec drift | `FlowLifecycleService.cs` L228-232 |
| azure chef token 已在 Key Vault（`02-configure.sh` L45-46、`Bpm__Chef__Token` KV ref）；本機 `az` 已登入 | `infra/azure/02-configure.sh` |
| bundle 在 admin-ui Submit 時 build + cache（BundleBlob）；chef MCP `chef_download_bundle` 拉 cache | `FlowsController.cs` L296 |
| MCP 工具：chef_get_flow / chef_get_messages / chef_post_message / chef_transition / chef_download_bundle / chef_set_worktree。**沒有列表工具** | `ChefMcpTools.cs` |
| Publish 是 admin JWT 權限（`PublishAsync`：Approved→Published）；chef transition 不含 Publish | `FlowLifecycleService.cs` ~L405 |
| `ITypedInboxProvider` assembly scan 目前只掃 Persistence assembly | `bpm-svc/src/Persistence/DependencyInjection.cs` L191-203 |
| git remote 只有 ssh（`git@github.com:acme/bpm.git`）；這台 Claude Bash 不能 ssh push；gh CLI **未安裝** | 實測 |
| `.gitattributes` 已加（鎖 LF）— item 0 已完成 | repo root |
| named Mutex 在 macOS/Linux 會丟 PlatformNotSupportedException → 跨平台鎖必須用 exclusive file lock | .NET 行為 |
| launchd `StartInterval` 不會喚醒睡眠中的 Mac → 防睡眠是 ops 設定 | macOS 行為 |

**全域政策（寫進 agent，不可省）**
- 同時最多 **1 個** chef session（全域 file lock）。
- 任務優先序：Stalled（可重試的斷頭 cook）> OnHold+user回覆 > Submitted；Approved merge 檢查便宜，每輪全跑。
- 環境不可達：skip + 連續失敗計數，連續失敗 ≥ 3 次才 TG 通知一次，之後 60 分鐘冷卻（azure 常態停機，不能每 5 分鐘吵）。
- stalled 自動重撈最多 1 次（agent 端記次數），第二次改 TG 叫人。
- chef session 上限：`--max-turns` + wall-clock timeout，超時殺進程 → 留 stalled 給下一輪政策處理。
- PR 制：chef 永不直接 merge main。merge 由 開發者 在 GitHub / GitKraken 做。
- Commit 規範：本 repo push 由 開發者 用 GitKraken 處理，計畫內所有「Commit」步驟只 commit 不 push。

---

## Sub-project A — Clean Arch 對齊（chef skill + testbed）

> 前置條件：A 沒完成前，**不開放 agent 自動 cook**（B/C 可並行開發，但 D 必須在 A 之後）。
> 原因：chef skill 現在教 chef 把 entity / state machine / inbox provider 全寫進
> `Persistence/Features/`，自動化會把錯分層的 code 連續灌進 PR。

### Task A1: Application 層接手 ITypedInboxProvider scan（main，lead 範圍）

**Files:**
- Modify: `bpm-svc/src/Application/DependencyInjection.cs`
- Modify: `bpm-svc/src/Persistence/DependencyInjection.cs:191-203`
- Test: `bpm-svc/tests/Bpm.Tests/`（既有 inbox integration test 必須照常綠）

- [ ] **Step 1: 調查現有 provider 分布**

```bash
grep -rln "ITypedInboxProvider" bpm-svc/src --include=*.cs
```
記下哪些 impl 在 Persistence assembly（shipped flows）— 過渡期兩個 assembly 都要掃。

- [ ] **Step 2: Application/DependencyInjection.cs 加掃描（掃 Application assembly）**

```csharp
// Unified inbox: chef-cooked flows register their ITypedInboxProvider
// from Application/Features/<CODE>/V<N>/ (Clean Arch home). The
// Persistence-side scan stays for legacy shipped flows until they are
// migrated; both registrations are additive.
foreach (var providerType in typeof(DependencyInjection).Assembly
    .GetTypes()
    .Where(t => !t.IsAbstract
                && !t.IsInterface
                && typeof(ITypedInboxProvider).IsAssignableFrom(t)))
{
    services.AddScoped(typeof(ITypedInboxProvider), providerType);
}
```
（`typeof(DependencyInjection)` 用 Application 層自己的 DI class；確認該檔案實際 class 名後替換。）

- [ ] **Step 3: Persistence 掃描區塊加註解標 legacy**

在 `Persistence/DependencyInjection.cs` L191 的註解前補：
```csharp
// LEGACY: shipped flows cooked before the Clean Arch alignment keep
// their providers in this assembly. New cooks register via the
// Application-side scan. Remove this block once shipped flows migrate.
```

- [ ] **Step 4: 跑 bpm-svc 測試**

```bash
cd bpm-svc && dotnet test
```
Expected: 全綠（沒有行為變化 — 只是多一個 assembly 掃描來源）。

- [ ] **Step 5: Commit**

```bash
git add bpm-svc/src/Application/DependencyInjection.cs bpm-svc/src/Persistence/DependencyInjection.cs
git commit -m "refactor(bpm-svc): inbox provider scan moves to Application layer (Persistence scan kept for legacy shipped flows)"
```

### Task A2: chef skill 文件對齊五層分層（main，lead 範圍）

**Files:**
- Modify: `chef/skill/conventions.md`（路徑邊界表）
- Modify: `chef/skill/workflow.md`（migration / 檔案放置步驟）
- Modify: `chef/skill/SKILL.md`（如有提到 Persistence/Features 的段落）

- [ ] **Step 1: 找出所有教錯位置的段落**

```bash
grep -n "Persistence/Features" chef/skill/SKILL.md chef/skill/conventions.md chef/skill/workflow.md
```

- [ ] **Step 2: 改寫成根 CLAUDE.md「chef 的 per-flow 寫入範圍」那張表的分層**

權威對照（直接照抄根 CLAUDE.md，已是正確版）：

| 層 | 路徑 | 放什麼 |
|---|---|---|
| Domain | `bpm-svc/src/Domain/Features/<CODE>/V<N>/**` | entity、enum、value object（無依賴） |
| Application | `bpm-svc/src/Application/Features/<CODE>/V<N>/**` | state machine service、notification templates、`ITypedInboxProvider` impl、actor 解析 |
| Persistence | `bpm-svc/src/Persistence/Features/<CODE>/V<N>/**` | **只有** EF mapping（`*Configuration`） |
| Migrations | `bpm-svc/src/Persistence/Migrations/` | `dotnet ef migrations add` 產物 |
| Api | `bpm-svc/src/Api/Features/<CODE>/V<N>/**` | controller + DTO |
| Tests | `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/**` | unit + integration |
| UI | `bpm-ui/src/features/<CODE>/V<N>/**` | form、case-detail、manifest.ts、bpmn.xml |

並在 conventions.md 明寫：「inbox provider 放 Application；DI 由 Application assembly scan 自動接，chef 不碰 DependencyInjection.cs」。

- [ ] **Step 3: workflow.md 的 `lsof` 段改跨平台寫法**（為未來 Windows chef）

```bash
# was: lsof -ti :5290 :5173 | xargs -r kill
# becomes (works in Git Bash on Windows too):
npx kill-port 5290 5173 2>/dev/null || true
```
（若不想引 npx 依賴，保留 lsof 但加 Windows 註記分支 `netstat -ano | findstr :5290` + `taskkill`。擇一，文件講清楚即可。）

- [ ] **Step 4: Commit**

```bash
git add chef/skill/
git commit -m "docs(chef): align skill layer map with Clean Architecture; inbox provider lives in Application"
```

### Task A3: LEAVE testbed 對齊（leave-test-6 分支，mutable）

> 規則（不可違反）：skill 編輯 land main；testbed branch 可 reset 重跑；
> **永不 wholesale merge** testbed 回 main。

- [ ] **Step 1: 從 main 重切 testbed**

```bash
git checkout main
git checkout -B leave-test-6
```

- [ ] **Step 2: 從 leave-test-5 把 LEAVE V1 檔案搬進正確層**

```bash
git checkout leave-test-5 -- bpm-svc/src/Persistence/Features/LEAVE/ bpm-ui/src/features/LEAVE/ bpm-svc/tests/Bpm.Tests/Features/LEAVE/ 2>/dev/null || true
git status --short   # 看 leave-test-5 實際有哪些 LEAVE 檔（路徑可能略不同，以實際為準）
```
然後逐檔移位：entity/enum → `Domain/Features/LEAVE/V1/`；state machine + inbox provider + notification → `Application/Features/LEAVE/V1/`；只留 `LEAVE_V1_CaseConfiguration` 在 `Persistence/Features/LEAVE/V1/`；controller/DTO → `Api/Features/LEAVE/V1/`。namespace 跟著層走。

- [ ] **Step 3: build + 測試 + 手動 boot 驗證 LEAVE 流程**

```bash
cd bpm-svc && dotnet build && dotnet test
```
再照 `chef/skill/workflow.md` §5c boot bpm-svc + bpm-ui，用 persona 切換跑一張 LEAVE 假單到結案（inbox provider 走 Application scan 的路必須通）。

- [ ] **Step 4: Commit（留在 testbed）**

```bash
git add -A && git commit -m "test(leave): LEAVE V1 aligned to Clean Arch layers on leave-test-6"
```

### Task A4: LEAVE V1 cherry-pick 進 main（🚪 GATE — 要 開發者 點頭才做）

- [ ] Step 1: 在 leave-test-6 上把要進 main 的 commit 整理成乾淨的一串（必要時 squash）
- [ ] Step 2: `git checkout main && git cherry-pick <range>`
- [ ] Step 3: `dotnet test`（bpm-svc + bpm-admin-svc）+ `npx tsc -p tsconfig.app.json --noEmit`（兩個 UI）全綠
- [ ] Step 4: commit 訊息標明「first chef-cooked flow lands main」；**push 由 開發者 用 GitKraken**

---

## Sub-project B — admin-svc：tasks endpoint + PR/merge 追蹤 + Publish gating

### Task B1: Flow 加 PrUrl / MergedAt 欄位 + migration

**Files:**
- Modify: `bpm-admin-svc/src/Bpm.Admin.Domain/Flows/Flow.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Application/Flows/FlowDtos.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/FlowsController.cs`（DTO 建構處：`List`、`Get`/`ToDetail`）
- Modify: `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/ChefFlowsController.cs`（`Get`、`Transition` 的 DTO 建構處）
- Create: migration `Flow_PrMergeTracking`

- [ ] **Step 1: Flow.cs 加欄位（接在 `BpmnXml` 之後）**

```csharp
/// <summary>
/// PR opened by the chef agent for this flow's cook branch (e.g.
/// "https://github.com/acme/bpm/pull/12"). Null until the agent
/// opens one; environments without a remote never set it.
/// </summary>
public string? PrUrl { get; set; }

/// <summary>
/// When the cook branch was confirmed merged into main — set by the
/// chef agent's merge detection, or manually via the admin "Mark
/// merged" escape hatch. Publish is blocked while null (PR-CA1).
/// </summary>
public DateTime? MergedAt { get; set; }
```

- [ ] **Step 2: 兩個 DTO record 尾端加參數**

`FlowSummaryDto`：`string? ChefWorkContextJson` 之後加 `string? PrUrl, DateTime? MergedAt`。
`FlowDetailDto`：`string? BpmnXml` 之後加 `string? PrUrl, DateTime? MergedAt`。

- [ ] **Step 3: 修所有 DTO 建構 call site（positional record，編譯器會逐一報錯，照著補 `f.PrUrl, f.MergedAt`）**

```bash
cd bpm-admin-svc && dotnet build 2>&1 | grep -c error   # 補完應為 0
```

- [ ] **Step 4: 產 migration + 套用**

```bash
dotnet ef migrations add Flow_PrMergeTracking \
  --project src/Bpm.Admin.Persistence --startup-project src/Bpm.Admin.Api
dotnet ef database update \
  --project src/Bpm.Admin.Persistence --startup-project src/Bpm.Admin.Api
```

- [ ] **Step 5: 全套測試**

```bash
dotnet test
```
Expected: 全綠。

- [ ] **Step 6: Commit**

```bash
git add bpm-admin-svc/
git commit -m "feat(admin-svc): Flow.PrUrl + Flow.MergedAt columns for chef-agent merge tracking"
```

### Task B2: SetPrUrl / MarkMerged 服務 + Publish guard（TDD）

**Files:**
- Modify: `bpm-admin-svc/src/Bpm.Admin.Application/Flows/IFlowLifecycleService.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Persistence/Flows/FlowLifecycleService.cs`
- Test: `bpm-admin-svc/tests/Bpm.Admin.Persistence.Tests/FlowLifecycleTests.cs`（已存在，沿用 `CreateService()` / `SeedFlow()` helper）

- [ ] **Step 1: 寫失敗測試（加在 FlowLifecycleTests.cs）**

```csharp
[Fact]
public async Task Publish_Blocks_When_Not_Merged()
{
    var (svc, ctx, conn) = CreateService();
    try
    {
        var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
        var ex = await Assert.ThrowsAsync<FlowLifecycleException>(
            () => svc.PublishAsync(row.Id, null));
        Assert.Contains("merge", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally { ctx.Dispose(); conn.Dispose(); }
}

[Fact]
public async Task Publish_Allows_After_MarkMerged()
{
    var (svc, ctx, conn) = CreateService();
    try
    {
        var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
        await svc.MarkMergedAsync(row.Id, null, "test");
        var published = await svc.PublishAsync(row.Id, null);
        Assert.Equal(FlowState.Published, published.State);
    }
    finally { ctx.Dispose(); conn.Dispose(); }
}

[Fact]
public async Task SetPrUrl_Persists_And_Is_Idempotent()
{
    var (svc, ctx, conn) = CreateService();
    try
    {
        var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
        await svc.SetPrUrlAsync(row.Id, "https://github.com/x/y/pull/1");
        await svc.SetPrUrlAsync(row.Id, "https://github.com/x/y/pull/1"); // no throw
        var fresh = await ctx.Flows.AsNoTracking().SingleAsync(f => f.Id == row.Id);
        Assert.Equal("https://github.com/x/y/pull/1", fresh.PrUrl);
    }
    finally { ctx.Dispose(); conn.Dispose(); }
}
```

- [ ] **Step 2: 跑測試，確認 compile error / FAIL**

```bash
dotnet test tests/Bpm.Admin.Persistence.Tests --filter FlowLifecycleTests
```

- [ ] **Step 3: 介面 + 實作**

`IFlowLifecycleService.cs`：
```csharp
/// <summary>Record the PR the chef agent opened for this flow's cook
/// branch. Idempotent. Chef-token path.</summary>
Task<Flow> SetPrUrlAsync(Guid flowId, string prUrl, CancellationToken ct = default);

/// <summary>Mark the cook branch merged into main; unblocks Publish.
/// `source` lands in the audit reason ("gh-pr" | "git-ancestry" |
/// "manual").</summary>
Task<Flow> MarkMergedAsync(Guid flowId, Guid? actorUserId, string source, CancellationToken ct = default);
```

`FlowLifecycleService.cs`（放在 `PublishAsync` 附近）：
```csharp
public async Task<Flow> SetPrUrlAsync(Guid flowId, string prUrl, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(prUrl)) throw new FlowLifecycleException("prUrl required");
    var row = await Load(flowId, ct);
    if (row.PrUrl == prUrl) return row;          // idempotent re-run
    var before = new { row.PrUrl };
    row.PrUrl = prUrl;
    row.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    await _audit.LogAsync("flow_pr_opened", "flow", row.Id.ToString(), null, null,
        before: before, after: new { row.PrUrl }, reason: "chef agent", ct: ct);
    return row;
}

public async Task<Flow> MarkMergedAsync(Guid flowId, Guid? actorUserId, string source, CancellationToken ct = default)
{
    var row = await Load(flowId, ct);
    if (row.MergedAt is not null) return row;    // idempotent
    row.MergedAt = DateTime.UtcNow;
    row.UpdatedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    await _audit.LogAsync("flow_branch_merged", "flow", row.Id.ToString(), actorUserId, null,
        after: new { row.MergedAt }, reason: source, ct: ct);
    return row;
}
```

`PublishAsync` 改成（guard 在 transition 前）：
```csharp
public async Task<Flow> PublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default)
{
    // PR-CA1: publish only after the cook branch is confirmed merged to
    // main. The chef agent sets MergedAt via merge detection; the admin
    // "Mark merged" button is the manual escape hatch (e.g. squash merge
    // in a remote-less environment breaks ancestry detection).
    var row = await Load(flowId, ct);
    if (row.MergedAt is null)
        throw new FlowLifecycleException(
            "Cannot publish: cook branch is not merged to main yet. Wait for merge detection or use Mark merged.");
    return await TransitionAsync(flowId, FlowState.Published, "flow_published", actorUserId, new[] { FlowState.Approved }, ct);
}
```

- [ ] **Step 4: `RegisterShippedAsync` 的新 row 加 `MergedAt = DateTime.UtcNow`**
（shipped = code 已在部署裡，視同已 merge；否則 register-shipped 出來的 Published row 語意矛盾。）
同時檢查既有測試是否覆蓋 register-shipped — 有就確認照常綠。

- [ ] **Step 5: 跑測試到綠**

```bash
dotnet test
```

- [ ] **Step 6: Commit**

```bash
git add bpm-admin-svc/
git commit -m "feat(admin-svc): publish gated on MergedAt; SetPrUrl/MarkMerged lifecycle ops (PR-CA1)"
```

### Task B3: GET /api/chef/flows/tasks + chef 端 pr/merged endpoints（TDD）

**Files:**
- Modify: `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/ChefFlowsController.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Application/Flows/FlowDtos.cs`（task DTO）
- Modify: `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/FlowsController.cs`（admin Mark merged endpoint）
- Test: `bpm-admin-svc/tests/Bpm.Admin.Api.Tests/ChefTasksEndpointTests.cs`（new，用既有 `AdminAppFactory`）

- [ ] **Step 1: DTO（FlowDtos.cs）**

```csharp
/// <summary>One row in the chef agent's work queue.</summary>
public record ChefTaskDto(
    Guid FlowId,
    string FlowCode,
    int Version,
    string DisplayName,
    FlowState State,
    DateTime UpdatedAt,
    string? ChefWorkContextJson,
    string? PrUrl,
    DateTime? LastUserMessageAt);

public record ChefTaskListDto(
    IReadOnlyList<ChefTaskDto> Submitted,
    IReadOnlyList<ChefTaskDto> AwaitingChef,        // OnHold 且最後一則非 System 訊息是 user（Reply 或 Issue）
    IReadOnlyList<ChefTaskDto> ApprovedAwaitingMerge, // Approved 且 MergedAt == null
    IReadOnlyList<ChefTaskDto> Stalled);            // Cooking 且 heartbeat 斷 >30 分（crash 的 session — 沒這條會永遠卡在 Cooking 不再入列）
```

- [ ] **Step 2: 失敗測試（ChefTasksEndpointTests.cs；參考 AdminAppFactory 既有用法，chef bearer 用 dev 預設 `dev-chef-token`）**

測五件事：
```csharp
// 1. Submitted flow 出現在 Submitted 清單
// 2. OnHold + 最後訊息 Sender=User(Kind=Reply) → 出現在 AwaitingChef
// 3. OnHold + 最後訊息 Sender=Chef(Kind=Question) → 不出現在 AwaitingChef
// 4. Approved + MergedAt=null → 出現在 ApprovedAwaitingMerge；MarkMerged 後消失
// 5. Cooking + LastChefHeartbeatAt 35 分鐘前 → 出現在 Stalled；heartbeat 5 分鐘前 → 不出現
```
（測試 seed 直接操作 factory 的 scoped AdminDbContext + FlowChatMessages；斷言用 GET /api/chef/flows/tasks 帶 `Authorization: Bearer dev-chef-token`。）

- [ ] **Step 3: 跑測試，確認 FAIL（404）**

- [ ] **Step 4: endpoint 實作（ChefFlowsController.cs）**

```csharp
/// <summary>The chef agent's polling target: everything actionable,
/// grouped. Does NOT bump any flow heartbeat (no specific flow).</summary>
[HttpGet("tasks")]
public async Task<ActionResult<ChefTaskListDto>> Tasks(CancellationToken ct)
{
    if (!RequireChef()) return Forbid();

    var submitted = await _db.Flows.AsNoTracking()
        .Where(f => f.State == FlowState.Submitted)
        .OrderBy(f => f.UpdatedAt)
        .ToListAsync(ct);

    var onHold = await _db.Flows.AsNoTracking()
        .Where(f => f.State == FlowState.OnHold)
        .ToListAsync(ct);
    var onHoldIds = onHold.Select(f => f.Id).ToList();
    var lastMsgs = await _db.FlowChatMessages.AsNoTracking()
        .Where(m => onHoldIds.Contains(m.FlowId) && m.Sender != FlowChatSender.System)
        .GroupBy(m => m.FlowId)
        .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
        .ToListAsync(ct);
    var awaiting = onHold
        .Where(f => lastMsgs.Any(m => m.FlowId == f.Id && m.Sender == FlowChatSender.User))
        .OrderBy(f => f.UpdatedAt)
        .ToList();

    var approved = await _db.Flows.AsNoTracking()
        .Where(f => f.State == FlowState.Approved && f.MergedAt == null)
        .OrderBy(f => f.UpdatedAt)
        .ToListAsync(ct);

    var stallCutoff = DateTime.UtcNow.AddMinutes(-30);
    var stalled = await _db.Flows.AsNoTracking()
        .Where(f => f.State == FlowState.Cooking
                    && (f.LastChefHeartbeatAt == null || f.LastChefHeartbeatAt < stallCutoff))
        .OrderBy(f => f.UpdatedAt)
        .ToListAsync(ct);

    ChefTaskDto ToTask(Flow f) => new(
        f.Id, f.FlowCode, f.Version, f.DisplayName, f.State, f.UpdatedAt,
        f.ChefWorkContextJson, f.PrUrl,
        lastMsgs.FirstOrDefault(m => m.FlowId == f.Id)?.CreatedAt);

    return Ok(new ChefTaskListDto(
        submitted.Select(ToTask).ToList(),
        awaiting.Select(ToTask).ToList(),
        approved.Select(ToTask).ToList(),
        stalled.Select(ToTask).ToList()));
}

public sealed record ChefSetPrRequest(string PrUrl);

[HttpPost("{flowId:guid}/pr")]
public async Task<IActionResult> SetPr(Guid flowId, [FromBody] ChefSetPrRequest req, CancellationToken ct)
{
    if (!RequireChef()) return Forbid();
    try { await _lifecycle.SetPrUrlAsync(flowId, req.PrUrl, ct); return NoContent(); }
    catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
}

[HttpPost("{flowId:guid}/merged")]
public async Task<IActionResult> Merged(Guid flowId, [FromBody] ChefMarkMergedRequest req, CancellationToken ct)
{
    if (!RequireChef()) return Forbid();
    try { await _lifecycle.MarkMergedAsync(flowId, null, req.Source ?? "chef-agent", ct); return NoContent(); }
    catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
}

public sealed record ChefMarkMergedRequest(string? Source);
```

注意 EF `GroupBy + First` 需要 EF Core 8+ / Npgsql 支援 — 測試會在 SQLite in-memory 跑，**若 SQLite translate 失敗**，fallback 寫法（兩段式：撈 ids+maxCreatedAt 再 join）：
```csharp
var lastAt = await _db.FlowChatMessages.AsNoTracking()
    .Where(m => onHoldIds.Contains(m.FlowId) && m.Sender != FlowChatSender.System)
    .GroupBy(m => m.FlowId)
    .Select(g => new { FlowId = g.Key, At = g.Max(m => m.CreatedAt) })
    .ToListAsync(ct);
// 再用 (FlowId, At) 撈各自那一筆，比對 Sender。
```

- [ ] **Step 5: admin 端 Mark merged endpoint（FlowsController.cs，admin JWT）**

```csharp
[HttpPost("{id:guid}/mark-merged")]
public async Task<ActionResult<FlowDetailDto>> MarkMerged(Guid id, CancellationToken ct)
{
    try
    {
        var f = await _lifecycle.MarkMergedAsync(id, CurrentUserId(), "manual", ct);
        return Ok(ToDetail(f));
    }
    catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
}
```

- [ ] **Step 6: 跑全套測試到綠**

```bash
dotnet test
```

- [ ] **Step 7: Commit**

```bash
git add bpm-admin-svc/
git commit -m "feat(admin-svc): chef tasks queue endpoint + pr/merged chef ops + admin mark-merged"
```

### Task B4: admin-ui — Publish gating + Mark merged

**Files:**
- Modify: `bpm-admin-ui/src/flowcook/api/flows.ts`（型別 + `markMerged()`）
- Modify: `bpm-admin-ui/src/flowcook/pages/aiKitchen/ServePanel.tsx`（Publish 按鈕 gating，現 L87-88 附近）

- [ ] **Step 1: flows.ts**

`FlowSummary` 介面加：
```ts
  /** PR opened by the chef agent for this flow's cook branch, or null. */
  prUrl: string | null
  /** When the cook branch was confirmed merged to main; Publish is blocked while null. */
  mergedAt: string | null
```
新 API fn：
```ts
export function markMerged(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/mark-merged`, { method: 'POST' })
}
```

- [ ] **Step 2: ServePanel gating**

調查 ServePanel 目前怎麼拿 flow（它 import `getFlow`）— 拿到 detail 後：
- Publish 按鈕：`disabled={state !== 'Approved' || busy || !flow.mergedAt}`
- `state === 'Approved' && !flow.mergedAt` 時顯示說明列：PR 連結（有 `prUrl` 就 `<a>`）+「等 branch merge 進 main 後才能 Publish」+ 一顆 ghost「Mark merged（手動確認已合併）」按鈕 → `window.confirm`（跟 ServePanel 既有風格一致）→ `markMerged(flowId)` → reload。

- [ ] **Step 3: type-check**

```bash
cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit
```

- [ ] **Step 4: 手動 boot 驗證**（admin-svc + admin-ui，造一筆 Approved 未 merged 的 flow 看按鈕 disable / Mark merged 後 enable）

- [ ] **Step 5: Commit**

```bash
git add bpm-admin-ui/
git commit -m "feat(admin-ui): Serve-phase publish gated on merge; PR link + manual Mark merged"
```

---

## Sub-project C — Bpm.ChefAgent（跨平台輪詢器）

> 設計決定：**one-shot 模式** — 每次被 launchd / Task Scheduler 喚起跑一輪就退出。
> 比長駐 daemon 簡單、崩潰自癒、排程器原生 retry。global file lock 防重疊
> （cook 一隻 flow 可能跑 30+ 分鐘，期間後續喚起直接搶不到鎖退出）。

### Task C1: 專案骨架 + config

**Files:**
- Create: `chef/agent/Bpm.ChefAgent/Bpm.ChefAgent.csproj`（net10.0, `<Nullable>enable</Nullable>`）
- Create: `chef/agent/Bpm.ChefAgent/Program.cs`
- Create: `chef/agent/Bpm.ChefAgent/AgentConfig.cs`
- Create: `chef/agent/chef-agent.example.json`
- Create: `chef/agent/Bpm.ChefAgent.Tests/`（xunit）
- Modify: `.gitignore`（加 `chef/agent/chef-agent.json` — 真 config 含 token 不進 repo）

- [ ] **Step 1: AgentConfig + 失敗測試（config 解析）**

```csharp
public sealed record EnvTarget(string Name, string BaseUrl, string ChefToken, bool Enabled);
public sealed record TelegramConfig(string BotToken, string ChatId);
public sealed record AgentConfig(
    List<EnvTarget> Environments,
    TelegramConfig? Telegram,
    string RepoPath,            // 本機 bpm repo 根目錄
    string WorktreeRoot,        // e.g. ~/claude/bpm-cooks
    string ClaudeBin,           // "claude"
    int MaxTurns,               // e.g. 80
    int MaxSessionMinutes,      // e.g. 45
    int MaxAutoRetries,         // stalled 自動重撈上限 = 1
    string LockFilePath,        // e.g. <WorktreeRoot>/agent.lock
    string StateFilePath)       // retry 計數等持久狀態（JSON）
{
    public static AgentConfig Load(string path) =>
        JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException($"empty config: {path}");
}
```
測試：example json round-trip、缺檔丟例外。

- [ ] **Step 2: chef-agent.example.json**

```json
{
  "environments": [
    { "name": "local", "baseUrl": "http://localhost:5266", "chefToken": "dev-chef-token", "enabled": true },
    { "name": "azure-poc", "baseUrl": "https://poc-flowcook-admin-api.azurewebsites.net", "chefToken": "<az keyvault secret show --vault-name kv-poc-flowcook -n chef-token>", "enabled": false }
  ],
  "telegram": { "botToken": "<bot token>", "chatId": "<chat id>" },
  "repoPath": "/Users/jason/claude/bpm",
  "worktreeRoot": "/Users/jason/claude/bpm-cooks",
  "claudeBin": "claude",
  "maxTurns": 80,
  "maxSessionMinutes": 45,
  "maxAutoRetries": 1,
  "lockFilePath": "/Users/jason/claude/bpm-cooks/agent.lock",
  "stateFilePath": "/Users/jason/claude/bpm-cooks/agent-state.json"
}
```

- [ ] **Step 3: global lock（跨平台 — named Mutex 在 mac 不能用，必須 file lock）**

```csharp
public static FileStream? TryAcquireLock(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    try
    {
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException) { return null; }   // someone else holds it → exit 0
}
```

- [ ] **Step 4: commit**

```bash
git add chef/agent/ .gitignore
git commit -m "feat(chef-agent): project skeleton, config, cross-platform single-instance lock"
```

### Task C2: AdminApi client + TaskPlanner（TDD — 純邏輯可測）

**Files:**
- Create: `chef/agent/Bpm.ChefAgent/AdminApiClient.cs`（HttpClient 包裝：GET tasks、POST transition、POST messages、POST pr、POST merged；`Authorization: Bearer <chefToken>`；timeout 15s）
- Create: `chef/agent/Bpm.ChefAgent/TaskPlanner.cs`
- Test: `chef/agent/Bpm.ChefAgent.Tests/TaskPlannerTests.cs`

- [ ] **Step 1: TaskPlanner 失敗測試**

```csharp
// 優先序：AwaitingChef > Submitted；一次只回一個 cook 任務；
// ApprovedAwaitingMerge 全部回（merge 檢查便宜、不佔 session）。
[Fact]
public void Picks_AwaitingChef_Before_Submitted()
{
    var tasks = new ChefTaskList(
        Submitted: [T("PUR")], AwaitingChef: [T("LEAVE")], ApprovedAwaitingMerge: []);
    var plan = TaskPlanner.Plan(tasks);
    Assert.Equal("LEAVE", plan.CookTask!.FlowCode);
}

[Fact]
public void Cook_Is_Null_When_Queue_Empty_But_MergeChecks_Flow_Through()
{
    var tasks = new ChefTaskList([], [], [T("APE")]);
    var plan = TaskPlanner.Plan(tasks);
    Assert.Null(plan.CookTask);
    Assert.Single(plan.MergeChecks);
}
```

- [ ] **Step 2: 實作（`Plan` 純函式：oldest-first、一輪一 cook）** → 測試綠 → commit

```bash
git add chef/agent/ && git commit -m "feat(chef-agent): admin api client + task planner (priority + single-cook policy)"
```

### Task C3: cook session runner

**Files:**
- Create: `chef/agent/Bpm.ChefAgent/CookRunner.cs`
- Create: `chef/agent/Bpm.ChefAgent/WorktreeManager.cs`

- [ ] **Step 1: WorktreeManager**

行為（git CLI 經 `Process`，工作目錄 = RepoPath）：
- 新 cook：`git worktree add <WorktreeRoot>/<env>-<code>-v<n> -b cook/<env>/<code>-v<n> main`
- resume（flow 的 ChefWorkContextJson 有 branch）：worktree 已在就重用；不在就 `git worktree add <path> <branch>`
- merge 後清理：`git worktree remove <path> --force` + `git branch -D <branch>`（只在 MergedAt 設定後）
- branch 名帶 env 前綴 → local 與 azure 同 code cook 不撞

- [ ] **Step 2: 在 worktree 生成 MCP config**

寫 `<worktree>/.mcp.json`：
```json
{ "mcpServers": { "flowcook-admin": { "type": "sse", "url": "<env.baseUrl>/mcp", "headers": { "Authorization": "Bearer <env.chefToken>" } } } }
```

- [ ] **Step 3: CookRunner spawn claude headless**

```csharp
var psi = new ProcessStartInfo
{
    FileName = cfg.ClaudeBin,
    WorkingDirectory = worktreePath,
    ArgumentList = { "-p", prompt, "--max-turns", cfg.MaxTurns.ToString(),
                     "--permission-mode", "bypassPermissions" },
    RedirectStandardOutput = true, RedirectStandardError = true,
};
```
prompt 模板（單一字串，flowId 等內插）：
```
你是 flowcook chef。用 chef-codegen skill cook flow <flowCode> v<version>（flowId=<flowId>）。
步驟：chef_get_flow 拿 spec → chef_get_messages 看完整對話（resume/issue 時必讀）→
chef_download_bundle → 按 chef skill 開發 → 測試全綠後 chef_transition Committed 並
chef_post_message Completion。卡到需要 user 決定的事就 chef_transition OnHold（附 question）後結束。
你在正確的 worktree 裡，不要切 branch。
```
wall-clock 超時（`MaxSessionMinutes`）→ `process.Kill(entireProcessTree: true)`。
結束後打 API 查 flow state 判定結果：Committed=成功 / OnHold=提問 / 仍 Cooking=異常（進 retry 政策）。

- [ ] **Step 4: retry 政策（state file）**

`agent-state.json`：`{ "retries": { "<flowId>": 1 }, "envFailures": { "azure-poc": 3 }, "lastEnvAlertAt": { ... } }`
crash 的 session 會讓 flow 卡在 Cooking、heartbeat 漸冷 → 30 分後出現在 tasks 的 `Stalled` 清單。agent 對 Stalled 任務：retries < MaxAutoRetries → 直接再開一次 cook session（state 已是 Cooking，**不需要也不能再 claim**；worktree 從 ChefWorkContextJson.branch 接回）並 retries+1。retries 已滿 → TG 通知一次 + 跳過，等人工（admin-ui stall-reset 或處理完手動清 state）。TaskPlanner 優先序更新為：**Stalled(可重試) > AwaitingChef > Submitted**。

- [ ] **Step 5: commit**

```bash
git add chef/agent/ && git commit -m "feat(chef-agent): worktree manager + headless cook runner + retry policy"
```

### Task C4: PR / merge 偵測 + TG 通知 + main loop

**Files:**
- Create: `chef/agent/Bpm.ChefAgent/PrManager.cs`
- Create: `chef/agent/Bpm.ChefAgent/TelegramNotifier.cs`
- Modify: `chef/agent/Bpm.ChefAgent/Program.cs`（串整個 loop）

- [ ] **Step 1: PrManager**

Approved 任務、PrUrl == null：
1. `git remote get-url origin || git remote get-url github` 失敗（無遠端）→ `chef_post_message`（Memo：「branch <b> 已就緒待手動 merge」）+ TG 提醒，**不要每輪重發**（state file 記 lastRemindedAt，24h 冷卻）
2. 有遠端：`gh pr create --head <branch> --title "cook: <CODE> v<N> — <displayName>" --body-file <tmp>` → 成功取 URL → POST `/api/chef/flows/{id}/pr` + TG 通知
   PR body 模板：spec 摘要（flowCode/version/displayName）+ Completion 訊息內容 + `🤖 opened by chef-agent`

Approved 任務、PrUrl != null 或無遠端：merge 偵測：
- 有 PrUrl：`gh pr view <url> --json mergedAt -q .mergedAt` 非空 → merged
- 無遠端：`git merge-base --is-ancestor <branch> main`（exit 0 → merged；squash merge 偵測不到 — admin UI 的 Mark merged 是 escape hatch，PR body 與 TG 通知都要提醒這點）
- merged → POST `/api/chef/flows/{id}/merged` + worktree/branch 清理 + TG「<CODE> 已 merge，可以 Publish 了」

- [ ] **Step 2: TelegramNotifier**

```csharp
public async Task Send(string text) =>
    await _http.PostAsJsonAsync(
        $"https://api.telegram.org/bot{_cfg.BotToken}/sendMessage",
        new { chat_id = _cfg.ChatId, text });
```
通知時機（僅此清單，避免吵）：cook 開始 / Committed / OnHold 提問 / 第二次失敗 / PR 已開 / 偵測到 merge / 無遠端提醒（24h 冷卻）/ 環境連續 3 次不可達（60 分鐘冷卻）。

- [ ] **Step 3: Program.cs main loop**

```
load config → try lock（失敗 exit 0）
for env in enabled environments:
    tasks = GET /tasks（失敗 → envFailures++，按冷卻決定是否通知，continue）
    envFailures 歸零
    plan = TaskPlanner.Plan(tasks)
    for m in plan.MergeChecks: PrManager.Process(m)
    if plan.CookTask != null:
        claim：POST transition Cooking（409 → skip）
        CookRunner.Run(...)
        break   # 一輪最多 cook 一隻（跨環境也是）
save state → exit 0
```

- [ ] **Step 4: 單元測試能測的測（planner 已測；PrManager 的冷卻邏輯抽純函式測）** → 全綠 → commit

```bash
git add chef/agent/ && git commit -m "feat(chef-agent): pr/merge detection, telegram notifier, one-shot main loop"
```

### Task C5: 排程安裝（mac now、Windows 文件）

**Files:**
- Create: `chef/agent/com.flowcook.chef-agent.plist`
- Create: `chef/agent/README.md`

- [ ] **Step 1: launchd plist（StartInterval 300、RunAtLoad false、stdout/err 到 `<WorktreeRoot>/logs/agent.log`）**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>com.flowcook.chef-agent</string>
  <key>ProgramArguments</key><array>
    <string>/usr/local/share/dotnet/dotnet</string>
    <string>/Users/jason/claude/bpm/chef/agent/Bpm.ChefAgent/bin/Release/net10.0/Bpm.ChefAgent.dll</string>
    <string>/Users/jason/claude/bpm/chef/agent/chef-agent.json</string>
  </array>
  <key>StartInterval</key><integer>300</integer>
  <key>StandardOutPath</key><string>/Users/jason/claude/bpm-cooks/logs/agent.log</string>
  <key>StandardErrorPath</key><string>/Users/jason/claude/bpm-cooks/logs/agent.err.log</string>
</dict></plist>
```
安裝：`dotnet publish -c Release` → `cp plist ~/Library/LaunchAgents/ && launchctl load ~/Library/LaunchAgents/com.flowcook.chef-agent.plist`

- [ ] **Step 2: README.md** — 安裝步驟（mac + Windows `schtasks /Create /SC MINUTE /MO 5 /TN flowcook-chef-agent /TR "dotnet <path>\Bpm.ChefAgent.dll <config>"`）、防睡眠（mac：`sudo pmset -a sleep 0` 或 Amphetamine；Windows：電源計畫 + 工作排程「喚醒電腦執行」）、Windows 額外前置（Git for Windows、`git config --global core.longpaths true`、系統 LongPathsEnabled、gh CLI）、token 取得（azure：`az keyvault secret show --vault-name kv-poc-flowcook -n chef-token`）、日誌位置、如何手動跑一輪。

- [ ] **Step 3: commit**

```bash
git add chef/agent/ && git commit -m "feat(chef-agent): launchd schedule + cross-platform install docs"
```

### Task C6: 前置工具安裝（人工，開發者 或 Claude 終端互動）

- [ ] `brew install gh` → `gh auth login`（建議 HTTPS + token，這台 ssh 對 GitHub 不通）
- [ ] 確認 `gh pr create` 在 bpm repo 可用（dry-run：`gh pr list`）
- [ ] `chef-agent.json` 從 example 複製填真值（TG bot token 沿用 telegram plugin 那顆或另開 bot — 開發者 決定）

---

## Sub-project D — 首隻 flow 監督跑（GATE：A + B + C 全完成後）

- [ ] D1: local 環境、azure `enabled:false`。開發者 在 admin-ui submit 一隻簡單流程（建議 WFH 或重 cook LEAVE 類）
- [ ] D2: 手動觸發一輪 agent（不等排程）：`dotnet run --project chef/agent/Bpm.ChefAgent -- chef-agent.json`，全程看 log
- [ ] D3: 驗證鏈：claim→Cooking ✓ / worktree + branch 建立 ✓ / heartbeat 跳動（admin-ui 無 stalled）✓ / Committed + Completion 訊息 ✓ / TG 通知齊 ✓
- [ ] D4: admin-ui Approve → 下一輪 agent 開 PR ✓ → Publish 按鈕 disabled ✓
- [ ] D5: 開發者 merge PR（GitKraken）→ 下一輪偵測 merged ✓ → Publish enable → Publish ✓ → bpm-ui launcher 看得到 ✓
- [ ] D6: 全程 OK → `launchctl load` 啟用排程；觀察兩天再開 azure（`enabled:true` + KV token）

---

## 風險備忘（執行中隨時對照）

1. **claim race**：Flow 無 RowVersion；Phase 1 靠單機 file lock。多 worker 前要做原子 claim（conditional UPDATE 或 concurrency token）— Phase 2。
2. **flow 被刪（pre-publish 刪除是今天剛開放的）**：chef API 會 404 → CookRunner 視為任務消失，清 worktree、不通知錯誤（TG 一行帶過即可）。
3. **azure 停機是常態**：`enabled:false` 是預設安全姿勢；開啟後靠失敗冷卻。
4. **EF migration 衝突**：單 session 串行天然避免；不要為了快把 maxConcurrent 改 >1。
5. **`bypassPermissions`**：chef session 有整機 shell 權限 — Phase 1 接受（自己的機器），客戶機部署前要換 sandbox 方案（Phase 2+ 課題）。
6. **token 在 config 檔是明文**：Phase 1 接受（gitignore + 本機）；Phase 2 換 Keychain / Credential Manager。
