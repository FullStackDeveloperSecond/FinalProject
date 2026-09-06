import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'
import PrimeVue from 'primevue/config'
import { doSelectPrimeVueOptions } from '@doselect/web-shared/ui'
import { createDoSelectQueryClient } from '@doselect/web-shared/query'
import { createPinia } from 'pinia'
import '@doselect/web-shared/brand.css'
import './style.css'
import App from './App.vue'
import router from './router'

const app = createApp(App)
const queryClient = createDoSelectQueryClient()

app.use(createPinia())
app.use(router)
app.use(VueQueryPlugin, { queryClient })
app.use(PrimeVue, doSelectPrimeVueOptions)
app.mount('#app')
