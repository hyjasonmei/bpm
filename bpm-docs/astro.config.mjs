import { defineConfig } from 'astro/config'
import starlight from '@astrojs/starlight'

// site = 正式網域（canonical/sitemap 用）。此站 noindex，但仍設正式網域。
export default defineConfig({
  site: 'https://guide.flowcook.ai',
  server: { port: 4331, host: 'localhost' },
  integrations: [
    starlight({
      title: 'flowcook 導入 / 使用手冊',
      // 對搜尋引擎隱藏（unlisted 內部站）
      head: [
        { tag: 'meta', attrs: { name: 'robots', content: 'noindex, nofollow' } },
      ],
      customCss: ['./src/styles/custom.css'],
      // Footer override = default footer + 全站圖片 lightbox（點圖放大）。
      components: { Footer: './src/components/Footer.astro' },
      pagination: true,
      // 七章結構（權威版見 spec 的 Content Structure）。多數章用 autogenerate；
      // 「開始」用 items 明確排序。前台/後台各自成第一階。
      sidebar: [
        {
          label: '開始',
          items: [
            { label: 'flowcook 是什麼', slug: 'start/overview' },
            { label: '系統全貌', slug: 'start/system' },
            { label: '名詞速查', slug: 'start/glossary' },
          ],
        },
        { label: '前台功能介紹', autogenerate: { directory: 'frontend' } },
        { label: '後台功能介紹', autogenerate: { directory: 'backend' } },
        { label: '導入指南', autogenerate: { directory: 'onboarding' } },
        { label: '使用案例（組織）', autogenerate: { directory: 'cases-org' } },
        { label: '使用案例（流程）', autogenerate: { directory: 'cases-flow' } },
        { label: 'API 串接', autogenerate: { directory: 'api' } },
      ],
    }),
  ],
})
