<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  ArrowRight,
  Bot,
  BrainCircuit,
  Cloud,
  DatabaseZap,
  Eye,
  EyeOff,
  Lock,
  ShieldCheck,
  User,
  Workflow
} from 'lucide-vue-next'
import { ApiError } from '@/services/apiClient'
import { useAuthStore } from '@/stores/authStore'

const router = useRouter()
const authStore = useAuthStore()

const form = reactive({
  username: '',
  password: ''
})
const showPassword = ref(false)

const initChecks = computed(() => {
  const status = authStore.initializationStatus
  if (!status) return []
  return [
    { label: 'Admin 角色', ok: status.hasAdminRole },
    { label: 'User 角色', ok: status.hasUserRole },
    { label: '引导管理员', ok: status.bootstrapAdminConfigured },
    { label: '启用中的管理员账号', ok: status.hasEnabledAdminUser }
  ]
})

function resolveAuthorizedPath() {
  if (authStore.canUseChat) return '/chat'
  if (authStore.canViewConfig) return '/config'
  if (authStore.canManageKnowledge) return '/knowledge'
  if (authStore.canManageAccess) return '/access'
  return '/forbidden'
}

async function handleLogin() {
  try {
    await authStore.login(form)
    await router.replace(resolveAuthorizedPath())
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return
    }
  }
}

onMounted(() => {
  void authStore.ensureCloudOidcStatus()
})
</script>

<template>
  <div class="login-page">
    <main class="login-shell">
      <section class="login-form-panel">
        <div class="brand-row">
          <div class="brand-mark">
            <Bot :size="26" stroke-width="2.5" />
          </div>
          <div>
            <strong>AICopilot</strong>
            <span>制造 AI 运维工作台</span>
          </div>
        </div>

        <div class="login-title">
          <span>安全访问</span>
          <h1>登录 A 助理</h1>
          <p>进入只读分析、知识检索、Agent 审批和产物工作区。</p>
        </div>

        <div v-if="authStore.errorMessage" class="error-banner" role="alert">
          {{ authStore.errorMessage }}
        </div>

        <div v-if="authStore.isInitializing" class="loading-card">
          <i />
          <i />
          <i />
          <span>正在检查初始化状态</span>
        </div>

        <section v-else-if="authStore.initializationStatus && !authStore.isInitialized" class="init-card">
          <strong>系统尚未完成初始化</strong>
          <p>请先完成后端角色与管理员引导配置，再使用登录入口。</p>
          <div class="init-grid">
            <span v-for="item in initChecks" :key="item.label" :class="{ ok: item.ok }">
              {{ item.label }}：{{ item.ok ? '已完成' : '待处理' }}
            </span>
          </div>
        </section>

        <form v-else class="login-form" @submit.prevent="handleLogin">
          <label class="field">
            <span>用户名</span>
            <div class="field-control">
              <User :size="21" />
              <input v-model="form.username" autocomplete="username" placeholder="输入用户名" />
            </div>
          </label>

          <label class="field">
            <span>密码</span>
            <div class="field-control">
              <Lock :size="21" />
              <input
                v-model="form.password"
                :type="showPassword ? 'text' : 'password'"
                autocomplete="current-password"
                placeholder="输入密码"
              />
              <button
                class="password-visibility-button"
                type="button"
                :aria-label="showPassword ? '隐藏密码' : '显示密码'"
                :title="showPassword ? '隐藏密码' : '显示密码'"
                @click="showPassword = !showPassword"
              >
                <EyeOff v-if="showPassword" :size="20" />
                <Eye v-else :size="20" />
              </button>
            </div>
          </label>

          <button class="primary-action" type="submit" :disabled="authStore.isSubmitting || !form.username || !form.password">
            <span>{{ authStore.isSubmitting ? '登录中' : '登录' }}</span>
            <ArrowRight :size="20" />
          </button>

          <button
            class="cloud-action"
            type="button"
            :disabled="authStore.isCloudLoginSubmitting || authStore.isCloudOidcStatusLoading || !authStore.isCloudOidcEnabled"
            @click="authStore.startCloudOidcLogin()"
          >
            <Cloud :size="20" />
            <span>{{ authStore.isCloudOidcEnabled ? '使用 Cloud 登录' : 'Cloud 登录未启用' }}</span>
          </button>
        </form>

        <div class="login-footnote">
          Cloud 数据保持只读边界，所有高风险动作必须经过人工审批。
        </div>
      </section>

      <section class="ai-preview-panel" aria-label="AICopilot 能力边界">
        <header class="preview-header">
          <div>
            <span>工作范围</span>
            <h2>工业生产助手</h2>
          </div>
          <div class="boundary-chip">
            <ShieldCheck :size="18" />
            Cloud 写入禁用
          </div>
        </header>

        <section class="boundary-summary">
          <span>回答、计划与执行清晰分层</span>
          <h3>只展示真实证据，不用演示数字代替运行状态。</h3>
          <p>查询结果、状态、行数、截断和失败原因均以本次后端响应为准。</p>
        </section>

        <div class="capability-grid">
          <article>
            <DatabaseZap :size="21" />
            <div>
              <strong>受控只读分析</strong>
              <span>设备、工序、客户端版本、日志与生产证据可按正式契约发起读取；未配置时返回真实错误。</span>
            </div>
          </article>
          <article>
            <BrainCircuit :size="21" />
            <div>
              <strong>知识检索</strong>
              <span>引用受权限约束的知识库内容，并保留来源边界。</span>
            </div>
          </article>
          <article>
            <Workflow :size="21" />
            <div>
              <strong>计划确认</strong>
              <span>计划草案确认前不执行 Cloud 查询、工具调用或 Worker 任务。</span>
            </div>
          </article>
          <article>
            <ShieldCheck :size="21" />
            <div>
              <strong>审批与产物留痕</strong>
              <span>工具审批和正式产物确认独立记录；人工确认不授权 Cloud 写入。</span>
            </div>
          </article>
        </div>

        <div class="truth-note">
          <ShieldCheck :size="20" />
          <div>
            <strong>状态来源原则</strong>
            <span>页面只展示后端返回的真实状态；空结果不会被解释为离线或成功。</span>
          </div>
        </div>
      </section>
    </main>
  </div>
