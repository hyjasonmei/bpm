# 手動開發流程指南（不靠 chef，人類自寫 + 註冊上架）

> 這份是「人類版」的流程開發 SOP。chef（AI codegen skill）只是「幫你把這些檔寫出來」的便利層——
> 你完全可以**自己手寫一隻流程、註冊、上架**，也可以**接手 chef 不滿意的 output 重做**。
> 整條註冊/上架/launcher 機制是 provenance-blind（不檢查「這是不是 chef 做的」），只看：
> ① 你部署上去的 `<CODE>_V<N>_Case` entity / 實體表　② `Admin_Flows` registry row 的 `State`。
> 兩個你都能自己生出來。

**「怎麼寫每一個檔」的權威參考是這兩份**（本指南不重複，只導讀 + 包上生命週期）：
- `chef/skill/conventions.md` — 路徑 map、命名、必用 primitive、render pattern、雷
- `chef/skill/SKILL.md` — model B 的完整 system prompt（state machine / case store / inbox 等寫法）

---

## 0. 心智模型（先讀）

- **Model B = 每隻流程一套手寫程式**：一個 state machine + EF entity + controller + React 表單 + manifest，全部放在 `Features/<CODE>/V<N>/` 子樹。**沒有通用 spec 直譯引擎**；`spec.json` 是設計文件，**不是 runtime 輸入**（註冊/上架完全不需要有效 spec——LEAVE 這隻根本沒附 spec 也照樣上架）。
- **chef ≠ gate**：chef skill 幫你寫 code、chef agent（`chef/agent/`）自動化 git/部署/上架輪詢。兩者都只是便利。每一步都有**人工逃生口**（下面會列）。
- **`<CODE>` / `<N>`**：`<CODE>` = 流程代碼大寫（如 `LEAVE`、`PURCHASE_REQUEST`），`<N>` = 版本整數。前綴是識別碼的一部分，**每個 class/檔名都帶**（不要用 namespace 或省略）。

### 黃金法則：複製一隻現成流程當骨架

chef 自己也是這樣做的。**動筆前先挑一隻 shape 最接近的已上線流程，整包複製改名**，比從零寫快又不易踩雷。`main` 上有 10+ 隻可抄：

| 你的流程長相 | 抄這隻 |
|---|---|
| 單表單 + 多段簽核 | `LEAVE/V1`（含條件加簽 + 附件）、`APE/V1` |
| 明細/發票 + 金額 + 幣別 + 總計 | `VENDOR_EXPENSE/V1`（視覺標竿）、`TEO/V1`、`PURCHASE_REQUEST/V1` |
| 人事資料密集（姓名/部門/日期一堆） | `EOB/V1`、`ETM/V1` |
| 資產/單關主管判定 | `FAD/V1`、`FAP/V1`（含自動 PO 號） |
| 條件加簽（達門檻才上級） | `WFH/V6`（≥100 天加簽上級） |

---

## 1. 前置

```bash
# 後端共用密鑰 + dev 模式（讓 /api/dev/login persona 快切可用）
export BPM_JWT_SECRET=$(openssl rand -hex 32)
export BPM_AUTH_MODE=dev

# 種 admin 端身分資料（13 user / 6 dept / 14 role）
cd bpm-admin-svc/src/Bpm.Admin.SeedCli && ASPNETCORE_ENVIRONMENT=Development dotnet run -- seed --org

# 起四個服務
cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http   # 5266
cd bpm-svc/src/Api && dotnet run                                          # 5290
cd bpm-admin-ui && npm install && npm run dev                            # 5174
cd bpm-ui && npm install && npm run dev                                  # 5173
```
- 工具：.NET 10 SDK、Node/npm、（雲端部署才要）`az` + `swa` CLI。
- demo 登入：`alice@acme.example` / `flowcook2026`（系統管理員用 `jack@acme.example`）。

---

## 2. Part A — 寫 code

權威清單在 `conventions.md`。這裡給你**檔案 checklist** 和**最容易漏的幾件事**。

### 2.1 一隻流程要寫的檔（全部在 `Features/<CODE>/V<N>/`）

