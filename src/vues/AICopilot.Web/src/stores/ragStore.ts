import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { RAG_STORE_MESSAGES } from '@/constants/messages'
import { ragService } from '@/services/ragService'
import {
  createDefaultUploadGovernanceForm,
  createEmptyDocumentGovernanceForm,
  createEmptyEmbeddingModelForm,
  createEmptyKnowledgeBaseForm,
  toDocumentGovernanceForm,
  toEmbeddingModelForm,
  toKnowledgeBaseForm
} from '@/stores/ragFormFactories'
import { toStoreErrorMessage, useDialogCrud } from '@/stores/useDialogCrud'
import { useAuthStore } from '@/stores/authStore'
import type {
  ConfigDialogMode,
  EmbeddingModelFormModel,
  EmbeddingModelSummary,
  KnowledgeBaseFormModel,
  KnowledgeBaseSummary,
  KnowledgeDocumentGovernanceForm,
  KnowledgeDocumentSummary,
  SearchKnowledgeBaseResult,
  UploadDocumentGovernanceForm
} from '@/types/app'

type EditableDomain = 'embeddingModel' | 'knowledgeBase' | 'documentGovernance'
type CrudDomain = 'embeddingModel' | 'knowledgeBase'
type LoadingDomain = 'embeddingModel' | 'knowledgeBase' | 'document' | 'search'

