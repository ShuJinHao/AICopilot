import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { useDocumentGovernanceDomain } from '@/stores/rag/documentGovernanceStore'
import { useDocumentDomain } from '@/stores/rag/documentStore'
import { useEmbeddingModelDomain } from '@/stores/rag/embeddingModelStore'
import { useKnowledgeBaseDomain } from '@/stores/rag/knowledgeBaseStore'
import { useKnowledgeSearchDomain } from '@/stores/rag/knowledgeSearchStore'
import type {
  RagDomainStates,
  RagEditableDomain,
  RagLoadingDomain
} from '@/stores/rag/ragStoreTypes'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type {
  ConfigDialogMode,
  KnowledgeDocumentSummary,
  SearchKnowledgeBaseResult
} from '@/types/app'

export const useRagStore = defineStore('rag', () => {
  const selectedKnowledgeBaseId = ref('')
  const documents = ref<KnowledgeDocumentSummary[]>([])
  const searchResults = ref<SearchKnowledgeBaseResult[]>([])
  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<RagLoadingDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    document: false,
    search: false
  })

  const dialogStates = reactive<Record<RagEditableDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    documentGovernance: false
  })

  const dialogModes = reactive<Record<RagEditableDomain, ConfigDialogMode>>({
    embeddingModel: 'create',
    knowledgeBase: 'create',
    documentGovernance: 'edit'
  })

  const submittingStates = reactive<Record<RagEditableDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    documentGovernance: false
  })

  const actionErrors = reactive<Record<RagEditableDomain | 'document' | 'search', string>>({
    embeddingModel: '',
    knowledgeBase: '',
    documentGovernance: '',
    document: '',
    search: ''
  })

  const domainStates: RagDomainStates = {
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors
  }

  const refreshKnowledgeBases = async () => {
    await knowledgeBaseDomain.refreshKnowledgeBases()
  }
  const embeddingModelDomain = useEmbeddingModelDomain(domainStates, {
    refreshKnowledgeBases
  })
  const searchDomain = useKnowledgeSearchDomain(domainStates, {
    selectedKnowledgeBaseId,
    searchResults
  })
  const documentDomain = useDocumentDomain(domainStates, {
    selectedKnowledgeBaseId,
    documents,
    refreshKnowledgeBases
  })
  const knowledgeBaseDomain = useKnowledgeBaseDomain(domainStates, {
    embeddingModels: embeddingModelDomain.embeddingModels,
    selectedKnowledgeBaseId,
    documents,
    searchResults,
    refreshDocuments: documentDomain.refreshDocuments,
    clearSearch: searchDomain.clearSearch
  })
  const documentGovernanceDomain = useDocumentGovernanceDomain(domainStates, {
    refreshDocuments: documentDomain.refreshDocuments
  })

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([
        embeddingModelDomain.refreshEmbeddingModels(),
        knowledgeBaseDomain.refreshKnowledgeBases()
      ])
      await documentDomain.refreshDocuments()
    } catch (error) {
      errorMessage.value = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.pageLoadFailed,
        RAG_STORE_MESSAGES.pageLoadForbidden
      )
      throw error
    } finally {
      isLoading.value = false
    }
  }

  return {
    ...embeddingModelDomain,
    ...knowledgeBaseDomain,
    ...documentDomain,
    ...documentGovernanceDomain,
    ...searchDomain,
    documents,
    searchResults,
    selectedKnowledgeBaseId,
    isLoading,
    errorMessage,
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors,
    refresh
  }
})
