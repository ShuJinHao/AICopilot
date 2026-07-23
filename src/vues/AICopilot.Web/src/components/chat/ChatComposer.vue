<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { onClickOutside, useEventListener } from '@vueuse/core'
import {
  FolderOpen,
  ListChecks,
  MessageCircle,
  Plus,
  Send,
  Sparkles,
  X,
} from 'lucide-vue-next'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import { shouldResetComposerForSessionChange } from '@/utils/composerSession'

type ComposerMode = 'plan' | 'chat'

const store = useChatStore()
const { canCreatePlan } = useAgentWorkbench()

const inputValue = ref('')
const agentGoal = ref('')
const composerMode = ref<ComposerMode>('chat')
const planAdvancedOpen = ref(false)
const planAdvancedButton = ref<HTMLElement | null>(null)
const planAdvancedPanel = ref<HTMLElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
let lastCommittedComposerSessionId = store.composerSessionId

const isComposerEditingDisabled = computed(
  () => !store.canEditComposerContext || store.isStreaming || store.hasPendingApproval,
)
const isChatSubmissionBlocked = computed(
  () => isComposerEditingDisabled.value || store.isApprovalAuthorityUnknown,
)
const isSessionReady = computed(() =>
  Boolean(store.resolvedSessionId && !store.isSessionTransitionBlocked),
)
const attachmentSummary = computed(() =>
  store.uploadedFiles.length ? `${store.uploadedFiles.length} 个附件` : '未添加附件',
)
const referencedTaskDigestLabel = computed(() => {
  const digest = store.referencedAgentTask?.evidenceSetDigest
  return digest ? `${digest.slice(0, 8)}…${digest.slice(-8)}` : null
})
const planPathSummary = computed(() => '自动生成执行路径')
const composerPrimaryLabel = computed(() => (composerMode.value === 'plan' ? '生成计划' : '发送'))
const composerPrimaryIcon = computed(() => (composerMode.value === 'plan' ? ListChecks : Send))
const composerPlaceholder = computed(() => {
  if (store.hasPendingApproval) {
    return '请先处理待审批请求'
  }
  if (store.isApprovalAuthorityUnknown) {
    return '正在确认待审批状态，可继续编辑'
  }

  return composerMode.value === 'plan'
    ? '输入目标，先生成可确认的计划'
    : '输入一个简单问题，直接回答'
})
const isComposerSubmitDisabled = computed(
  () =>
    !isSessionReady.value ||
    !inputValue.value.trim() ||
    (composerMode.value === 'plan'
      ? !canCreatePlan.value || store.isAgentBusy
      : isChatSubmissionBlocked.value),
)
async function sendDirectMessage() {
  const content = inputValue.value.trim()
  if (!content || !isSessionReady.value || isChatSubmissionBlocked.value) return
  inputValue.value = ''
  await store.sendMessage(content)
}

function handleComposerKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void submitComposer()
  }
}

function openFilePicker() {
  if (!isSessionReady.value) return
  fileInput.value?.click()
}

function handleKnowledgeBaseChange(event: Event) {
  const target = event.target as HTMLSelectElement
  store.selectKnowledgeBase(target.value || null)
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file || !isSessionReady.value) {
    target.value = ''
    return
  }
  await store.uploadSessionFile(file)
  target.value = ''
}

async function createAgentPlan() {
  const goal = inputValue.value.trim() || agentGoal.value.trim()
  if (!goal || !isSessionReady.value || !canCreatePlan.value) return
  agentGoal.value = goal
  inputValue.value = ''
  await store.planAgentTask(goal)
}

async function submitComposer() {
  if (isComposerSubmitDisabled.value) return

  if (composerMode.value === 'chat') {
    await sendDirectMessage()
    return
  }

  await createAgentPlan()
}

function setComposerMode(mode: ComposerMode) {
  if (!store.canEditComposerContext) return

  composerMode.value = mode
  if (mode === 'plan') {
    store.clearReferencedAgentTask()
  }
  planAdvancedOpen.value = false
  store.clearCurrentSessionError()
}

