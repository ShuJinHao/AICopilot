import { apiClient } from './apiClient'
import type {
  CloudReadonlyStatus,
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  ConversationTemplateSummary,
  LanguageModelDetail,
  LanguageModelFormModel,
  LanguageModelSummary,
  LanguageModelTestRequest,
  LanguageModelTestResult
} from '@/types/app'

export const configService = {
  async getLanguageModel(id: string) {
    return await apiClient.get<LanguageModelDetail>('/aigateway/language-model', { id })
  },

  async getLanguageModels() {
    return await apiClient.get<LanguageModelSummary[]>('/aigateway/language-model/list')
  },

  async createLanguageModel(payload: LanguageModelFormModel) {
    return await apiClient.post('/aigateway/language-model', {
      provider: payload.provider,
      protocolType: payload.protocolType,
      name: payload.name,
      baseUrl: payload.baseUrl,
      apiKey: payload.apiKey,
      maxTokens: payload.maxTokens,
      contextWindowTokens: payload.contextWindowTokens,
      maxOutputTokens: payload.maxOutputTokens,
      isEnabled: payload.isEnabled,
      usages: payload.usages,
      temperature: payload.temperature
    })
  },

  async updateLanguageModel(payload: LanguageModelFormModel) {
    return await apiClient.put('/aigateway/language-model', {
      id: payload.id,
      provider: payload.provider,
      protocolType: payload.protocolType,
      name: payload.name,
      baseUrl: payload.baseUrl,
      apiKey: payload.apiKey,
      clearApiKey: payload.clearApiKey,
      maxTokens: payload.maxTokens,
      contextWindowTokens: payload.contextWindowTokens,
      maxOutputTokens: payload.maxOutputTokens,
      isEnabled: payload.isEnabled,
      usages: payload.usages,
      temperature: payload.temperature
    })
  },

  async testLanguageModel(payload: LanguageModelTestRequest) {
    return await apiClient.post<LanguageModelTestResult>('/aigateway/language-model/test', payload)
  },

  async deleteLanguageModel(id: string) {
    return await apiClient.delete('/aigateway/language-model', { id })
  },

  async getConversationTemplate(id: string) {
    return await apiClient.get<ConversationTemplateDetail>('/aigateway/conversation-template', { id })
  },

  async getConversationTemplates() {
    return await apiClient.get<ConversationTemplateSummary[]>('/aigateway/conversation-template/list')
  },

  async createConversationTemplate(payload: ConversationTemplateFormModel) {
    return await apiClient.post('/aigateway/conversation-template', {
      name: payload.name,
      description: payload.description,
      systemPrompt: payload.systemPrompt,
      modelId: payload.modelId,
      maxTokens: payload.maxTokens,
      temperature: payload.temperature
    })
  },

  async updateConversationTemplate(payload: ConversationTemplateFormModel) {
    return await apiClient.put('/aigateway/conversation-template', {
      id: payload.id,
      name: payload.name,
      description: payload.description,
      systemPrompt: payload.systemPrompt,
      modelId: payload.modelId,
      maxTokens: payload.maxTokens,
      temperature: payload.temperature,
      isEnabled: payload.isEnabled
    })
  },

  async deleteConversationTemplate(id: string) {
    return await apiClient.delete('/aigateway/conversation-template', { id })
  },

  async resetBuiltInConversationTemplates(modelId: string) {
    return await apiClient.post('/aigateway/conversation-template/reset-builtins', { modelId })
  },

  async getCloudReadonlyStatus() {
    return await apiClient.get<CloudReadonlyStatus>('/aigateway/cloud-readonly/status')
  }
}
