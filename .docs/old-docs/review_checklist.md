# Review checklist — v0.2

> Phase B 用：Review Agent 拿到客戶 repo 後（已經被 Claude Code 跑過 prompt
> 生過 code），照這份清單一條一條檢查，pass / fail / flag。
> 對應 `inovation_idea.md` §3.4 Phase B pipeline 的 review step。
>
> 來源：前 3 輪 dogfood（leave round 1、leave round 2、purchase round 3）
> 累積出的「會踩坑、值得自動化檢查」的點。會隨後續 dogfood 演化。

---

## 怎麼用

每個 check：
- **Look:** 看哪裡（檔案 / 命令 / DOM）
- **Pass:** 通過條件
- **Fail signal:** 看到什麼就是失敗
- 標 ⚙ 的可以寫成 shell / script，全自動跑
- 標 👁 的需要看 spec.json + 看 code，要 LLM 判斷
- 標 🌐 的需要瀏覽器運行時驗證（chrome-devtools MCP）

每一條對應 `prompt_template_v1.md` 的某條 RULE，後面括號標出來方便互相 trace。

---

## 1. Spec faithfulness（RULE #1）

### 1.1 ⚙ flowCode 一致性
- **Look:** `spec.meta.flowCode` vs Domain/Application/Persistence/Api 命名
- **Pass:** 找得到 `{FlowCode}Workflow.cs` / `{FlowCode}Case.cs` / `{FlowCode}State.cs` / `{FlowCode}Events.cs` / `{FlowCode}Controller.cs` 等對應檔案；class 名稱一致
- **Fail signal:** spec 寫 `PURCHASE` 但 code 裡有 `BuyCase` 或縮寫不同的命名

### 1.2 👁 userTasks[].fields[].id 全部映射
- **Look:** 每個 `spec.userTasks[].fields[].id` 在 `Domain/Cases/{FlowCode}Case.cs` 都該找到對應的 property
- **Pass:** 所有 spec field id 都有相同語意的 property，遵循下列命名約定：
  - snake_case → PascalCase（`quote_file` → `QuoteFile…`）
  - `*_file` 欄位 → `…FileName` property（只存檔名，不存 bytes — 見 leave round 的 `CertFileName` 慣例）
  - `*_range` 欄位 → 拆成 `…Start` + `…End` 兩個 property
  - `derived` 欄位 → property + 註解標 `// derived: {expr}`
- **Fail signal:** `quote_file` → `QuoteFile` (string, 沒 `Name` 後綴) 違反 leave 慣例
- **Fail signal:** `date_range` → 一個 `DateRange` 字串欄位，沒拆成兩個 DateOnly

### 1.3 👁 approvals[] 全部實作
- **Look:** `spec.approvals[]` vs `Application/{FlowCode}/Services/{FlowCode}ApprovalResolver.cs`
- **Pass:** 每個 approval node 在 Resolver 都有對應方法（如 `ResolveManagerApproverAsync` / `ResolveFinanceApproverAsync`）；fallback 規則有實作
- **Fail signal:** spec 寫 fallback 但 Resolver 沒寫；或 rule.type 是 `direct_manager` 但 Resolver 用了 role lookup

### 1.4 👁 decisions[] 全部實作
- **Look:** `spec.decisions[]` vs `{FlowCode}DecisionEvaluator.cs`
- **Pass:** 每個 gateway 都有 evaluate 方法；branch condition 數值與 spec 一致；isDefault 處理正確
- **Fail signal:** spec 寫 `amount >= 10000` 但 code 寫 `> 10000`（off-by-one）

### 1.5 👁 notifications[] 全部觸發
- **Look:** `spec.notifications[]` vs `{FlowCode}NotificationEmitter.cs` 呼叫點
- **Pass:**
  - 每個 trigger 都在對應 state 轉換時被呼叫
  - 每個 notification 的 `template.variables[]` 跟 emitter 提供給 MustacheLite 的 dictionary key set 是**集合相等**（不是 ⊆）
- **Fail signal:** spec 寫 `notify_complete` trigger=on_complete 但 emitter 沒在 Execute/Archive handler 呼叫
- **Fail signal:** spec 的 `variables: ["a", "b", "c"]` 但 emitter 只 `values["a"] = ...`，`{{c}}` render 成空字串

