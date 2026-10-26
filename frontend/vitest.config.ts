import { defineConfig, mergeConfig } from 'vitest/config'

import { baseConfig } from './vite.config'

/**
 * Vitest + RTL (jsdom). Compartilha a configuração do Vite (research §1).
 */
export default mergeConfig(
  baseConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      setupFiles: ['src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      globals: false,
      restoreMocks: true,
      clearMocks: true,
      unstubEnvs: true,
      unstubGlobals: true,
    },
  }),
)
