# 客戶 Demo 反饋報告 — 2026-06-30

> 首次客戶 demo（flowcook BPM）。本報告彙整現場提問 / 需求 + 我方可行性評估 + 對客戶回應建議 + 後續 roadmap。
> ⚠️ 我（Claude）不在現場，目前只掌握你轉述的 **OData 整合** 那題——其餘現場反饋請補在 §3 與 §1.0，我再一起整進來。

---

## 🟢 進度更新（2026-07-02）— 現況彙整

Demo 後這波開發：**committed backlog 7 項 → 6 項完整完成並實測，1 項（1.1b）刻意延後。**

| # | 項目 | 狀態 | 驗證 |
|---|---|---|---|
| 1.4 | Dashboard 已完成數恆 0 | ✅ 完成 | 加 canonical `InboxRow.Lifecycle`，dashboard 改用它計數；Chrome 實測顯示 8（非 0） |
| 1.5 | 一人多職 / 角色共享佇列 | ✅ 完成 | `CurrentAssigneeRoleCode` + 角色感知 `CanActAsync`；涵蓋全部 4 隻有角色關的流程（LEAVE / PURCHASE_REQUEST / TEO / VENDOR_EXPENSE）；Chrome 實測（Frank 以 FINANCE 角色看到共享佇列） |
| 1.1a | OData 組織資料 | ✅ 完成（升級為 CRUD） | admin-svc `/odata`（Users / Departments / Roles / Memberships），Basic Auth 專屬帳密、唯一性 / soft-delete / audit；含 email（D3=a）；curl E2E |
| 1.2 | 代理人接受機制 | ✅ 完成（含通知） | Delegation `Pending → Accepted / Declined`；`CanActAsync` 改認 Accepted；delegator / delegatee 兩端 UI + 指定 / 接受 / 拒絕通知；Chrome 實測 |
| 1.3 | 自定義資料集 | ✅ 完成 | admin 建資料集 + 連動選單（縣市→行政區）+ shadcn row edit-mode；4 服務 loop + Chrome 實測寫入連動 |
| 1.6 | 並簽 / 會簽 | ✅ 完成（P1+P2+P3） | 真 BPMN 平行 gateway + 守 Model B；共用 primitive（10 測綠）+ CONTRACT_REVIEW 參考流程 + chef skill 範本 + AI Kitchen 設計 UI（門檻 M/N）；Postgres + Chrome BPMN 多節點高亮 E2E |
| 1.1b | OData 自定義資料集（動態 EDM） | ✅ 完成（2026-07-03） | 每個資料集 = 一張動態 OData 表（/odata-ds/{key}，欄位來自 ColumnsJson）。$metadata(CSDL) + $filter/$select/$orderby/$top/$count（in-memory 評估，因 CellsJson 存 TEXT + EnableQuery 不支援 untyped）。OdataBasic 帳密、唯讀。curl 實測含 `city eq '台北市'`、`contains()`、多資料集。→ Power BI/Excel 可把每個資料集當表拉 + 單欄過濾。 |

**併帶交付（非原 backlog）：** reseed provider bug 修復（Postgres 上 Reset 沙盒賣點會 500）、smoke 腳本過時修正（auth token / 移除的 process-admin / reset 段）、資料集編輯器 shadcn Input/Switch + row edit-mode 重構。

**§2 roadmap「餵進來」評估更新（2026-07-02，定調「客戶推 → 我們 OData 收」）：**

因 OData 做成 **CRUD**，安全寫入地基已具備。**定位改為：讀 Entra/AD + 對帳 的髒活丟回客戶側（他們的 IT / Power Automate 匯出再往我們 OData 打），我們只提供「安全寫入的 ingress」。** 於是：

