/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{astro,html,js,jsx,md,mdx,svelte,ts,tsx,vue}'],
  theme: {
    extend: {
      colors: {
        // Kitchen palette — distinct from bpm-admin-ui's neutral.
        // Warm orange = flame, deep ink = night kitchen, cream = plate.
        brand: {
          flame:   '#E85D2F',   // primary accent — buttons, links, kitchen highlight
          ember:   '#C44A22',   // hover state
          night:   '#1B2845',   // ink / body text on cream
          midnight:'#0F1729',   // headings
          cream:   '#FAF7F2',   // page background
          plate:   '#F2EBDF',   // section divider tone
          basil:   '#2D5F3F',   // CTA confirm / "ready to serve" status
          rule:    '#E5DDD0',   // border default on cream
          muted:   '#6E6A60',   // subhead / muted text
        },
      },
      fontFamily: {
        display: ['Fraunces', 'Newsreader', 'Georgia', 'serif'],
        body:    ['Inter', '-apple-system', 'BlinkMacSystemFont', 'sans-serif'],
        zh:      ['"PingFang TC"', '"Noto Sans TC"', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
