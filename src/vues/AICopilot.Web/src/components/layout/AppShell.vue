<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ChatLineRound,
  Collection,
  DataAnalysis,
  Key,
  Lock,
  SwitchButton
} from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/authStore'
import { useChatStore } from '@/stores/chatStore'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const chatStore = useChatStore()

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
  background: #17202a;
  color: #f8fafc;
}

.brand {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.12);
}

.brand-mark {
  display: grid;
  width: 40px;
  height: 40px;
  place-items: center;
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: 8px;
  background: #0f766e;
}

.brand-copy {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.brand-copy strong {
  font-size: 16px;
  font-weight: 800;
}

.brand-copy span {
  color: #b9c5d1;
  font-size: 12px;
}

.nav-list {
  display: grid;
  gap: 6px;
  padding: 14px;
}

.nav-item {
  display: grid;
  grid-template-columns: 28px minmax(0, 1fr);
  gap: 10px;
  width: 100%;
  align-items: center;
  border: 1px solid transparent;
  border-radius: 8px;
  padding: 11px 12px;
  background: transparent;
  color: #dbe5ef;
  cursor: pointer;
  text-align: left;
}

.nav-item:hover,
.nav-item.active {
  border-color: rgba(255, 255, 255, 0.16);
  background: rgba(255, 255, 255, 0.08);
}

.nav-item.active {
  color: #ffffff;
}

.nav-item span {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.nav-item strong {
  font-weight: 750;
}

.nav-item small {
  color: #a8b5c3;
  font-size: 12px;
}

.sidebar-footer {
  margin-top: auto;
  padding: 14px;
}

.boundary-note {
  display: grid;
  gap: 4px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 8px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.06);
}

.boundary-note strong {
  color: #ffffff;
  font-size: 13px;
}

.boundary-note span {
  color: #b9c5d1;
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
  background: rgba(255, 255, 255, 0.94);
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
}

.user-zone {
  display: flex;
  align-items: center;
  gap: 10px;
}

.user-avatar {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: 8px;
  background: #e6f3f1;
  color: var(--app-primary-strong);
  font-weight: 800;
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
  padding: 18px;
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
