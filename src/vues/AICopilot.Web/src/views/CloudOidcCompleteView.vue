<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Loading, WarningFilled } from '@element-plus/icons-vue'
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
  } catch {
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
        <el-icon class="state-icon loading"><Loading /></el-icon>
        <h1>正在完成 Cloud 登录</h1>
        <p>正在校验 Cloud 身份并换取 AICopilot 登录态。</p>
      </div>

      <div v-else class="state-content">
        <el-icon class="state-icon warning"><WarningFilled /></el-icon>
        <h1>Cloud 登录失败</h1>
        <p>{{ message }}</p>
        <el-button type="primary" @click="router.replace('/login')">返回登录页</el-button>
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
  background: var(--app-bg);
}

.cloud-login-panel {
  display: grid;
  width: min(420px, calc(100vw - 48px));
  min-height: 260px;
  place-items: center;
  border: 1px solid var(--app-border);
  border-radius: var(--radius-lg);
  padding: 32px;
  background: var(--app-surface);
  box-shadow: var(--shadow-lg);
}

.state-content {
  display: grid;
  justify-items: center;
  gap: 12px;
  text-align: center;
}

.state-icon {
  font-size: 42px;
}

.state-icon.loading {
  color: var(--app-primary);
  animation: rotate 1s linear infinite;
}

.state-icon.warning {
  color: var(--app-warning);
}

.state-content h1 {
  margin: 0;
  font-size: 22px;
  color: var(--app-text);
}

.state-content p {
  margin: 0;
  color: var(--app-text-muted);
}

@keyframes rotate {
  from {
    transform: rotate(0deg);
  }

  to {
    transform: rotate(360deg);
  }
}
</style>
