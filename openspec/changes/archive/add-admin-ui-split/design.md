# Design — add-admin-ui-split

## 1. 為什麼是 monorepo workspace 而不是兩個 git repo

**Alternative**：完全獨立 repo（bpm-ui-employee, bpm-ui-admin），share 透過 npm package。

**Rejected**：
- Setup 麻煩：要架 npm registry / 用 git submodule
- Lock-step 變動痛：每次共用 component 改了要 publish package、bump version，兩邊更新
- POC 階段沒人有時間管套件版本號

採用 **single repo + npm workspaces**：
- `bpm-shared` 是 root 下的本地 package，import 路徑 `@bpm/shared`
- 改共用元件直接同 commit 反映在兩個 UI
- 部署仍是兩份獨立 dist
- 未來真的要拆 repo（可能有公司外包接 admin UI），workspace 結構也方便切

## 2. bpm-shared 應該放什麼

**放**：
- 純函數 lib（apiFetch, cn, parsing utils）
- 通用 types（DTO mirrors）
- 基礎 UI primitive（Button, Card, Form inputs, ConfirmDialog）
- 共用 hooks（useDebounce 等）

**不放**：
- 業務畫面（Home, Onboarding, etc.）
- 業務邏輯（workflow.ts, role.ts — 雖然兩邊都會 import role，但 role 內容兩邊用法不同）
- 各自 AppLayout（員工跟 admin 的版面風格不同）

劃線標準：「如果這個檔案改了會同時影響兩邊行為」→ shared；「只是恰好兩邊都用，但用法可能分歧」→ 各自一份。

## 3. 為什麼 admin UI 用 sidebar 而員工用 top nav

員工 UI（bpm-ui）目前是 top nav + 大內容區，仿 SaaS app 的「跑流程」場景。

Admin UI 預期會有：
- 多個設定面板（Sandbox, Roles, Audit, Tenant config, Onboarding）
- 每個設定都是獨立小工具
- 同時要看「目前在哪個面板」+「這個面板下的子選單」

→ 二層導航（左側 sidebar 主分類 + 主內容區內的 tab）比 top nav 友善。

視覺上也明確區隔：員工進 admin UI 一眼看出版面不同，不會搞混。

## 4. Admin role guard 的時機

`bpm-admin-ui` 載入時：
1. 檢查 localStorage 有沒有 `bpm_jwt`
2. 沒有 → redirect 到 `/app/login`（或 dev mode 直接彈 dev-login）
3. 有 → decode JWT 看 `roles` claim 是否含 `admin`
4. 沒 admin → 顯示「You don't have permission. Redirecting to employee app...」3 秒後 location.replace('/app/')
5. 有 admin → 進 admin UI

額外保險：所有 admin API endpoint 自己也要 `[Authorize(Roles="admin")]`。前端 guard 是 UX，後端 guard 是 security。

## 5. JWT 跨子應用共用

兩個 UI 同 origin（同一個 host，不同 path），localStorage 自然共享。前端讀寫都用 `bpm_jwt` 鍵名。

如果未來 prod 改用不同 sub-domain（admin.bpm.com vs app.bpm.com），localStorage 不能跨 domain，要改用 cookie + Domain=.bpm.com，這超出本 change 範圍。當下決策：sub-path 部署。

## 6. Onboarding 為什麼是 admin 工具

Onboarding wizard 是「IT / 流程設計師起一個新流程」用的，產出 spec.json 給後端跑。員工不會用、不該看到。屬於 admin 的「設定平台」工具。

搬遷風險：onboarding 引用 bpm-ui 內部某些 component / lib（mocks, role, etc.）。Migration 過程會發現哪些是該 promote 到 bpm-shared、哪些只屬於 onboarding。每個重構成本都不高，但有實際工作量。

## 7. dogfood.command 怎麼改

目前 dogfood.command 大概是 `cd bpm-ui && npm run dev` 之類。

After：
```sh
# Terminal 1: backend
cd bpm-svc && dotnet run --project src/Api

# Terminal 2: employee UI
cd bpm-ui && npm run dev   # → http://localhost:5173

# Terminal 3: admin UI
cd bpm-admin-ui && npm run dev   # → http://localhost:5174
```

或用 `npm run dev` from root → concurrently 跑兩個 UI（package.json scripts）。

dev port 不衝突 — 員工 :5173、admin :5174。

## 8. CORS 設定

`appsettings.json` 的 `Cors:BpmUiOrigin` 從單一字串變多個：

```json
"Cors": {
  "BpmUiOrigin": "http://localhost:5173,http://localhost:5174"
}
```

Program.cs 已經 `Split(',')` 處理多 origin（看程式現況確認）。Prod CORS 也要更新成兩個 origin。

## 9. 風險：兩份 build 的版本漂移

如果 bpm-ui 用 v1 的 type，bpm-admin-ui build 時還是 v0，會出現 runtime 錯誤。

緩解：
- `bpm-shared` 是 file: dependency，不是 npm registry — 兩邊永遠拿 working tree 的版本
- CI 必須 build 兩個 UI 後才算成功（一個 type 改錯兩邊都掛）

## 10. 不做的事

明確列出來避免 scope creep：

- ❌ 不改後端 API
- ❌ 不引入 React Router / 其他 routing 庫（兩個 UI 還是用簡單 state-based screen）
- ❌ 不引入 Tailwind config 共用（複製貼上 — Tailwind 設定改動低頻）
- ❌ 不上 Storybook
- ❌ 不寫 design system 文件
- ❌ 不一次性整理所有元件 — 只搬該搬的，其他維持原樣

這個 change 的成功標準：「Onboarding 從員工 UI 消失、進 admin UI 能用」。其他 admin 功能（Sandbox, Impersonation, Roles）由它們各自的 change 加進來。
