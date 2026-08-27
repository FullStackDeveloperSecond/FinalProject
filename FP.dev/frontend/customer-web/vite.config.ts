import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    preserveSymlinks: true,
  },
  server: {
    port: 5173,
    strictPort: true,
    // Same-origin proxy keeps local development and browser E2E on the frontend
    // origin while forwarding API requests to the explicitly bound backend.
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5126',
        changeOrigin: true,
      },
    },
  },
})
