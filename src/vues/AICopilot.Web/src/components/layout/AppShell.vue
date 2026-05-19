<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ChatLineRound,
  Collection,
  DataAnalysis,
  Key,
  Lock,
  Moon,
  Sunny,
  SwitchButton
} from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/authStore'
import { useChatStore } from '@/stores/chatStore'
import { useTheme } from '@/composables/useTheme'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const chatStore = useChatStore()
const { isDark, toggleTheme } = useTheme()

const activePath = computed(() => route.path)
const userInitial = computed(() => (authStore.userName || 'A').slice(0, 1).toUpperCase())

const navigationItems = computed(() =>
  [
    authStore.canUseChat
      ? { path: '/chat', label: 'AI 工作台', description: '会话、审批、分析结果', icon: ChatLineRound }
      : null,
    authStore.canViewConfig
      ? { path: '/config', label: '运行配置', description: '模型、MCP、数据源', icon: DataAnalysis }
      : null,
    authStore.canManageKnowledge
      ? { path: '/knowledge', label: '知识库', description: '文档、向量、检索', icon: Collection }
      : null,
    authStore.canManageAccess
      ? { path: '/access', label: '权限治理', description: '用户、角色、审计', icon: Lock }
      : null
  ].filter(
    (item): item is { path: string; label: string; description: string; icon: typeof ChatLineRound } =>
      item !== null
  )
)

async function navigate(path: string) {
  if (route.path !== path) {
    await router.push(path)
  }
}

async function logout() {
  authStore.clearAuth()
  chatStore.reset()
  await router.replace('/login')
}
</script>

<template>
  <div class="app-shell">
    <aside class="app-sidebar">
      <div class="brand">
        <div class="brand-mark">
          <el-icon><Key /></el-icon>
        </div>
        <div class="brand-copy">
          <strong>AICopilot</strong>
          <span>制造 AI 运维工作台</span>
        </div>
      </div>

      <nav class="nav-list">
        <button
          v-for="item in navigationItems"
          :key="item.path"
          class="nav-item"
          :class="{ active: activePath === item.path }"
          type="button"
          @click="navigate(item.path)"
        >
          <el-icon><component :is="item.icon" /></el-icon>
          <span>
            <strong>{{ item.label }}</strong>
            <small>{{ item.description }}</small>
          </span>
        </button>
      </nav>

      <div class="sidebar-footer">
        <div class="boundary-note">
          <strong>只读边界</strong>
          <span>AI 只做分析、诊断和建议，控制写入必须人工执行。</span>
        </div>
      </div>
    </aside>

    <section class="app-content">
      <header class="topbar">
        <div class="topbar-title">
          <span class="status-dot" />
          <span>生产系统 AI 辅助运行中</span>
        </div>
        <div class="user-zone">
          <el-button text circle :icon="isDark ? Moon : Sunny" @click="toggleTheme()" class="theme-toggle" />
          <div class="user-avatar">{{ userInitial }}</div>
          <div class="user-copy">
            <strong>{{ authStore.userName || '未登录' }}</strong>
            <span>{{ authStore.roleName || '未分配角色' }}</span>
          </div>
          <el-button text :icon="SwitchButton" @click="logout">退出</el-button>
        </div>
      </header>

      <main class="content-main">
        <slot />
      </main>
    </section>
  </div>
</template>

<style scoped>
.app-shell {
  display: grid;
  grid-template-columns: 264px minmax(0, 1fr);
  height: 100%;
  min-height: 0;
  background: var(--app-bg);
}

.app-sidebar {
  display: flex;
  min-height: 0;
  flex-direction: column;
  border-right: 1px solid var(--app-border);
  background: var(--app-surface-muted);
  color: var(--app-text);
}

html.dark .app-sidebar {
  background: var(--app-surface);
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 20px;
  border-bottom: 1px solid var(--app-border);
}

.brand-mark {
  display: grid;
  width: 40px;
  height: 40px;
  place-items: center;
  border-radius: var(--radius-md);
  background: var(--app-primary);
  color: #fff;
  box-shadow: var(--shadow-sm);
}

.brand-copy {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.brand-copy strong {
  font-size: 16px;
  font-weight: 800;
  color: var(--app-text);
}

.brand-copy span {
  color: var(--app-text-muted);
  font-size: 12px;
}

.nav-list {
  display: grid;
  gap: var(--space-2);
  padding: var(--space-4);
}

.nav-item {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr);
  gap: 10px;
  width: 100%;
  align-items: center;
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  padding: 11px 12px;
  background: transparent;
  color: var(--app-text-soft);
  cursor: pointer;
  text-align: left;
  transition: background-color 0.2s ease, border-color 0.2s ease, color 0.2s ease;
}

.nav-item:hover {
  background: var(--app-surface);
  border-color: var(--app-border);
}

.nav-item.active {
  background: var(--app-surface-raised);
  border-color: var(--app-border-strong);
  color: var(--app-primary);
  box-shadow: var(--shadow-sm);
}

.nav-item span {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.nav-item strong {
  font-weight: 750;
  color: var(--app-text);
}

.nav-item small {
  color: var(--app-text-muted);
  font-size: 12px;
}

.nav-item.active small {
  color: var(--app-text-soft);
}

.sidebar-footer {
  margin-top: auto;
  padding: var(--space-4);
}

.boundary-note {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: var(--radius-md);
  padding: 12px;
  background: var(--app-surface);
  box-shadow: var(--shadow-sm);
}

.boundary-note strong {
  color: var(--app-text);
  font-size: 13px;
}

.boundary-note span {
  color: var(--app-text-muted);
  font-size: 12px;
}

.app-content {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
}

.topbar {
  display: flex;
  height: 64px;
  flex-shrink: 0;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  border-bottom: 1px solid var(--app-border);
  background: var(--app-surface);
  padding: 0 20px;
}

.topbar-title {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--app-text-muted);
  font-size: 13px;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--app-success);
  box-shadow: 0 0 0 2px var(--app-surface), 0 0 0 4px rgba(74, 222, 128, 0.2);
}

.user-zone {
  display: flex;
  align-items: center;
  gap: 10px;
}

.theme-toggle {
  margin-right: 8px;
  font-size: 18px;
}

.user-avatar {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: var(--radius-md);
  background: var(--app-surface-muted);
  color: var(--app-primary-strong);
  font-weight: 800;
  border: 1px solid var(--app-border);
}

.user-copy {
  display: grid;
  min-width: 0;
  text-align: right;
}

.user-copy strong {
  color: var(--app-text);
  font-size: 13px;
}

.user-copy span {
  color: var(--app-text-muted);
  font-size: 12px;
}

.content-main {
  min-height: 0;
  flex: 1;
  overflow: hidden;
  padding: var(--space-4);
}

@media (max-width: 900px) {
  .app-shell {
    grid-template-columns: 1fr;
    min-height: 100%;
    overflow-x: hidden;
  }

  .app-sidebar {
    position: static;
    max-width: 100vw;
    overflow: hidden;
    border-right: none;
    border-bottom: 1px solid var(--app-border);
  }

  .brand,
  .sidebar-footer {
    display: none;
  }

  .nav-list {
    display: flex;
    max-width: 100%;
    min-width: 0;
    overflow-x: auto;
    padding: 10px;
  }

  .nav-item {
    flex: 0 0 152px;
    min-width: 0;
  }

  .topbar {
    height: auto;
    align-items: flex-start;
    padding: 12px;
  }

  .content-main {
    overflow: visible;
    padding: 12px;
  }
}
</style>
