import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// package.json の version をビルド時に __APP_VERSION__ として埋め込む。
// バックエンドの .csproj <Version> と揃えること。
const pkg = JSON.parse(
  readFileSync(new URL('./package.json', import.meta.url), 'utf-8'),
) as { version: string }

export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5254',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../backend/wwwroot',
    emptyOutDir: true,
  },
})
