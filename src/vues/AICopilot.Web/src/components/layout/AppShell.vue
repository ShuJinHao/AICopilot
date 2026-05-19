<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import {
  Bot,
  BookOpen,
  Cloud,
  LogOut,
  Moon,
  Settings2,
  ShieldCheck,
  Sparkles,
  Sun,
  UserRound
} from 'lucide-vue-next'
import AiTooltip from '@/components/ai/AiTooltip.vue'
import { useAuthStore } from '@/stores/authStore'
import { useChatStore } from '@/stores/chatStore'
import { useTheme } from '@/composables/useTheme'

type NavigationItem = {
  path: string
  label: string
  description: string
  icon: typeof Bot
}

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const chatStore = useChatStore()
const { isDark, toggleTheme } = useTheme()
const { t } = useI18n()

const cloudPlatformUrl = (import.meta.env.VITE_CLOUD_PLATFORM_URL as string | undefined) || ''
const activePath = computed(() => route.path)
const userInitial = computed(() => (authStore.userName || 'A').slice(0, 1).toUpperCase())

const navigationItems = computed<NavigationItem[]>(() => {
  const items: Array<NavigationItem | null> = [
    authStore.canUseChat
      ? { path: '/chat', label: t('nav.chat'), description: '会话、审批、分析结果', icon: Bot }
      : null,
    authStore.canViewConfig
      ? { path: '/config', label: t('nav.config'), description: '模型、MCP、数据源', icon: Settings2 }
      : null,
    authStore.canManageKnowledge
      ? { path: '/knowledge', label: t('nav.knowledge'), description: '文档、向量、检索', icon: BookOpen }
      : null,
    authStore.canManageAccess
      ? { path: '/access', label: t('nav.access'), description: '用户、角色、审计', icon: ShieldCheck }
      : null
  ]

  return items.filter((item): item is NavigationItem => item !== null)
})

async function navigate(path: string) {
  if (route.path !== path) {
    await router.push(path)
  }
}

function openCloudPlatform() {
  if (cloudPlatformUrl) {
    window.location.assign(cloudPlatformUrl)
  }
}

async function logout() {
  authStore.clearAuth()
  chatStore.reset()
  await router.replace('/login')
}
</script>

<template>
  <div class="ai-app-shell">
    <aside class="ai-icon-dock" aria-label="AICopilot primary navigation">
      <div class="dock-brand" aria-label="AICopilot">
        <Sparkles :size="22" stroke-width="2.5" />
      </div>

      <nav class="dock-nav">
        <AiTooltip v-for="item in navigationItems" :key="item.path" :content="item.label">
          <button
            class="dock-button"
            :class="{ active: activePath === item.path }"
            type="button"
            :aria-label="item.label"
            @click="navigate(item.path)"
          >
            <component :is="item.icon" :size="21" stroke-width="2.4" />
          </button>
        </AiTooltip>
      </nav>

      <div class="dock-spacer" />

      <div class="dock-nav">
        <AiTooltip :content="t('nav.cloud')">
          <button
            class="dock-button"
            type="button"
            :disabled="!cloudPlatformUrl"
            :aria-label="t('nav.cloud')"
            @click="openCloudPlatform"
          >
            <Cloud :size="20" stroke-width="2.3" />
          </button>
        </AiTooltip>

        <AiTooltip :content="t('nav.theme')">
          <button class="dock-button" type="button" :aria-label="t('nav.theme')" @click="toggleTheme()">
            <Moon v-if="!isDark" :size="20" stroke-width="2.3" />
            <Sun v-else :size="20" stroke-width="2.3" />
          </button>
        </AiTooltip>

        <AiTooltip :content="t('nav.logout')">
          <button class="dock-button" type="button" :aria-label="t('nav.logout')" @click="logout">
            <LogOut :size="20" stroke-width="2.3" />
          </button>
        </AiTooltip>
      </div>

      <div class="dock-user" :aria-label="authStore.userName || '未登录'">
        <UserRound v-if="!authStore.userName" :size="18" />
        <span v-else>{{ userInitial }}</span>
      </div>
    </aside>

    <section class="ai-shell-surface">
      <header class="ai-topbar">
        <div class="topbar-copy">
          <span class="topbar-kicker">AI COMMAND WORKBENCH</span>
          <strong>{{ t('brand.subtitle') }}</strong>
        </div>
        <div class="topbar-status">
          <span class="status-pill">
            <i />
            Cloud 只读
          </span>
          <span class="status-pill dark">
            {{ authStore.loginSource }}
          </span>
          <div class="user-chip">
            <span>{{ userInitial }}</span>
            <div>
              <strong>{{ authStore.userName || '未登录' }}</strong>
              <small>{{ authStore.roleName || '未分配角色' }}</small>
            </div>
          </div>
        </div>
      </header>

      <main class="ai-content-main">
        <slot />
      </main>
    </section>
  </div>
</template>