### 1.6 ⚙ validator/conditional 規則
- **Look:** `spec.userTasks[].fields[].validator` / `.conditional` vs Validator class
- **Pass:** validator 有對應 `RuleFor()`；conditional 有對應 `.When()`；error message 引用 spec key
- **Fail signal:** spec 寫 `value > 0 && value <= 10000000` 但 validator 只 `.GreaterThan(0)`，少了上界
- **Fail signal:** error message 是 `"Quote required"`，沒寫 `"quote_file is required when amount >= 10000"`（無法 trace 回 spec）

### 1.7 ⚙ 雙語 labels 全覆蓋（RULE #4）
- **Look:** UI 元件 + 通知 template
- **Pass:** 所有 user-visible 字串都有 zh-TW / en 兩種；UI 用「中 / English」格式
- **Fail signal:** 有 `<Field label="Vendor">` 沒中文

### 1.8 ⚙ permissions.submitter role 強制
- **Look:** `spec.userTasks[].permissions.submitter` 是 `role:X` 的，handler 是否檢查
- **Pass:** Handler 從 IIdentityProvider 拿 user，檢查 `Roles.Contains("X")`，否則 throw ConflictException
- **Fail signal:** spec 寫 `submitter: role:Purchase`，但 ExecuteHandler 沒檢查

---

## 2. 結構與 idempotency（RULE #2 / #3）

### 2.1 ⚙ 沒有寫死客戶名稱在 business logic
- **Look:** `grep -ri "{tenant_code}" src/Domain src/Application` 應該只在 namespace / 註解出現
- **Pass:** business logic 是 tenant-agnostic 的；tenant code 只在 config / folder path
- **Fail signal:** `if (tenant == "acme") ...` 在 Domain / Application 出現

### 2.2 ⚙ Imports / case 排序
- **Look:** 所有 `.cs` 檔案 using 區塊
- **Pass:** using 排序一致（System 先 / 字母順）；switch case 順序穩定
- **Fail signal:** 同一個 PR 兩次跑同 spec 產出的 diff 不為空（idempotency 破）

---

## 3. End-to-end reachable（RULE #6）

### 3.1 ⚙ Create dropdown 有新 flow
- **Look:** `bpm-ui/src/components/AppLayout.tsx` 的 `FORM_GROUPS`
- **Pass:** 新 flowCode 被加到對應 group（HR / Expense / Travel / Purchase）
- **Fail signal:** 新 flow 在 dropdown 找不到

### 3.2 ⚙ 標籤是人類可讀
- **Look:** 同上的 `FORM_GROUPS[].items[].label`
- **Pass:** 像 `"Purchase Request (採購申請)"` 這種雙語完整描述
- **Fail signal:** `"PURCHASE"`、`"PURCHASE-v2"`、`"PURCHASE (spec)"`、`"*PURCHASE"`

### 3.3 ⚙ Hash deep-link 路由註冊
- **Look:** `bpm-ui/src/App.tsx`
- **Pass:**
  - `readSavedScreen` 解析 `#{flowcode-lower}/<caseId>`
  - 有一個 `useEffect` listener `hashchange` 把 hash 同步回 state
  - 一個 `useEffect` 把 state 同步寫回 hash
- **Fail signal:** 重新整理頁面後 caseId 丟失（沒解析 hash）

### 3.4 ⚙ App.tsx switch case 處理
- **Look:** `App.tsx` 的 `screen.code` switch
- **Pass:** 新 flowCode 有 case，正確 render `<{FlowCode}Form>` 或 `<{FlowCode}View>`（依 caseId 是否存在）
- **Fail signal:** 點 dropdown 後白屏（switch 沒這個 case）

### 3.5 ⚙ workflow.ts FORMS 註冊
- **Look:** `bpm-ui/src/lib/workflow.ts`
- **Pass:** `FormCode` type 包含新 code；`FORMS` 對應 entry 有 `steps` / `ownerByStep` / `initialActive`
- **Fail signal:** TypeScript 報 `Type 'XXX' is not assignable to type 'FormCode'`

