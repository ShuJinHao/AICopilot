<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { PanelLeftOpen, RefreshCw, TriangleAlert, X } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentPlanPreview } from '@/composables/useAgentPlanPreview'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import { useUiLayoutStore } from '@/stores/uiLayoutStore'
import AgentRunThread from './AgentRunThread.vue'
import ChatComposer from './ChatComposer.vue'
import ChatEmptyState from './ChatEmptyState.vue'
import MessageItem from './MessageItem.vue'
import SessionList from './SessionList.vue'

const store = useChatStore()
const uiLayoutStore = useUiLayoutStore()
const { latestTask, taskSteps, pendingAgentApprovals, taskArtifacts } = useAgentWorkbench()
const { latestPlanIsCloudReadonly } = useAgentPlanPreview()

const scrollContainer = ref<HTMLElement | null>(null)
const isMobile = ref(typeof window !== 'undefined' ? window.innerWidth < 1024 : false)
const sessionDrawerVisible = ref(false)
const preserveScrollAnchor = ref(false)

const currentTitle = computed(() => store.currentSession?.title || '新会话')
const workbenchStatusLabel = computed(() => {
  if (store.isSessionActivating) return '初始化中'
  if (!store.resolvedSessionId && store.errorMessage) return '不可用'
  return store.isStreaming ? '生成中' : '就绪'
})
const workbenchStatusTone = computed(() =>
  workbenchStatusLabel.value === '就绪' ? 'success' : 'warning',
)
const hasInlineAgentRun = computed(() =>
  Boolean(
    latestTask.value ||
    taskSteps.value.length ||
    pendingAgentApprovals.value.length ||
    taskArtifacts.value.length ||
    store.currentWorkspace,
  ),
)

async function createPlanFromSuggestion(text: string) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) {
    return
  }

  store.clearCurrentSessionError()
  await store.planAgentTask(text)
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
  [
    () => store.currentMessages,
    () => latestTask.value?.id,
    () => taskSteps.value.map((step) => step.status).join(','),
    () => pendingAgentApprovals.value.length,
    () => taskArtifacts.value.length,
  ],
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
  <div class="command-workbench">
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
          <AiTag :tone="workbenchStatusTone">
            {{ workbenchStatusLabel }}
          </AiTag>
          <AiTag :tone="latestPlanIsCloudReadonly ? 'warning' : 'success'">
            {{ latestPlanIsCloudReadonly ? 'Cloud 只读' : '只读分析' }}
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
        <div v-if="store.errorMessage" class="canvas-error" role="alert">
          <TriangleAlert :size="18" />
          {{ store.errorMessage }}
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

        <AgentRunThread v-if="hasInlineAgentRun" />
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

<style src="./chat-workbench.css"></style>