| 層 | 路徑 | 內容（以 LEAVE V1 為例的檔名） |
|---|---|---|
| Domain | `bpm-svc/src/Domain/Features/<CODE>/V<N>/` | entity `LEAVE_V1_Case` + status enum `LEAVE_V1_CaseStatus`（POCO，無依賴） |
| Application | `bpm-svc/src/Application/Features/<CODE>/V<N>/` | state machine `LEAVE_V1_LeaveService`、`LEAVE_V1_NotificationTemplates`、`LEAVE_V1_InboxProvider`、actor 解析 helper、**per-flow `I<CODE>_V<N>_CaseStore` 介面** |
| Persistence | `bpm-svc/src/Persistence/Features/<CODE>/V<N>/` | EF config `LEAVE_V1_CaseConfiguration` + `LEAVE_V1_CaseStore` 實作（**只有這裡認得 entity 型別**） |
| Migration | `bpm-svc/src/Persistence/Migrations/` | `dotnet ef migrations add` 自動產（含 `AppDbContextModelSnapshot.cs` 重生）——**讓工具產，別手寫** |
| Api | `bpm-svc/src/Api/Features/<CODE>/V<N>/` | controller `LEAVE_V1_Controller` + DTOs，路由 `/api/leave/v1/...` |
| Tests | `bpm-svc/tests/Bpm.Tests/Features/<CODE>/V<N>/` | state machine / gateway / notification 測試 |
| UI | `bpm-ui/src/features/<CODE>/V<N>/` | 表單 `*_LeaveForm.tsx`、case-detail `*_CaseDetail.tsx`、`manifest.ts`、`<CODE>_V<N>.bpmn.xml` |

**Clean-Arch 鐵律**：entity 不准掉進 Persistence；商業邏輯不准掉進 Api。Persistence 只放 EF mapping + case store 實作。

### 2.2 manifest（UI 自動發現的關鍵，必寫）

`registry.ts` 用 Vite glob 自動撈 `features/*/V*/manifest.ts`——**你不用碰 App.tsx/router.tsx**，丟對檔就被收進去，同 code 取最高版本。

```ts
// bpm-ui/src/features/LEAVE/V1/manifest.ts
import type { FormManifest } from '@/features/registry'
import LEAVE_V1_BpmnXml from './LEAVE_V1.bpmn.xml?raw'
import { LEAVE_V1_CaseDetail } from './LEAVE_V1_CaseDetail'
import { LEAVE_V1_LeaveForm } from './LEAVE_V1_LeaveForm'

const manifest: FormManifest = {
  code: 'LEAVE', version: 1,
  component: LEAVE_V1_LeaveForm,
  detailComponent: LEAVE_V1_CaseDetail,
  bpmnXml: LEAVE_V1_BpmnXml,
}
export default manifest
```

### 2.3 你會需要碰的 lead-side 檔（少數幾處，可接受）

| 檔 | 為什麼 |
|---|---|
| `bpm-svc/src/Application/DependencyInjection.cs` | 註冊你的 state machine service `AddScoped<<CODE>_V<N>_...Service>()`。（`ITypedInboxProvider` 這檔的 scan 已自動撈，通常不用手加。） |
| `bpm-svc/src/Persistence/DependencyInjection.cs` | 把 `I<CODE>_V<N>_CaseStore` 綁到它的 EF 實作。 |
| `bpm-ui/src/lib/workflow.ts` | `FormCode` union 加你的 code + `FORMS.<CODE>` 加一筆（label / steps / ownerByStep），讓 `FormShell` + stepper + BpmnView 能渲染。 |

### 2.4 一定要做、最容易漏的幾件事（踩了就 demo 出包）

