import { env } from 'node:process'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

export default defineConfig(() => {
  const target = env.aicopilot_httpapi_http
  const apiBaseUrl = env.VITE_API_BASE_URL || '/api'

  return {
    plugins: [vue(), vueDevTools()],
    resolve: {
      alias: {
        '@': fileURLToPath(new URL('./src', import.meta.url))
      }
    },
    server: {
      host: true,
      proxy: target
        ? {
            [apiBaseUrl]: {
              target,
              changeOrigin: true,
              secure: false
            }
          }
        : undefined
    }
  }
})
