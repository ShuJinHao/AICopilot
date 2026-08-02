<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PanelLeftOpen, RefreshCw, TriangleAlert, X } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import {
  getChatModePresentation,
  requiresNewConversation,
  resolveConversationStatus,
} from '@/protocol/chatPresentation'
import { useChatStore } from '@/stores/chatStore'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'
import AgentSessionRecoveryNotice from './AgentSessionRecoveryNotice.vue'
import ChatComposer from './ChatComposer.vue'
import ChatEmptyState from './ChatEmptyState.vue'
import ChatErrorBanner from './ChatErrorBanner.vue'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const uiLayoutStore = useUiLayoutStore()

const scrollContainer = ref<HTMLElement | null>(null)
const isNarrowViewport = ref(typeof window !== 'undefined' ? window.innerWidth <= 1100 : false)
const sessionDrawerVisible = ref(false)
const preserveScrollAnchor = ref(false)

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const modePresentation = computed(() => getChatModePresentation(store.agentMode))
const sessionStatus = computed(() =>
  resolveConversationStatus({
    isSessionActivating: store.isSessionActivating,
    agentSessionStatus: store.agentSessionStatus,
    agentSessionResetRequired: store.currentSession?.agentSessionResetRequired,
    isStreaming: store.isStreaming,
    hasPendingApproval: Boolean(
      store.hasPendingApproval || store.currentSession?.hasPendingApproval,
    ),
    hasMessages: store.currentMessages.length > 0,
    hasError: Boolean(store.errorPresentation),
  }),
)
const unavailableStatus = computed<'Interrupted' | 'ResetRequired' | null>(() => {
  if (
    !requiresNewConversation(
      store.agentSessionStatus,
      store.currentSession?.agentSessionResetRequired,
    )
  ) {
    return null
  }

  return store.agentSessionStatus === 'Interrupted' ? 'Interrupted' : 'ResetRequired'
})

async function useSuggestion(text: string) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) {
    return
  }

  store.clearCurrentSessionError()
  await store.sendMessage(text)
}

async function createNewSession() {
  if (store.isSessionTransitionBlocked) return
  await store.createNewSession()
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
  isNarrowViewport.value = window.innerWidth <= 1100
  if (!isNarrowViewport.value) {
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
      v-if="!isNarrowViewport"
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
            v-if="isNarrowViewport"
            class="icon-button"
            type="button"
            aria-label="打开会话"
            @click="sessionDrawerVisible = true"
          >
            <PanelLeftOpen :size="20" />
          </button>
          <div>
            <p class="canvas-kicker">AI 对话</p>
            <h1>{{ currentTitle }}</h1>
          </div>
        </div>
        <div class="canvas-toolbar">
          <AiTag :tone="sessionStatus.tone">
            {{ sessionStatus.label }}
          </AiTag>
          <AiTag :tone="store.agentMode === 'execute' ? 'blue' : 'teal'">
            {{ modePresentation.label }}
          </AiTag>
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
        <ChatErrorBanner v-if="store.errorPresentation" :error="store.errorPresentation" />
        <AgentSessionRecoveryNotice
          v-if="unavailableStatus"
          :status="unavailableStatus"
          :message="store.agentSessionNotice"
          :busy="store.isSessionTransitionBlocked"
          @create-session="createNewSession"
        />
        <div v-else-if="store.agentSessionNotice" class="canvas-notice" role="status">
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
          v-if="
            store.currentMessages.length === 0 &&
            !store.isLoadingHistory &&
            !store.isAgentSessionUnavailable
          "
          :mode="modePresentation.mode"
          @use-suggestion="useSuggestion"
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
