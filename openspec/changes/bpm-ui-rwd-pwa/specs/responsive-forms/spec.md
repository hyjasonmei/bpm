## ADDED Requirements

### Requirement: 共用表單 primitives 行動適配
共用 form primitives(Field/Input/Select/Textarea)SHALL 在所有 viewport 佔滿容器寬度,且輸入元件在行動 viewport 的點擊高度 SHALL 至少 44px。此適配 MUST 僅透過共用元件與 FormShell 達成,MUST NOT 修改 `features/<CODE>/V<N>/` 內的 chef 產出檔案。

#### Scenario: 表單欄位手機呈現
- **WHEN** 以 390px viewport 開啟任一流程表單(如 /apply/LEAVE)
- **THEN** 所有欄位單欄直排、輸入框佔滿寬度、高度 ≥ 44px,無橫向溢出

#### Scenario: chef 產出檔零改動
- **WHEN** 本 change 實作完成
- **THEN** `git diff` 顯示 `bpm-ui/src/features/` 底下無任何檔案變更

### Requirement: FormShell 申請人摘要響應式
FormShell 的申請人摘要區 SHALL 在視窗寬度小於 768px 時由兩欄改為單欄;≥768px 維持既有兩欄佈局。

#### Scenario: 手機摘要單欄
- **WHEN** 以 390px viewport 開啟任一表單
- **THEN** 申請人摘要的 label/value 群組單欄堆疊,無被截斷的文字

### Requirement: ActionFooter 行動版動作列
ActionFooter SHALL 在視窗寬度小於 768px 時固定於視窗底部(sticky)且按鈕以全寬/等寬排列;既有 confirm modal 機制 SHALL 維持不變。

#### Scenario: 手機 sticky 動作列
- **WHEN** 以 390px viewport 開啟含動作的表單或 CaseDetail 並捲動頁面
- **THEN** 動作按鈕維持在視窗底部可見,點擊後彈出既有 confirm modal

### Requirement: 桌面觀感不回歸
共用 primitives 與 FormShell 的行動適配 SHALL 不改變 ≥768px viewport 的既有視覺呈現(容許 ≤4px 的間距差異)。

#### Scenario: 桌面表單截圖比對
- **WHEN** 改動完成後以 1280px viewport 開啟 LEAVE、PURCHASE_REQUEST、WFH V6 表單
- **THEN** 版面與改動前一致(欄位排列、分區、動作列位置無變化)