onClickOutside(
  planAdvancedPanel,
  () => {
    planAdvancedOpen.value = false
  },
  { ignore: [planAdvancedButton] },
)

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.key === 'Escape' && planAdvancedOpen.value) {
    planAdvancedOpen.value = false
  }
})

watch(
  () => store.composerSessionId,
  (nextSessionId) => {
    if (!nextSessionId) return

    const shouldReset = shouldResetComposerForSessionChange(
      lastCommittedComposerSessionId,
      nextSessionId,
    )
    lastCommittedComposerSessionId = nextSessionId
    if (!shouldReset) return

    inputValue.value = ''
    agentGoal.value = ''
    composerMode.value = 'chat'
    planAdvancedOpen.value = false
  },
)
</script>

<template>
  <footer class="command-composer">
    <div class="composer-mode-bar">
      <div class="mode-switch" role="group" aria-label="输入模式">
        <button
          type="button"
          :class="{ active: composerMode === 'chat' }"
          :disabled="!store.canEditComposerContext"
          @click="setComposerMode('chat')"
        >
          <MessageCircle :size="16" />
          聊天模式
        </button>
        <button
          type="button"
          :class="{ active: composerMode === 'plan' }"
          :disabled="!store.canEditComposerContext"
          @click="setComposerMode('plan')"
        >
          <ListChecks :size="16" />
          计划模式
        </button>
      </div>
      <button
        class="composer-add-button"
        type="button"
        :disabled="!isSessionReady || store.isAgentBusy"
        @click="openFilePicker"
      >
        <Plus :size="17" />
        添加
      </button>
      <span class="composer-context-line">
        <template v-if="composerMode === 'plan'">
          {{ planPathSummary }} · {{ attachmentSummary }}
        </template>
        <template v-else-if="referencedTaskDigestLabel">
          基于已完成任务证据 {{ referencedTaskDigestLabel }} · {{ attachmentSummary }}
        </template>
        <template v-else>普通聊天 · {{ attachmentSummary }}</template>
      </span>
      <button
        v-if="composerMode === 'chat' && store.referencedAgentTaskId"
        class="composer-add-button"
        type="button"
        :disabled="!store.canEditComposerContext"
        aria-label="取消任务结果引用"
        @click="store.clearReferencedAgentTask()"
      >
        <X :size="16" />
        取消结果引用
      </button>
    </div>

    <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange" />

    <div v-if="composerMode === 'plan'" class="composer-plan-strip">
      <div>
        <strong>输入目标，系统会自动生成可确认的计划</strong>
        <span>系统根据目标生成业务能力和执行节点；需要限定资料范围时再展开高级选项。</span>
      </div>
      <button
        ref="planAdvancedButton"
        class="composer-advanced-toggle"
        type="button"
        :disabled="!store.canEditComposerContext"
        :aria-expanded="planAdvancedOpen"
        @click="planAdvancedOpen = !planAdvancedOpen"
      >
        <Sparkles :size="16" />
        高级选项
      </button>
    </div>

    <div
      v-if="composerMode === 'plan' && planAdvancedOpen"
      ref="planAdvancedPanel"
      class="composer-options-panel"
    >
      <section
        v-if="store.availableKnowledgeBases.length"
        class="composer-option-group"
      >
        <div class="option-title">
          <FolderOpen :size="17" />
          <span>知识库</span>
        </div>
        <label class="select-field">
          <select
            :value="store.selectedKnowledgeBaseId || ''"
            :disabled="!store.canEditComposerContext"
            aria-label="选择知识库"
            @change="handleKnowledgeBaseChange"
          >
            <option value="">不使用知识库</option>
            <option
              v-for="knowledgeBase in store.availableKnowledgeBases"
              :key="knowledgeBase.id"
              :value="knowledgeBase.id"
            >
              {{ knowledgeBase.name }}
            </option>
          </select>
        </label>
        <p>{{ store.selectedKnowledgeBase?.description || '需要限定资料范围时再手动选择。' }}</p>
      </section>

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
        :disabled="isComposerSubmitDisabled"
        :aria-label="composerPrimaryLabel"
        @click="submitComposer"
      >
        <component :is="composerPrimaryIcon" :size="19" />
        <span>{{ composerPrimaryLabel }}</span>
      </button>
    </div>
  </footer>
</template>