<style scoped>
.ai-app-shell {
  display: grid;
  grid-template-columns: 96px minmax(0, 1fr);
  min-height: 100%;
  padding: 24px;
  background:
    radial-gradient(circle at 18% 10%, rgba(200, 255, 61, 0.13), transparent 26%),
    linear-gradient(135deg, var(--ai-bg-warm), var(--ai-bg));
  color: var(--ai-text);
}

.ai-icon-dock {
  display: flex;
  align-items: center;
  gap: 18px;
  flex-direction: column;
  min-height: 0;
  padding: 20px 0;
}

.dock-brand,
.dock-user,
.dock-button {
  display: grid;
  width: 52px;
  height: 52px;
  place-items: center;
  border: 1px solid rgba(63, 111, 115, 0.12);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.82);
  color: var(--ai-text);
  box-shadow: var(--ai-shadow-xs);
}

.dock-brand {
  background: var(--ai-graphite);
  color: var(--ai-lime);
  box-shadow: 0 16px 28px rgba(63, 111, 115, 0.2);
}

.dock-nav {
  display: grid;
  gap: 10px;
  padding: 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.62);
  box-shadow: 0 16px 34px rgba(70, 64, 55, 0.1);
  backdrop-filter: blur(16px);
}

.dock-button {
  width: 48px;
  height: 48px;
  border: 0;
  cursor: pointer;
  transition: transform 0.18s ease, background-color 0.18s ease, color 0.18s ease, box-shadow 0.18s ease;
}

.dock-button:hover:not(:disabled) {
  transform: translateY(-1px);
  background: #f7f7f4;
}

.dock-button.active {
  background: var(--ai-graphite);
  color: var(--ai-lime);
  box-shadow: 0 12px 24px rgba(63, 111, 115, 0.2);
}

.dock-button:disabled {
  cursor: not-allowed;
  opacity: 0.45;
}

.dock-spacer {
  flex: 1;
}

.dock-user {
  color: var(--ai-graphite);
  font-weight: 900;
}

.ai-shell-surface {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  min-width: 0;
  min-height: 0;
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.7);
  border-radius: var(--ai-radius-xl);
  background: color-mix(in srgb, var(--ai-shell) 92%, transparent);
  box-shadow: var(--ai-shadow-shell);
}

.ai-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  min-height: 84px;
  padding: 18px 28px 12px;
}

.topbar-copy {
  display: grid;
  gap: 4px;
}

.topbar-kicker {
  color: var(--ai-text-soft);
  font-size: 12px;
  font-weight: 850;
}

.topbar-copy strong {
  color: var(--ai-text);
  font-size: 25px;
  font-weight: 900;
}

.topbar-status {
  display: flex;
  align-items: center;
  gap: 10px;
}

.status-pill {
  display: inline-flex;
  min-height: 40px;
  align-items: center;
  gap: 8px;
  border-radius: 999px;
  padding: 0 14px;
  background: var(--ai-surface);
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 800;
  box-shadow: var(--ai-shadow-xs);
}

.status-pill i {
  width: 8px;
  height: 8px;
  border-radius: 999px;
  background: var(--ai-teal);
  box-shadow: 0 0 0 5px rgba(19, 164, 173, 0.1);
}

.status-pill.dark {
  background: var(--ai-graphite);
  color: white;
}

.user-chip {
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 48px;
  border-radius: 999px;
  padding: 4px 14px 4px 5px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.user-chip > span {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  border-radius: 999px;
  background: var(--ai-surface-soft);
  font-weight: 900;
}

.user-chip div {
  display: grid;
  gap: 1px;
}

.user-chip strong {
  font-size: 13px;
}

.user-chip small {
  color: var(--ai-text-muted);
  font-size: 11px;
}

.ai-content-main {
  min-height: 0;
  padding: 0 28px 28px;
}

html.dark .ai-app-shell {
  background:
    radial-gradient(circle at 18% 10%, rgba(200, 255, 61, 0.08), transparent 26%),
    linear-gradient(135deg, var(--ai-bg-warm), var(--ai-bg));
}

html.dark .dock-nav,
html.dark .dock-button,
html.dark .dock-user,
html.dark .status-pill,
html.dark .user-chip {
  background: rgba(29, 29, 34, 0.82);
  color: var(--ai-text);
}

html.dark .dock-button.active {
  background: var(--ai-lime);
  color: var(--ai-graphite);
}

@media (max-width: 1024px) {
  .ai-app-shell {
    grid-template-columns: 76px minmax(0, 1fr);
    padding: 16px;
  }

  .dock-brand,
  .dock-user {
    width: 46px;
    height: 46px;
  }

  .dock-button {
    width: 44px;
    height: 44px;
  }

  .ai-topbar {
    align-items: flex-start;
    flex-direction: column;
    padding: 18px 20px 10px;
  }

  .ai-content-main {
    padding: 0 18px 18px;
  }
}
</style>