1. **Inbox provider 一定要寫**：每隻流程實作 `ITypedInboxProvider`（放 Application 層）。沒寫 → 案件進了 DB 但**首頁 inbox 完全看不到**（這是舊 Model A 最常見的雷）。`GetPendingAsync` = 等這個人簽的、`GetMineAsync` = 這個人送的。`InboxRow.title` 要寫人看得懂（例「Bob 申請 特休 3 天」）。
2. **`detailUrl` 用底線、不是連字號**：`/cases/{flowCode.ToLower()}/{id}`，多字代碼**保留底線**：`PURCHASE_REQUEST → /cases/purchase_request/...`。**別**借 REST 路由的 kebab（`/api/purchase-request/v1`）——兩者分隔符不同。錯了會顯示「還沒提供 case detail view」，單字流程藏得住、多字流程必爆。
3. **每個簽核/指派步驟用 `IActorAuthorizer.CanActAsync`**：用 `if (!await auth.CanActAsync(assigneeId, caller, ct)) throw new ForbiddenException(...)`，**不要**寫 `if (assigneeId != caller)`。前者會放行「指派人本人**或其有效代理人**」，委任（代理人）才會生效。UI 端按鈕也要 `assignee === viewer || useDelegatedFor().includes(assignee)`。送件人專屬動作（撤回/重送）才用嚴格 `==`。
4. **derived 欄位雙端重算**：像「請假天數」這種衍生值，TypeScript（`useMemo`）算一份給畫面、C# 送出時**再算一份**存 DB，別信客戶端傳的值。
5. **stepper 要鏡像「你的」狀態機，不是抄來那隻的**：抄別人的 `FORMS.<CODE>` 會連他的 stage 一起抄進來。`activeStepFor(status)` 的 switch 要對到**你自己**狀態機的每個狀態。APE V1 是標竿。
6. **必用 lead 的 primitive，別自己造**：`FormShell`、`ActionFooter`（送出/簽核 bar 一律用它，不要 inline 按鈕列）、`ConfirmDialog`、`FilePicker`/`AuthedFileLink`（檔案讀回要用 `AuthedFileLink`，純 `<a href>` 會 401）、`apiFetch`、`IOrgChartReader`/`IPrincipalDirectory`（actor 解析）、`INotifyDispatcher`（通知）。完整對照在 conventions.md「Cross-cutting primitives」。

---

## 3. Part B — build + 測試

```bash
# 後端編譯 + 跑你的測試
dotnet build bpm-svc/src/Api
dotnet test bpm-svc/tests/Bpm.Tests --filter <CODE>_V<N>

# 前端：沒有 JS test runner，用 type-check（一定要帶 -p，否則 silently 跳過 src）
cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit
```

---

## 4. Part C — migration

```bash
cd bpm-svc/src/Api
dotnet ef migrations add <CODE>_V<N>_InitialCreate -p ../Persistence -s .
dotnet ef database update -p ../Persistence -s .
```
- 要產**正式（Postgres）schema** 時前面加 `BPM_DB_PROVIDER=postgres`，否則 SQLite 型別會被寫進 migration。
- migration 檔 + `AppDbContextModelSnapshot.cs` 讓工具產，不要手改。

---

## 5. Part D — 部署

- **本機**：跑著的 `dotnet run`（5290）+ `npm run dev`（5173）改完即生效（前端 dev server 會 reload 撈新 manifest）。
- **雲端**：`infra/azure/03-deploy.sh`（dotnet publish→zip→`az webapp deploy`；前端 `npm build`→`swa deploy`）。只改 bpm-svc + bpm-ui 的話，可只跑對應段落，避免重部署 admin-svc（它啟動會重 seed 身分）。bpm-svc 啟動只 `MigrateAsync()` **不 seed**，不會洗資料。

> ⚠️ launcher 要「**兩半都到**」才會出現磚：① 前端 manifest 有部署　② registry row 是 `Published`。只部署 code 忘了註冊 → 後端能跑但 launcher 空的；只註冊忘了部署 manifest → 有 registry row 但沒磚。

---

## 6. Part E — 註冊 + 上架（兩條路，挑一條）

### 路 1（最快）：register-shipped 一鍵直接 Published

專門設計給「純粹 merge 上去的 code、沒走過 AI Kitchen 精靈」的流程。

- admin AI Kitchen 按 **「register shipped flows」**，或直接打 API：
  ```bash
  TOK=<jack 的 token>   # 系統管理員
  curl -s -X POST localhost:5266/api/flows/register-shipped -H "Authorization: Bearer $TOK"
  ```
