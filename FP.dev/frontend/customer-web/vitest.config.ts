import vue from '@vitejs/plugin-vue'
import { configDefaults, defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    preserveSymlinks: true,
  },
  test: {
    exclude: [...configDefaults.exclude, 'e2e/**'],
    environment: 'jsdom',
    coverage: {
      provider: 'v8',
      include: ['src/stores/**/*.ts', 'src/features/**/use*.ts'],
      reporter: ['text', 'html', 'json-summary'],
      thresholds: {
        lines: 60,
      },
    },
  },
})
