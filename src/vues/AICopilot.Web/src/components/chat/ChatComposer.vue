<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ListChecks, MessageCircle, Send } from 'lucide-vue-next'
import { useChatStore } from '@/stores/chatStore'
import { shouldResetComposerForSessionChange } from '@/utils/composerSession'

type AgentMode = 'plan' | 'execute'

const store = useChatStore()
const inputValue = ref('')
const isAgentModeChanging = ref(false)
let lastCommittedComposerSessionId = store.composerSessionId

const isComposerEditingDisabled = computed(
  () => !store.canEditComposerContext || store.isStreaming || store.hasPendingApproval,
)
const isSubmissionBlocked = computed(
  () => isComposerEditingDisabled.value || store.isApprovalAuthorityUnknown,
)
const isSessionReady = computed(() =>
  Boolean(store.resolvedSessionId && !store.isSessionTransitionBlocked),
)
const composerPlaceholder = computed(() => {
  if (store.isAgentSessionUnavailable) {
    return '当前会话不可恢复，请新建会话'
  }
  if (store.hasPendingApproval) {
    return '请先处理待审批请求'
  }
  if (store.isApprovalAuthorityUnknown) {
    return '正在确认待审批状态，可继续编辑'
  }

  return store.agentMode === 'plan'
    ? 'Plan 模式：描述目标，助手仅规划与整理待办'
    : 'Execute 模式：输入问题，助手可按权限使用只读业务与知识工具'
})
const isSubmitDisabled = computed(
  () => !isSessionReady.value || !inputValue.value.trim() || isSubmissionBlocked.value,
)

async function submitMessage() {
  const content = inputValue.value.trim()
  if (!content || !isSessionReady.value || isSubmissionBlocked.value) return

  inputValue.value = ''
  await store.sendMessage(content)
}

function handleComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submitMessage()
  }
}

async function setAgentMode(mode: AgentMode) {
  if (!store.canChangeAgentMode || isAgentModeChanging.value || store.agentMode === mode) return

  isAgentModeChanging.value = true
  store.clearCurrentSessionError()
  try {
    await store.changeAgentMode(mode)
  } finally {
    isAgentModeChanging.value = false
  }
}

watch(
  () => store.composerSessionId,
  (nextSessionId) => {
    if (!nextSessionId) return

    const shouldReset = shouldResetComposerForSessionChange(
      lastCommittedComposerSessionId,
      nextSessionId,
    )
    lastCommittedComposerSessionId = nextSessionId
    if (shouldReset) {
      inputValue.value = ''
    }
  },
)
</script>

<template>
  <footer class="command-composer">
    <div class="composer-mode-bar">
      <div class="mode-switch" role="group" aria-label="Harness Agent 运行模式">
        <button
          type="button"
          :class="{ active: store.agentMode === 'plan' }"
          :disabled="!store.canChangeAgentMode || isAgentModeChanging"
          @click="setAgentMode('plan')"
        >
          <ListChecks :size="16" />
          Plan · 规划
        </button>
        <button
          type="button"
          :class="{ active: store.agentMode === 'execute' }"
          :disabled="!store.canChangeAgentMode || isAgentModeChanging"
          @click="setAgentMode('execute')"
        >
          <MessageCircle :size="16" />
          Execute · 执行
        </button>
      </div>
      <span class="composer-context-line">
        服务端权威模式：{{ store.agentMode === 'execute' ? 'Execute' : 'Plan' }} · Harness 主链
      </span>
    </div>

    <div class="composer-input-row">
      <textarea
        v-model="inputValue"
        :disabled="isComposerEditingDisabled"
        :placeholder="composerPlaceholder"
        rows="1"
        @keydown="handleComposerKeydown"
      />
      <button
        class="send-button"
        type="button"
        :disabled="isSubmitDisabled"
        aria-label="发送"
        @click="submitMessage"
      >
        <Send :size="19" />
        <span>发送</span>
      </button>
    </div>
  </footer>
</template>
