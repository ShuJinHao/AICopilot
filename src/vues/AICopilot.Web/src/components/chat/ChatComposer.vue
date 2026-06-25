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
const composerMode = ref<ComposerMode>('chat')
const planAdvancedOpen = ref(false)
const planAdvancedButton = ref<HTMLElement | null>(null)
const planAdvancedPanel = ref<HTMLElement | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

const isInputDisabled = computed(() => store.isStreaming || store.isWaitingForApproval)
const planTypeValue = computed({
  get: () => store.selectedSkillCode || 'auto',
  set: (value: string) => {
    store.selectSkill(value === 'auto' ? null : value)
  }
})
const attachmentSummary = computed(() =>
  store.uploadedFiles.length ? `${store.uploadedFiles.length} 个附件` : '未添加附件'
)
const planPathSummary = computed(() =>
  store.selectedSkill ? `${store.selectedSkill.displayName} · 手动指定` : '自动选择执行路径'
)
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

const skillDisplayDescriptions: Record<string, string> = {
  cloud_readonly: '查询和分析 Cloud 只读业务数据',
  data_analysis: '查询和分析产线数据',
  knowledge_search: '从知识库检索相关文档',
  free_goal_chat: '普通对话，不调用工具'
}

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
  planAdvancedOpen.value = false
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

function skillDisplayDescription(skillCode: string, fallback?: string) {
  const normalizedCode = skillCode.toLowerCase()
  if (skillDisplayDescriptions[normalizedCode]) {
    return skillDisplayDescriptions[normalizedCode]
  }

  if (normalizedCode.includes('cloud') || normalizedCode.includes('data')) {
    return '查询和分析业务数据'
  }

  if (normalizedCode.includes('knowledge') || normalizedCode.includes('rag')) {
    return '从知识库检索相关资料'
  }

  return fallback || '系统根据目标自动选择执行路径'
}

onClickOutside(
  planAdvancedPanel,
  () => {
    planAdvancedOpen.value = false
  },
  { ignore: [planAdvancedButton] }
)

useEventListener('keydown', (event: KeyboardEvent) => {
  if (event.key === 'Escape' && planAdvancedOpen.value) {
    planAdvancedOpen.value = false
  }
})

watch(() => store.currentSessionId, () => {
  inputValue.value = ''
  agentGoal.value = ''
  composerMode.value = 'chat'
  planAdvancedOpen.value = false
})
</script>

<template>
  <footer class="command-composer">
    <div class="composer-mode-bar">
      <div class="mode-switch" role="group" aria-label="输入模式">
        <button
          type="button"
          :class="{ active: composerMode === 'chat' }"
          @click="setComposerMode('chat')"
        >
          <MessageCircle :size="16" />
          聊天模式
        </button>
        <button
          type="button"
          :class="{ active: composerMode === 'plan' }"
          @click="setComposerMode('plan')"
        >
          <ListChecks :size="16" />
          计划模式
        </button>
      </div>
      <button
        class="composer-add-button"
        type="button"
        :disabled="store.isAgentBusy"
        @click="openFilePicker"
      >
        <Plus :size="17" />
        添加
      </button>
      <span class="composer-context-line">
        <template v-if="composerMode === 'plan'">
          {{ planPathSummary }} · {{ attachmentSummary }}
        </template>
        <template v-else>
          普通聊天 · {{ attachmentSummary }}
        </template>
      </span>
    </div>

    <input ref="fileInput" class="hidden-file" type="file" @change="handleFileChange">

    <div v-if="composerMode === 'plan'" class="composer-plan-strip">
      <div>
        <strong>输入目标，系统会自动生成可确认的计划</strong>
        <span>默认自动选择 Skill、工具和知识库；需要人工覆盖时再展开高级选项。</span>
      </div>
      <button
        ref="planAdvancedButton"
        class="composer-advanced-toggle"
        type="button"
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
      <section class="composer-option-group">
        <div class="option-title">
          <Sparkles :size="17" />
          <span>执行路径</span>
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
              {{ skill.displayName }} · {{ skillDisplayDescription(skill.skillCode, skill.description) }}
            </option>
          </select>
        </label>
        <p>{{ store.selectedSkill ? skillDisplayDescription(store.selectedSkill.skillCode, store.selectedSkill.description) : '保持自动识别，系统会根据目标选择合适路径。' }}</p>
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
        <p>{{ store.selectedKnowledgeBase?.description || '需要限定资料范围时再手动选择。' }}</p>
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
