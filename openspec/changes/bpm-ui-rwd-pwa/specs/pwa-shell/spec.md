## ADDED Requirements

### Requirement: 可安裝的 web manifest
bpm-ui SHALL 提供 web app manifest(name、short_name、`display: standalone`、theme_color、background_color、192/512 icons 含 maskable),使支援的瀏覽器可將站台安裝至主畫面;`index.html` SHALL 含 theme-color meta 與 apple-touch-icon。

#### Scenario: 安裝性檢查
- **WHEN** 以 Chrome 對 production build 執行 installability 檢查(Lighthouse PWA 或 DevTools Application 面板)
- **THEN** manifest 有效、icons 齊備,無 installability 錯誤

#### Scenario: standalone 啟動
- **WHEN** 使用者從主畫面開啟已安裝的 app
- **THEN** app 以 standalone(無瀏覽器 UI)模式啟動並顯示登入或 Home

### Requirement: 靜態資源 precache 與自動更新
Service worker SHALL precache build 產物(JS/CSS/HTML/SVG)並以 `autoUpdate` 策略註冊:部署新版後,使用者下次導航 SHALL 取得新版資源而無需手動清快取。SPA 導航請求 SHALL fallback 至 `/index.html`,深連結(如 `/cases/...`)重新整理後仍 SHALL 正常載入。

#### Scenario: 離線載入 shell
- **WHEN** 使用者造訪過站台後斷網並重新開啟
- **THEN** app shell(HTML/JS/CSS)自快取載入;API 資料請求失敗時顯示既有錯誤處理,不白屏

#### Scenario: 版本更新生效
- **WHEN** 部署新 build 後使用者重新導航至站台
- **THEN** service worker 更新並提供新版 bundle,無舊版殘留

### Requirement: API 與認證流量不經 service worker 快取
Service worker MUST NOT 快取任何對 `VITE_BPM_SVC_URL`(bpm-svc API)的請求或回應;401 回應 SHALL 原样到達應用層,既有 token 清除與登出行為不受 SW 影響。

#### Scenario: API 回應不被快取
- **WHEN** 使用者在 SW 啟用下操作簽核並重新整理
- **THEN** 案件資料每次皆為 network 取得(DevTools Network 顯示非 from ServiceWorker),token 過期時 401 正常觸發登出流程
