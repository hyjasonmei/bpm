# chef-agent — 怎麼手動跑 poll（local + 雲端）

> 排程（launchd 每 5 分鐘）目前**沒啟用**，所以 poll 由你手動觸發。
> 這份是「我要按一次 poll，讓 chef 把待煮的 flow 煮出來」的操作手冊。

## 心智模型（30 秒）

- agent 是 **one-shot**：跑一輪 poll → 掃過「所有 enabled 的環境」→ 結束（排程會每 5 分鐘再叫一次，但現在沒排程，你自己叫）。
- 一輪 poll **最多只煮一隻** flow（優先序：卡住可重試 > 使用者已回覆的 OnHold > 新送出的 Submitted）。
- 同時對所有 enabled 環境做 merge 檢查 + 發 Telegram 通知。

## poll 抓哪些環境？只抓 `enabled: true` 的

`chef-agent.json` → `environments[]`：

| env | baseUrl | enabled |
|---|---|---|
| local | http://localhost:5266 | `true` |
| azure-poc | https://poc-flowcook-admin-api.azurewebsites.net | `false` ← 雲端目前**被跳過** |

程式依據：
- `Program.cs` → `foreach (var env in config.EnabledEnvironments)`
- `AgentConfig.cs` → `EnabledEnvironments => Environments.Where(e => e.Enabled)`

所以「現在按 poll 只會打本機」。要含雲端，得把 azure-poc 打開（見下）。

## 怎麼跑 + log 去哪（重要）

agent 只用 `Console.WriteLine` 輸出，**自己不寫 log 檔**。log 檔（`agent.log`）只有
**launchd 排程模式**才會被寫（plist 的 `StandardOutPath` 重導）。所以**手動** `dotnet run`
時輸出只進你的終端機 —— 要留檔（也方便別人盯）就自己用 `tee` 導：

```bash
cd /Users/jason/claude/bpm/chef/agent
dotnet run --project Bpm.ChefAgent -- chef-agent.json 2>&1 | tee -a ~/claude/bpm-cooks/logs/agent.log
```

> 用獨立終端機跑，別用 `! ` 貼進 Claude session —— 一輪 cook 可能好幾分鐘，會卡住對話。

前提：本機 admin-svc(5266) + bpm-svc(5290) 在跑；本機佇列要有 Submitted flow，否則它只做 merge 檢查就結束（不會煮）。雲端則看 azure-poc 是否 enabled（見下）。

## B. 把雲端（azure-poc）加進來

> ✅ **現況（2026-06-17）：已幫你設好** —— azure-poc 的 `chefToken` 已換成 KV 真 token、
> `enabled` 已改成 `true`，雲端 chef API 實測回 200（佇列裡有 WFH）。
> 也就是說現在直接跑上面那行 `dotnet run … | tee …` 就會同時掃 local + 雲端。
> 下面是「下次要重設 / 換環境」時的步驟備查。

改 `chef-agent.json` 兩個地方（這檔有 gitignore，放真 token 安全）：

### 1) 換成正確的 chef token（**重點**）

檔裡 azure-poc 的 token 是舊的（會 401）。正確的在 Key Vault：

```bash
az keyvault secret show --vault-name kv-poc-flowcook -n chef-token -o tsv --query value
```

把這串（48 字元）貼到 azure-poc 的 `chefToken`。

### 2) 打開它

azure-poc 的 `"enabled": false` → 改成 `true`。

### 跑之前先驗 token 通不通

```bash
TOK=$(az keyvault secret show --vault-name kv-poc-flowcook -n chef-token -o tsv --query value)
curl -s https://poc-flowcook-admin-api.azurewebsites.net/api/chef/flows/tasks \
  -H "Authorization: Bearer $TOK"
```

預期回 200 + JSON（`submitted` / `awaitingChef` / `approvedAwaitingMerge`）。
⚠️ auth header 是 **`Authorization: Bearer <chefToken>`**，不是 `X-Chef-Token`（用錯 header 會全 401，別被騙）。

驗過後，跑上面那行 `dotnet run … | tee …` → 這次會同時掃 local + 雲端。

## 這次雲端測試會發生什麼

- 你的雲端 **WFH（Submitted）** 已經在佇列裡。poll 會認領它（Submitted→Cooking），在一個 git worktree 裡開 headless `claude` session 煮。
- cook 的 chef MCP 是 **env-aware** 的（`WorktreeManager.WriteMcpConfigAsync(worktree, env)`）→ 指向**雲端** admin-svc，所以 chef 的進度 / 提問 / 完成訊息會寫進雲端的 Flow → **雲端 Cook tab 即時更新**。
- 一輪只煮一隻。本機佇列是空的，所以它會挑雲端的 WFH。

## 怎麼盯

- **終端機**：手動跑就直接看 stdout。
- **log 檔**：`~/claude/bpm-cooks/logs/agent.log` —— 只有你用了上面的 `tee`（或排程模式）才有內容。
- **Telegram**：目前 `chef-agent.json` 的 `telegram` 是 `null`，所以 agent **不會**自己發 TG（只會印 `[tg-noop] …`）。要 agent 主動推播再去填 `telegram: { botToken, chatId }`。
- **雲端 Cook tab**：admin-ui → 開那隻 flow → Cook tab → 看 chef ↔ user 對話串即時長出來（這是這次測試的主要觀察點）。

## 注意事項（雷）

- **`gh` 沒裝 → local PR 模式**：agent 會把 cook commit 到本機 branch，但不會開 PR。雲端「Publish」是 gated on merge，所以要上線得自己 merge 那條 branch + 在雲端 admin-ui 按「Mark merged」。但 cook 本身 + Cook tab 更新不受影響。
- remote cook 的程式碼是寫進**這台 Mac** 的 worktree，只是狀態 / MCP 指向雲端 admin-svc。雲端 stack 要開著（目前是開的）。
- 全域單一 session（file lock）——別同時跑兩個 poll。

## 測完把雲端關掉

把 azure-poc `"enabled": false` 改回去，避免之後的 poll 不小心又去煮雲端。
