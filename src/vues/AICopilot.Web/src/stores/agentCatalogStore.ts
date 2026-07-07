import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type { AgentPlannerToolSummary, KnowledgeBaseSummary, SkillDefinition } from '@/types/app'
import { toFriendlyMessage } from './chatErrorStore'

type ErrorReporter = (message: string) => void

function reportLoadError(reportError: ErrorReporter | undefined, action: string, error: unknown) {
  reportError?.(`${action}失败：${toFriendlyMessage(error)}`)
}

export const useAgentCatalogStore = defineStore('agentCatalog', () => {
  const availableSkills = ref<SkillDefinition[]>([])
  const selectedSkillCode = ref<string | null>(null)
  const availablePluginTools = ref<AgentPlannerToolSummary[]>([])
  const selectedToolCodes = ref<string[]>([])
  const isLoadingPluginTools = ref(false)
  const availableKnowledgeBases = ref<KnowledgeBaseSummary[]>([])
  const selectedKnowledgeBaseId = ref<string | null>(null)

  const selectedSkill = computed(() =>
    selectedSkillCode.value
      ? availableSkills.value.find((skill) => skill.skillCode === selectedSkillCode.value) ?? null
      : null
  )
  const selectedPluginTools = computed(() =>
    availablePluginTools.value.filter((tool) => selectedToolCodes.value.includes(tool.toolCode))
  )
  const isSkillAutoMode = computed(() => !selectedSkillCode.value)
  const selectedSkillSupportsKnowledge = computed(() =>
    isSkillAutoMode.value ||
    selectedSkill.value?.allowedKnowledgeScopes?.some((scope) => scope === 'SelectedKnowledgeBase') ||
    false
  )
  const selectedKnowledgeBase = computed(() =>
    availableKnowledgeBases.value.find((knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value) ?? null
  )
  const selectedKnowledgeBaseIdsForPlan = computed(() =>
    selectedSkillSupportsKnowledge.value && selectedKnowledgeBaseId.value ? [selectedKnowledgeBaseId.value] : []
  )

  async function loadSkills(reportError?: ErrorReporter) {
    try {
      availableSkills.value = await chatService.getSkills()
      if (selectedSkillCode.value && !availableSkills.value.some((skill) => skill.skillCode === selectedSkillCode.value)) {
        selectedSkillCode.value = null
      }
    } catch (error) {
      console.error('Failed to load agent skills.', error)
      reportLoadError(reportError, '加载 Skill 列表', error)
      availableSkills.value = []
      selectedSkillCode.value = null
    }
  }

  function selectSkill(skillCode: string | null, reportError?: ErrorReporter) {
    if (!skillCode) {
      selectedSkillCode.value = null
      void loadPluginTools(reportError)
      return
    }

    selectedSkillCode.value = availableSkills.value.some((skill) => skill.skillCode === skillCode)
      ? skillCode
      : null
    void loadPluginTools(reportError)
  }

  async function loadPluginTools(reportError?: ErrorReporter) {
    isLoadingPluginTools.value = true
    try {
      const catalog = await chatService.getToolCatalog(selectedSkillCode.value)
      const tools = Array.isArray(catalog.tools) ? catalog.tools : []
      availablePluginTools.value = tools
      const availableCodes = new Set(tools.map((tool) => tool.toolCode))
      selectedToolCodes.value = selectedToolCodes.value.filter((code) => availableCodes.has(code))
    } catch (error) {
      console.error('Failed to load agent plugin tools.', error)
      reportLoadError(reportError, '加载插件能力', error)
      availablePluginTools.value = []
      selectedToolCodes.value = []
    } finally {
      isLoadingPluginTools.value = false
    }
  }

  function togglePluginTool(toolCode: string) {
    if (!availablePluginTools.value.some((tool) => tool.toolCode === toolCode)) {
      return
    }

    selectedToolCodes.value = selectedToolCodes.value.includes(toolCode)
      ? selectedToolCodes.value.filter((code) => code !== toolCode)
      : [...selectedToolCodes.value, toolCode]
  }

  function clearPluginTools() {
    selectedToolCodes.value = []
  }

  async function loadKnowledgeBases(reportError?: ErrorReporter) {
    try {
      availableKnowledgeBases.value = await chatService.getKnowledgeBases()
      if (
        !selectedKnowledgeBaseId.value ||
        !availableKnowledgeBases.value.some((knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value)
      ) {
        selectedKnowledgeBaseId.value = null
      }
    } catch (error) {
      console.error('Failed to load available knowledge bases.', error)
      reportLoadError(reportError, '加载知识库列表', error)
      availableKnowledgeBases.value = []
      selectedKnowledgeBaseId.value = null
    }
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    if (!knowledgeBaseId) {
      selectedKnowledgeBaseId.value = null
      return
    }

    selectedKnowledgeBaseId.value = availableKnowledgeBases.value.some(
      (knowledgeBase) => knowledgeBase.id === knowledgeBaseId
    )
      ? knowledgeBaseId
      : null
  }

  function resetSelections() {
    selectedSkillCode.value = null
    selectedToolCodes.value = []
    selectedKnowledgeBaseId.value = null
  }

  function reset() {
    availableSkills.value = []
    selectedSkillCode.value = null
    availablePluginTools.value = []
    selectedToolCodes.value = []
    isLoadingPluginTools.value = false
    availableKnowledgeBases.value = []
    selectedKnowledgeBaseId.value = null
  }

  return {
    availableSkills,
    selectedSkillCode,
    availablePluginTools,
    selectedToolCodes,
    isLoadingPluginTools,
    availableKnowledgeBases,
    selectedKnowledgeBaseId,
    selectedSkill,
    selectedPluginTools,
    isSkillAutoMode,
    selectedSkillSupportsKnowledge,
    selectedKnowledgeBase,
    selectedKnowledgeBaseIdsForPlan,
    loadSkills,
    selectSkill,
    loadPluginTools,
    togglePluginTool,
    clearPluginTools,
    loadKnowledgeBases,
    selectKnowledgeBase,
    resetSelections,
    reset
  }
})