export const useRagStore = defineStore('rag', () => {
  const authStore = useAuthStore()
  const embeddingModels = ref<EmbeddingModelSummary[]>([])
  const knowledgeBases = ref<KnowledgeBaseSummary[]>([])
  const documents = ref<KnowledgeDocumentSummary[]>([])
  const searchResults = ref<SearchKnowledgeBaseResult[]>([])
  const selectedKnowledgeBaseId = ref('')
  const searchQuery = ref('')
  const searchTopK = ref(5)
  const searchMinScore = ref(0.5)
  const isLoading = ref(false)
  const errorMessage = ref('')

  const loadingStates = reactive<Record<LoadingDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    document: false,
    search: false
  })

  const dialogStates = reactive<Record<EditableDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    documentGovernance: false
  })

  const dialogModes = reactive<Record<EditableDomain, ConfigDialogMode>>({
    embeddingModel: 'create',
    knowledgeBase: 'create',
    documentGovernance: 'edit'
  })

  const submittingStates = reactive<Record<EditableDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false,
    documentGovernance: false
  })

  const actionErrors = reactive<Record<EditableDomain | 'document' | 'search', string>>({
    embeddingModel: '',
    knowledgeBase: '',
    documentGovernance: '',
    document: '',
    search: ''
  })

  const currentEmbeddingModel = ref<EmbeddingModelFormModel>(createEmptyEmbeddingModelForm())
  const currentKnowledgeBase = ref<KnowledgeBaseFormModel>(createEmptyKnowledgeBaseForm())
  const currentDocumentGovernance = ref<KnowledgeDocumentGovernanceForm>(
    createEmptyDocumentGovernanceForm()
  )
  const currentDocumentGovernanceName = ref('')
  const uploadGovernanceForm = ref<UploadDocumentGovernanceForm>(createDefaultUploadGovernanceForm())

  function syncSelectedKnowledgeBase() {
    if (
      selectedKnowledgeBaseId.value &&
      knowledgeBases.value.some((item) => item.id === selectedKnowledgeBaseId.value)
    ) {
      return
    }

    selectedKnowledgeBaseId.value = knowledgeBases.value[0]?.id ?? ''
  }

  async function refreshEmbeddingModels() {
    if (!authStore.hasAnyPermission(['Rag.GetEmbeddingModel', 'Rag.GetListEmbeddingModels'])) {
      embeddingModels.value = []
      return
    }

    loadingStates.embeddingModel = true
    try {
      embeddingModels.value = await ragService.getEmbeddingModels()
    } finally {
      loadingStates.embeddingModel = false
    }
  }

  async function refreshKnowledgeBases() {
    if (!authStore.hasAnyPermission(['Rag.GetKnowledgeBase', 'Rag.GetListKnowledgeBases'])) {
      knowledgeBases.value = []
      selectedKnowledgeBaseId.value = ''
      return
    }

    loadingStates.knowledgeBase = true
    try {
      knowledgeBases.value = await ragService.getKnowledgeBases()
      syncSelectedKnowledgeBase()
    } finally {
      loadingStates.knowledgeBase = false
    }
  }

  async function refreshDocuments() {
    if (!authStore.hasPermission('Rag.GetListDocuments')) {
      documents.value = []
      return
    }

    if (!selectedKnowledgeBaseId.value) {
      documents.value = []
      return
    }

    loadingStates.document = true
    try {
      documents.value = await ragService.getDocuments(selectedKnowledgeBaseId.value)
    } finally {
      loadingStates.document = false
    }
  }

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([refreshEmbeddingModels(), refreshKnowledgeBases()])
      await refreshDocuments()
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

  async function selectKnowledgeBase(id: string) {
    selectedKnowledgeBaseId.value = id
    searchResults.value = []
    actionErrors.search = ''
    await refreshDocuments()
  }

  const crudStates = {
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors
  } satisfies {
    loadingStates: Record<CrudDomain, boolean>
    dialogStates: Record<CrudDomain, boolean>
    dialogModes: Record<CrudDomain, ConfigDialogMode>
    submittingStates: Record<CrudDomain, boolean>
    actionErrors: Record<CrudDomain, string>
  }

  const embeddingModelCrud = useDialogCrud({
    domain: 'embeddingModel',
    states: crudStates,
    current: currentEmbeddingModel,
    messages: RAG_STORE_MESSAGES.embeddingModel,
    createEmptyForm: createEmptyEmbeddingModelForm,
    toForm: toEmbeddingModelForm,
    loadDetail: ragService.getEmbeddingModel,
    saveForm: async (form, mode) => {
      const payload = {
        ...form,
        name: form.name.trim(),
        provider: form.provider.trim(),
        baseUrl: form.baseUrl.trim(),
        modelName: form.modelName.trim(),
        apiKey: form.apiKey.trim()
      }

      if (mode === 'create') {
        await ragService.createEmbeddingModel(payload)
      } else {
        await ragService.updateEmbeddingModel(payload)
      }
    },
    deleteItem: async (id) => {
      await ragService.deleteEmbeddingModel(id)
    },
    afterSave: refreshEmbeddingModels,
    afterDelete: async () => {
      await refreshEmbeddingModels()
      await refreshKnowledgeBases()
    }
  })

  const knowledgeBaseCrud = useDialogCrud({
    domain: 'knowledgeBase',
    states: crudStates,
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
      currentKnowledgeBase.value.embeddingModelId = embeddingModels.value[0]?.id ?? ''
    },
    afterSave: async () => {
      await refreshKnowledgeBases()
      await refreshDocuments()
    },
    afterDelete: async (id) => {
      if (selectedKnowledgeBaseId.value === id) {
        selectedKnowledgeBaseId.value = ''
        documents.value = []
        searchResults.value = []
      }

      await refreshKnowledgeBases()
      await refreshDocuments()
    }
  })

  const closeEmbeddingModelDialog = embeddingModelCrud.closeDialog
  const openCreateEmbeddingModelDialog = embeddingModelCrud.openCreateDialog
  const openEditEmbeddingModelDialog = embeddingModelCrud.openEditDialog
  const saveEmbeddingModel = embeddingModelCrud.saveDialog
  const deleteEmbeddingModel = embeddingModelCrud.deleteDialog

  const closeKnowledgeBaseDialog = knowledgeBaseCrud.closeDialog
  const openCreateKnowledgeBaseDialog = knowledgeBaseCrud.openCreateDialog
  const openEditKnowledgeBaseDialog = knowledgeBaseCrud.openEditDialog
  const saveKnowledgeBase = knowledgeBaseCrud.saveDialog
  const deleteKnowledgeBase = knowledgeBaseCrud.deleteDialog

  async function uploadDocument(file: File) {
    if (!selectedKnowledgeBaseId.value) {
      actionErrors.document = RAG_STORE_MESSAGES.selectKnowledgeBaseFirst
      return
    }

    loadingStates.document = true
    actionErrors.document = ''

    try {
      await ragService.uploadDocument(selectedKnowledgeBaseId.value, file, uploadGovernanceForm.value)
      await refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.uploadFailed,
        RAG_STORE_MESSAGES.document.uploadForbidden
      )
      throw error
    } finally {
      loadingStates.document = false
    }
  }

  async function deleteDocument(id: number) {
    actionErrors.document = ''

    try {
      await ragService.deleteDocument(id)
      await refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.deleteFailed,
        RAG_STORE_MESSAGES.document.deleteForbidden
      )
      throw error
    }
  }

  function closeDocumentGovernanceDialog() {
    dialogStates.documentGovernance = false
    actionErrors.document = ''
    currentDocumentGovernance.value = createEmptyDocumentGovernanceForm()
    currentDocumentGovernanceName.value = ''
  }

  function openEditDocumentGovernanceDialog(document: KnowledgeDocumentSummary) {
    actionErrors.document = ''
    currentDocumentGovernance.value = toDocumentGovernanceForm(document)
    currentDocumentGovernanceName.value = document.name
    dialogStates.documentGovernance = true
  }

  async function saveDocumentGovernance() {
    submittingStates.documentGovernance = true
    actionErrors.document = ''

    try {
      await ragService.updateDocumentGovernance(currentDocumentGovernance.value)
      await refreshDocuments()
      closeDocumentGovernanceDialog()
    } catch (error) {
      actionErrors.document = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.document.governanceSaveFailed,
        RAG_STORE_MESSAGES.document.governanceSaveForbidden
      )
      throw error
    } finally {
      submittingStates.documentGovernance = false
    }
  }

  async function searchKnowledgeBase() {
    if (!selectedKnowledgeBaseId.value || !searchQuery.value.trim()) {
      searchResults.value = []
      return
    }

    loadingStates.search = true
    actionErrors.search = ''

    try {
      searchResults.value = await ragService.searchKnowledgeBase(
        selectedKnowledgeBaseId.value,
        searchQuery.value.trim(),
        searchTopK.value,
        searchMinScore.value
      )
    } catch (error) {
      actionErrors.search = toStoreErrorMessage(
        error,
        RAG_STORE_MESSAGES.search.failed,
        RAG_STORE_MESSAGES.search.forbidden
      )
      throw error
    } finally {
      loadingStates.search = false
    }
  }

  return {
    embeddingModels,
    knowledgeBases,
    documents,
    searchResults,
    selectedKnowledgeBaseId,
    searchQuery,
    searchTopK,
    searchMinScore,
    isLoading,
    errorMessage,
    loadingStates,
    dialogStates,
    dialogModes,
    submittingStates,
    actionErrors,
    currentEmbeddingModel,
    currentKnowledgeBase,
    currentDocumentGovernance,
    currentDocumentGovernanceName,
    uploadGovernanceForm,
    refresh,
    refreshEmbeddingModels,
    refreshKnowledgeBases,
    refreshDocuments,
    selectKnowledgeBase,
    closeEmbeddingModelDialog,
    openCreateEmbeddingModelDialog,
    openEditEmbeddingModelDialog,
    saveEmbeddingModel,
    deleteEmbeddingModel,
    closeKnowledgeBaseDialog,
    openCreateKnowledgeBaseDialog,
    openEditKnowledgeBaseDialog,
    saveKnowledgeBase,
    deleteKnowledgeBase,
    uploadDocument,
    deleteDocument,
    closeDocumentGovernanceDialog,
    openEditDocumentGovernanceDialog,
    saveDocumentGovernance,
    searchKnowledgeBase
  }
})
