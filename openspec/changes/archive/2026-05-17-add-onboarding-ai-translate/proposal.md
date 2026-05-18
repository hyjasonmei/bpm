## Why

`add-i18n-locale-switching` 把**讀取面**架好了：使用者有 `PreferredLocale`、notification dispatch 會挑對應 locale 的字串、前端有 `t()` lookup。但 spec.json 內**填寫面**還停留在硬寫 `zh-TW` + optional `en` 兩個 key。客戶要支援 ja / en / vi 等其他 locale 時：

- spec schema 卡死在 zh + en 兩欄，無法擴
- 即使 schema 改了，客戶要逐 label 手填三四種語言（一個流程動輒 30+ label / message），實務上沒人會做
- 沒有領域上下文，逐字翻譯品質差（「會簽」≠ "Co-sign"，正確是 "Joint approval"；「呈核」≠ "Submit for approval"，正確視語境）

這個 change 解兩件事：
1. **Spec schema 升級**：所有 label / subject / body / option / hint 改成 i18n bundle（`Record<locale, string>`），不再 zh + en 兩欄寫死
2. **Onboarding 加 Translate step（#9，介於 SLA 和 Go Live 之間）**：AI 一次把 spec 全部 label 翻譯到客戶要的 target locales，看得到整個流程上下文（行業、用途、同流程其他 label 用字），客戶逐個 review / edit

非目標（明確排除）：

- 不改使用者讀取面的 locale fallback 邏輯（那是 `add-i18n-locale-switching` 範圍）
- 不做 RTL 排版
- 不做 glossary（客戶專用字典）UI — 預留 schema 欄位，UI 留給後續 change `add-locale-glossary`
- 不做 post-launch translation 補譯管理頁 — 預留 endpoint，UI 留給後續 change `add-locales-admin-ui`
- 不做使用者主動切 locale（已在 `add-i18n-locale-switching`）
- AI 翻譯結果不入 audit table（每 label 的歷史變化由 spec 版本管理含蓋）

## What Changes

### Spec schema upgrade — Localizable strings

定義新型別 `LocalizableString`：

```json
{ "default": "請假天數", "i18n": { "en": "Leave Days", "ja": "休暇日数" } }
```

- `default` (required, string) — 客戶主語言原文（onboarding 階段填）
- `i18n` (optional, Record<locale, string>) — 其他 locale 的翻譯
- 既有「zh + en 雙欄」結構（field label, notification subject/body 等）統一遷成此型別。Schema migration 路徑見 design §1。

需要套用此型別的 spec 元素（取自既有 `spec_schema.md`）：

- `meta.flowName`
- `nodes[].label`
- `forms[].fields[].label` / `.placeholder` / `.hint`
- `forms[].fields[].options[].label` (select / radio enum)
- `notifications[].subject` / `.body`
- `gateways[].branches[].label`

不變更（明確留 zh-TW 的）：
- `meta.flowCode` (UPPERCASE_SNAKE 識別碼，不翻譯)
- node `id` (snake_case 識別碼)
- ActorRef expressions / role codes / group codes

### Spec.meta.locales — 客戶要支援的 locale set

```json
"meta": {
  "tenant": "acme",
  "flowCode": "LEAVE",
  "flowName": { "default": "請假申請", "i18n": { "en": "Leave Request" } },
  "primaryLocale": "zh-TW",
  "targetLocales": ["en", "ja"]
}
```

- `primaryLocale` — `default` 對應的語言
- `targetLocales` — 客戶要翻譯到哪些 locale（不含 primary）

驗證：每個 LocalizableString 的 `i18n` 至少含 `targetLocales` 的所有 key（否則 GoLive validator 擋住，標明哪幾個 label 缺哪些 locale）。

### Onboarding wizard — 新 step 9 (Translate)

原本的 9 個 step：Source → Structure → Forms → Decisions → Approvers → Notify → SLA → Test → GoLive

改為 10 個：... → Test → **Translate** → GoLive

Step 9 (Translate) UI 結構：

- 頁面頂顯示：`Primary locale: zh-TW · Translate to: [+ Add locale chip] en × ja ×`
- 客戶選 target locales（empty 也合法，跳過 step 直接 GoLive）
- 表格列出所有 LocalizableString：
  - **第一欄**：欄位 path（如 `forms.LEAVE.fields.days.label`）+ default 值
  - **後續欄**：每個 target locale 一欄，AI 自動填 + `✏️` edit icon
  - 不確定的翻譯（AI confidence < threshold or 包含領域術語）用 **黃底** 標出