- **目錄同步 / 餵進來（⑤⑥ 合併）**：不是我們建連接器 → 塌縮成「用我們的 OData CRUD 端口」= **現成**。客戶推、我們收。首次一次性灌資料現在就能用。
- **批次匯入（③）**：OData **`$batch` 已開**（2026-07-02）——一次 request 推多筆、少 round-trip。⚠️ **非交易性**：OData 每個 sub-request 各自 DbContext scope，changeset 不會 all-or-nothing（逐筆各自成敗）。對匯入通常反而好（回報哪幾筆失敗、不會一筆爛全退）。真要原子 changeset 需另做共用交易，多數情境用不到。若要「上傳檔案 UI + 欄位對應 + 預覽」則另加小–中的那層。
- **定期重推的冪等性**：**已做**（2026-07-02）——POST 帶 `?upsert=true` 時 Users 以 email、Roles 以 code 命中就更新（Membership 重複冪等），可在 `$batch` 內使用；預設仍嚴格（撞就 400）。客戶無腦重推整批組織資料不會撞唯一性。
- **SSO（①）**：屬登入 / 驗證層、與資料進出無關，另算（最先得分的整合點）。

> 一句話：OData-CRUD 通了之後，「餵進來」的工程重心不在我們——我們是收件口，客戶負責把資料弄出來推進來。`$batch` + upsert 是唯二值得順手加的小補強。真正大工的「AD 全同步連接器」不建（丟客戶側）。

> 仍未補：§0 demo 整體結果、§1.7 其餘現場反饋（仍 `[待補]`）。

## 反饋一覽 + 建議優先序

| # | 項目 | 類型 | 現況 | 工程量 | 優先序 |
|---|---|---|---|---|---|
| 1.4 | Dashboard 已完成數恆 0 | 🐞 Bug | 根因已定位 | 小 | **P0 快贏** |
| 1.5 | 一人多職 / 角色共享佇列 | 能力（部分） | 身分層支援多角色；但簽核是「單一指派人」、非「有角色就能簽」 | 中 | P1–P2 |
| 1.1a | OData：組織資料 | 整合（客戶要） | 可行、薄層 | 小 | **P1** |
| 1.2 | 代理人接受機制 | UX（客戶要） | 指定即生效、待加接受 | 中 | **P1** |
| 1.3 | 自定義資料集 | 差異化（已設計） | spec + plan 已備 | 中 | **P1** |
| 1.1b | OData：自定義資料集 | 整合 | 需動態 EDM | 中 | P2（接 1.3 後） |
| 1.6 | 並簽 / 會簽 | 能力（已決定排） | 待建並行 primitive | 中–大 | **P2**（先 spec） |

**建議節奏：**
- **P0 立刻**（低風險快贏、救客戶第一印象）：① 修 dashboard bug　② 補一人多職 demo（種子 + 示範流程）
- **P1 近期**（客戶明確需求 + 差異化）：③ OData 組織資料（薄層、最快能 demo 的整合切片）　④ 代理人接受機制　⑤ 自定義資料集（plan 已備、落地）
- **P2 需設計 / 較大**：⑥ 並簽 / 會簽（先 brainstorm→spec→plan 再 build）　⑦ OData 自定義資料集 (b) 動態 EDM　⑧ 更廣整合（SSO / 匯入 / 同步 API，見 §2）

> 定調：先清「客戶第一眼會看到的 bug + 沒 demo 的現成能力」（P0），再做「客戶當場要的整合與代理人 + 已設計好的資料集」（P1），最後才碰「要設計週期的並簽/會簽與深度整合」（P2）。

---

## 0. Demo 結果（待 Jason 補充）

- 2026-06-30（週二）完成首次客戶 demo。demo 前已做雲端體檢、smoke 全綠、11 隻流程 + Grace 採購快切皆可現場操作。
- 兩大賣點：AI Kitchen onboarding（9-step + AI 對話 + 即時問卷）、無痛上線 sandbox（mail capture / persona / 時間快轉 / reset）。
- _[待補：客戶整體反應、最有興趣的點、疑慮、決策者態度、後續意願]_

---

## 1. 客戶提問與需求

### 1.1（✅ 現場確認）暴露 OData API 端口做資料整合 — 組織資料集 + 自定義資料集

**客戶情境：** demo 現場被問「是否可暴露 OData API 端口給 ①組織資料集 ②自定義資料集，做資料整合」。

**我方評估：可行，且自然——加一層唯讀 API，不是重寫。**

