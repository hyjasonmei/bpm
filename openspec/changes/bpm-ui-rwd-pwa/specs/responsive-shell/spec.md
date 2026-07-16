## ADDED Requirements

### Requirement: 行動版導航選單
在視窗寬度小於 768px 時,app shell 的主導航(Home/Create/Search/Attendance)SHALL 收合為 hamburger 選單;767px 以下 MUST NOT 以內聯文字連結呈現主導航。

#### Scenario: 窄螢幕收合導航
- **WHEN** viewport 寬度為 390px 且使用者已登入
- **THEN** header 顯示 hamburger 按鈕,點擊後展開含 Home/Create/Search/Attendance 的選單,點擊項目導航並收合選單

#### Scenario: 桌面維持內聯導航
- **WHEN** viewport 寬度 ≥ 768px
- **THEN** 主導航以既有內聯文字連結呈現,不顯示 hamburger

### Requirement: 無橫向溢出
在 360px–767px 寬的 viewport 下,app shell 與所有核心頁面(Home、Create、Search、Attendance、表單、CaseDetail)的 body SHALL 不產生水平捲軸(頁面層級);寬內容 MUST 限制在各自的 overflow 容器內。

#### Scenario: 手機 viewport 無頁面級橫向捲動
- **WHEN** 以 390×844 viewport 開啟核心頁面
- **THEN** `document.documentElement.scrollWidth` 不超過 viewport 寬度

### Requirement: Touch target 尺寸
行動 viewport 下,導航項目與主要互動元件(按鈕、選單項)的可點擊高度 SHALL 至少 44px。

#### Scenario: 行動選單項目高度
- **WHEN** 於 390px viewport 展開行動導航選單
- **THEN** 每個選單項目的點擊區高度 ≥ 44px