- 頁面右上角 `🪄 AI translate all` 按鈕：批次重翻所有空格 / 黃格
- 頁面底 `Continue` 按鈕：所有 target locales 都填齊才解鎖（缺項時顯示「en 缺 3 項，ja 缺 5 項」）

### AI translate prompt 結構

呼叫 `/api/spec-translate` (NEW endpoint)：

```json
{
  "primaryLocale": "zh-TW",
  "targetLocales": ["en", "ja"],
  "context": {
    "industry": "manufacturing",      // from onboarding step 1
    "flowName": "請假申請",
    "flowCode": "LEAVE",
    "domain_hint": "Taiwan SME HR processes"
  },
  "items": [
    { "path": "nodes.task_apply.label", "value": "提出申請" },
    { "path": "forms.LEAVE.fields.days.label", "value": "請假天數" },
    ...
  ]
}
```

Backend 組 system prompt：

```
You are translating a Taiwan-Manufacturing SME HR workflow ("請假申請" — Leave Request)
from zh-TW to {targetLocale}. Preserve domain terminology. Examples:
- 呈核 = "submit for approval"
- 會簽 = "joint approval"
- 副本 = "carbon copy" (NOT "duplicate")
- 加簽 = "additional approval"
For each item, return the translation. If unsure (terminology, ambiguity, very short), set "confidence": "low".
```

Response:
```json
{
  "translations": [
    { "path": "nodes.task_apply.label", "en": "Submit Application", "ja": "申請を提出", "confidence": { "en": "high", "ja": "high" } },
    { "path": "forms.LEAVE.fields.days.label", "en": "Days", "ja": "日数", "confidence": { "en": "low", "ja": "high" } },
    ...
  ]
}
```

`confidence: low` 在 UI 標黃，提示客戶 review。

### Backend endpoint

- `POST /api/spec-translate` — body 如上、呼叫 `IAiBackend` (claude / cli)、回 translations array
- `[Authorize]`，admin / designer role 才能呼叫
- Rate limit：每 tenant 每分鐘 10 次（避免 token 爆掉）

### Out of scope — explicit non-goals

- 客戶 glossary 機制（往後 `add-locale-glossary`）
- Post-launch 補譯 admin UI
- 自動偵測客戶語言（永遠是 zh-TW primary on POC）

## Impact

- Affected specs: NEW `bpm-onboarding-translate`
- Affected schema: `spec_schema.md` — major version bump（從 v1 升 v2，所有 label 結構改）
- Affected code:
  - `bpm-svc/src/Domain/Spec/SpecModels.cs`（如有 LabelDto / NotificationDto 等強型別 model）— LocalizableString record + serializer
  - `bpm-svc/src/Application/Spec/SpecValidator.cs` — 加 i18n completeness 檢查
  - `bpm-svc/src/Api/SpecTranslateController.cs` (NEW)
  - `bpm-admin-ui/src/screens/onboarding/steps/StepTranslate.tsx` (NEW)
  - `bpm-admin-ui/src/screens/onboarding/Onboarding.tsx` — 9 step → 10 step
  - `bpm-admin-ui/src/lib/api/specTranslate.ts` (NEW)
  - 既有 `sample_specs/*.json` — schema migrate
- Migration path：詳見 design §1

### Backwards compatibility

舊 v1 spec（`label_zh` + `label_en`）有兩種策略：
1. 自動 migrate at load time：runtime spec loader 偵測舊形 → 在記憶體升 v2，不寫回 disk
2. 一次性遷移腳本：寫一支 `migrate-spec-v1-to-v2.cs`，掃 `sample_specs/` + 已上線 instance.SpecSnapshotJson 升級

採 **方案 1**（自動 at load time），理由：sample 數量小、in-flight instances 不可改、reader 加 ~20 行 code 比寫腳本 + 跑遷移更輕量。詳見 design §2。

### Dependencies

- 依賴 `add-i18n-locale-switching` 的 `ILocaleResolver` 用於 runtime fallback
- 與 `add-form-runtime-rendering` 互動：表單元件讀 LocalizableString 透過 LocaleResolver
