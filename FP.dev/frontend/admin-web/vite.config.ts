import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  base: '/admin/',
  plugins: [vue()],
  resolve: {
    preserveSymlinks: true,
  },
  server: {
    port: 5174,
    strictPort: true,
    // Same-origin proxy so the browser never makes a cross-origin request to
    // the API in local dev; mirrors customer-web/vite.config.ts (no CORS
    // policy exists on the backend yet — that's alex's SH-04 work package).
    proxy: {
      '/api': {
        target: 'http://localhost:5126',
        changeOrigin: true,
      },
    },
  },
})
