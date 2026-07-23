import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type { KnowledgeBaseSummary } from '@/types/app'
import { toFriendlyMessage } from './chatErrorStore'

type ErrorReporter = (message: string) => void

function reportLoadError(reportError: ErrorReporter | undefined, action: string, error: unknown) {
  reportError?.(`${action}失败：${toFriendlyMessage(error)}`)
}

export const useAgentCatalogStore = defineStore('agentCatalog', () => {
  const availableKnowledgeBases = ref<KnowledgeBaseSummary[]>([])
  const selectedKnowledgeBaseId = ref<string | null>(null)

  const selectedKnowledgeBase = computed(
    () =>
      availableKnowledgeBases.value.find(
        (knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value,
      ) ?? null,
  )
  const selectedKnowledgeBaseIdsForPlan = computed(() =>
    selectedKnowledgeBaseId.value ? [selectedKnowledgeBaseId.value] : [],
  )

  async function loadKnowledgeBases(reportError?: ErrorReporter) {
    try {
      availableKnowledgeBases.value = await chatService.getKnowledgeBases()
      if (
        !selectedKnowledgeBaseId.value ||
        !availableKnowledgeBases.value.some(
          (knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value,
        )
      ) {
        selectedKnowledgeBaseId.value = null
      }
      return true
    } catch (error) {
      console.error('Failed to load available knowledge bases.', error)
      reportLoadError(reportError, '加载知识库列表', error)
      availableKnowledgeBases.value = []
      selectedKnowledgeBaseId.value = null
      return false
    }
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    if (!knowledgeBaseId) {
      selectedKnowledgeBaseId.value = null
      return
    }

    selectedKnowledgeBaseId.value = availableKnowledgeBases.value.some(
      (knowledgeBase) => knowledgeBase.id === knowledgeBaseId,
    )
      ? knowledgeBaseId
      : null
  }

  function resetSelections() {
    selectedKnowledgeBaseId.value = null
  }

  function reset() {
    availableKnowledgeBases.value = []
    selectedKnowledgeBaseId.value = null
  }

  return {
    availableKnowledgeBases,
    selectedKnowledgeBaseId,
    selectedKnowledgeBase,
    selectedKnowledgeBaseIdsForPlan,
    loadKnowledgeBases,
    selectKnowledgeBase,
    resetSelections,
    reset,
  }
})