</template>

<style scoped>
.login-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 28px;
  background: var(--ai-bg);
}

.login-shell {
  display: grid;
  grid-template-columns: minmax(380px, 0.9fr) minmax(520px, 1.25fr);
  width: min(1500px, calc(100vw - 56px));
  min-height: min(780px, calc(100vh - 56px));
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.72);
  border-radius: 34px;
  background: var(--ai-shell);
  box-shadow: var(--ai-shadow-shell);
}

.login-form-panel {
  display: grid;
  align-content: center;
  gap: 32px;
  min-width: 0;
  padding: 56px 64px;
  background: var(--ai-surface);
}

.brand-row,
.field-control,
.primary-action,
.cloud-action,
.preview-header,
.boundary-chip,
.capability-grid article,
.truth-note {
  display: flex;
  align-items: center;
}

.brand-row {
  gap: 14px;
}

.brand-mark {
  display: grid;
  width: 58px;
  height: 58px;
  place-items: center;
  border-radius: 18px;
  background: var(--ai-graphite);
  color: var(--ai-lime);
  box-shadow: 0 18px 34px rgba(63, 111, 115, 0.2);
}

.brand-row div:last-child {
  display: grid;
  gap: 2px;
}

.brand-row strong {
  font-size: 25px;
  font-weight: 900;
}

.brand-row span,
.login-title p,
.login-footnote,
.field span {
  color: var(--ai-text-muted);
}

.login-title {
  display: grid;
  gap: 10px;
}

.login-title span,
.preview-header span {
  color: var(--ai-text-soft);
  font-size: 12px;
  font-weight: 850;
}

.login-title h1 {
  margin: 0;
  font-size: 48px;
  font-weight: 950;
  letter-spacing: 0;
}

.login-title p {
  margin: 0;
  max-width: 430px;
  font-size: 16px;
}

.login-form,
.init-card,
.loading-card {
  display: grid;
  gap: 18px;
}

.field {
  display: grid;
  gap: 8px;
  font-weight: 800;
}

