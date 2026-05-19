<script setup lang="ts">
import { computed, onMounted, reactive } from 'vue'
import { useRouter } from 'vue-router'
import {
  Activity,
  ArrowRight,
  Bot,
  BrainCircuit,
  Cloud,
  DatabaseZap,
  KeyRound,
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

const initChecks = computed(() => {
  const status = authStore.initializationStatus
  if (!status) return []
  return [
    { label: 'Admin 角色', ok: status.hasAdminRole },
    { label: 'User 角色', ok: status.hasUserRole },
    { label: '引导管理员', ok: status.bootstrapAdminConfigured },
    { label: '管理员账号', ok: status.hasAdminUser }
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
          <span>SECURE COMMAND ACCESS</span>
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
              <input v-model="form.password" type="password" autocomplete="current-password" placeholder="输入密码" />
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

      <section class="ai-preview-panel" aria-label="AICopilot workbench preview">
        <header class="preview-header">
          <div>
            <span>AI OPERATIONS</span>
            <h2>今日工作台</h2>
          </div>
          <div class="live-chip">
            <i />
            在线
          </div>
        </header>

        <div class="preview-kpis">
          <article class="kpi-card lime">
            <DatabaseZap :size="21" />
            <span>只读数据源</span>
            <strong>12</strong>
          </article>
          <article class="kpi-card blue">
            <BrainCircuit :size="21" />
            <span>知识命中率</span>
            <strong>86%</strong>
          </article>
          <article class="kpi-card teal">
            <ShieldCheck :size="21" />
            <span>审批守门</span>
            <strong>ON</strong>
          </article>
        </div>

        <div class="canvas-preview">
          <div class="orb yellow">
            <strong>RAG</strong>
            <span>知识检索</span>
          </div>
          <div class="orb coral">
            <strong>MCP</strong>
            <span>工具受控</span>
          </div>
          <div class="orb dark">
            <strong>Cloud</strong>
            <span>只读</span>
          </div>
          <div class="legend-list">
            <span><i class="yellow-dot" /> 可信上下文</span>
            <span><i class="coral-dot" /> 待审批动作</span>
            <span><i class="dark-dot" /> 产物工作区</span>
          </div>
        </div>

        <div class="status-list">
          <article>
            <Workflow :size="20" />
            <div>
              <strong>Agent 计划待确认</strong>
              <span>计划 / 步骤 / 产物 / 审计统一收纳</span>
            </div>
            <small>READY</small>
          </article>
          <article>
            <Activity :size="20" />
            <div>
              <strong>流式会话稳定</strong>
              <span>SSE、Markdown、Widget 保持原协议</span>
            </div>
            <small>OK</small>
          </article>
          <article>
            <KeyRound :size="20" />
            <div>
              <strong>安全边界锁定</strong>
              <span>不新增 Cloud 写入能力</span>
            </div>
            <small>LOCKED</small>
          </article>
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
  background:
    radial-gradient(circle at 20% 12%, rgba(200, 255, 61, 0.12), transparent 24%),
    linear-gradient(135deg, var(--ai-bg-warm), var(--ai-bg));
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
.live-chip,
.status-list article {
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
.field span,
.status-list span,
.kpi-card span,
.canvas-preview span {
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
  background: linear-gradient(90deg, #ece8df, #ffffff, #ece8df);
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
  background: #ecefeb;
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

.live-chip {
  gap: 8px;
  min-height: 46px;
  border-radius: 999px;
  padding: 0 18px;
  background: #dff4ee;
  color: #0f766e;
  font-weight: 900;
}

.live-chip i {
  width: 9px;
  height: 9px;
  border-radius: 999px;
  background: #10a37f;
}

.preview-kpis {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 16px;
}

.kpi-card {
  display: grid;
  gap: 12px;
  min-height: 172px;
  border-radius: 24px;
  padding: 22px;
}

.kpi-card strong {
  align-self: end;
  font-size: 42px;
  font-weight: 950;
  font-variant-numeric: tabular-nums;
}

.kpi-card.lime {
  background: #e9ff9a;
}

.kpi-card.blue {
  background: #cfe1ff;
}

.kpi-card.teal {
  background: #9de5c1;
}

.canvas-preview {
  position: relative;
  min-height: 260px;
  overflow: hidden;
  border-radius: 28px;
  padding: 28px;
  background:
    radial-gradient(circle at 72% 26%, rgba(255, 220, 88, 0.28), transparent 28%),
    linear-gradient(135deg, #dfe7df, #ccd9d4);
  color: var(--ai-text);
  box-shadow: var(--ai-shadow-canvas);
}

.orb {
  position: absolute;
  display: grid;
  place-items: center;
  width: 142px;
  height: 142px;
  border-radius: 999px;
  text-align: center;
  box-shadow: inset 0 0 26px rgba(255, 255, 255, 0.25);
}

.orb strong {
  font-size: 22px;
  font-weight: 950;
}

.orb span {
  color: rgba(23, 27, 36, 0.66);
  font-size: 12px;
}

.orb.yellow {
  top: 36px;
  right: 66px;
  width: 176px;
  height: 176px;
  background: radial-gradient(circle, #ffdc58, #f7c62f);
  color: var(--ai-text);
}

.orb.coral {
  right: 198px;
  bottom: 38px;
  background: radial-gradient(circle, #ff8a7f, #f55f55);
  color: var(--ai-text);
}

.orb.dark {
  top: 48px;
  left: 54px;
  width: 112px;
  height: 112px;
  background: radial-gradient(circle, #79aaa4, #3f6f73);
  color: #ffffff;
}

.orb.dark span {
  color: rgba(255, 255, 255, 0.78);
}

.legend-list {
  position: absolute;
  bottom: 26px;
  left: 28px;
  display: grid;
  gap: 10px;
}

.legend-list span {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: rgba(23, 27, 36, 0.72);
  font-weight: 700;
}

.legend-list i {
  width: 18px;
  height: 8px;
  border-radius: 999px;
}

.yellow-dot {
  background: #ffdc58;
}

.coral-dot {
  background: #ff7b6f;
}

.dark-dot {
  background: #5f8f8b;
}

.status-list {
  display: grid;
  gap: 12px;
}

.status-list article {
  gap: 14px;
  min-height: 74px;
  border-radius: 22px;
  padding: 16px 18px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.status-list article > div {
  display: grid;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.status-list strong {
  font-size: 16px;
}

.status-list small {
  color: var(--ai-text-soft);
  font-family: "Cascadia Mono", Consolas, monospace;
  font-weight: 900;
}

html.dark .ai-preview-panel {
  background: #171a14;
}

html.dark .field-control,
html.dark .cloud-action,
html.dark .status-list article {
  background: var(--ai-surface);
}

html.dark .canvas-preview {
  background:
    radial-gradient(circle at 72% 26%, rgba(255, 220, 88, 0.16), transparent 28%),
    linear-gradient(135deg, #202a27, #17201f);
}

@media (max-width: 1180px) {
  .login-shell {
    grid-template-columns: minmax(0, 1fr);
  }

  .ai-preview-panel {
    display: none;
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
