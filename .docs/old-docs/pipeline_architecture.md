# Multi-Agent Dev Pipeline — Phase B Architecture

> Phase B 的 pipeline 設計：客戶送 spec 後，由多個 AI agent 協作寫 code、互審、跑測試，Jason 跟客戶各只在最關鍵的一個 gate 介入。

> 對應 `inovation_idea.md` §9 Phase B。Phase A 不蓋這套——先用人工跑 1-3 客戶累積經驗，再自動化。

---

## 1. Pipeline 全貌

```
[客戶 9 step UI] → spec.json → POST /api/spec
                                 ↓
                        ┌────────────────┐
                        │ Spec Queue     │
                        │ (Postgres /    │
                        │  Redis Stream) │
                        └────────┬───────┘
                                 ↓ poll
┌── 我們公司一台 always-on 機器（runner host）─────────┐
│                                                       │
│  ┌─ ① Dev Agent（Claude Code CLI）────────────────┐  │
│  │ Input: spec.json + prompt template + customer    │  │
│  │        repo                                       │  │
│  │ Steps:                                            │  │
│  │   git checkout -b spec-{spec_id}                 │  │
│  │   write code per spec                             │  │
│  │   gh pr create                                    │  │
│  │ Output: PR url                                    │  │
│  └────────────────┬──────────────────────────────────┘  │
│                   ↓                                     │
│  ┌─ ② Review Agent（Claude Sonnet/Opus）───────────┐  │
│  │ Input: PR diff + spec.json + checklist           │  │
│  │ Output: APPROVE / REQUEST_CHANGES with reasons   │  │
│  │ If reject → loop back to ① with feedback         │  │
│  │            (iteration cap = 3)                    │  │
│  └────────────────┬──────────────────────────────────┘  │
│                   ↓ approve                             │
│            gh pr merge → CI/CD                          │
│                   ↓                                     │
│            dev 環境部署完成                              │
│                   ↓                                     │
│  ┌─ ③ E2E Agent（Claude Agent SDK + HTTP tool）────┐  │
│  │ Input: spec.json + dev URL + test data generator │  │
│  │ Steps:                                            │  │
│  │   1. 建測資（員工、案件 sample）                  │  │
│  │   2. 跑表單送出 → 經過所有節點                   │  │
│  │   3. 比對 final state 跟 spec 預期               │  │
│  │   4. 跑 escalation / SLA / edge cases            │  │
│  │ Output: PASS / FAIL with report                   │  │
│  │ If fail → 開 issue 回 ①（iteration cap = 3）     │  │
│  └────────────────┬──────────────────────────────────┘  │
│                   ↓ pass                                │
│            ★ Telegram bot 通知 Jason                   │
└────────────────────────────────────────────────────────┘
                    ↓
            Jason 看 dev 環境 ★ 人類 gate 1
                    ↓ 沒問題
              push tag (v1.x.x)
                    ↓
            CI/CD → 客戶 STG 環境
                    ↓
            Email Mary：「您的 STG 已上線，請驗收」
                    ↓
            Mary 點「核准」 ★ 人類 gate 2
                    ↓
            CI/CD → 客戶 PRD
                    ↓
            完成。客戶 / Jason / 系統都有紀錄
```

---

## 2. Agent contracts

### ① Dev Agent

**任務**：從 spec.json 產出客戶的 workflow 實作 code，開 PR。

**輸入**：
- `spec.json`（spec deliverable）
- 客戶的 repo / branch（git clone 過來）
- Prompt template（包含 CLAUDE.md 的 convention）
- 上一輪 Review Agent 的 feedback（如果是 retry）

**輸出**：
- 一個 git PR，包含：
  - C# Workflow Engine code（Domain / Application / Persistence / Api）
  - EF migration
  - React form component
  - Notify template
  - Unit tests

**實作**：
```bash
claude --dangerously-skip-permissions \
  --append-system-prompt "$(cat prompt_template.md)" \
  -p "Read spec.json from this repo. Generate workflow code per CLAUDE.md.
      Output a clean PR with all changes."
```

**iteration cap**：3 次。第 4 次直接 escalate 給 Jason 處理。

---

### ② Review Agent