- **技術契合度高**：後端是 .NET + EF Core，OData 是一等公民——官方 `Microsoft.AspNetCore.OData`，把 EF `IQueryable` 暴露出去即自動支援 `$filter / $select / $expand / $orderby / $top / $count` + `$metadata` 描述文件。
- **正中定位**：OData 是微軟生態的整合標準，**Power BI / Excel / Power Automate / Dynamics 原生吃 OData**——對應產品「跟微軟生態整合」的賣點，是加分項而非負擔。
- **① 組織資料集**（使用者 / 部門 / 角色 / 主管 / 群組）：admin-svc 已有這些關聯表（SharedIdentity）。暴露成唯讀 OData entity set（Users / Departments / Roles / Memberships）即可——**薄層、最快**。
- **② 自定義資料集**（Dataset/DatasetRow，設計已備）：rows 以 JSON cells 存（寬表），兩種做法：
  - **(a) 簡單版**：暴露 raw rows（id / datasetKey / cells），客戶端自解 JSON——最快，但 `$filter` 不能精準打單一欄。
  - **(b) 強版**：把「每個資料集」動態變成「它自己的 OData table」（真欄位、可 `$filter` 單欄）——需動態建 EDM model，工較多，但最有企業感、最好賣。**建議先 (a) 再 (b)。**

**安全（企業客戶必問）：**
- 帶驗證（沿用 unify-jwt）、唯讀
- per-customer 部署 = 資料天生隔離、不跨租戶
- 機器對接（BI / 排程）建議發 API key / service principal，不逼用人類 JWT
- 可限制能查的 entity set / 欄位（避免暴露敏感欄位如 email）

**工程量：** 加法、非重寫；不碰前端、流程、DB schema。組織資料 OData = 小；自定義資料集 OData = (a) 小 /(b) 中。

**建議回客戶（話術）：**
> 「可以。我們後端是 .NET + EF Core，OData 是一等公民——我們能對你的組織資料與自定義資料集開唯讀 OData 端口（支援 $filter/$select/$expand/$metadata），帶驗證、per-customer 隔離，讓你的 Power BI / Excel / Power Automate 直接拉即時資料。組織資料可先上（薄層），自定義資料集接著。」

### 1.2（✅ 現場反饋 #2）代理人（委任）需要「接受」機制

**客戶情境：** 現行「被指定為代理人」是**指定即生效**；客戶反映應有 approve（接受）機制，因為：
- ① 某人 A 可能不想成為 B 的代理人（被指定方應有拒絕權）
- ② 有可能指錯人（誤指需可被擋下 / 撤回）

**現況（demo 版）：** delegator 在「代理人」設定指定某人 + 起迄日，**儲存即生效**——代理人馬上能看到並代簽 delegator 的待辦（E2E 實測過 Alice→Frank 即時生效）。被指定方不需同意、也不會被詢問。

**我方評估：合理，建議採納——中小型改動，但跨多層。**
- **資料模型**：Delegation 加狀態 `Pending → Accepted / Declined`（+ 既有 Revoked）。建立時 = Pending、**不生效**。
- **授權（關鍵一行）**：`IActorAuthorizer` 判斷「代理人可代簽」的條件，從「有 active 委任」改成「有 **Accepted** 且在有效期間的委任」。這就是「接受才生效」的落地點。
- **UI**：
  - delegator 端：指定後顯示「等待對方接受」（pending），非立即生效；可在對方接受前**取消**（解決「指錯人」）。
  - 被指定方：收到通知 +「X 指定你為其代理人，接受 / 拒絕」介面（解決「不想當」）。
- **通知**：指定時通知被指定方；接受/拒絕時回通知 delegator。
- **可選**：pending 超過 N 天自動失效。

**工程量：** 中（admin-svc Delegation domain 加狀態 + accept/decline 端點、`IActorAuthorizer` 改判斷、兩個 UI 加 pending/accept 介面、通知）。非大改，但 backend + 2 UI + 通知都要碰。

**建議回客戶（話術）：**
> 「好建議。目前是指定即生效；我們可以加一道『代理人接受』機制——被指定者會收到通知、需『接受』才生效，也能『拒絕』；指錯人的話，在對方接受前可直接撤回。兼顧『不想被指定』與『誤指』兩種情況。」

### 1.3（✅ demo 前已預判、現可佐證需求）自定義資料集功能

- demo 前即預判客戶會問「選項想改不用上新流程 / 連動選單」；OData 這題也再次帶到它。
- **現況：已超前**——設計 spec + 12-task 實作計畫已備：
  - `docs/superpowers/specs/2026-06-27-custom-datasets-design.md`
  - `docs/superpowers/plans/2026-06-27-custom-datasets.md`