- 它會掃你部署的 `<CODE>_V<N>_Case` entity（`FlowCodesController` 用 regex `^(?<code>.+)_V(?<ver>\d+)_Case$` + 檢查有 `Status`/`SubmittedAt`），插一筆 `Admin_Flows` row **直接 `Published`**，並自動蓋 `MergedAt=now`。
- 沒有有效 spec 也 OK（fallback `"{}"`）。

### 路 2：wizard 生命週期（想要 review gate 時）

狀態機：`Draft → Submitted → (Cooking) → Committed → Approved → Publishing → Published`。三個**人類關卡**（submit / approve / publish）都是 admin 操作；chef agent 只自動化中間「確定性的輪詢步驟」（偵測 merge、跑部署、health check），且這段 deploy worker **標明 NO LLM**。

人工等價步驟：
| chef agent 自動做的 | 你手動做 |
|---|---|
| 開 `gh` PR、偵測 merge、蓋 `MergedAt` | 自己 merge，然後按 **「Mark merged」**（`POST /api/flows/{id}/mark-merged`） |
| Cooking/Committed transition | 走 user-JWT 端點 `/submit`、`/approve`、`/publish` |
| 建 main、`az webapp deploy`+restart、`swa deploy`、health check、`MarkPublished` | 手動跑 `infra/azure/03-deploy.sh`，部署完走同一個 publish transition |

> ⚠️ **publish 卡在 `MergedAt`**：`PublishAsync` 會丟「cook branch is not merged to main yet」除非 `MergedAt` 有蓋。自己 merge 的話記得按「Mark merged」。（路 1 的 register-shipped 會自動幫你蓋。）

---

## 7. Part F — 驗證

1. **launcher 出現**：bpm-ui 首頁 Quick Actions / Create 頁看得到你的流程磚（manifest globs in + registry `Published`）。
2. **跑一遍 happy path**：用右上角 persona 快切器，submit（員工）→ 各關核准 → 結案。confirm modal、必填擋關、簽核 timeline、stepper 都對。
3. **smoke 套件**：改了 identity / 狀態機 / flow，或上 demo 前，跑 `/bpm-smoke-test` skill（happy + unhappy + Reset）一次掃過。

---

## 8. Part G — 接手 chef 的流程重做

chef 跟你**用同一個 git checkout**，產出就在 `bpm-svc/` + `bpm-ui/` 的 `Features/<CODE>/V<N>/`。

1. 直接改那些檔（state machine / form / case-detail / inbox provider…）。
2. `dotnet build` + `dotnet test` + `tsc` 確認。
3. 若改了 schema → 補 migration。
4. 部署。
5. 註冊/上架照 Part E（已 Published 的就重部署即生效；要新版本就 bump `<N>` 加一套 `V<N+1>/` 並重新 register/publish）。

chef 唯一留下的東西（聊天備註 `FlowChatMessage`、branch 標籤 `ChefWorkContextJson`）是**純參考**，註冊上架不需要它，可無視。

---

## 9. 雷區檢查清單（上 demo 前對一遍）

- [ ] 寫了 `ITypedInboxProvider`，案件在首頁 inbox 看得到（不是只進 DB）。
- [ ] `detailUrl` = `/cases/{flowCode.ToLower()}/...`，多字保留底線。
- [ ] 每個簽核步驟用 `CanActAsync`（含 UI 的 `useDelegatedFor`），委任才會動。
- [ ] derived 欄位伺服器端有重算。
- [ ] stepper 的 `FORMS.<CODE>.steps` + `activeStepFor` 對到自己的狀態機。
- [ ] manifest 的 `code` 是 UPPER_SNAKE、`version` 正確。
- [ ] migration 用 `BPM_DB_PROVIDER=postgres` 產正式 schema。
- [ ] 部署後**有按 register-shipped / 完成 publish**（launcher 兩半都到）。
- [ ] 同一個 `FlowCode` 沒跟精靈裡的草稿撞（active-code 唯一）。
- [ ] 送出/簽核 bar 用 `ActionFooter`，檔案讀回用 `AuthedFileLink`。

---

## 10. 參考

