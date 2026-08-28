import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { createDoSelectQueryClient } from '@doselect/web-shared/query'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import { DoSelectPreset } from '@doselect/web-shared/theme'
import '@doselect/web-shared/styles/design-tokens.css'
import './style.css'
import App from './App.vue'
import router from './router'

const app = createApp(App)
const queryClient = createDoSelectQueryClient()

app.use(createPinia())
app.use(router)
app.use(VueQueryPlugin, { queryClient })
app.use(PrimeVue, {
  theme: {
    preset: DoSelectPreset,
    options: {
      // Light-only：不啟用 Dark；Token 結構已預留 :root[data-theme="dark"]
      darkModeSelector: false,
    },
  },
})

app.mount('#app')
