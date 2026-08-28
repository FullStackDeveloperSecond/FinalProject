import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    preserveSymlinks: true,
  },
  test: {
    environment: 'jsdom',
    coverage: {
      provider: 'v8',
      include: ['src/features/**/stores/**/*.ts', 'src/features/**/use*.ts'],
      reporter: ['text', 'html', 'json-summary'],
      thresholds: {
        lines: 60,
      },
    },
  },
})
