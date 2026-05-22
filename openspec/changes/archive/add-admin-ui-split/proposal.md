## Why

`bpm-ui` 目前同時放員工日常用的「跑流程 / 看自己案件」和管理員 / 流程設計師用的「AI Onboarding wizard」。兩種使用者目標、節奏、技術背景完全不同，混在同一個前端造成：

- **員工 nav 被污染**：員工點開 nav 看到「Onboard」這個對他無意義的字，要解釋它是什麼
- **未來 admin 功能會塞越來越多**：sandbox、impersonation、admin user manage、system settings、audit dashboard、licensing、tenant config… 全塞 bpm-ui 會讓員工 UI 變雜貨店
- **權限模型不一致**：員工 UI 預設大家可進，admin 功能要 case-by-case 隱藏；管理頁應該預設「沒 admin role 直接 redirect 出去」
- **部署 / 安全策略不同**：admin UI 未來可能要綁 IP、要走 SSO、deploy cycle 慢；員工 UI 要快速迭代

這個 change 把專案結構從單一 `bpm-ui` 拆成 monorepo 形式，加一個獨立的 `bpm-admin-ui`，並把 Onboarding wizard 整套搬過去。

非目標：

- 不改變後端 API（兩個前端共用同一個 bpm-svc）
- 不改 auth 機制（同樣 JWT）
- 不另外開 git repo（保持單一 repo，只是拆 vite project）
- 不改寫 components / lib（共用模組透過相對路徑或 workspace package）

## What Changes

### 目錄結構（before → after）

**Before:**
```
bpm/
├── bpm-svc/         # 後端
├── bpm-ui/          # 員工 + admin 全混
└── ...
```

**After:**
```
bpm/
├── bpm-svc/
├── bpm-ui/          # 純員工 — Home, Forms, Search, Report, Attendance
├── bpm-admin-ui/    # 純 admin — Onboarding, Site Settings, Sandbox, Impersonation, Roles
├── bpm-shared/      # NPM workspace package (共用 lib/types/components)
└── package.json     # workspace root
```

### Workspace 設定

- Root `package.json` with `"workspaces": ["bpm-ui", "bpm-admin-ui", "bpm-shared"]`
- `bpm-shared` 含：
  - `lib/apiFetch.ts`
  - `lib/cn.ts`
  - `types/*.ts`（hrFlows, attendance, sandbox, impersonation 等）
  - `components/ui/*` (button, card, form, dialog 等基礎元件)
- `bpm-ui` 和 `bpm-admin-ui` 都 `import { ... } from '@bpm/shared'`

### 從 bpm-ui 搬走的檔案

搬到 bpm-admin-ui：
- `src/screens/onboarding/**` 整個目錄
- nav 上的 `Onboard` button 移除
- `case 'onboarding'` 從 App.tsx 拿掉（Screen union 收斂）
- 相關 lib：`onboarding.ts`, `onboardingTools.ts`, `bpmnXml.ts`, `bpmnXmlParse.ts`（如果只 onboarding 用）

留在 bpm-ui：
- Home, Search, Report, Attendance, 所有員工 form

### bpm-admin-ui 新增

- vite project structure mirror bpm-ui
- 自己的 `App.tsx` + `AppLayout.tsx`（admin 風格 — 例如左側 sidebar 而非頂部 nav，視覺上跟員工 UI 區分）
- 自己的 `RoleSwitcher`（dev mode）
- 強制 `admin` role guard：mount 時呼叫 `/api/me`，沒 admin role 直接 redirect 到 `bpm-ui`
- 預設 nav items：
  - Onboarding（從 bpm-ui 搬過來）
  - Site Settings（NEW — 容納 Sandbox toggle, system config）
  - Users & Roles（依賴 add-admin-roles-ui）
  - Impersonation（依賴 add-user-impersonation）
  - Audit Logs（未來）

### 部署

- bpm-ui 走 sub-path `/app/*`（或 root `/`，依 deploy 偏好）
- bpm-admin-ui 走 sub-path `/admin/*`
- Vite 各自 build 出獨立 dist
- 同一個 web server / CDN 服務兩份靜態資源
- 後端 CORS 同時允許 `/app` 和 `/admin` origins

### Auth 互通

- 同一份 JWT secret + JWT issuer
- `bpm_jwt` localStorage key 共用（同 origin）
- 員工 UI 不顯示 `Onboard` 入口；admin 從員工 UI 點某些 admin 連結（例如 footer 的 "Admin Console"）→ 帶 token 跳到 `/admin`，無感登入
- 未來 SSO 整合後：兩個 UI 都走同一個 IdP

## Impact

- Affected specs: NEW `bpm-admin-ui-shell`
- Affected code:
  - `package.json` (root) — NEW workspace config
  - `bpm-shared/` — NEW package (mostly moved files)
  - `bpm-ui/` — purge admin code, depend on `@bpm/shared`
  - `bpm-admin-ui/` — NEW project
  - `vite.config.ts` for both projects (base path setup)
  - `bpm-svc/src/Api/Program.cs` — extend CORS origins config
  - Deploy scripts / docs — describe two-build flow

### Backwards compatibility

- localStorage `bpm_jwt` 鍵名不變
- bpm-svc API 不變
- 既有的 dev-login flow 不變（兩個 UI 都能用）
- `dogfood.command` 腳本要改成 build / serve 兩個 UI

### Migration order suggestion

1. Set up workspace + create `bpm-shared` (move 1-2 files as proof)
2. Move all shared lib/components into `bpm-shared`, update bpm-ui imports
3. Create `bpm-admin-ui` skeleton with admin role guard
4. Move onboarding screens from bpm-ui to bpm-admin-ui
5. Remove "Onboard" nav button from bpm-ui
6. Update `dogfood.command` and CI scripts