**任務**：審 PR 是否符合 spec 跟 convention，獨立判斷不被 Dev Agent 影響。

**輸入**：
- PR diff（`gh pr diff <pr-id>`）
- `spec.json`
- Review checklist（從 Phase A 累積而來）

**Checklist 範例**（Phase A 的 review 問題會慢慢累積進來）：
- [ ] 所有 spec.userTasks[].fields 都在 form 元件中對應到？
- [ ] 所有 spec.approvalNodes 都在 workflow state machine 中實作？
- [ ] EF migration 有對應到所有需要新增的欄位？
- [ ] 沒有 hardcode customer name / 環境 URL？
- [ ] Convention 對嗎（MediatR command/query 命名、Clean Architecture 分層）？
- [ ] 有對應的 unit test？
- [ ] notification template 用的是 spec.notifications 內容，不是 hardcode？

**輸出**：
```json
{
  "decision": "APPROVE" | "REQUEST_CHANGES",
  "reasons": ["...", "..."],
  "blocking_issues": [...],
  "non_blocking_suggestions": [...]
}
```

**Reject 後**：把 reasons 餵回 Dev Agent 作為 retry context。

**獨立性**：Review Agent 用「不同的 Claude session」、不同的 system prompt，不能跟 Dev Agent 共享 context，避免 echo chamber。

---

### ③ E2E Agent

**任務**：在 dev 環境真的跑流程，比對結果跟 spec 預期一致。

**輸入**：
- `spec.json`
- dev 環境 URL + admin credentials
- 測資生成 prompt

**Tools**（透過 Claude Agent SDK 提供）：
- `http.post / get / put`（call API）
- `db.query`（查 dev DB 確認 state）
- `time.advance`（測 SLA / escalation）

**測試案例自動生成**：
- 對每個 userTasks → 至少一張案
- 對每個 decision branch → 至少一條路徑
- 對每個 SLA threshold → 越過跟沒越過各一張
- 對每個 conditional field → 觸發跟沒觸發各一張

**輸出**：
```json
{
  "result": "PASS" | "FAIL",
  "tests_run": 12,
  "tests_passed": 11,
  "failures": [
    {
      "test": "leave_8_days_should_route_to_VP",
      "expected": "approver = VP_engineering",
      "actual": "approver = direct_manager"
    }
  ]
}
```

**Fail 後**：開 issue（內含 failure detail）→ 回 Dev Agent retry。

---

## 3. 人類 gates

### Gate 1：Jason 看 dev

**通知**：Telegram bot 推訊息：
```
✓ Customer: Acme
✓ Spec: 請假流程 v1
✓ Review Agent: APPROVE
✓ E2E Agent: 12/12 PASS
🔗 PR: https://github.com/.../pull/123
🔗 Dev: https://dev-acme.bpm.internal
[Approve & tag] [Send back to Dev]
```

**Jason 做什麼**：
- 看 PR 邏輯（5-10 分鐘）
- 在 dev 環境隨機跑一個 case 直覺嗅探
- 沒問題：點 Approve → bot 自動 push tag → CI 自動 deploy 客戶 STG
- 有問題：點 Send back → bot 開 issue 給 Dev Agent retry

### Gate 2：客戶（Mary）驗收 STG

**通知**：Email + 客戶 site 內的紅點通知
```
Hi Mary,
您的請假流程已部署到 STG 環境，請驗收：
👉 https://stg-acme.bpm/verify/spec-{spec_id}

請執行：
- 用測試帳號送一張請假
- 檢查通知是否正確
- 確認核准流程符合您的設計

驗收完成後，點「核准上 PRD」。
```

**Mary 做什麼**：
- 跑她準備的真實 case（譬如員工請婚假 5 天）
- 看到結果跟她想像的一樣 → 點核准
- 不一致 → 點「需修正」+ 寫 comment → 開 issue 回 Dev Agent

---

## 4. 失敗 / Escalation 路徑

