import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { defineConfig, type UserConfig } from 'vite'

/**
 * Configuração base do Vite. Exportada separadamente para o `vitest.config.ts`
 * reaproveitar plugins e aliases (research §7, T004/T005).
 */
export const baseConfig: UserConfig = {
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      // Mesma origem em todos os ambientes: em dev o Vite repassa `/api` à API
      // da HU 001 removendo o prefixo (research §7).
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
  build: {
    sourcemap: false,
    reportCompressedSize: true,
  },
}

export default defineConfig(baseConfig)
