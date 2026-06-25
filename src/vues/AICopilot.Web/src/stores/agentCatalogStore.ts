import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type { AgentPlannerToolSummary, KnowledgeBaseSummary, SkillDefinition } from '@/types/app'

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

  async function loadSkills() {
    try {
      availableSkills.value = await chatService.getSkills()
      if (selectedSkillCode.value && !availableSkills.value.some((skill) => skill.skillCode === selectedSkillCode.value)) {
        selectedSkillCode.value = null
      }
    } catch {
      availableSkills.value = []
      selectedSkillCode.value = null
    }
  }

  function selectSkill(skillCode: string | null) {
    if (!skillCode) {
      selectedSkillCode.value = null
      void loadPluginTools()
      return
    }

    selectedSkillCode.value = availableSkills.value.some((skill) => skill.skillCode === skillCode)
      ? skillCode
      : null
    void loadPluginTools()
  }

  async function loadPluginTools() {
    isLoadingPluginTools.value = true
    try {
      const catalog = await chatService.getToolCatalog(selectedSkillCode.value)
      availablePluginTools.value = catalog.tools
      const availableCodes = new Set(catalog.tools.map((tool) => tool.toolCode))
      selectedToolCodes.value = selectedToolCodes.value.filter((code) => availableCodes.has(code))
    } catch {
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

  async function loadKnowledgeBases() {
    try {
      availableKnowledgeBases.value = await chatService.getKnowledgeBases()
      if (
        !selectedKnowledgeBaseId.value ||
        !availableKnowledgeBases.value.some((knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value)
      ) {
        selectedKnowledgeBaseId.value = null
      }
    } catch {
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
