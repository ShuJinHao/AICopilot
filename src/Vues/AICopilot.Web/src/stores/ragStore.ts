import { defineStore } from 'pinia'
import { reactive, ref } from 'vue'
import { ApiError, getProblemDetails } from '@/services/apiClient'
import { ragService } from '@/services/ragService'
import { useAuthStore } from '@/stores/authStore'
import type {
  ConfigDialogMode,
  EmbeddingModelDetail,
  EmbeddingModelFormModel,
  EmbeddingModelSummary,
  KnowledgeBaseDetail,
  KnowledgeBaseFormModel,
  KnowledgeBaseSummary,
  KnowledgeDocumentSummary,
  SearchKnowledgeBaseResult
} from '@/types/app'

type EditableDomain = 'embeddingModel' | 'knowledgeBase'
type LoadingDomain = EditableDomain | 'document' | 'search'

function createEmptyEmbeddingModelForm(): EmbeddingModelFormModel {
  return {
    name: '',
    provider: 'OpenAI',
    baseUrl: 'https://api.openai.com/v1',
    apiKey: '',
    apiKeyAction: 'replace',
    modelName: 'text-embedding-3-small',
    dimensions: 1536,
    maxTokens: 8191,
    isEnabled: true,
    hasApiKey: false,
    apiKeyMasked: null
  }
}

function createEmptyKnowledgeBaseForm(): KnowledgeBaseFormModel {
  return {
    name: '',
    description: '',
    embeddingModelId: ''
  }
}

function toEmbeddingModelForm(detail: EmbeddingModelDetail): EmbeddingModelFormModel {
  return {
    id: detail.id,
    name: detail.name,
    provider: detail.provider,
    baseUrl: detail.baseUrl,
    apiKey: '',
    apiKeyAction: 'keep',
    modelName: detail.modelName,
    dimensions: detail.dimensions,
    maxTokens: detail.maxTokens,
    isEnabled: detail.isEnabled,
    hasApiKey: detail.hasApiKey,
    apiKeyMasked: detail.apiKeyMasked
  }
}

function toKnowledgeBaseForm(detail: KnowledgeBaseDetail): KnowledgeBaseFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    embeddingModelId: detail.embeddingModelId
  }
}

