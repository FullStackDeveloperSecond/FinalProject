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
    // Same-origin proxy so the browser never makes a cross-origin request to
    // the API in local dev; avoids needing a CORS policy decision (a shared
    // concern owned by alex's SH-04 work package) just to preview pages.
    proxy: {
      '/api': {
        target: 'http://localhost:5126',
        changeOrigin: true,
      },
    },
  },
})
