import { ref, type Ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { RagDomainStates } from '@/stores/rag/ragStoreTypes'
import type { SearchKnowledgeBaseResult } from '@/types/app'

interface KnowledgeSearchDomainOptions {
  selectedKnowledgeBaseId: Ref<string>
  searchResults: Ref<SearchKnowledgeBaseResult[]>
}

export function useKnowledgeSearchDomain(
  states: RagDomainStates,
  options: KnowledgeSearchDomainOptions
) {
  const searchQuery = ref('')
  const searchTopK = ref(5)
  const searchMinScore = ref(0.5)

  function clearSearch() {
    options.searchResults.value = []
    states.actionErrors.search = ''
  }

  async function searchKnowledgeBase() {
    if (!options.selectedKnowledgeBaseId.value || !searchQuery.value.trim()) {
      options.searchResults.value = []
      return
    }

    states.loadingStates.search = true
    states.actionErrors.search = ''

    try {
      options.searchResults.value = await ragService.searchKnowledgeBase(
        options.selectedKnowledgeBaseId.value,
        searchQuery.value.trim(),
        searchTopK.value,
        searchMinScore.value
      )
    } catch (error) {
      states.actionErrors.search = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.search.failed,
        RAG_STORE_MESSAGES.search.forbidden
      )
      throw error
    } finally {
      states.loadingStates.search = false
    }
  }

  return {
    searchQuery,
    searchTopK,
    searchMinScore,
    clearSearch,
    searchKnowledgeBase
  }
}
