## ADDED Requirements

### Requirement: Home 儀表板響應式版面
Home 儀表板 SHALL 在視窗寬度小於 768px 時以單欄呈現:收件匣(待處理/我的案件)在前、320px 側欄內容移至主內容下方;≥768px 維持既有兩欄版面。

#### Scenario: 手機單欄
- **WHEN** 以 390×844 viewport 開啟 Home
- **THEN** 版面為單欄,收件匣區塊位於側欄內容之前,無橫向溢出

#### Scenario: 桌面兩欄不變
- **WHEN** 以 ≥768px viewport 開啟 Home
- **THEN** 維持主欄 + 320px 側欄的兩欄版面

### Requirement: 收件匣卡片式呈現
Home 的待處理與我的案件清單在視窗寬度小於 768px 時 SHALL 以卡片式呈現,且每張卡片 SHALL 顯示對應表格的全部資料欄位(待處理:Case ID、Type、Title、Submitted、Status;我的案件:Case ID、Type、Title、Status、Started、Last activity),不省略欄位;整卡可點擊,SHALL 導向對應 CaseDetail;≥768px 維持表格呈現。卡片與表格 MUST 共用同一資料來源與導航 handler。

#### Scenario: 手機卡片列表
- **WHEN** 以 390px viewport 開啟 Home 且收件匣有待處理案件
- **THEN** 案件以卡片列表呈現,點擊任一卡片導向該案件的 CaseDetail

#### Scenario: 手機完成簽核動線
- **WHEN** 使用者在 390px viewport 從 Home 卡片進入 CaseDetail 並執行核准動作
- **THEN** confirm modal 正常顯示、動作成功送出,返回 Home 後該案件離開待處理清單