- `chef/skill/conventions.md` — 命名/路徑/primitive/render pattern（權威）
- `chef/skill/SKILL.md` — model B 完整寫法（state machine / case store / inbox）
- `bpm-svc/CLAUDE.md`、`bpm-ui/CLAUDE.md` — 各層邊界 + SharedIdentity
- `README.md` — run / seed / test / migrate 指令
- `docs/superpowers/specs/2026-06-04-deploy-model-publish-stage.md` — approve vs publish 設計
- `docs/superpowers/plans/2026-06-17-publish-deploy-pipeline.md` — 上架管線（3 人類關卡 + 3 確定性輪詢）
- `/bpm-smoke-test` skill — 上 demo 前的全流程煙霧測試
- 參考實作：`LEAVE/V1`（簽核 + 條件加簽 + 附件）、`VENDOR_EXPENSE/V1`（視覺標竿 + 4 段簽核）、`APE/V1`（stepper 標竿）

---

# 附錄 A — 最小可跑範例：APE V1 逐檔

下面是 `main` 上**最簡單的單關核准 Clean-Arch 流程**（APE / 預支現金，8 個欄位）的每個檔的**實際內容**，當你的 copy-paste 範本。狀態圖最單純：

```
start ─► PendingManager ──approve──► Completed
              │
           reject
              ▼
        ResubmitRequired ──resubmit──► PendingManager（下一回合）
```

> **改成你自己的流程**：把所有 `APE` → `<你的CODE>`、`V1` → `V<N>`，調整 `*_Case` 的業務欄位、status enum、controller 路由、form 欄位、`FORMS.<CODE>` 的 steps/ownerByStep。骨架完全照搬。

13 個檔：Domain×2、Application×4、Persistence×2、Api×2、UI(manifest+form+detail+bpmn)，外加 3 處膠水。

---

## Domain（2 檔，POCO，無依賴）

`bpm-svc/src/Domain/Features/APE/V1/APE_V1_CaseStatus.cs`
```csharp
namespace Bpm.Domain.Features.APE.V1;

public enum APE_V1_CaseStatus
{
    PendingManager   = 0,
    ResubmitRequired = 1,
    Completed        = 2,
    Cancelled        = 3,
}
```

`bpm-svc/src/Domain/Features/APE/V1/APE_V1_Case.cs`
```csharp
namespace Bpm.Domain.Features.APE.V1;

public class APE_V1_Case
{
    public Guid Id { get; set; }

    // 業務資料（對應表單欄位）—— 改成你自己的欄位
    public Guid SubmitterUserId { get; set; }
    public DateOnly ExpectReceiveDate { get; set; }
    public DateOnly DeductReturnDate { get; set; }
    public string ChargeDepartment { get; set; } = string.Empty;
    public string? RechargeOutside { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    // 工作流狀態（每隻流程都有這組）
    public APE_V1_CaseStatus Status { get; set; } = APE_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // 簽核關卡的決定欄位（一關一組）
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }      // ← register-shipped 偵測要靠 Status + SubmittedAt
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

## Application（4 檔：state machine + case store 介面 + 通知 + inbox）

`Application/Features/APE/V1/IAPE_V1_CaseStore.cs` —— per-flow 資料存取 port（Application 不能參考 Persistence，所以要這層）
```csharp
using Bpm.Domain.Features.APE.V1;
namespace Bpm.Application.Features.APE.V1;