.field-control {
  gap: 12px;
  min-height: 56px;
  border: 1px solid var(--ai-border-strong);
  border-radius: 18px;
  padding: 0 16px;
  background: #fbfaf7;
  color: var(--ai-text-muted);
  transition: border-color 0.18s ease, box-shadow 0.18s ease, background-color 0.18s ease;
}

.field-control:focus-within {
  border-color: rgba(63, 111, 115, 0.34);
  background: var(--ai-surface);
  box-shadow: 0 0 0 4px var(--ai-focus);
}

.field-control input {
  width: 100%;
  min-width: 0;
  border: 0;
  outline: none;
  background: transparent;
  color: var(--ai-text);
  font-size: 16px;
}

.password-visibility-button {
  display: grid;
  flex: 0 0 auto;
  width: 38px;
  height: 38px;
  place-items: center;
  border: 0;
  border-radius: 12px;
  background: transparent;
  color: var(--ai-text-muted);
  cursor: pointer;
  transition: background-color 0.18s ease, color 0.18s ease;
}

.password-visibility-button:hover {
  background: rgba(63, 111, 115, 0.08);
  color: var(--ai-text);
}

.password-visibility-button:focus-visible {
  outline: 3px solid var(--ai-focus);
  outline-offset: 2px;
}

.primary-action,
.cloud-action {
  justify-content: center;
  gap: 10px;
  min-height: 56px;
  border: 0;
  border-radius: 18px;
  font-size: 16px;
  font-weight: 900;
  cursor: pointer;
  transition: transform 0.18s ease, opacity 0.18s ease, background-color 0.18s ease;
}

.primary-action {
  margin-top: 4px;
  background: var(--ai-graphite);
  color: white;
  box-shadow: 0 18px 34px rgba(63, 111, 115, 0.22);
}

.primary-action:hover:not(:disabled),
.cloud-action:hover:not(:disabled) {
  transform: translateY(-1px);
}

.cloud-action {
  border: 1px solid var(--ai-border);
  background: var(--ai-surface-soft);
  color: var(--ai-text);
}

.primary-action:disabled,
.cloud-action:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.error-banner,
.init-card {
  border: 1px solid #fecaca;
  border-radius: 18px;
  padding: 14px 16px;
  background: #fef2f2;
  color: #b42318;
  font-weight: 800;
}

.init-card {
  border-color: var(--ai-border);
  background: var(--ai-surface-soft);
  color: var(--ai-text);
}

.init-card p {
  margin: 0;
  color: var(--ai-text-muted);
}

.init-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.init-grid span {
  border-radius: 999px;
  padding: 8px 10px;
  background: #fff7ed;
  color: #b45309;
  font-size: 12px;
  font-weight: 800;
}

.init-grid span.ok {
  background: #f0fdf4;
  color: #15803d;
}

.loading-card {
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 16px;
  background: var(--ai-surface-soft);
}

.loading-card i {
  height: 14px;
  border-radius: 999px;
  background: #ece8df;
}

.login-footnote {
  font-size: 13px;
}

.ai-preview-panel {
  display: grid;
  align-content: start;
  gap: 22px;
  min-width: 0;
  padding: 52px;
  background: var(--ai-surface-soft);
  border-left: 1px solid var(--ai-border);
}

.preview-header {
  justify-content: space-between;
  gap: 18px;
}

.preview-header h2 {
  margin: 4px 0 0;
  font-size: 36px;
  font-weight: 950;
}

.boundary-chip {
  gap: 8px;
  min-height: 46px;
  border-radius: 999px;
  padding: 0 18px;
  border: 1px solid rgba(63, 111, 115, 0.16);
  background: var(--ai-surface);
  color: var(--ai-graphite);
  font-weight: 900;
}

