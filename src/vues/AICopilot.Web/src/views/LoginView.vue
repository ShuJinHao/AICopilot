<script setup lang="ts">
import { reactive } from 'vue'
import { useRouter } from 'vue-router'
import { Lock, User } from '@element-plus/icons-vue'
import { ApiError } from '@/services/apiClient'
import { useAuthStore } from '@/stores/authStore'

const router = useRouter()
const authStore = useAuthStore()

const form = reactive({
  username: '',
  password: ''
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
</script>

<template>
  <div class="login-page">
    <section class="login-panel">
      <div class="product-copy">
        <div class="brand-line">
          <span class="brand-mark">AI</span>
          <span>AICopilot</span>
        </div>
        <h1>制造 AI 运维工作台</h1>
        <p>
          面向设备状态、日志根因、配方知识、产能数据和只读业务查询的统一入口。
        </p>
        <div class="capability-list">
          <span>只读分析</span>
          <span>审批守门</span>
          <span>知识检索</span>
          <span>图表结果</span>
        </div>
      </div>

      <div class="login-card">
        <header>
          <p class="page-kicker">Secure Sign In</p>
          <h2>登录控制台</h2>
          <span>使用已授权账号进入 AICopilot。</span>
        </header>

        <el-alert
          v-if="authStore.errorMessage"
          :title="authStore.errorMessage"
          type="error"
          show-icon
          :closable="false"
        />

        <div v-if="authStore.isInitializing" class="state-panel">
          <el-skeleton :rows="4" animated />
        </div>

        <div v-else-if="authStore.initializationStatus && !authStore.isInitialized" class="state-panel">
          <strong>系统尚未完成初始化</strong>
          <p>请先完成后端角色与管理员引导配置，再使用登录入口。</p>
          <div class="status-grid">
            <el-tag :type="authStore.initializationStatus.hasAdminRole ? 'success' : 'warning'">
              Admin 角色：{{ authStore.initializationStatus.hasAdminRole ? '已创建' : '未创建' }}
            </el-tag>
            <el-tag :type="authStore.initializationStatus.hasUserRole ? 'success' : 'warning'">
              User 角色：{{ authStore.initializationStatus.hasUserRole ? '已创建' : '未创建' }}
            </el-tag>
            <el-tag :type="authStore.initializationStatus.bootstrapAdminConfigured ? 'success' : 'warning'">
              引导管理员：{{ authStore.initializationStatus.bootstrapAdminConfigured ? '已配置' : '未配置' }}
            </el-tag>
            <el-tag :type="authStore.initializationStatus.hasAdminUser ? 'success' : 'warning'">
              管理员账号：{{ authStore.initializationStatus.hasAdminUser ? '已存在' : '未创建' }}
            </el-tag>
          </div>
        </div>

        <el-form v-else class="login-form" label-position="top" @submit.prevent="handleLogin">
          <el-form-item label="用户名">
            <el-input v-model="form.username" placeholder="输入用户名" autocomplete="username">
              <template #prefix>
                <el-icon><User /></el-icon>
              </template>
            </el-input>
          </el-form-item>

          <el-form-item label="密码">
            <el-input
              v-model="form.password"
              type="password"
              show-password
              placeholder="输入密码"
              autocomplete="current-password"
            >
              <template #prefix>
                <el-icon><Lock /></el-icon>
              </template>
            </el-input>
          </el-form-item>

          <el-button
            type="primary"
            size="large"
            class="login-button"
            :loading="authStore.isSubmitting"
            :disabled="!form.username || !form.password"
            @click="handleLogin"
          >
            登录
          </el-button>
        </el-form>
      </div>
    </section>
  </div>
</template>

<style scoped>
.login-page {
  display: grid;
  min-height: 100vh;
  overflow-x: hidden;
  place-items: center;
  padding: 24px;
  background:
    linear-gradient(90deg, rgba(23, 32, 42, 0.92), rgba(23, 32, 42, 0.82)),
    repeating-linear-gradient(90deg, rgba(255, 255, 255, 0.06) 0 1px, transparent 1px 72px),
    repeating-linear-gradient(0deg, rgba(255, 255, 255, 0.04) 0 1px, transparent 1px 72px),
    #17202a;
}

.login-panel {
  display: grid;
  width: min(980px, calc(100vw - 48px));
  grid-template-columns: minmax(0, 1fr) 420px;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.96);
  box-shadow: var(--app-shadow);
}

.product-copy {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  gap: 16px;
  min-width: 0;
  min-height: 520px;
  padding: 38px;
  background: #17202a;
  color: #ffffff;
}

.brand-line {
  display: flex;
  align-items: center;
  gap: 10px;
  font-weight: 800;
}

.brand-mark {
  display: grid;
  width: 40px;
  height: 40px;
  place-items: center;
  border-radius: 8px;
  background: var(--app-primary);
}

.product-copy h1 {
  margin: 0;
  max-width: 520px;
  overflow-wrap: anywhere;
  font-size: 38px;
  font-weight: 850;
  letter-spacing: 0;
}

.product-copy p {
  margin: 0;
  max-width: 560px;
  overflow-wrap: anywhere;
  color: #cbd5e1;
  font-size: 15px;
}

.capability-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.capability-list span {
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 8px;
  padding: 6px 10px;
  color: #e2e8f0;
  font-size: 12px;
}

.login-card {
  display: grid;
  align-content: center;
  gap: 18px;
  min-width: 0;
  padding: 38px;
  background: var(--app-surface);
}

.login-card header h2 {
  margin: 0;
  font-size: 24px;
  font-weight: 800;
}

.login-card header span,
.state-panel p {
  color: var(--app-text-muted);
}

.state-panel {
  display: grid;
  gap: 12px;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 14px;
  background: var(--app-surface-muted);
}

.status-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.login-form {
  display: grid;
  gap: 8px;
}

.login-button {
  width: 100%;
  margin-top: 4px;
}

@media (max-width: 820px) {
  .login-page {
    padding: 16px;
  }

  .login-panel {
    width: min(100%, calc(100vw - 32px));
    grid-template-columns: 1fr;
  }

  .product-copy {
    min-height: auto;
    padding: 28px;
  }

  .product-copy h1 {
    font-size: 28px;
  }

  .login-card {
    padding: 28px;
  }
}
</style>
