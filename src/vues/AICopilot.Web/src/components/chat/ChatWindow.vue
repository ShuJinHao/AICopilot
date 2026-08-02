<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PanelLeftOpen, RefreshCw, TriangleAlert, X } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useChatStore } from '@/stores/chatStore'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'
import ChatComposer from './ChatComposer.vue'
import ChatEmptyState from './ChatEmptyState.vue'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const uiLayoutStore = useUiLayoutStore()

const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 1024 : false)
const sessionDrawerVisible = ref(false)
const preserveScrollAnchor = ref(false)

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const sessionStatusLabel = computed(() => {
  if (store.isSessionActivating) return '初始化中'
  if (store.agentSessionStatus === 'Interrupted') return '已中断'
  if (store.isAgentSessionUnavailable) return '需重建'
  if (store.agentSessionStatus === 'Running' && !store.isStreaming) return '中断检查'
  if (!store.resolvedSessionId && store.errorMessage) return '不可用'
  return store.isStreaming ? '生成中' : '就绪'
})
const sessionStatusTone = computed(() => {
  if (store.isAgentSessionUnavailable) return 'danger'
  return sessionStatusLabel.value === '就绪' ? 'success' : 'warning'
})
const agentModeLabel = computed(() =>
  store.agentMode === 'execute' ? 'Execute · 执行' : 'Plan · 规划',
)

async function createPlanFromSuggestion(text: string) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) {
    return
  }

  store.clearCurrentSessionError()
  await store.sendMessage(text)
}

async function loadOlderMessages() {
  if (!store.resolvedSessionId || !scrollContainer.value) {
    return
  }

  const container = scrollContainer.value
  const previousTop = container.scrollTop
  const previousHeight = container.scrollHeight
  preserveScrollAnchor.value = true
  try {
    const changed = await store.loadOlderHistory(store.resolvedSessionId)
    await nextTick()
    if (changed && scrollContainer.value) {
      scrollContainer.value.scrollTop =
        previousTop + (scrollContainer.value.scrollHeight - previousHeight)
    }
  } finally {
    preserveScrollAnchor.value = false
  }
}

async function scrollToBottom() {
  await nextTick()
  if (scrollContainer.value) {
    scrollContainer.value.scrollTop = scrollContainer.value.scrollHeight
  }
}

function handleResize() {
  if (typeof window === 'undefined') return
  isMobile.value = window.innerWidth < 1024
  if (!isMobile.value) {
    sessionDrawerVisible.value = false
  }
}

watch(
  () => store.currentMessages,
  () => {
    if (!preserveScrollAnchor.value) {
      void scrollToBottom()
    }
  },
  { deep: true },
)

watch(
  () => store.currentSessionId,
  () => {
    sessionDrawerVisible.value = false
    void scrollToBottom()
  },
)

onMounted(() => {
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', handleResize)
  }
})
onBeforeUnmount(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('resize', handleResize)
  }
})
</script>

<template>
  <div
    class="chat-shell"
    :class="{ 'session-rail-collapsed': uiLayoutStore.isSessionRailCollapsed }"
  >
    <aside
      v-if="!isMobile"
      class="session-rail"
      :class="{ collapsed: uiLayoutStore.isSessionRailCollapsed }"
    >
      <div class="rail-head">
        <div>
          <span>会话列表</span>
          <strong>历史会话</strong>
        </div>
        <button type="button" aria-label="折叠会话栏" @click="uiLayoutStore.toggleSessionRail()">
          <PanelLeftOpen :size="18" />
        </button>
      </div>
      <SessionList class="sessions" />
    </aside>

    <section class="ai-canvas">
      <header class="canvas-header">
        <div class="title-zone">
          <button
            v-if="isMobile"
            class="icon-button"
            type="button"
            aria-label="打开会话"
            @click="sessionDrawerVisible = true"
          >
            <PanelLeftOpen :size="20" />
          </button>
          <div>
            <p class="canvas-kicker">对话工作区</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="canvas-toolbar">
          <AiTag :tone="sessionStatusTone">
            {{ sessionStatusLabel }}
          </AiTag>
          <AiTag :tone="store.agentMode === 'execute' ? 'blue' : 'teal'">
            {{ agentModeLabel }}
          </AiTag>
          <AiTag tone="success">Harness 主链</AiTag>
          <button
            class="soft-action"
            type="button"
            :disabled="!store.resolvedSessionId || store.isSessionTransitionBlocked"
            @click="
              store.resolvedSessionId &&
              !store.isSessionTransitionBlocked &&
              store.selectSession(store.resolvedSessionId, true)
            "
          >
            <RefreshCw :size="17" />
            刷新
          </button>
        </div>
      </header>

      <div ref="scrollContainer" class="message-viewport">
        <div v-if="store.errorMessage" class="canvas-error" role="alert">
          <TriangleAlert :size="18" />
          {{ store.errorMessage }}
        </div>
        <div v-if="store.agentSessionNotice" class="canvas-error" role="alert">
          <TriangleAlert :size="18" />
          {{ store.agentSessionNotice }}
        </div>

        <div v-if="store.isLoadingHistory" class="loading-lines">
          <i />
          <i />
          <i />
          <i />
        </div>

        <div
          v-if="store.hasMoreHistoryBefore && store.currentMessages.length"
          class="history-loader"
        >
          <button
            type="button"
            :disabled="store.isSessionTransitionBlocked"
            @click="loadOlderMessages"
          >
            <RefreshCw :size="16" />
            {{ store.isLoadingOlderHistory ? '加载中' : '加载更早消息' }}
          </button>
        </div>

        <ChatEmptyState
          v-if="store.currentMessages.length === 0 && !store.isLoadingHistory"
          @use-suggestion="createPlanFromSuggestion"
        />

        <div class="message-list">
          <MessageItem
            v-for="message in store.currentMessages"
            :key="message.messageId ?? message.timestamp"
            :message="message"
          />
        </div>
      </div>

      <ChatComposer />
    </section>

    <div
      v-if="sessionDrawerVisible"
      class="mobile-overlay"
      @click.self="sessionDrawerVisible = false"
    >
      <aside class="mobile-drawer left">
        <button
          class="drawer-close"
          type="button"
          aria-label="关闭会话"
          @click="sessionDrawerVisible = false"
        >
          <X :size="18" />
        </button>
        <SessionList />
      </aside>
    </div>
  </div>
</template>

<style src="./chat-shell.css"></style>
