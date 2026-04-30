import { apiClient } from './apiClient'
import type {
  EmbeddingModelDetail,
  EmbeddingModelFormModel,
  EmbeddingModelSummary,
  KnowledgeBaseDetail,
  KnowledgeBaseFormModel,
  KnowledgeBaseSummary,
  KnowledgeDocumentSummary,
  SearchKnowledgeBaseResult,
  UploadDocumentResponse
} from '@/types/app'

function resolveEmbeddingApiKey(payload: EmbeddingModelFormModel) {
  switch (payload.apiKeyAction) {
    case 'keep':
      return null
    case 'clear':
      return ''
    case 'replace':
      return payload.apiKey.trim()
    default:
      return ''
  }
}

export const ragService = {
  async getEmbeddingModel(id: string) {
    return await apiClient.get<EmbeddingModelDetail>('/rag/embedding-model', { id })
  },

  async getEmbeddingModels() {
    return await apiClient.get<EmbeddingModelSummary[]>('/rag/embedding-model/list')
  },

  async createEmbeddingModel(payload: EmbeddingModelFormModel) {
    return await apiClient.post('/rag/embedding-model', {
      name: payload.name,
      provider: payload.provider,
      baseUrl: payload.baseUrl,
      apiKey: payload.apiKey.trim() || null,
      modelName: payload.modelName,
      dimensions: payload.dimensions,
      maxTokens: payload.maxTokens,
      isEnabled: payload.isEnabled
    })
  },

  async updateEmbeddingModel(payload: EmbeddingModelFormModel) {
    return await apiClient.put('/rag/embedding-model', {
      id: payload.id,
      name: payload.name,
      provider: payload.provider,
      baseUrl: payload.baseUrl,
      apiKey: resolveEmbeddingApiKey(payload),
      modelName: payload.modelName,
      dimensions: payload.dimensions,
      maxTokens: payload.maxTokens,
      isEnabled: payload.isEnabled
    })
  },

  async deleteEmbeddingModel(id: string) {
    return await apiClient.delete('/rag/embedding-model', { id })
  },

  async getKnowledgeBase(id: string) {
    return await apiClient.get<KnowledgeBaseDetail>('/rag/knowledge-base', { id })
  },

  async getKnowledgeBases() {
    return await apiClient.get<KnowledgeBaseSummary[]>('/rag/knowledge-base/list')
  },

  async createKnowledgeBase(payload: KnowledgeBaseFormModel) {
    return await apiClient.post('/rag/knowledge-base', {
      name: payload.name,
      description: payload.description,
      embeddingModelId: payload.embeddingModelId
    })
  },

  async updateKnowledgeBase(payload: KnowledgeBaseFormModel) {
    return await apiClient.put('/rag/knowledge-base', {
      id: payload.id,
      name: payload.name,
      description: payload.description,
      embeddingModelId: payload.embeddingModelId
    })
  },

  async deleteKnowledgeBase(id: string) {
    return await apiClient.delete('/rag/knowledge-base', { id })
  },

  async getDocuments(knowledgeBaseId: string) {
    return await apiClient.get<KnowledgeDocumentSummary[]>('/rag/document/list', { knowledgeBaseId })
  },

  async uploadDocument(knowledgeBaseId: string, file: File) {
    const form = new FormData()
    form.append('knowledgeBaseId', knowledgeBaseId)
    form.append('file', file)
    return await apiClient.postForm<UploadDocumentResponse>('/rag/document', form)
  },

  async deleteDocument(id: number) {
    return await apiClient.delete('/rag/document', { id })
  },

  async searchKnowledgeBase(
    knowledgeBaseId: string,
    queryText: string,
    topK: number,
    minScore: number
  ) {
    return await apiClient.post<SearchKnowledgeBaseResult[]>('/rag/search', {
      knowledgeBaseId,
      queryText,
      topK,
      minScore
    })
  }
}
