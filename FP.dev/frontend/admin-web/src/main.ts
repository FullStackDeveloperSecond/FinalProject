import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'
import { createDoSelectQueryClient } from '@doselect/web-shared/query'
import { createPinia } from 'pinia'
import './style.css'
import App from './App.vue'
import router from './router'

const app = createApp(App)
const queryClient = createDoSelectQueryClient()

app.use(createPinia())
app.use(router)
app.use(VueQueryPlugin, { queryClient })
app.mount('#app')
