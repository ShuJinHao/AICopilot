<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { onClickOutside, useEventListener } from '@vueuse/core'
import {
  FileUp,
  FolderOpen,
  ListChecks,
  MessageCircle,
  Plus,
  Send,
  Sparkles,
  Wrench
} from 'lucide-vue-next'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import type { AgentPlannerToolSummary } from '@/types/app'

type ComposerMode = 'plan' | 'chat'

const store = useChatStore()
const { canCreatePlan } = useAgentWorkbench()

const inputValue = ref('')
const agentGoal = ref('')
const composerMode = ref<ComposerMode>('plan')
const composerOptionsOpen = ref(false)
const composerAddButton = ref<HTMLElement | null>(null)
const composerOptionsPanel = ref<HTMLElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval)
const planTypeValue = computed({
  get: () => store.selectedSkillCode || 'auto',
  set: (value: string) => {
    store.selectSkill(value === 'auto' ? null : value)
  }
})
const selectedPlanTypeLabel = computed(() =>
  store.selectedSkill?.displayName || '自动识别'
)
const selectedPluginLine = computed(() => {
  if (store.selectedPluginTools.length > 0) {
    return `${store.selectedPluginTools.length} 个插件能力`
  }

  if (store.availablePluginTools.length > 0) {
    return '可选插件能力'
  }

  return '无可选插件'
})
const composerPrimaryLabel = computed(() => composerMode.value === 'plan' ? '生成计划' : '发送')
const composerPrimaryIcon = computed(() => composerMode.value === 'plan' ? ListChecks : Send)
const composerPlaceholder = computed(() => {
  if (store.isWaitingForApproval) {
    return '请先处理待审批请求'
  }

  return composerMode.value === 'plan'
    ? '输入目标，先生成可确认的计划'
    : '输入一个简单问题，直接回答'
})
const isComposerSubmitDisabled = computed(() =>
  !inputValue.value.trim() ||
  (composerMode.value === 'plan'
    ? !canCreatePlan.value || store.isAgentBusy
    : isInputDisabled.value)
)
const visiblePluginTools = computed(() => store.availablePluginTools.slice(0, 12))

async function sendDirectMessage() {
  const content = inputValue.value.trim()
  if (!content || isInputDisabled.value) return
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
  fileInput.value?.click()
}

function handleSkillChange(event: Event) {
  const target = event.target as HTMLSelectElement
  planTypeValue.value = target.value || 'auto'
}

function handleKnowledgeBaseChange(event: Event) {
  const target = event.target as HTMLSelectElement
  store.selectKnowledgeBase(target.value || null)
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  await store.uploadSessionFile(file)
  target.value = ''
}

async function createAgentPlan() {
  const goal = inputValue.value.trim() || agentGoal.value.trim()
  if (!goal || !canCreatePlan.value) return
  agentGoal.value = goal
  inputValue.value = ''
  await store.planAgentTask(goal)
}

async function submitComposer() {
  if (composerMode.value === 'chat') {
    await sendDirectMessage()
    return
  }

  await createAgentPlan()
}

function setComposerMode(mode: ComposerMode) {
  composerMode.value = mode
  composerOptionsOpen.value = false
  store.clearCurrentSessionError()
}

function togglePluginTool(toolCode: string) {
  store.togglePluginTool(toolCode)
}

function pluginToolLabel(tool: AgentPlannerToolSummary) {
  return tool.displayName || tool.toolCode
}

function pluginToolMeta(tool: AgentPlannerToolSummary) {
  const parts = [
    tool.category || tool.providerKind || '能力',
    tool.requiresApproval ? '需确认' : '只读'
  ]

  return parts.filter(Boolean).join(' · ')
}

onClickOutside(
  composerOptionsPanel,
  () => {
    composerOptionsOpen.value = false
  },
  { ignore: [composerAddButton] }
)

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.key === 'Escape' && composerOptionsOpen.value) {
    composerOptionsOpen.value = false
  }
})

