# Design — add-onboarding-ai-translate

## 1. Spec schema migration v1 → v2

**v1 形態（既有）**：
```json
{ "label_zh": "請假天數", "label_en": "Leave Days" }
```
或
```json
{ "subject": { "zh-TW": "...", "en": "..." } }
```
（不同地方寫法不一）

**v2 形態（本 change）**：
```json
{ "label": { "default": "請假天數", "i18n": { "en": "Leave Days" } } }
```

統一用 `LocalizableString` JSON shape：
```json
{
  "default": "<primary locale text>",
  "i18n": { "<locale>": "<translation>" }   // optional
}
```

`primaryLocale` 在 `meta.primaryLocale` 一處宣告，所有 `default` 都對應該語言。

## 2. Auto-migrate at load time（採用）

`SpecLoader` 拿到 spec.json 時：

```csharp
public Spec Load(string json) {
    var raw = JsonNode.Parse(json);
    var version = raw["meta"]?["specSchemaVersion"]?.GetValue<int>() ?? 1;
    if (version < 2) MigrateV1ToV2(raw);
    return raw.Deserialize<Spec>();
}

void MigrateV1ToV2(JsonNode root) {
    // For each known LocalizableString location:
    //   - if shape is { "label_zh": x, "label_en": y } → { "default": x, "i18n": { "en": y } }
    //   - if shape is { "zh-TW": x, "en": y } → { "default": x, "i18n": { "en": y } }
    // Also write meta.primaryLocale = "zh-TW" (assumed) and meta.targetLocales = []
    // Mark meta.specSchemaVersion = 2
}
```

Cost: 一次 in-memory 升級，spec snapshot 在 ProcessInstance 仍是原 v1 格式（但 runtime 用 v2 view）。新建 spec 直接寫 v2。

## 3. 為什麼不做 polyfill 反向（讀 v2 給舊代碼）

舊代碼（pre-this-change）期待 `{label_zh, label_en}`。一旦 schema 改成 v2，所有 spec 寫入點 / 讀取點都要改。Reverse polyfill 只會延後痛苦。

採 **forward only**：本 change 落地後所有 spec 編輯介面（onboarding wizard）只產 v2，所有 reader 強制走 v2 path（v1 由 loader migrate）。

## 4. AI translate — context 來源

batch translate 給 AI 的 `context` block 從 onboarding wizard 累積的 draft 抽：

| Context key | 來源 |
|---|---|
| `industry` | Step 1 (Source) — wizard 問「你哪個行業」（POC 寫死 manufacturing）|
| `flowName` | Step 1 — meta.flowName.default |
| `flowCode` | Step 1 — meta.flowCode |
| `domainHint` | 寫死 "Taiwan SME HR/Finance/Procurement processes" |
| `priorTranslations` | 同 spec 內已翻好的 label（保證術語一致）|

這個 context 對品質提升很大 — AI 不會把「呈核」翻成 "Process Approval"，因為 prompt 明示是 Taiwan SME HR 流程。

## 5. Confidence threshold

AI 對每筆翻譯回 `confidence: high|medium|low`。實作上：
- Backend system prompt 要求 AI 自評
- UI：medium / low 標 amber 黃底，提示客戶 review
- 「📊 X 個 high / Y 個 medium / Z 個 low」status bar

不存 confidence 到 spec.json — review 完就丟。spec.json 內所有 i18n 字串對 runtime 等價。

## 6. 為什麼不在 wizard 中即時翻譯

**Alternative**：每填一個 label，AI 立刻翻成 target locales 顯示

**Rejected**：
- 每次 keystroke 觸發 AI 太貴
- 客戶填到一半 idea 還會改，過早翻譯浪費 token
- 失去「全 spec 上下文」優勢（前面 label 還在打字，AI 只看到部分）

採 **Step 9 batch**：客戶填完 spec → 點 Translate → 一次送整個 spec 給 AI → 拿回所有 locales 翻譯。

## 7. 客戶能 edit 任一翻譯

Step 9 表格每一格：
- AI 填的值，旁邊 `✏️` icon
- 點 ✏️ 變 inline 可編輯，blur 存
- 編過的翻譯 metadata 標 `userEdited: true`（存於 spec, runtime 無感，但統計 / 後續 `add-locales-admin-ui` 會顯示 user-edited 比例）

不採用「鎖定 user-edited 不再被 AI 覆蓋」：客戶可能 edit 完想再 retry，所以 retry 是手動觸發（"AI translate all" 按鈕只填空 / 重翻 low-confidence），user-edited 不會被覆蓋（除非客戶自己清空再翻）。

## 8. Empty target locales

`meta.targetLocales = []` → 直接跳過 Step 9 進 GoLive。Validator 不擋 empty target。

理由：客戶可能只服務本地市場（純 zh-TW 用戶），沒必要強迫翻譯。

## 9. 與 add-i18n-locale-switching 的整合

Runtime：
- LocaleResolver.Pick(LocalizableString, locale) → string
- 邏輯：
  - 如果 `locale === primaryLocale` → return default
  - 如果 i18n[locale] 存在 → return i18n[locale]
  - 否則 → return default（fallback）+ log warning（locale missing）

`add-i18n-locale-switching` 原本對「bilingual map」的 Pick 已經做這事，本 change 升級到 LocalizableString shape。Pick 簽名：

```csharp
string Pick(LocalizableString s, string locale) {
    if (locale == primaryLocale) return s.Default;
    if (s.I18n?.TryGetValue(locale, out var v) == true) return v;
    return s.Default;  // fallback
}
```

## 10. Validation rules

GoLive validator（既有）擴充：

1. 對每個 LocalizableString，檢查 `i18n` 是否含 `meta.targetLocales` 全部
2. 缺項 → 報錯「`{path}` missing translation for `{locale}`」
3. 如果 targetLocales 為空 → skip 此 validator

Validator 在 Translate step 也跑（即時提示客戶哪幾項缺），在 GoLive step 強制 block 上線。

## 11. 不存 confidence、不審計翻譯歷史 — 為什麼

每筆翻譯被 user-edit 後就是 spec 的一部分。spec.json 的版本控制機制（spec_version / SpecSnapshot）已經涵蓋「歷史」需求 — 任何時候可以撈出某個 instance 啟用時的翻譯版本。

額外的「翻譯 history」（誰在何時改了哪個 label 的英文翻譯）對 audit 沒貢獻 — runtime 行為由 spec snapshot 決定，不由翻譯歷史決定。所以略掉。

## 12. Rate limit on /api/spec-translate

10 calls/min/tenant — 一個 typical 流程 30 label，AI 一次拿 30 → 一個 GoLive 攻略撐 5+ 次「重翻」。實際使用很少打到上限。

擋 abuse：客戶開了 wizard 但忘了關，瀏覽器 buggy 一直 retry 不會把 token 用爆。

## 13. POC 階段先不做的：

- Locale 自動偵測（從 browser Accept-Language）— 客戶 IT 一律 zh-TW，POC 不需要
- Pluralization rules（"1 day" vs "2 days"）— 中文沒這問題、英文之後再加
- ICU MessageFormat — Mustache 簡單字串替換已夠用
- 翻譯記憶（Translation Memory）— 多 spec 共用 glossary 是 `add-locale-glossary` 的事
