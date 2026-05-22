/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{astro,html,js,jsx,md,mdx,svelte,ts,tsx,vue}'],
  theme: {
    extend: {
      colors: {
        // Aligned with bpm-ui + bpm-admin-ui index.css tokens
        // (TREND BPM dark navy + slate / amber accent palette).
        // Marketing site reuses the same brand visual system so a
        // customer's demo experience is visually continuous from
        // landing page → admin → employee app.
        page:      '#F1F5F9',      // slate-100 page bg
        card:      '#FFFFFF',
        rule:      '#E2E8F0',      // slate-200 hairline
        labelBg:   '#F1F5F9',
        header:    '#1E2D3D',      // TREND BPM dark navy
        header2:   '#243849',

        accent:    '#F59E0B',      // amber active step
        primary:   '#2563EB',      // primary CTA blue
        good:      '#16A34A',
        danger:    '#DC2626',
        warn:      '#F59E0B',
        info:      '#2563EB',

        ink:       '#0F172A',      // slate-900
        inkMuted:  '#64748B',      // slate-500
        inkFaint:  '#94A3B8',      // slate-400
      },
      fontFamily: {
        sans: ['"DM Sans"', '"Noto Sans TC"', '"PingFang TC"', '"Microsoft JhengHei"', 'system-ui', 'sans-serif'],
        mono: ['"DM Mono"', 'ui-monospace', '"SF Mono"', 'Menlo', 'Consolas', 'monospace'],
      },
    },
  },
  plugins: [],
}