### 3.6 ⚙ Vite proxy `/api` → :5290（v0.2）
- **Look:** `bpm-ui/vite.config.ts`
- **Pass:** `server.proxy['/api'].target === 'http://localhost:5290'`，`changeOrigin: true`
- **Fail signal:** 沒有 proxy 設定 → 前端 fetch `/api/{flow}/cases` 會打到 vite 5173 自己，回 404 HTML
- **註：** 第 3 輪 dogfood prompt 剛好寫對了，但 v0.1 沒檢查；下次靜默壞掉就會搜 1 小時

### 3.7 ⚙ Controller route prefix 對齊 flowCode
- **Look:** `Api/Controllers/{FlowCode}Controller.cs` 的 `[Route("api/...")]`
- **Pass:** route 是 `api/{flowCode.toLowerInvariant()}`，跟前端 `purchaseApi.ts` 裡 `'/api/purchase/...'` 對得起來
- **Fail signal:** spec.meta.flowCode = `PURCHASE` 但 route = `api/purchasing` → 前端 404
- **註：** spec / route / api client 三方要對齊，一個漂走全錯

### 3.8 👁 personaToActingUserId 多階段重映射（v0.2）
- **Look:** `bpm-ui/src/lib/{flowCode}Api.ts`
- **Pass:** 如果流程有 ≥2 個 approval node，要有 `personaToActingUserId(persona, state)` 函式，能在不同 state 下把同一個 persona 映射到不同 userId
- **Fail signal:** 流程有 manager+finance+CEO 三層，但 demo 只能用 finance persona 簽到 finance 那層；CEO 那層卡住沒人能簽
- **註：** LEAVE_SPEC 跟 PURCHASE 都用了這個 trick，已是慣例；單階流程（譬如只有 manager）可以略過

---

## 4. Migrations（RULE #7）

### 4.1 ⚙ 三個 migration 檔案都 commit
- **Look:** `bpm-svc/src/Persistence/Migrations/`
- **Pass:** 存在 `{Timestamp}_Add{FlowCode}.cs` + `.Designer.cs` + 更新後的 `AppDbContextModelSnapshot.cs`，且都 `git ls-files` 看得到
- **Fail signal:** 只有 .cs 沒有 .Designer.cs；snapshot 沒更新

### 4.2 ⚙ DbSet 註冊
- **Look:** `bpm-svc/src/Persistence/AppDbContext.cs` + `bpm-svc/src/Application/Common/Abstractions/IAppDbContext.cs`
- **Pass:** 兩邊都有 `DbSet<{FlowCode}Case> {FlowCode}Cases { get; }`
- **Fail signal:** AppDbContext 有 DbSet 但 IAppDbContext 沒對應 → handler compile fail

### 4.3 ⚙ 跑 migration db update 過得了
- **Look:** `cd bpm-svc/src/Persistence && dotnet ef database update --startup-project ../Api`
- **Pass:** exit code 0，db 檔案有 `{FlowCode}Cases` 表
- **Fail signal:** `Build failed` 或 `Unable to resolve service` → 通常是 DI 沒註冊（見 §6）

### 4.4 ⚙ Api.csproj `<None Update="identity-*.csv"><CopyToOutputDirectory>`（v0.2）
- **Look:** `bpm-svc/src/Api/Api.csproj`
- **Pass:** identity CSV 有 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`
- **Fail signal:** CSV 沒被 copy 到 bin/Debug/net10.0/ → 啟動時 `CsvIdentityProvider` 拋 `FileNotFoundException`，但只有跑時看得到，build 不會抓
- **註：** 第 1 輪 leave dogfood 踩過，慘案

### 4.5 ⚙ appsettings.json `Identity:CsvPath` 設定（v0.2）
- **Look:** `bpm-svc/src/Api/appsettings.json`
- **Pass:** 有 `"Identity": { "CsvPath": "identity-{tenant}.csv" }`
- **Fail signal:** 沒這個 section，DI fallback 會用 `"identity-acme.csv"` 寫死的 default — multi-tenant 時就壞了

---

## 5. Browser walk-through（RULE #8 a–k）

要在跑完 dogfood 後驗證。整段建議用 chrome-devtools MCP 自動化跑一遍。

### 5.1 ⚙ Step a: db 有表
- `sqlite3 bpm-svc/src/Api/bpm.db ".tables"` 包含 `{FlowCode}Cases`

### 5.2 ⚙ Step b: API up
- `curl -s http://localhost:5290/health` 回 `{"status":"healthy"}`

