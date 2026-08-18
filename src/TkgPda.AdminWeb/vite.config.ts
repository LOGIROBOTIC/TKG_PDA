import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 4173,
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.TKG_API_TARGET ?? 'http://localhost:5000',
        changeOrigin: true,
      },
      '/health': {
        target: process.env.TKG_API_TARGET ?? 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    exclude: ['e2e/**', 'node_modules/**', 'dist/**'],
    css: true,
  },
})