- 差異化：連動選單（縣市→行政區）、寬表 distinct / 分組——業界多數工具原生沒有。
- 對客戶可直接展示「我們已經想清楚、有設計」。

### 1.4（🐞 Bug，現場發現）Dashboard「已完成案件」恆為 0

**現象：** 案件已跑完（Completed），但首頁 dashboard 的「已完成案件 / Completed (all-time)」仍顯示 0。

**根因（已定位並確認 code）：**
- 首頁 stat 計算 `bpm-ui/src/screens/Home.tsx:115`：`myCases.filter(c => c.status === 'Completed')`——以英文字串 `'Completed'` 精準比對。
- 但 `InboxRow.Status`（`bpm-svc/src/Application/Inbox/InboxRow.cs:14`）契約明寫是「**per-flow 顯示用字串、UI 不該 parse**」，每隻流程 inbox provider 各自吐**中文在地化**字串——例如 APE / TEO 的 Completed 都吐 `"已核准"`（不是 `"Completed"`）。
- 結果 `"已核准" === "Completed"` 永遠 false → `myCompleted` 恆 0。同問題也波及「My Active / Total / 取消數」與 Activity Feed 圖示判斷（`statusToActivityKind` 同樣比對 'Completed'/'Cancelled'/'Rejected'）。
- **一句話：UI 去 parse 一個契約上標明「純顯示、不可 parse」的欄位，而各流程吐在地化字串，永遠對不上 → 恆 0。**

**正解（乾淨修法）：** 在 `InboxRow` 加一個 **canonical 生命週期欄位**（如 `Lifecycle: Open|Completed|Cancelled|Rejected`），由每個 provider「結構化」設定（與顯示用的在地化 `Status` 分離）；dashboard 改用 `Lifecycle` 計數。
- 動到：`InboxRow.cs`（加欄位）、~12 個 `*_InboxProvider.cs`（各加一行 canonical 映射，本來就有 status switch）、`Home.tsx`（改用 lifecycle 計數）。
- 工程量：小–中、低風險、機械式。屬 lead-side 修。
- ⚠️ 不要用「在 Home.tsx 把中文字串列舉進來」的速修——脆弱，新流程換個字就破。

**建議：** dashboard 是客戶第一眼看的，這是低風險、高可見度的修，列為 demo 後 **quick win 優先修**。

### 1.5（能力確認 #4a）一人多職（multi-role / 多部門）

**客戶問：** 如何應對一人多職。**Jason 答：** principal↔role 應可應付。

**⚠️ 修正（2026-07-01，已讀原始碼）：身分模型支援「一人多角色」，但簽核授權目前是「單一指派人」、不是「角色共享佇列」——所以客戶預期的『有該角色就能簽』目前並未成立。先前把此項評為「≈0」是**錯的**。**
- ✅ **身分層 M:N 沒問題**：`PrincipalRole(PrincipalId, RoleId)` 一人可多角色（Jack 現有 `SYSTEM_ADMIN + PERSONA_SWITCH`）；`UserDept.IsPrimary` 一人可跨多部門。管理員能給一人掛多角色。
- ⚠️ **但簽核路由是「單一指派人」**：角色關卡用 `FindFirstUserInRoleAsync`——從該角色成員裡挑 **DisplayName 字母序第一個**，把案子指派給**一個特定人**（`CurrentAssigneeUserId`）。`ActorAuthorizer.CanActAsync` 只認「caller == 被指派人 或其代理人」，**完全不看角色**。→ 同角色其他人**看不到也不能簽**。
- ➡️ 客戶要的「有角色就能簽」＝「**角色共享佇列 (role queue)**」：任何持該角色者都能認領 / 簽該關。**目前未實作。**
- **修正工程量：不是 ≈0，是中型。** 要做角色共享佇列＝案子改成「pending 某角色」（非 pending 某人）、pending 佇列對該角色全員可見、`CanAct` 放行該角色任何人（+ 代理）。動到共用授權 primitive + 各有角色關的流程 + inbox。**與並簽/會簽同屬「多人簽核」家族的基礎件。**
- （原「seed 雙職帳號當 demo」的想法因「單一指派人」不成立：雙職者只在「字母序剛好第一」才收得到那關，且會位移既有指派人 Grace/Frank。）

