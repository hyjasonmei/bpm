import { defineConfig } from 'astro/config'
import tailwind from '@astrojs/tailwind'
import sitemap from '@astrojs/sitemap'

export default defineConfig({
  // Canonical URLs, OG tags, and the generated sitemap all key off this.
  site: 'https://flowcook.ai',
  integrations: [tailwind(), sitemap()],
  server: {
    port: 4321,
    host: 'localhost',
  },
})