.boundary-summary {
  display: grid;
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 24px;
  padding: 28px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.boundary-summary span,
.capability-grid span,
.truth-note span {
  color: var(--ai-text-muted);
}

.boundary-summary > span {
  font-size: 12px;
  font-weight: 850;
}

.boundary-summary h3 {
  margin: 0;
  max-width: 680px;
  color: var(--ai-text);
  font-size: clamp(28px, 3vw, 44px);
  font-weight: 950;
  line-height: 1.08;
}

.boundary-summary p {
  margin: 0;
  max-width: 660px;
  color: var(--ai-text-muted);
  font-size: 15px;
  line-height: 1.65;
}

.capability-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.capability-grid article {
  align-items: flex-start;
  gap: 14px;
  min-height: 132px;
  border: 1px solid var(--ai-border);
  border-radius: 22px;
  padding: 20px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.capability-grid article > svg,
.truth-note > svg {
  flex: 0 0 auto;
  color: var(--ai-graphite);
}

.capability-grid article > div,
.truth-note > div {
  display: grid;
  gap: 6px;
  min-width: 0;
}

.capability-grid strong,
.truth-note strong {
  color: var(--ai-text);
  font-size: 15px;
  font-weight: 900;
}

.capability-grid span,
.truth-note span {
  font-size: 13px;
  font-weight: 700;
  line-height: 1.6;
}

.truth-note {
  align-items: flex-start;
  gap: 14px;
  border-left: 3px solid var(--ai-graphite);
  border-radius: 0 18px 18px 0;
  padding: 16px 18px;
  background: color-mix(in srgb, var(--ai-surface) 78%, var(--ai-graphite-muted));
}

html.dark .ai-preview-panel {
  background: #171a14;
}

html.dark .field-control,
html.dark .cloud-action {
  background: var(--ai-surface);
}

@media (max-height: 850px) and (min-width: 1181px) {
  .login-page {
    padding: 18px;
  }

  .login-shell {
    width: min(1500px, calc(100vw - 36px));
    min-height: calc(100vh - 36px);
  }

  .login-form-panel {
    gap: 20px;
    padding: 32px 44px;
  }

  .brand-mark {
    width: 50px;
    height: 50px;
    border-radius: 16px;
  }

  .login-title {
    gap: 6px;
  }

  .login-title h1 {
    font-size: 40px;
  }

  .login-title p {
    font-size: 14px;
  }

  .login-form {
    gap: 12px;
  }

  .field {
    gap: 5px;
  }

  .field-control,
  .primary-action,
  .cloud-action {
    min-height: 50px;
  }

  .ai-preview-panel {
    gap: 14px;
    padding: 28px 36px;
  }

  .preview-header h2 {
    font-size: 30px;
  }

  .boundary-chip {
    min-height: 40px;
  }

  .boundary-summary {
    gap: 7px;
    padding: 20px 24px;
  }

  .boundary-summary h3 {
    font-size: 32px;
  }

  .boundary-summary p {
    font-size: 13px;
  }

  .capability-grid article {
    min-height: 104px;
    padding: 15px 16px;
  }

  .capability-grid span,
  .truth-note span {
    font-size: 12px;
    line-height: 1.5;
  }

  .truth-note {
    padding: 12px 16px;
  }
}

@media (max-width: 1180px) {
  .login-shell {
    grid-template-columns: minmax(0, 1fr);
  }

  .ai-preview-panel {
    display: none;
  }
}

@media (max-width: 1180px) and (min-width: 641px) and (max-height: 850px) {
  .login-page {
    padding: 18px;
  }

  .login-shell {
    width: calc(100vw - 36px);
    min-height: calc(100vh - 36px);
  }

  .login-form-panel {
    gap: 20px;
    padding: 32px 64px;
  }

  .brand-mark {
    width: 50px;
    height: 50px;
    border-radius: 16px;
  }

  .login-title {
    gap: 6px;
  }

  .login-title h1 {
    font-size: 40px;
  }

  .login-form {
    gap: 12px;
  }

  .field {
    gap: 5px;
  }

  .field-control,
  .primary-action,
  .cloud-action {
    min-height: 50px;
  }
}

@media (max-width: 640px) {
  .login-page {
    padding: 14px;
  }

  .login-shell {
    width: 100%;
    min-height: calc(100vh - 28px);
    border-radius: 26px;
  }

  .login-form-panel {
    padding: 34px 24px;
  }

  .login-title h1 {
    font-size: 38px;
  }
}
</style>
