import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'node:path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    port: 5174,
    proxy: {
      '/api': {
        target: 'http://localhost:5266',
        changeOrigin: false,
      },
      // White-label branding lives on bpm-svc's shared TenantSettings; the
      // Branding tab writes it there. POC convenience — production would
      // front both services behind one gateway / an admin-svc proxy endpoint.
      '/bpmsvc': {
        target: 'http://localhost:5290',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/bpmsvc/, ''),
      },
    },
  },
})
