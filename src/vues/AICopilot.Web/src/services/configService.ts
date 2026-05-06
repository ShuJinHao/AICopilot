import { apiClient } from './apiClient'
import type {
  ApprovalPolicyDetail,
  ApprovalPolicyFormModel,
  ApprovalPolicySummary,
  BusinessDatabaseDetail,
  BusinessDatabaseFormModel,
  BusinessDatabaseSummary,
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  ConversationTemplateSummary,
  LanguageModelDetail,
  LanguageModelFormModel,
  LanguageModelSummary,
  McpServerDetail,
  McpServerFormModel,
  McpServerSummary,
  SemanticSourceStatus
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
      name: payload.name,
      baseUrl: payload.baseUrl,
      apiKey: payload.apiKey,
      maxTokens: payload.maxTokens,
      temperature: payload.temperature
    })
  },

  async updateLanguageModel(payload: LanguageModelFormModel) {
    return await apiClient.put('/aigateway/language-model', {
      id: payload.id,
      provider: payload.provider,
      name: payload.name,
      baseUrl: payload.baseUrl,
      apiKey: payload.apiKey,
      clearApiKey: payload.clearApiKey,
      maxTokens: payload.maxTokens,
      temperature: payload.temperature
    })
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

  async getApprovalPolicy(id: string) {
    return await apiClient.get<ApprovalPolicyDetail>('/aigateway/approval-policy', { id })
  },

  async getApprovalPolicies() {
    return await apiClient.get<ApprovalPolicySummary[]>('/aigateway/approval-policy/list')
  },

  async createApprovalPolicy(payload: ApprovalPolicyFormModel) {
    return await apiClient.post('/aigateway/approval-policy', {
      name: payload.name,
      description: payload.description || null,
      targetType: payload.targetType,
      targetName: payload.targetName,
      toolNames: payload.toolNames,
      isEnabled: payload.isEnabled,
      requiresOnsiteAttestation: payload.requiresOnsiteAttestation
    })
  },

  async updateApprovalPolicy(payload: ApprovalPolicyFormModel) {
    return await apiClient.put('/aigateway/approval-policy', {
      id: payload.id,
      name: payload.name,
      description: payload.description || null,
      targetType: payload.targetType,
      targetName: payload.targetName,
      toolNames: payload.toolNames,
      isEnabled: payload.isEnabled,
      requiresOnsiteAttestation: payload.requiresOnsiteAttestation
    })
  },

  async deleteApprovalPolicy(id: string) {
    return await apiClient.delete('/aigateway/approval-policy', { id })
  },

  async getBusinessDatabase(id: string) {
    return await apiClient.get<BusinessDatabaseDetail>('/data-analysis/business-database', { id })
  },

  async getBusinessDatabases() {
    return await apiClient.get<BusinessDatabaseSummary[]>('/data-analysis/business-database/list')
  },

  async createBusinessDatabase(payload: BusinessDatabaseFormModel) {
    return await apiClient.post('/data-analysis/business-database', {
      name: payload.name,
      description: payload.description,
      connectionString: payload.connectionString,
      provider: payload.provider,
      isEnabled: payload.isEnabled,
      isReadOnly: true,
      externalSystemType: payload.externalSystemType,
      readOnlyCredentialVerified: payload.readOnlyCredentialVerified
    })
  },

  async updateBusinessDatabase(payload: BusinessDatabaseFormModel) {
    return await apiClient.put('/data-analysis/business-database', {
      id: payload.id,
      name: payload.name,
      description: payload.description,
      connectionString: payload.connectionString,
      provider: payload.provider,
      isEnabled: payload.isEnabled,
      isReadOnly: true,
      externalSystemType: payload.externalSystemType,
      readOnlyCredentialVerified: payload.readOnlyCredentialVerified
    })
  },

  async deleteBusinessDatabase(id: string) {
    return await apiClient.delete('/data-analysis/business-database', { id })
  },

  async getSemanticSourceStatuses() {
    return await apiClient.get<SemanticSourceStatus[]>('/data-analysis/semantic-source/status')
  },

  async getMcpServer(id: string) {
    return await apiClient.get<McpServerDetail>('/mcp/server', { id })
  },

  async getMcpServers() {
    return await apiClient.get<McpServerSummary[]>('/mcp/server/list')
  },

  async createMcpServer(payload: McpServerFormModel) {
    return await apiClient.post('/mcp/server', {
      name: payload.name,
      description: payload.description,
      transportType: payload.transportType,
      command: payload.command || null,
      arguments: payload.arguments,
      chatExposureMode: payload.chatExposureMode,
      allowedTools: payload.allowedTools,
      externalSystemType: payload.externalSystemType,
      capabilityKind: payload.capabilityKind,
      riskLevel: payload.riskLevel,
      isEnabled: payload.isEnabled
    })
  },

  async updateMcpServer(payload: McpServerFormModel) {
    return await apiClient.put('/mcp/server', {
      id: payload.id,
      name: payload.name,
      description: payload.description,
      transportType: payload.transportType,
      command: payload.command || null,
      arguments: payload.arguments,
      chatExposureMode: payload.chatExposureMode,
      allowedTools: payload.allowedTools,
      externalSystemType: payload.externalSystemType,
      capabilityKind: payload.capabilityKind,
      riskLevel: payload.riskLevel,
      isEnabled: payload.isEnabled
    })
  },

  async deleteMcpServer(id: string) {
    return await apiClient.delete('/mcp/server', { id })
  }
}
