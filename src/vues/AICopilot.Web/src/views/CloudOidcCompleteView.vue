<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { LoaderCircle, TriangleAlert } from 'lucide-vue-next'
import { useRoute, useRouter } from 'vue-router'
import AiButton from '@/components/ai/AiButton.vue'
import { useAuthStore } from '@/stores/authStore'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const hasFailed = ref(false)

const message = computed(() => {
  if (route.query.error) {
    return 'Cloud 登录未完成，请重新登录。'
  }

  return authStore.errorMessage || 'Cloud 登录失败，请重新登录。'
})

function resolveAuthorizedPath() {
  if (authStore.canUseChat) return '/chat'
  if (authStore.canViewConfig) return '/config'
  if (authStore.canManageKnowledge) return '/knowledge'
  if (authStore.canManageAccess) return '/access'
  return '/forbidden'
}

async function completeLogin() {
  if (route.query.error) {
    hasFailed.value = true
    return
  }

  try {
    await authStore.finalizeCloudOidcLogin()
    await router.replace(resolveAuthorizedPath())
  } catch (error) {
    console.error('Failed to complete Cloud OIDC login.', error)
    hasFailed.value = true
  }
}

onMounted(() => {
  void completeLogin()
})
</script>

<template>
  <div class="cloud-login-page">
    <section class="cloud-login-panel">
      <div v-if="!hasFailed" class="state-content">
        <span class="state-icon loading"><LoaderCircle class="h-10 w-10 animate-spin" /></span>
        <h1>正在完成 Cloud 登录</h1>
        <p>正在校验 Cloud 身份并换取 AICopilot 登录态。</p>
      </div>

      <div v-else class="state-content">
        <span class="state-icon warning"><TriangleAlert class="h-10 w-10" /></span>
        <h1>Cloud 登录失败</h1>
        <p>{{ message }}</p>
        <AiButton variant="primary" @click="router.replace('/login')">返回登录页</AiButton>
      </div>
    </section>
  </div>
</template>

<style scoped>
.cloud-login-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 24px;
  background: var(--ai-bg-warm);
}

.cloud-login-panel {
  display: grid;
  width: min(440px, calc(100vw - 48px));
  min-height: 280px;
  place-items: center;
  border: 1px solid var(--ai-border);
  border-radius: 30px;
  padding: 36px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-shell);
}

.state-content {
  display: grid;
  justify-items: center;
  gap: 14px;
  text-align: center;
}

.state-icon {
  display: grid;
  height: 68px;
  width: 68px;
  place-items: center;
  border-radius: 24px;
}

.state-icon.loading {
  background: #efffbe;
  color: var(--ai-graphite);
}

.state-icon.warning {
  background: #fff7ed;
  color: #b45309;
}

.state-content h1 {
  margin: 0;
  color: var(--ai-text);
  font-size: 24px;
  font-weight: 950;
}

.state-content p {
  margin: 0;
  color: var(--ai-text-muted);
  font-weight: 700;
}
</style>
