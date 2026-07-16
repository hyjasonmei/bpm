import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'
import path from 'node:path'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      // autoUpdate: a new deploy takes over on the next navigation — no
      // stale-bundle purgatory, no "update available" toast to maintain.
      registerType: 'autoUpdate',
      // SW only exists in production builds; dev keeps plain Vite.
      devOptions: { enabled: false },
      includeAssets: ['favicon.svg', 'apple-touch-icon.png'],
      manifest: {
        name: 'flowcook BPM',
        short_name: 'flowcook',
        description: '員工流程平台 — 表單申請與簽核',
        display: 'standalone',
        start_url: '/',
        // Tokens from src/index.css: bg-header (dark navy) / bg (slate-50).
        theme_color: '#1e293b',
        background_color: '#f8fafc',
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icon-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
          { src: '/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        // The 512px icons push past workbox's 2MB default only if the
        // bundle grows; keep the cap explicit so builds never silently
        // drop index-*.js from the precache.
        maximumFileSizeToCacheInBytes: 4 * 1024 * 1024,
        navigateFallback: '/index.html',
        // API traffic must NEVER be served from SW cache: bpm-svc is a
        // different origin (VITE_BPM_SVC_URL), and workbox runtime caching
        // is simply not configured for it — cross-origin requests pass
        // straight through to the network, 401s reach the app untouched.
        // Same-origin /api/* (local dev proxy shape) is denied fallback too.
        navigateFallbackDenylist: [/^\/api\//],
        runtimeCaching: [
          {
            // Google Fonts stylesheets + woff2 — the only external assets.
            urlPattern: /^https:\/\/fonts\.(googleapis|gstatic)\.com\/.*/i,
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'google-fonts',
              expiration: { maxEntries: 24, maxAgeSeconds: 60 * 60 * 24 * 365 },
            },
          },
        ],
      },
    }),
  ],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: { port: 5173 },
})