### 1.6（能力確認 #4b）並簽（parallel approval）

**客戶問：** 支援並簽嗎？**Jason 答：** 猜可以、但目前沒這麼複雜的流程。

**確認：分兩層——**
- **Spec 層「可表達」**：ActorRef DSL 有 `collection`（any/all + `min_approvals`），spec 能描述「N 人並簽 / M-of-N 法定人數」。
- **但目前「沒有任何流程實作它」**：所有 cooked 流程的案件都是**單一 `CurrentAssigneeUserId`** + 循序狀態機；沒有多簽核人/法定人數結構（grep 只在 Spec ActorRef 定義檔命中，無任何流程 runtime 用到）。
- **要真做並簽**＝一個「新流程模式 + lead-side primitive」：案件改成「多待簽人」結構（per-approver 決定子表或待簽清單）+ 法定人數狀態機（全簽 / 達 `min_approvals` 才前進）。chef 能 cook，但要先有這個 primitive。
- 工程量：**中–大**（不是開關，是並行狀態 + 法定人數邏輯）。

**BPMN 子問題「並簽會多 user task 同時亮黃燈嗎？」**
- **目前不會。** `bpm-ui/src/components/BpmnView.tsx` 的高亮吃單一 `activeStep`/`currentNode`，一次只有一個節點上 `bpm-active`（黃燈）。
- 要兩個 user task 同時亮：BpmnView 改成吃「一組 active 節點 id」全部上 marker（**這部分小**，marker 基建 `addMarker` 已可標任意節點）；**難的是 runtime/案件要能同時持有多個並行待辦**（接上面的並行狀態模型）。

**建議回客戶（話術）：**
> 「架構與 spec 都支援並簽 / 法定人數的概念；目前還沒有這麼複雜的範例流程。要做的話我們會加一個『並行簽核』流程模式（多待簽人 + 法定人數），BPMN 也會讓並行的 user task 同時亮起。屬中型開發，可排。一人多職則是現成就支援，只是還沒做成 demo。」

**延伸：會簽也支援嗎？——先分清楚簽核型態（這幾個詞常被混用，分清才答得準）：**

| 簽核型態 | 意思 | 現況 |
|---|---|---|
| 串簽 / 順簽（sequential） | 多關依序簽（主管→財務→…） | ✅ **已支援**——每隻流程就是循序簽核鏈 |
| 加簽（conditional add） | 條件到了自動加一關（如 ≥7 天加 VP） | ✅ **已支援**（LEAVE VP、WFH 上級）。但「簽核人臨時手動加別人」(runtime ad-hoc) 目前無 |
| 代簽（delegate） | 委任他人代簽 | ✅ **已支援**（代理人；客戶還想加接受機制＝反饋 #2） |
| 並簽（parallel, any / quorum） | 多人同時簽，達 M-of-N 即過 | ⚠️ spec 可表達、**未實作**（見上） |
| **會簽（joint, all-must-sign）** | 多單位 / 多人**同時都要簽**才過 | ⚠️ **同一個待建 primitive 的「全簽」變體** |

**重點：會簽 ≈ 並簽的「全部都要簽」版本。** 一旦做了「並行簽核」primitive（多待簽人 + 完成條件），會簽（all）和並簽（any / quorum）只是同一個東西的**設定差別——一起做、不是兩份工**。

⚠️ **建議跟客戶確認他講的「會簽」是哪一種**：「多單位同時都要簽（joint / parallel-all）」還是「依序多關（串簽）」——這兩個答案天差地別（後者現成、前者要開發）。台灣企業實務上有人把「會簽」當串簽用。

> **決議（Jason 2026-06-30）：多人簽核支援度目前不足，並簽 + 會簽案例（程式邏輯 + BPMN 高亮）排入計畫。** 交付物見 §4。

### 1.7（待補）其他現場反饋 / 提問

_[Jason 補：價格 / 合約 / 上線時程 / 特定流程需求 / 權限與稽核 / 報表 / SSO / 既有系統對接 / 客戶疑慮 / 競品比較…現場聽到什麼都列這，我再整進對應評估。]_

---

## 2. 整合能力 — 完整 roadmap（把「餵進來」+「拉出去」合成一張圖）

