<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ChatDotRound,
  Collection,
  DataAnalysis,
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

const navigationItems = computed(() => {
  return [
    authStore.canUseChat ? { path: '/chat', label: '聊天', icon: ChatDotRound } : null,
    authStore.canViewConfig ? { path: '/config', label: '配置管理', icon: DataAnalysis } : null,
    authStore.canManageKnowledge ? { path: '/knowledge', label: '知识库', icon: Collection } : null,
    authStore.canManageAccess ? { path: '/access', label: '权限管理', icon: Lock } : null
  ].filter((item): item is { path: string; label: string; icon: typeof ChatDotRound } => item !== null)
})

async function handleSelect(path: string) {
  await router.push(path)
}

async function logout() {
  authStore.clearAuth()
  chatStore.reset()
  await router.replace('/login')
}
</script>

<template>
  <div class="shell">
    <header class="shell-header">
      <div class="shell-brand">
        <div class="brand-mark">AI</div>
        <div>
          <div class="brand-title">AICopilot</div>
          <div class="brand-subtitle">制造业务助手</div>
        </div>
      </div>

      <el-menu
        mode="horizontal"
        :default-active="activePath"
        class="shell-menu"
        @select="handleSelect"
      >
        <el-menu-item v-for="item in navigationItems" :key="item.path" :index="item.path">
          <el-icon><component :is="item.icon" /></el-icon>
          <span>{{ item.label }}</span>
        </el-menu-item>
      </el-menu>

      <div class="shell-user">
        <div class="user-text">
          <div class="user-name">{{ authStore.userName || '未登录' }}</div>
          <div class="user-role">{{ authStore.roleName || '未分配角色' }}</div>
        </div>
        <el-button text @click="logout">
          <el-icon><SwitchButton /></el-icon>
          退出登录
        </el-button>
      </div>
    </header>

    <main class="shell-main">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.shell {
  min-height: 100%;
  display: flex;
  flex-direction: column;
  background:
    radial-gradient(circle at top left, rgba(64, 158, 255, 0.1), transparent 30%),
    linear-gradient(180deg, #f8fbff 0%, #eef3f8 100%);
}

.shell-header {
  height: 72px;
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: center;
  gap: 20px;
  padding: 0 24px;
  border-bottom: 1px solid rgba(15, 23, 42, 0.08);
  background: rgba(255, 255, 255, 0.86);
  backdrop-filter: blur(14px);
}

.shell-brand {
  display: flex;
  align-items: center;
  gap: 12px;
}

.brand-mark {
  width: 42px;
  height: 42px;
  border-radius: 8px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #0f766e, #2563eb);
  color: #fff;
  font-weight: 700;
}

.brand-title {
  font-size: 16px;
  font-weight: 700;
  color: #0f172a;
}

.brand-subtitle {
  font-size: 12px;
  color: #64748b;
}

.shell-menu {
  justify-self: center;
  border-bottom: none;
  background: transparent;
}

.shell-user {
  display: flex;
  align-items: center;
  gap: 12px;
}

.user-text {
  text-align: right;
}

.user-name {
  font-size: 14px;
  font-weight: 600;
  color: #0f172a;
}

.user-role {
  font-size: 12px;
  color: #64748b;
}

.shell-main {
  flex: 1;
  min-height: 0;
  padding: 20px 24px 24px;
}

@media (max-width: 960px) {
  .shell-header {
    grid-template-columns: 1fr;
    height: auto;
    padding: 16px;
  }

  .shell-menu {
    justify-self: stretch;
  }

  .shell-user {
    justify-content: space-between;
  }

  .shell-main {
    padding: 12px;
  }
}
</style>