### 5.3 ⚙ Step c: Vite up
- `lsof -nP -iTCP:5173 -sTCP:LISTEN` 不為空

### 5.4 ⚙ Step d: Chrome MCP attached
- `list_pages` 不報 error

### 5.5 🌐 Step e: 從 home 點到 form（不准 typing URL）
- 用 click，途中不 navigate

### 5.6 🌐 Step f: spec.testCases[0] 全部 fill 進去 + 路由預覽正確
chrome-devtools MCP 的 input 處理三種：
- **`<input>` controlled (text/number/date/textarea)**：用 prototype value setter + bubbling input event（v1.2）
  ```js
  const setter = Object.getOwnPropertyDescriptor(
    el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype,
    'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
  ```
- **`<input type="file">`**：用 DataTransfer + new File，因為 `e.target.files` 是 FileList 不是 string（v0.2，第 3 輪 tc_3 找到）
  ```js
  const dt = new DataTransfer();
  dt.items.add(new File(['mock'], 'foo.pdf', { type: 'application/pdf' }));
  input.files = dt.files;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  ```
- **`<select>` 用 chrome-devtools `fill` 工具**：value 要傳**顯示 label**（如 `"服務委外 / Service"`），不能傳 `<option value>`（如 `"service"`），會 "Could not find option" 失敗（v0.2）

### 5.7 ⚙ Step g: 案件入 list
- `curl /api/{flow}/cases?applicantUserId=...` 回 ≥1 筆，state 是預期值

### 5.8 🌐 Step h: detail 顯示所有 spec field
- 每個 `spec.userTasks[].fields[].id` 對應的值都在 DOM 看得到（用 a11y snapshot）

### 5.9 ⚙ Step i: 截圖目錄存在
- `dogfood-screenshots/<ISO-timestamp>/` 內有 `step-e.png` `step-f.png` `step-g.png` `step-h.png`

### 5.10 👁 Step j: 每張截圖一條 assertion
- 同目錄有 `ASSERTIONS.md`，每張圖有一行 trace 回 spec rule 的句子

### 5.11 ⚙ Step k: 跑完 server 都關了（v1.3）
- `lsof -nP -iTCP:5290 -sTCP:LISTEN` 空
- `lsof -nP -iTCP:5173 -sTCP:LISTEN` 空
- `ps aux | grep -iE "Bpm.Api|chrome-profile|/vite" | grep -v grep` 空

### 5.12 ⚙ 0 console error / 0 4xx 5xx
- `list_console_messages(error,warn)` 為空
- `list_network_requests` 沒有 status >= 400

---

## 6. DI 註冊（這輪 dogfood 第一次踩到）

### 6.1 ⚙ Resolver / Emitter 手動 AddScoped
- **Look:** `bpm-svc/src/Application/DependencyInjection.cs`
- **Pass:** 每個 spec 衍生的非 IRequestHandler/IValidator 服務（`{FlowCode}ApprovalResolver`、`{FlowCode}NotificationEmitter`）都 `services.AddScoped<>()` 註冊
- **Fail signal:** `dotnet ef migrations add` 跑時報 `Unable to resolve service for type 'XXX'` —— 等於 DI 漏註冊
- **註：** MediatR `RegisterServicesFromAssembly` 只認 `IRequestHandler` / `IPipelineBehavior`，FluentValidation `AddValidatorsFromAssembly` 只認 `IValidator<T>`，這兩個之外的 service 都要手動加

### 6.2 ⚙ Persistence DI 三件套
- **Look:** `bpm-svc/src/Persistence/DependencyInjection.cs`
- **Pass:**
  - `IAppDbContext` 註冊到 `AppDbContext`
  - `IIdentityProvider` 註冊到 `CsvIdentityProvider`
  - `INotificationSender` 註冊到 `LoggingNotificationSender`（或實際的）
- **Fail signal:** 任一漏 → 啟動報 DI error

