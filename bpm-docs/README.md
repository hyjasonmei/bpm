# bpm-docs — flowcook 導入/使用手冊

內部 enablement docs（Astro + Starlight）。**不對外索引、不從公開官網連。** 導入中客戶可拿連結參閱。

## 跑

    npm install
    npm run dev      # http://localhost:4331
    npm run build    # astro check + build → dist/

## 內容

`src/content/docs/**` 的 Markdown/MDX，七章：開始 / 前台功能介紹 / 後台功能介紹 / 導入指南 / 使用案例（組織）/ 使用案例（流程）/ API 串接。
嵌影片：相對路徑 `import YouTube from '../../../components/YouTube.astro'`，`<YouTube id="..." />`（YouTube unlisted）。
截圖放 `src/assets/screens/`，Markdown 相對路徑引用（Astro 會最佳化）。

## 部署

Azure SWA `poc-flowcook-docs`（Free tier），自訂網域 **guide.flowcook.ai**。接在 `infra/azure` 的 `03-deploy.sh`。noindex + robots disallow。
綁網域：GoDaddy 加 CNAME `guide` → SWA default hostname，再 `az staticwebapp hostname set -n poc-flowcook-docs -g rg-poc --hostname guide.flowcook.ai`（Azure 自動簽 TLS）。