這次 OData 是「**往外暴露**」；跟 demo 前聊的「把客戶組織資料**餵進來**」合起來，才是完整的整合故事。架構天生支援：admin-svc 是 canonical 身分源、可被外部餵也可被外部讀。

| 方向 | 能力 | 工程量 | 備註 |
|---|---|---|---|
| 餵進來 | ① Microsoft SSO（Entra/AD, OIDC） | 小–中 | 登入頁已有占位「Microsoft 登入即將推出 add-sso-oidc」；最先得分 |
| 拉出去 | ② OData：組織資料（本次客戶要的） | 小 | 薄層 over SharedIdentity |
| 餵進來 | ③ 組織資料批次匯入（CSV/Excel） | 小–中 | 與自定義資料集的「匯入」基建共用 |
| 拉出去 | ④ OData：自定義資料集 (a)→(b) | 小→中 | (b) 動態 EDM，最好賣 |
| 餵進來 | ⑤ Provisioning / 同步 API（SCIM-like） | 中 | 客戶 HR/IT 推送或定時同步 |
| 餵進來 | ⑥ 目錄同步連接器（拉 Entra/AD） | 中–大 | 衝突解決 / 刪除語意 / 排程，水較深 |

**建議順序（先得分 → 水深）：** ① SSO → ② OData 組織資料 → ③ 批次匯入 → ④ OData 自定義資料集 → ⑤ 同步 API → ⑥ 連接器。

> 務實提醒：①②③④ 是低風險快贏；⑤⑥ 是雙向同步、水較深，建議等客戶真要、且談定範圍再做，別在報價時過度承諾時程。

---

## 3. 內部技術探討（非客戶反饋，存檔）

- **.NET 後端 → Node.js 評估**（Jason 內部提問，現階段不做）：等於整個後端重寫——兩後端每層 + 10+ 隻已 cook 流程 + chef codegen + 跨切面 primitive + EF→Prisma migrations + 測試 + 部署。前端（React）、DB schema、產品設計不受影響。**結論：現階段不動；對外是穩定 REST/JWT 合約，真有 Node 需求多半只需做 Node adapter，不需整換。** 諷刺的是——OData 那題正好說明留在 .NET 的好處。

---

## 4. 下一步建議

- [ ] **Jason 補齊** §0 / §1.3 的其餘 demo 反饋，我再整進評估。
- [ ] 決定是否把「整合能力（含 OData）」做成正式 **mini-spec + 報價用 roadmap**（客戶真需求，值得一頁講清楚分幾階段、各多大工）。
- [x] **自定義資料集**：✅ 已實作（連動選單 + shadcn row edit-mode，4 服務 loop 實測）。
- [x] **代理人接受機制**（反饋 #2）：✅ 已實作（Pending→Accepted、兩端 UI、通知；Chrome 實測）。
- [x] **🐞 Dashboard 已完成數恆 0**（反饋 #3 / bug）：✅ 已修（canonical `InboxRow.Lifecycle`，dashboard 改用它計數；實測顯示 8）。
- [x] **一人多職 / 角色共享佇列**（反饋 #4a）：✅ 已實作（`CurrentAssigneeRoleCode` + 角色感知 `CanActAsync`，涵蓋全部 4 隻有角色關的流程；Chrome 實測）。
- [x] **並簽 / 會簽 / 並行簽核**（反饋 #4b）—— ✅ **已決定排入計畫（Jason 2026-06-30）**。交付物三件：
  - ① **並行簽核 primitive（lead-side）**：案件改成「多待簽人」結構（per-approver 決定子表 / 待簽清單）+ 完成條件（會簽=all、並簽=any/quorum 的 `min_approvals`）的狀態機。
  - ② **一隻示範流程**（chef cook）同時演示並簽與會簽。
  - ③ **BpmnView 多節點同時高亮**：改成吃「一組 active 節點 id」全部上 `bpm-active`（marker 基建已支援；runtime 要能同時持有多個並行待辦）。
  - 串簽 / 加簽 / 代簽已現成、不在此項。規模：中–大型。**下一步**：開 brainstorming → spec → plan（像自定義資料集那樣）。
- [ ] 若客戶要 PoC：OData 組織資料（薄層）是最快能 demo 給他看的整合切片。