### 6.3 ⚙ Application.csproj 引用 EFC（v0.2）
- **Look:** `bpm-svc/src/Application/Application.csproj`
- **Pass:** `<PackageReference Include="Microsoft.EntityFrameworkCore" />` 存在
- **Fail signal:** 沒引用 → `IAppDbContext` 用 `DbSet<>` 編譯失敗
- **註：** 第 3 輪 dogfood 因為 IAppDbContext 從 leave round 帶過來時要這個 package；開新 flow 時容易忘記

---

## 7. Tests（RULE #5）

### 7.1 ⚙ Tests 編譯且全綠
- `dotnet test bpm-svc/tests/Bpm.Tests --no-build`
- **Pass:** exit 0，所有測試 pass

### 7.2 👁 spec.testCases 全覆蓋
- **Look:** `bpm-svc/tests/Bpm.Tests/Integration/{FlowCode}FlowIntegrationTests.cs`
- **Pass:**
  - 每個 `spec.testCases[]` 都有對應 `[Fact]`，方法名以 `Tc{N}_` prefix 開頭
  - DisplayName 引用 testCase id（如 `[Fact(DisplayName = "tc_2: ...")]`）
  - 機械 assertion：`grep -c "public async Task Tc[0-9]" tests/.../FlowIntegrationTests.cs >= len(spec.testCases)`
  - assert expectedPath 跟 expectedApprovers 都有 .Should()
- **Fail signal:** spec 有 4 個 testCase，integration test 只有 2 個
- **Fail signal:** test 方法名是 `HappyPath` / `EdgeCase`，沒法 trace 回 spec.testCases 哪一個

### 7.3 👁 ApprovalResolver / DecisionEvaluator unit test
- **Look:** `Unit/{FlowCode}ApprovalResolverTests.cs` + `Unit/{FlowCode}DecisionEvaluatorTests.cs`
- **Pass:**
  - ApprovalResolver 每個 `spec.approvals[]` rule 一個 test；fallback 一個 test
  - DecisionEvaluator 每個 `spec.decisions[]` branch 一個 `[Theory]` parametric test，含閾值上下界

---

## 8. 注意但不擋（warnings）

### 8.1 review_checklist.md 自己的演化
- 這個檔案應該每輪 dogfood 結束後，把新發現補進去
- 連續 3 輪沒生這個檔，這次第 3 輪後半才補 v0.1。下次別讓它再 stale。

### 8.2 spec_schema.md 跟 prompt 的一致性
- spec.json 出的欄位名跟 spec_schema.md 寫的對得起來嗎？
- 兩者該被同步維護；現在沒有自動 check。

### 8.3 Phase A → Phase B 的 onboarding 拆解
- 真的客戶 repo 跑 Phase A 時，這份 checklist 應該變成 Phase B Review Agent 的 system prompt
- 那時候要把 ⚙ 條目寫成具體 shell command；👁 條目寫成具體 LLM judge prompt

---

## 演化紀錄

- v0.1（2026-05-03，第 3 輪 dogfood 後）：
  - 從前 3 輪累積的 prompt 條文 + ASSERTIONS.md findings 抽出
  - 8 大類、~30 條 check
  - ⚙ 自動化候選 ~70%、👁 LLM judge ~25%、🌐 browser ~5%
  - 還沒接 Review Agent；這版主要當作 reference 跟下輪 dogfood 結束後的對照表
- v0.2（2026-05-03，v0.1 自我驗證後）：
  - 把 v0.1 拿來跑 dogfood-purchase branch 一遍（見 `dogfood-screenshots/20260503T032239Z/checklist_v0.1_run.md`）
  - **0 false positive**（v0.1 沒誤報）、**7 false negative**（漏抓 7 種 bug）
  - 補上：
    - §1.2 命名約定加詳（file → FileName、range → Start/End、derived 標註）
    - §1.5 notification variables 集合相等（不是 ⊆）
    - §3.6 Vite proxy `/api → :5290`
    - §3.7 Controller route 對齊 flowCode
    - §3.8 personaToActingUserId 多階段重映射
    - §4.4 Api.csproj `<CopyToOutputDirectory>` for identity-*.csv
    - §4.5 appsettings.json `Identity:CsvPath`
    - §5.6 file input 用 DataTransfer / select 用 display label
    - §6.3 Application.csproj 引用 EFC
    - §7.2 機械可驗證的 spec.testCases 全覆蓋（grep count）
  - ~40 條 check（+10）