watch(() => store.currentSessionId, () => {
  inputValue.value = ''
  agentGoal.value = ''
  composerMode.value = 'plan'
  composerOptionsOpen.value = false
})
</script>

<template>
  <footer class="command-composer">
    <div class="composer-mode-bar">
      <div class="mode-switch" role="group" aria-label="输入模式">
        <button
          type="button"
          :class="{ active: composerMode === 'plan' }"
          @click="setComposerMode('plan')"
        >
          <ListChecks :size="16" />
          计划模式
        </button>
        <button
          type="button"
          :class="{ active: composerMode === 'chat' }"
          @click="setComposerMode('chat')"
        >
          <MessageCircle :size="16" />
          聊天模式
        </button>
      </div>
      <button
        ref="composerAddButton"
        class="composer-add-button"
        type="button"
        :aria-expanded="composerOptionsOpen"
        @click="composerOptionsOpen = !composerOptionsOpen"
      >
        <Plus :size="17" />
        添加
      </button>
      <span class="composer-context-line">
        {{ selectedPlanTypeLabel }} · {{ selectedPluginLine }}
      </span>
    </div>

    <div v-if="composerOptionsOpen" ref="composerOptionsPanel" class="composer-options-panel">
      <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange">
      <section class="composer-option-group">
        <div class="option-title">
          <Sparkles :size="17" />
          <span>计划类型</span>
        </div>
        <label class="select-field">
          <select
            :value="planTypeValue"
            :disabled="store.isAgentBusy"
            aria-label="选择计划类型"
            @change="handleSkillChange"
          >
            <option value="auto">自动识别</option>
            <option
              v-for="skill in store.availableSkills"
              :key="skill.skillCode"
              :value="skill.skillCode"
            >
              {{ skill.displayName }}
            </option>
          </select>
        </label>
        <p>{{ store.selectedSkill?.description || '系统会根据目标自动选择最合适的只读分析路径。' }}</p>
      </section>

      <section class="composer-option-group">
        <div class="option-title">
          <FileUp :size="17" />
          <span>输入材料</span>
        </div>
        <button class="tool-button" type="button" :disabled="store.isAgentBusy" @click="openFilePicker">
          上传文件
        </button>
        <span class="uploaded-hint">
          {{ store.uploadedFiles.length ? `${store.uploadedFiles.length} 个输入文件` : '未上传文件' }}
        </span>
      </section>

      <section
        v-if="store.selectedSkillSupportsKnowledge && store.availableKnowledgeBases.length"
        class="composer-option-group"
      >
        <div class="option-title">
          <FolderOpen :size="17" />
          <span>知识库</span>
        </div>
        <label class="select-field">
          <select
            :value="store.selectedKnowledgeBaseId || ''"
            :disabled="store.isAgentBusy"
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
        <p>{{ store.selectedKnowledgeBase?.description || '管理员建库后，普通用户可选择资料参与分析。' }}</p>
      </section>

      <section class="composer-option-group plugin-option-group">
        <div class="option-title">
          <Wrench :size="17" />
          <span>插件能力</span>
          <small v-if="store.isLoadingPluginTools">加载中</small>
        </div>
        <div v-if="visiblePluginTools.length" class="plugin-tool-grid">
          <button
            v-for="tool in visiblePluginTools"
            :key="tool.toolCode"
            type="button"
            class="plugin-tool-chip"
            :class="{ active: store.selectedToolCodes.includes(tool.toolCode) }"
            :title="tool.description"
            @click="togglePluginTool(tool.toolCode)"
          >
            <strong>{{ pluginToolLabel(tool) }}</strong>
            <span>{{ pluginToolMeta(tool) }}</span>
          </button>
        </div>
        <div v-else class="panel-empty compact">当前计划类型暂无可选插件能力</div>
        <button
          v-if="store.selectedToolCodes.length"
          class="quiet-link"
          type="button"
          @click="store.clearPluginTools()"
        >
          清空插件选择
        </button>
      </section>
    </div>

    <div class="composer-input-row">
      <textarea
        v-model="inputValue"
        :disabled="isInputDisabled"
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
