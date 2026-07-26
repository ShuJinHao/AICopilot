<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { KeyRound, LoaderCircle, TriangleAlert } from 'lucide-vue-next'
import { useRoute, useRouter } from 'vue-router'
import AiButton from '@/components/ai/AiButton.vue'
import BuildIdentityFacts from '@/components/layout/BuildIdentityFacts.vue'
import { useAuthStore } from '@/stores/authStore'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const state = ref<'loading' | 'confirm' | 'failed'>('loading')
const password = ref('')

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
    state.value = 'failed'
    return
  }

  try {
    await authStore.finalizeCloudOidcLogin()
    await router.replace(resolveAuthorizedPath())
  } catch (error) {
    console.error('Failed to complete Cloud OIDC login.', error)
    state.value = authStore.isCloudAccountConfirmationRequired ? 'confirm' : 'failed'
  }
}

async function confirmExistingAccount() {
  if (!password.value || authStore.isCloudAccountConfirming) {
    return
  }

  try {
    await authStore.confirmExistingCloudOidcAccount(password.value)
    password.value = ''
    await router.replace(resolveAuthorizedPath())
  } catch (error) {
    console.error('Failed to confirm the existing AICopilot account.', error)
    password.value = ''
    state.value = authStore.isCloudAccountConfirmationRequired ? 'confirm' : 'failed'
  }
}

async function cancelConfirmation() {
  password.value = ''
  try {
    await authStore.cancelCloudOidcAccountConfirmation()
    await router.replace('/login')
  } catch (error) {
    console.error('Failed to cancel Cloud OIDC account confirmation.', error)
    state.value = 'failed'
  }
}

onMounted(() => {
  void completeLogin()
})
</script>

<template>
  <div class="cloud-login-page">
    <section class="cloud-login-panel">
      <div v-if="state === 'loading'" class="state-content">
        <span class="state-icon loading"><LoaderCircle class="h-10 w-10 animate-spin" /></span>
        <h1>正在完成 Cloud 登录</h1>
        <p>正在校验 Cloud 身份并换取 AICopilot 登录态。</p>
      </div>

      <form v-else-if="state === 'confirm'" class="state-content confirmation-form" @submit.prevent="confirmExistingAccount">
        <span class="state-icon confirmation"><KeyRound class="h-10 w-10" /></span>
        <h1>确认本地 AI 账号</h1>
        <p>检测到同名的本地账号。请输入该账号的本地密码，确认后会保留原有 AI 角色和权限。</p>
        <label class="password-field">
          <span>本地 AI 账号密码</span>
          <input
            v-model="password"
            type="password"
            autocomplete="current-password"
            placeholder="输入本地密码"
            autofocus
          />
        </label>
        <p v-if="authStore.errorMessage" class="form-error" role="alert">{{ authStore.errorMessage }}</p>
        <div class="confirmation-actions">
          <AiButton
            type="submit"
            variant="primary"
            :disabled="!password || authStore.isCloudAccountConfirming"
          >
            {{ authStore.isCloudAccountConfirming ? '确认中' : '确认并登录' }}
          </AiButton>
          <AiButton
            type="button"
            variant="soft"
            :disabled="authStore.isCloudAccountConfirming"
            @click="cancelConfirmation"
          >
            取消
          </AiButton>
        </div>
      </form>

      <div v-else class="state-content">
        <span class="state-icon warning"><TriangleAlert class="h-10 w-10" /></span>
        <h1>Cloud 登录失败</h1>
        <p>{{ message }}</p>
        <AiButton variant="primary" @click="router.replace('/login')">返回登录页</AiButton>
      </div>

      <BuildIdentityFacts variant="auth" />
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

.state-icon.confirmation {
  background: #efffbe;
  color: var(--ai-graphite);
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

.confirmation-form {
  width: 100%;
}

.password-field {
  display: grid;
  width: 100%;
  gap: 8px;
  text-align: left;
}

.password-field span {
  color: var(--ai-text);
  font-size: 14px;
  font-weight: 850;
}

.password-field input {
  width: 100%;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 12px 14px;
  background: var(--ai-surface);
  color: var(--ai-text);
  font: inherit;
  outline: none;
}

.password-field input:focus {
  border-color: var(--ai-graphite);
  box-shadow: 0 0 0 3px rgba(63, 111, 115, 0.14);
}

.state-content .form-error {
  color: #b42318;
  font-size: 14px;
}

.confirmation-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 10px;
}
</style>
