import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    preserveSymlinks: true,
  },
  // `@doselect/web-shared` is a file: link, so in dev Vite pre-bundled the entries the
  // app imports (primevue/config, the shared theme and components) while serving the
  // PrimeVue internals those components pull in as raw ESM. That produced two copies of
  // the @primeuix/styled engine: DoSelectPreset registered its component tokens into the
  // pre-bundled one and @primevue/core/base/style read from the raw one, so every
  // --p-button-* / --p-paginator-* token came out empty and PrimeVue components rendered
  // unstyled. Production was unaffected because Rollup emits a single instance. Excluding
  // the shared package and the whole PrimeVue/PrimeUIX runtime keeps dev on raw ESM only,
  // so there is one engine instance again — without touching resolve.preserveSymlinks.
  optimizeDeps: {
    exclude: [
      '@doselect/web-shared',
      'primevue',
      '@primevue/core',
      '@primevue/icons',
      '@primeuix/themes',
      '@primeuix/styled',
      '@primeuix/styles',
    ],
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