public interface IAPE_V1_CaseStore
{
    void Add(APE_V1_Case @case);
    Task<APE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<APE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<APE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

`Application/Features/APE/V1/APE_V1_AdvancePaymentService.cs` —— **狀態機（核心）**。一個 public method = 一個 transition。注意 **`CanActAsync`** 那段（委任授權）和 actor 解析走 `IOrgChartReader`：
```csharp
public sealed class APE_V1_AdvancePaymentService(
    IAPE_V1_CaseStore store, IOrgChartReader org, IPrincipalDirectory directory,
    IClock clock, ILogger<APE_V1_AdvancePaymentService> log,
    INotifyDispatcher notify, IActorAuthorizer auth)
{
    public const string FlowCode = "APE";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(Guid SubmitterUserId, DateOnly ExpectReceiveDate,
        DateOnly DeductReturnDate, string ChargeDepartment, string? RechargeOutside,
        string Description, decimal Amount, string Currency);

    public async Task<APE_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null) throw new ConflictException("submitter has no manager / manager is self");

        var now = clock.UtcNow;
        var c = new APE_V1_Case {
            Id = Guid.NewGuid(), SubmitterUserId = input.SubmitterUserId,
            /* …把 input 的欄位填進去… */
            Status = APE_V1_CaseStatus.PendingManager,
            ManagerUserId = manager, CurrentAssigneeUserId = manager,
            RoundCount = 1, SubmittedAt = now, LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);     // 通知主管待簽
        return c;
    }

    public Task<APE_V1_Case> ApproveByManagerAsync(Guid id, Guid actor, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(id, actor, approve: true, comment, ct);
    public Task<APE_V1_Case> RejectByManagerAsync(Guid id, Guid actor, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(id, actor, approve: false, comment, ct);

    private async Task<APE_V1_Case> ManagerDecisionAsync(Guid id, Guid actor, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(id, ct);
        if (c.Status != APE_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is {c.Status}, expected PendingManager");      // ← 狀態守衛 (409)
        if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actor, ct))
            throw new ForbiddenException("only the assigned manager or their delegate may act"); // ← 委任授權 (403)

        c.ManagerApproved = approve; c.ManagerComment = comment;
        c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve) {                                  // 退件 → 回送件人補件
            c.Status = APE_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }
        c.Status = APE_V1_CaseStatus.Completed;          // 核准 → 結案
        c.CurrentAssigneeUserId = null; c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);   // ← actor 解析走 lead 的 port
        return managerId is null || managerId == submitterUserId ? null : managerId;
    }
    // …ResubmitAsync / CancelAsync / Validate / Notify* 省略，見真檔 (256 行)
}
```

`Application/Features/APE/V1/APE_V1_NotificationTemplates.cs` —— 純 render function
```csharp
namespace Bpm.Application.Features.APE.V1;

public static class APE_V1_NotificationTemplates
{
    public record Rendered(string Subject, string Body);

    public static Rendered RenderAssign(string applicantName, string summary, string caseUrl)
        => new($"【待簽】{applicantName} 的預支申請",
               $"申請人：{applicantName}\n摘要：{summary}\n\n請點此核准：{caseUrl}");
    // …RenderSubmitted 略
}
```

`Application/Features/APE/V1/APE_V1_InboxProvider.cs` —— **必寫**，案件才會出現在首頁 inbox。注意 `DetailUrl` 用 `/cases/ape/...`（底線、小寫）：
```csharp
public sealed class APE_V1_InboxProvider(IAPE_V1_CaseStore store, IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => APE_V1_AdvancePaymentService.FlowCode;
    public int FlowVersion => APE_V1_AdvancePaymentService.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"預支現金 · {c.Currency} {c.Amount:N0}", Status: ZhStatus(c.Status),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/ape/{c.Id}")).ToList();              // ← 底線 slug，多字流程保留底線
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindPendingAsync(userId, ct);       // = 指派給我、未終態的
        var names = await directory.GetManyAsync(cases.Select(c => c.SubmitterUserId).Distinct().ToArray(), ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"{names.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? "—"} 預支現金 · {c.Currency} {c.Amount:N0}",
            Status: ZhStatus(c.Status), SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/ape/{c.Id}")).ToList();
    }

    private static string ZhStatus(APE_V1_CaseStatus s) => s switch {
        APE_V1_CaseStatus.PendingManager => "待主管核准", APE_V1_CaseStatus.ResubmitRequired => "退回補件",
        APE_V1_CaseStatus.Completed => "已核准", APE_V1_CaseStatus.Cancelled => "已撤回", _ => s.ToString() };
}
```

## Persistence（2 檔：EF mapping + case store 實作）

`Persistence/Features/APE/V1/APE_V1_CaseConfiguration.cs`
```csharp
public sealed class APE_V1_CaseConfiguration : IEntityTypeConfiguration<APE_V1_Case>
{
    public void Configure(EntityTypeBuilder<APE_V1_Case> b)
    {
        b.ToTable("APE_V1_case");                       // 表名 <CODE>_V<N>_<purpose_snake>
        b.HasKey(c => c.Id);
        b.Property(c => c.ChargeDepartment).IsRequired().HasMaxLength(200);
        b.Property(c => c.Description).IsRequired().HasMaxLength(2000);
        b.Property(c => c.Currency).IsRequired().HasMaxLength(10);
        b.Property(c => c.Amount).HasColumnType("decimal(18,2)");
        b.Property(c => c.Status).HasConversion<int>();  // enum → int
        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
```

`Persistence/Features/APE/V1/APE_V1_CaseStore.cs` —— `IAPE_V1_CaseStore` 的 EF 實作（**只有這裡認得 entity 型別**）
```csharp
public sealed class APE_V1_CaseStore(AppDbContext db) : IAPE_V1_CaseStore
{
    public void Add(APE_V1_Case @case) => db.Set<APE_V1_Case>().Add(@case);
    public Task<APE_V1_Case?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => db.Set<APE_V1_Case>().SingleOrDefaultAsync(c => c.Id == id, ct);
    public async Task<IReadOnlyList<APE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<APE_V1_Case>().AsNoTracking().Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);
    public async Task<IReadOnlyList<APE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<APE_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != APE_V1_CaseStatus.Completed && c.Status != APE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
```

## Api（2 檔：controller + DTOs）

`Api/Features/APE/V1/APE_V1_Controller.cs` —— `[Authorize]` + `BpmControllerBase`，路由 `/api/ape/v1`，每個 transition 一個 endpoint：
```csharp
[ApiController]
[Authorize]
[Route("api/ape/v1")]
public sealed class APE_V1_Controller(
    APE_V1_AdvancePaymentService service, IAPE_V1_CaseStore store, IPrincipalDirectory directory) : BpmControllerBase
{
    [HttpPost]
    public async Task<ActionResult<APE_V1_CaseResponse>> Submit([FromBody] APE_V1_SubmitRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();                               // ← JWT sub
        var c = await service.SubmitAsync(APE_V1_DtoMapping.ToServiceInput(userId, req), ct);
        return CreatedAtAction(nameof(GetById), new { caseId = c.Id }, await BuildResponseAsync(c, ct));
    }

    [HttpPost("{caseId:guid}/manager-decision")]
    public async Task<ActionResult<APE_V1_CaseResponse>> ManagerDecision(Guid caseId, [FromBody] ManagerDecisionRequest req, CancellationToken ct)
    {
        var userId = RequireUserId();
        var c = req.Approve
            ? await service.ApproveByManagerAsync(caseId, userId, req.Comment, ct)
            : await service.RejectByManagerAsync(caseId, userId, req.Comment, ct);
        return await BuildResponseAsync(c, ct);
    }

    [HttpGet("{caseId:guid}")] public async Task<ActionResult<APE_V1_CaseResponse>> GetById(Guid caseId, CancellationToken ct) { /* … */ }
    [HttpGet("mine")]    public async Task<IReadOnlyList<APE_V1_CaseRowResponse>> Mine(CancellationToken ct) { /* … */ }
    [HttpGet("pending")] public async Task<IReadOnlyList<APE_V1_CaseRowResponse>> Pending(CancellationToken ct) { /* … */ }
    // …resubmit / cancel / BuildResponseAsync 省略，見真檔 (92 行)

    public sealed record ManagerDecisionRequest(bool Approve, string? Comment);
}
```
DTOs（`APE_V1_Dtos.cs`）= 一個 `*_SubmitRequest`（送出）、一個 `*_CaseResponse`（完整）、一個 `*_CaseRowResponse`（列表）+ 一個 `*_DtoMapping` 把 entity 映射成 response。照搬改欄位即可。

## UI（manifest 必寫；form / case-detail crib 現成的）

`bpm-ui/src/features/APE/V1/manifest.ts` —— **registry 自動 glob 進來，這是 UI 發現流程的關鍵**
```ts
import type { FormManifest } from '@/features/registry'
import APE_V1_BpmnXml from './APE_V1.bpmn.xml?raw'
import { APE_V1_CaseDetail } from './APE_V1_CaseDetail'
import { APE_V1_AdvancePaymentForm } from './APE_V1_AdvancePaymentForm'

const manifest: FormManifest = {
  code: 'APE', version: 1,
  component: APE_V1_AdvancePaymentForm,
  detailComponent: APE_V1_CaseDetail,
  bpmnXml: APE_V1_BpmnXml,
}
export default manifest
```

`APE_V1_AdvancePaymentForm.tsx` —— 表單骨架（**必用 `FormShell` + `apiFetch` + `ActionFooter` + `ConfirmDialog`**；完整 226 行見真檔）：
```tsx
export function APE_V1_AdvancePaymentForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [form, setForm] = useState<APE_V1_SubmitPayload>(emptyPayload())
  const [confirmOpen, setConfirmOpen] = useState(false)
  // …valid 檢查、attemptSubmit 開 confirm…

  async function doSubmit() {
    const res = await apiFetch('/api/ape/v1', {            // ← POST 到你的 controller
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
    if (!res.ok) throw new Error(await res.text())
    const body = await res.json()
    navigate(`/cases/ape/${body.id}`)                      // ← 導去 case-detail
  }

  return (
    <FormShell code="APE" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>預支資訊 / Advance Payment</SectionTitle>
        {/* …用 <Field> + <Input>/<Select>/<Textarea> 排欄位… */}
      </SectionCard>
      <ActionFooter
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', onClick: () => navigate('/') },
          { id: 'submit', label: '送出申請', variant: 'primary', disabled: !valid, onClick: attemptSubmit },
        ]} />
      <ConfirmDialog open={confirmOpen} titleZh="送出預支申請？" confirmText="確認送出"
        onCancel={() => setConfirmOpen(false)} onConfirm={doSubmit} />
    </FormShell>
  )
}
```
`APE_V1_CaseDetail.tsx` —— case-detail（read-only + 簽核區）：crib `APE/V1` 那隻（stepper 標竿）。重點：`<Stepper steps={FORMS.APE.steps} activeStep={activeStepFor(status)}>` 要對到你的狀態機；簽核按鈕用 `CanActAsync` 對應的 UI 判斷（`assignee===viewer || useDelegatedFor().includes(assignee)`）。
`APE_V1.bpmn.xml` —— 從 bundle 複製或畫一張；UI「View BPMN」用。

## 膠水（3 處）

`bpm-svc/src/Application/DependencyInjection.cs`（約 line 109）
```csharp
services.AddScoped<APE_V1_AdvancePaymentService>();
```
`bpm-svc/src/Persistence/DependencyInjection.cs`（約 line 193）
```csharp
services.AddScoped<IAPE_V1_CaseStore, APE_V1_CaseStore>();
```
> `ITypedInboxProvider`（inbox provider）由 assembly scan 自動註冊，**不用手加**。

`bpm-ui/src/lib/workflow.ts` —— `FormCode` union 加 `'APE'`，再加 `FORMS.APE`：
```ts
export type FormCode = '…' | 'APE' | '…'

export const FORMS: Record<FormCode, FormDef> = {
  // …
  APE: {
    code: 'APE',
    label: 'Advance Payment (APE)',
    zhLabel: '預支費用申請',
    steps: [ STEP('apply','APPLY','申請'), STEP('approve','APPROVE','主管簽核'), STEP('close','CLOSE','結案') ],
    ownerByStep: ['employee', 'manager', null],   // ← steps 必須對到你「真正的」狀態機，不要照抄別人的
    initialActive: 0,
  },
  // …
}
```

## migration + 測試（指令）

```bash
cd bpm-svc/src/Api
BPM_DB_PROVIDER=postgres dotnet ef migrations add APE_V1_InitialCreate -p ../Persistence -s .   # 正式 schema
dotnet ef database update -p ../Persistence -s .
dotnet test bpm-svc/tests/Bpm.Tests --filter APE_V1   # 測試放 tests/Bpm.Tests/Features/APE/V1/
```

完成後接回主文 **Part C→F**（migration → 部署 → register-shipped 一鍵 Published → launcher 驗證 + smoke）。