function toErrorMessage(error: unknown, fallback: string, forbiddenMessage: string) {
  if (error instanceof ApiError && error.status === 403) {
    return forbiddenMessage
  }

  if (error instanceof ApiError) {
    const problem = getProblemDetails(error.details)
    return problem?.detail || problem?.title || fallback
  }

  return fallback
}

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
    knowledgeBase: false
  })

  const dialogModes = reactive<Record<EditableDomain, ConfigDialogMode>>({
    embeddingModel: 'create',
    knowledgeBase: 'create'
  })

  const submittingStates = reactive<Record<EditableDomain, boolean>>({
    embeddingModel: false,
    knowledgeBase: false
  })

  const actionErrors = reactive<Record<EditableDomain | 'document' | 'search', string>>({
    embeddingModel: '',
    knowledgeBase: '',
    document: '',
    search: ''
  })

  const currentEmbeddingModel = ref<EmbeddingModelFormModel>(createEmptyEmbeddingModelForm())
  const currentKnowledgeBase = ref<KnowledgeBaseFormModel>(createEmptyKnowledgeBaseForm())

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
      errorMessage.value = toErrorMessage(
        error,
        '知识库页面加载失败，请稍后重试。',
        '当前账号没有查看知识库配置的权限。'
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

  function closeEmbeddingModelDialog() {
    dialogStates.embeddingModel = false
    dialogModes.embeddingModel = 'create'
    actionErrors.embeddingModel = ''
    currentEmbeddingModel.value = createEmptyEmbeddingModelForm()
  }

  function openCreateEmbeddingModelDialog() {
    actionErrors.embeddingModel = ''
    dialogModes.embeddingModel = 'create'
    currentEmbeddingModel.value = createEmptyEmbeddingModelForm()
    dialogStates.embeddingModel = true
  }

  async function openEditEmbeddingModelDialog(id: string) {
    loadingStates.embeddingModel = true
    actionErrors.embeddingModel = ''

    try {
      const detail = await ragService.getEmbeddingModel(id)
      currentEmbeddingModel.value = toEmbeddingModelForm(detail)
      dialogModes.embeddingModel = 'edit'
      dialogStates.embeddingModel = true
    } catch (error) {
      actionErrors.embeddingModel = toErrorMessage(
        error,
        '加载嵌入模型详情失败，请稍后重试。',
        '当前账号没有查看嵌入模型详情的权限。'
      )
      throw error
    } finally {
      loadingStates.embeddingModel = false
    }
  }

  async function saveEmbeddingModel() {
    submittingStates.embeddingModel = true
    actionErrors.embeddingModel = ''

    try {
      const payload = {
        ...currentEmbeddingModel.value,
        name: currentEmbeddingModel.value.name.trim(),
        provider: currentEmbeddingModel.value.provider.trim(),
        baseUrl: currentEmbeddingModel.value.baseUrl.trim(),
        modelName: currentEmbeddingModel.value.modelName.trim(),
        apiKey: currentEmbeddingModel.value.apiKey.trim()
      }

      if (dialogModes.embeddingModel === 'create') {
        await ragService.createEmbeddingModel(payload)
      } else {
        await ragService.updateEmbeddingModel(payload)
      }

      await refreshEmbeddingModels()
      closeEmbeddingModelDialog()
    } catch (error) {
      actionErrors.embeddingModel = toErrorMessage(
        error,
        '保存嵌入模型失败，请稍后重试。',
        '当前账号没有管理嵌入模型的权限。'
      )
      throw error
    } finally {
      submittingStates.embeddingModel = false
    }
  }

  async function deleteEmbeddingModel(id: string) {
    actionErrors.embeddingModel = ''

    try {
      await ragService.deleteEmbeddingModel(id)
      await refreshEmbeddingModels()
      await refreshKnowledgeBases()
    } catch (error) {
      actionErrors.embeddingModel = toErrorMessage(
        error,
        '删除嵌入模型失败，请确认没有知识库仍在使用该模型。',
        '当前账号没有删除嵌入模型的权限。'
      )
      throw error
    }
  }

  function closeKnowledgeBaseDialog() {
    dialogStates.knowledgeBase = false
    dialogModes.knowledgeBase = 'create'
    actionErrors.knowledgeBase = ''
    currentKnowledgeBase.value = createEmptyKnowledgeBaseForm()
  }

  function openCreateKnowledgeBaseDialog() {
    actionErrors.knowledgeBase = ''
    dialogModes.knowledgeBase = 'create'
    currentKnowledgeBase.value = {
      ...createEmptyKnowledgeBaseForm(),
      embeddingModelId: embeddingModels.value[0]?.id ?? ''
    }
    dialogStates.knowledgeBase = true
  }

  async function openEditKnowledgeBaseDialog(id: string) {
    loadingStates.knowledgeBase = true
    actionErrors.knowledgeBase = ''

    try {
      const detail = await ragService.getKnowledgeBase(id)
      currentKnowledgeBase.value = toKnowledgeBaseForm(detail)
      dialogModes.knowledgeBase = 'edit'
      dialogStates.knowledgeBase = true
    } catch (error) {
      actionErrors.knowledgeBase = toErrorMessage(
        error,
        '加载知识库详情失败，请稍后重试。',
        '当前账号没有查看知识库详情的权限。'
      )
      throw error
    } finally {
      loadingStates.knowledgeBase = false
    }
  }

  async function saveKnowledgeBase() {
    submittingStates.knowledgeBase = true
    actionErrors.knowledgeBase = ''

    try {
      const payload = {
        ...currentKnowledgeBase.value,
        name: currentKnowledgeBase.value.name.trim(),
        description: currentKnowledgeBase.value.description.trim()
      }

      if (dialogModes.knowledgeBase === 'create') {
        await ragService.createKnowledgeBase(payload)
      } else {
        await ragService.updateKnowledgeBase(payload)
      }

      await refreshKnowledgeBases()
      await refreshDocuments()
      closeKnowledgeBaseDialog()
    } catch (error) {
      actionErrors.knowledgeBase = toErrorMessage(
        error,
        '保存知识库失败，请稍后重试。',
        '当前账号没有管理知识库的权限。'
      )
      throw error
    } finally {
      submittingStates.knowledgeBase = false
    }
  }

  async function deleteKnowledgeBase(id: string) {
    actionErrors.knowledgeBase = ''

    try {
      await ragService.deleteKnowledgeBase(id)
      if (selectedKnowledgeBaseId.value === id) {
        selectedKnowledgeBaseId.value = ''
        documents.value = []
        searchResults.value = []
      }

      await refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      actionErrors.knowledgeBase = toErrorMessage(
        error,
        '删除知识库失败，请稍后重试。',
        '当前账号没有删除知识库的权限。'
      )
      throw error
    }
  }

  async function uploadDocument(file: File) {
    if (!selectedKnowledgeBaseId.value) {
      actionErrors.document = '请先选择一个知识库。'
      return
    }

    loadingStates.document = true
    actionErrors.document = ''

    try {
      await ragService.uploadDocument(selectedKnowledgeBaseId.value, file)
      await refreshKnowledgeBases()
      await refreshDocuments()
    } catch (error) {
      actionErrors.document = toErrorMessage(
        error,
        '上传文档失败，请检查文件大小和格式后重试。',
        '当前账号没有上传知识库文档的权限。'
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
      actionErrors.document = toErrorMessage(
        error,
        '删除文档失败，请稍后重试。',
        '当前账号没有删除知识库文档的权限。'
      )
      throw error
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
      actionErrors.search = toErrorMessage(
        error,
        '检索知识库失败，请稍后重试。',
        '当前账号没有检索知识库的权限。'
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
    searchKnowledgeBase
  }
})
