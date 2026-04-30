import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import './assets/base.css'
import './assets/main.css'

import App from './App.vue'
import router from './router'
import { setUnauthorizedHandler } from './services/apiClient'
import { useAuthStore } from './stores/authStore'
import { useChatStore } from './stores/chatStore'

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(ElementPlus)
app.use(router)

const authStore = useAuthStore(pinia)
const chatStore = useChatStore(pinia)
setUnauthorizedHandler(async (problem) => {
  authStore.clearAuth(authStore.resolveUnauthorizedMessage(problem))
  chatStore.reset()
  if (router.currentRoute.value.path !== '/login') {
    await router.replace('/login')
  }
})

app.mount('#app')
