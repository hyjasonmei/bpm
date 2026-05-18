# BPM 流程引擎設計（客戶版）

> 寫給業務、IT 採購、HR/IT 主管 — 一份「為什麼這個架構好賣」的說明。技術細節在 `openspec/changes/add-process-runtime/design.md`。

## 一、核心概念：四張表撐起整個引擎

```
ProcessInstance   ←  一個 case（Wilson 5/8 的請假申請）
ProcessTask       ←  一個簽核點（主管簽 / HR 簽 / IT 審）
TaskHistory       ←  審計日誌（誰在何時做了什麼）
SpecSnapshot      ←  起案那一刻的流程定義（凍結）
```

**重點**：流程定義（spec）和實際 case（instance）是分離的。

---

## 二、為什麼要有 SpecSnapshot

每次起案時，引擎會把當下的流程定義**完整 deep-copy** 一份到 instance 裡。之後即使流程被改了（比如管理員把「部門主管簽」改成「副理簽」），**正在跑的 case 不受影響**。

對客戶的訴求：

- 「我這張請假申請是上個月按那時的規則送的，怎麼這個月新規則上線後，我的單子變得要多人簽？」← 不會發生
- 5 年後 ISO 稽核員問：「2026 年這張單子當時是按什麼流程跑的？」← 直接從 instance 撈出 snapshot 給看，byte-for-byte

---

## 三、Task 是誰的？— Actor Resolver

每個簽核點不寫死「找王主管」，而是寫**規則**：

| 規則 | 範例 |
|---|---|
| `expr:submitter.manager` | 申請人的直屬主管 |
| `functional_members:hr` | 所有 HR 角色的成員（任一人可簽）|
| `collection mode=all, actors=[A, B, C]` | A B C 都要簽（會簽）|
| `collection mode=any, min=2, actors=[A, B, C]` | A B C 任 2 個簽即可 |

引擎在 spawn task 時呼叫 Actor Resolver，把規則轉成具體 user list。**組織異動或新人到職完全不用改流程定義。**

---

## 四、Delegation（代理人）— 主管請假怎麼辦

每個使用者可設定「2026/05/10–05/15 我的審批請小李代理」。引擎 spawn task 時會：

1. Actor Resolver 算出原本是「王主管」
2. Delegation Service 查王主管目前有沒有代理人
3. 有 → task 的 `OriginalAssigneeUserId = 王主管`、`ActualAssigneeUserId = 小李`
4. 寫一筆 `DelegationApplied` 到 audit

對客戶的訴求：「我們主管常出差，案子卡住怎麼辦？」← **自動代理，audit 完整可查**

---

## 五、TaskHistory — Append-only Audit

任何狀態變化（task 被指派、被認領、被簽、被退、流程結案）都寫一筆 history row，**EF Core 攔截器強制禁止 UPDATE / DELETE**。連工程師 console 進 DB 改都會 throw。

對客戶的訴求（特別是 TS 16949 / ISO 9001 認證的製造業）：

- 「稽核員問：這張單到底經過誰手？什麼時候？」← **一個 query 撈完整時間軸**
- 「會不會被誰偷改？」← 應用層擋 + DB trigger 雙層

---

## 六、Notification — on_assign / on_complete Hook

流程定義可以宣告：「task 被指派時發 mail 給簽核者」「流程結案時發站內通知給申請人」。引擎在狀態轉換時自動觸發，**不用每個流程自己寫通知 code**。

通知用 outbox pattern：

1. 引擎在交易內 insert NotificationDelivery row（Status=Queued）
2. 背景 worker 撿出來實際發 mail / IM
3. 發送結果寫回 row

對客戶的訴求：「不用每個流程手動 hook 通知，平台統一管」

---

## 七、Gateway — 條件分支

例：請假 ≥ 7 天要再加簽副總。流程定義裡寫：

```
gateway: leave_days_check
  condition: leave.days >= 7
  true → approval_vp
  false → end
```

引擎跑到 gateway 時用 instance 當下的 form_data 算 condition，選邊走。

未來會用 CEL（Google 開源 expression language），現在先實作子集（`==`, `!=`, `>=`, `&&`, `||`）。

---

## 八、為什麼這設計能賣

### 對 IT

- 流程定義是 JSON，可版本化、diff、code review
- 引擎本身不認識「請假」「採購」，純粹跑狀態機，新流程不用碰 engine code
- 每個 instance 自帶 snapshot → 流程演進不會打到歷史 case

### 對業務 / HR

- 換主管 / 加部門不用改流程
- 主管休假設代理 → 案子不會卡
- 稽核問什麼都能秒回

### 對 C-level

- ISO / TS 16949 audit 不用焦慮
- 微軟 Entra ID 整合進來後，HR 異動 → AD → 流程簽核人自動同步（roadmap）

---

## 延伸閱讀

技術 buyer 想看深一點的，請開：

- `openspec/changes/add-process-runtime/design.md` — state machine 流程、為什麼用 EF interceptor 而不是 DB trigger、CEL vs JSON Logic 取捨
- `openspec/changes/add-process-runtime/specs/bpm-process-runtime/spec.md` — Requirements + GIVEN/WHEN/THEN scenarios
- `openspec/changes/add-actor-and-org-model/` — Actor Resolver 細節
- `openspec/changes/add-delegation/` — Delegation 機制細節
- `openspec/changes/add-notification-engine/` — 通知 outbox 細節
