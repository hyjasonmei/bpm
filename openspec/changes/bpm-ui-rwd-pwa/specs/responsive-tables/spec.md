## ADDED Requirements

### Requirement: Search 結果行動呈現
Search 頁的案件搜尋結果在視窗寬度小於 768px 時 SHALL 以卡片式呈現(含標題、流程、狀態、日期等關鍵欄位),點擊卡片導向 CaseDetail;≥768px 維持既有 6 欄表格。搜尋條件輸入區 SHALL 在窄螢幕單欄堆疊。

#### Scenario: 手機搜尋動線
- **WHEN** 以 390px viewport 於 Search 頁執行搜尋且有結果
- **THEN** 結果以卡片列表呈現、無頁面級橫向捲動,點擊卡片可進入該案件

### Requirement: Attendance 表格行動呈現
Attendance 頁的補登記錄與每日彙總在視窗寬度小於 768px 時 SHALL 改為卡片式或精簡欄位呈現,主要操作(查看、進入補登審核)SHALL 不依賴橫向捲動完成;≥768px 維持既有表格。

#### Scenario: 手機出勤頁操作
- **WHEN** 以 390px viewport 開啟 Attendance
- **THEN** 記錄以卡片或精簡列呈現、無頁面級橫向捲動,可點入任一補登記錄的審核頁

### Requirement: 卡片與表格資料一致
所有「窄螢幕卡片 / 寬螢幕表格」雙呈現 MUST 共用同一資料來源、排序與點擊 handler,僅 presentation 分支。

#### Scenario: 同資料雙呈現
- **WHEN** 同一查詢結果分別在 390px(卡片)與 1280px(表格)呈現
- **THEN** 筆數、排序、每筆導向的目的地完全一致
