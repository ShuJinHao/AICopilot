import { ref, type Ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import {
  createEmptyKnowledgeBaseForm,
  toKnowledgeBaseForm
} from '@/stores/ragFormFactories'
import { useAuthStore } from '@/stores/authStore'
import { useDialogCrud } from '@/stores/useDialogCrud'
import { toRagCrudStates, type RagDomainStates } from '@/stores/rag/ragStoreTypes'
import type {
  EmbeddingModelSummary,
  KnowledgeBaseFormModel,
  KnowledgeBaseSummary,
  KnowledgeDocumentSummary,
  SearchKnowledgeBaseResult
} from '@/types/app'

interface KnowledgeBaseDomainOptions {
  embeddingModels: Ref<EmbeddingModelSummary[]>
  selectedKnowledgeBaseId: Ref<string>
  documents: Ref<KnowledgeDocumentSummary[]>
  searchResults: Ref<SearchKnowledgeBaseResult[]>
  refreshDocuments: () => Promise<void>
  clearSearch: () => void
}

export function useKnowledgeBaseDomain(
  states: RagDomainStates,
  options: KnowledgeBaseDomainOptions
) {
  const authStore = useAuthStore()
  const knowledgeBases = ref<KnowledgeBaseSummary[]>([])
  const currentKnowledgeBase = ref<KnowledgeBaseFormModel>(createEmptyKnowledgeBaseForm())

  function syncSelectedKnowledgeBase() {
    if (
      options.selectedKnowledgeBaseId.value &&
      knowledgeBases.value.some((item) => item.id === options.selectedKnowledgeBaseId.value)
    ) {
      return
    }

    options.selectedKnowledgeBaseId.value = knowledgeBases.value[0]?.id ?? ''
  }

  async function refreshKnowledgeBases() {
    if (!authStore.hasAnyPermission(['Rag.GetKnowledgeBase', 'Rag.GetListKnowledgeBases'])) {
      knowledgeBases.value = []
      options.selectedKnowledgeBaseId.value = ''
      return
    }

    states.loadingStates.knowledgeBase = true
    try {
      knowledgeBases.value = await ragService.getKnowledgeBases()
      syncSelectedKnowledgeBase()
    } finally {
      states.loadingStates.knowledgeBase = false
    }
  }

  async function selectKnowledgeBase(id: string) {
    options.selectedKnowledgeBaseId.value = id
    options.clearSearch()
    await options.refreshDocuments()
  }

  const knowledgeBaseCrud = useDialogCrud({
    domain: 'knowledgeBase',
    states: toRagCrudStates(states),
    current: currentKnowledgeBase,
    messages: RAG_STORE_MESSAGES.knowledgeBase,
    createEmptyForm: createEmptyKnowledgeBaseForm,
    toForm: toKnowledgeBaseForm,
    loadDetail: ragService.getKnowledgeBase,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        description: form.description.trim()
      }

      if (mode === 'create') {
        await ragService.createKnowledgeBase(payload)
      } else {
        await ragService.updateKnowledgeBase(payload)
      }
    },
    deleteItem: async (id) => {
      await ragService.deleteKnowledgeBase(id)
    },
    afterOpenCreate: () => {
      currentKnowledgeBase.value.embeddingModelId = options.embeddingModels.value[0]?.id ?? ''
    },
    afterSave: async () => {
      await refreshKnowledgeBases()
      await options.refreshDocuments()
    },
    afterDelete: async (id) => {
      if (options.selectedKnowledgeBaseId.value === id) {
        options.selectedKnowledgeBaseId.value = ''
        options.documents.value = []
        options.searchResults.value = []
      }

      await refreshKnowledgeBases()
      await options.refreshDocuments()
    }
  })

  return {
    knowledgeBases,
    currentKnowledgeBase,
    refreshKnowledgeBases,
    selectKnowledgeBase,
    closeKnowledgeBaseDialog: knowledgeBaseCrud.closeDialog,
    openCreateKnowledgeBaseDialog: knowledgeBaseCrud.openCreateDialog,
    openEditKnowledgeBaseDialog: knowledgeBaseCrud.openEditDialog,
    saveKnowledgeBase: knowledgeBaseCrud.saveDialog,
    deleteKnowledgeBase: knowledgeBaseCrud.deleteDialog
  }
}