| 情況 | 處理 |
|---|---|
| Review Agent reject 第 1-2 次 | 回 Dev Agent 改 |
| Review Agent reject 第 3 次 | 升 Jason 手動處理 |
| E2E Agent fail 第 1-2 次 | 回 Dev Agent 改 |
| E2E Agent fail 第 3 次 | 升 Jason 手動處理 |
| Cost cap 超過 | 暫停 spec、通知 Jason |
| Time cap 超過（譬如 24h 內沒進到 Jason gate） | 通知 Jason |
| Jason rejects dev | 開 issue 回 Dev Agent 帶 Jason 的 feedback |
| Mary rejects STG | 開 issue 回 Dev Agent 帶 Mary 的 feedback |
| Spec 本身有歧義 | Dev Agent 先試解讀；解不開時 → 通知 Jason → Jason 跟 Mary 補問 |

---

## 5. 配套基礎設施

### Spec Queue
- Postgres `spec_queue` 表 + `FOR UPDATE SKIP LOCKED` 取
- 或 Redis Stream
- 起點：Phase B 流量低，Postgres 就夠

### Runner Host
- 一台 always-on Linux VM（雲或公司內網都行）
- 跑 systemd service：`spec-poller.service`
- 每分鐘 poll spec_queue
- 每個 spec 起一個 isolated workspace（tmp dir + git clone）

### Cost / Iteration Tracking
- 每 spec 一個 `pipeline_run` 紀錄：
  ```
  spec_id, customer, started_at, dev_iter_count, review_iter_count,
  e2e_iter_count, total_input_tokens, total_output_tokens, cost_usd,
  status (running/escalated/jason_review/customer_review/done)
  ```

### Review Checklist Versioning
- `review_checklist.md` 在 main repo
- 每次 Phase A 學到新 corner case 就追加
- Phase B 啟動時把這份檔當 Review Agent system prompt

### Telegram Bot
- 已有現成 plugin（Jason 在用）
- bot 推通知 + 提供 callback button（Approve / Send back）
- callback 觸發 webhook → runner host

---

## 6. Phase A → Phase B 平滑過渡

Phase A 期間：
- Jason 做的每件事都記下來（review 抓到什麼、E2E 跑了什麼）
- 寫進 `review_checklist.md` v0
- 寫進 `e2e_test_fixtures.json` v0

Phase B 啟動時：
- Review Agent 的 prompt = Phase A 的 review_checklist.md
- E2E Agent 的測資生成器 read e2e_test_fixtures.json
- 第一個 Phase B 客戶的 pipeline 跑得不順時，Jason 從介入位置往上補 prompt

**這就是為什麼 Phase A 一定要先做手動**——沒這份 know-how，Phase B 的 agent prompt 就是空殼。

---

## 7. What the pipeline produces — the runtime target

Dev Agent's PR doesn't write a bespoke workflow engine per customer. The
generated code drops a `spec.json` + form components + notification
templates into the customer repo and configures them against the **shared
process runtime** (`Bpm.Application.Process.Runtime.IProcessRuntime`,
implemented in `Bpm.Persistence.Process.ProcessRuntime`). That engine
takes an immutable `SpecSnapshot` at instance start, drives nodes
through `IActorResolver` → `IDelegationService` → `INotificationDispatcher`
hooks, evaluates gateway `condition` strings via the CelNet evaluator,
and writes append-only `TaskHistory` rows the E2E Agent scrapes for its
PASS/FAIL diff. This is also why §3 E2E Agent's `db.query` tool can
afford to be generic — the table layout is fixed (`ProcessInstances`,
`ProcessTasks`, `TaskHistories`); only the `SpecSnapshot` JSON content
varies per customer flow. See `bpm-svc/CLAUDE.md` for runtime invariants.

---

## 8. Open Implementation Questions

- Iteration cap 預設 3 次合理嗎？要不要 dev/review/e2e 各自獨立 cap？
- Cost cap 預設多少？$5 / spec？$20？
- Review Agent 跟 Dev Agent 用同一個 Claude 模型嗎？還是 Review 用更強的（Opus）抓 bug、Dev 用便宜的（Sonnet）寫量？
- Customer STG approval 是 Email 點 link，還是客戶 site 內 admin page？
- 多 spec 並行：runner host 一次跑幾個 pipeline？需不需要排隊？
- 失敗模式：spec 本身有歧義，agent 自己卡住——怎麼判斷「卡」（無進展超過 N 分鐘？token 超過 cap？）

---

*Last updated: 2026-05-02 by Jason + Claude*
