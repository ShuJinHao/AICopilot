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
  if (authStore.canUseChat) {
    return '/chat'
  }

  if (authStore.canViewConfig) {
    return '/config'
  }

  if (authStore.canManageAccess) {
    return '/access'
  }

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
    <div class="login-card">
      <div class="login-hero">
        <div class="hero-mark">AI</div>
        <h1>AICopilot</h1>
        <p>面向设备、配方、产能、日志和生产数据的业务助手。</p>
      </div>

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
        <h2>系统尚未完成初始化</h2>
        <p>当前不开放前端注册。请先在后端完成角色和管理员引导配置，再使用登录页进入系统。</p>
        <div class="status-grid">
          <el-tag :type="authStore.initializationStatus.hasAdminRole ? 'success' : 'warning'">
            Admin 角色: {{ authStore.initializationStatus.hasAdminRole ? '已创建' : '未创建' }}
          </el-tag>
          <el-tag :type="authStore.initializationStatus.hasUserRole ? 'success' : 'warning'">
            User 角色: {{ authStore.initializationStatus.hasUserRole ? '已创建' : '未创建' }}
          </el-tag>
          <el-tag :type="authStore.initializationStatus.bootstrapAdminConfigured ? 'info' : 'warning'">
            BootstrapAdmin: {{ authStore.initializationStatus.bootstrapAdminConfigured ? '已配置' : '未配置' }}
          </el-tag>
          <el-tag :type="authStore.initializationStatus.hasAdminUser ? 'success' : 'warning'">
            管理员账号: {{ authStore.initializationStatus.hasAdminUser ? '已存在' : '未创建' }}
          </el-tag>
        </div>
      </div>

      <el-form v-else class="login-form" label-position="top" @submit.prevent="handleLogin">
        <el-form-item label="用户名">
          <el-input v-model="form.username" placeholder="请输入用户名">
            <template #prefix>
              <el-icon><User /></el-icon>
            </template>
          </el-input>
        </el-form-item>

        <el-form-item label="密码">
          <el-input v-model="form.password" type="password" show-password placeholder="请输入密码">
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
  </div>
</template>

<style scoped>
.login-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background:
    radial-gradient(circle at top, rgba(15, 118, 110, 0.18), transparent 28%),
    linear-gradient(135deg, #eff6ff 0%, #f8fafc 45%, #eef2ff 100%);
}

.login-card {
  width: min(100%, 440px);
  display: grid;
  gap: 18px;
  padding: 28px;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.92);
  box-shadow: 0 18px 48px rgba(15, 23, 42, 0.14);
}

.login-hero {
  text-align: center;
}

.hero-mark {
  width: 64px;
  height: 64px;
  margin: 0 auto 16px;
  border-radius: 20px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #0f766e, #2563eb);
  color: white;
  font-size: 24px;
  font-weight: 700;
}

.login-hero h1 {
  margin: 0 0 8px;
  font-size: 28px;
  font-weight: 700;
  color: #0f172a;
}

.login-hero p,
.state-panel p {
  color: #64748b;
}

.state-panel {
  display: grid;
  gap: 16px;
  padding: 18px;
  border-radius: 18px;
  background: #f8fafc;
  border: 1px solid rgba(148, 163, 184, 0.2);
}

.status-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.login-form {
  display: grid;
  gap: 8px;
}

.login-button {
  width: 100%;
  margin-top: 8px;
}
</style>
