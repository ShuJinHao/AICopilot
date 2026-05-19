import { apiClient } from './apiClient'
import type {
  ApprovalPolicyDetail,
  ApprovalPolicyFormModel,
  ApprovalPolicySummary,
  ArtifactWorkspaceSettings,
  BusinessDatabaseDetail,
  BusinessDatabaseFormModel,
  BusinessDatabaseSummary,
  ChatRuntimeSettings,
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  ConversationTemplateSummary,
  LanguageModelDetail,
  LanguageModelFormModel,
  LanguageModelSummary,
  LanguageModelTestRequest,
  LanguageModelTestResult,
  McpServerDetail,
  McpServerFormModel,
  McpServerSummary,
  ProviderReliabilityConfig,
  RoutingModelDetail,
  RoutingModelFormModel,
  RoutingModelSummary,
  SemanticSourceStatus
} from '@/types/app'
import type {
  AgentRunQueuePage,
  AgentRunQueueSummary,
  AgentWorkerStatus
} from '@/types/protocols'

export const configService = {
  async getLanguageModel(id: string) {
    return await apiClient.get<LanguageModelDetail>('/aigateway/language-model', { id })
  },

  async getLanguageModels() {
    return await apiClient.get<LanguageModelSummary[]>('/aigateway/language-model/list')
  },

  async getProviderReliability() {
    return await apiClient.get<ProviderReliabilityConfig>('/aigateway/provider-reliability')
  },

  async getRuntimeSettings() {
    return await apiClient.get<ChatRuntimeSettings>('/aigateway/runtime-settings')
  },

  async getWorkspaceSettings() {
    return await apiClient.get<ArtifactWorkspaceSettings>('/aigateway/workspace-settings')
  },

  async getAgentRunQueueSummary() {
    return await apiClient.get<AgentRunQueueSummary>('/aigateway/agent/run-queue/summary')
  },

  async getAgentRunQueue() {
    return await apiClient.get<AgentRunQueuePage>('/aigateway/agent/run-queue', {
      pageIndex: 1,
      pageSize: 8
    })
  },

  async getAgentWorkerStatus() {
    return await apiClient.get<AgentWorkerStatus>('/aigateway/agent/worker/status')
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

  async getRoutingModel(id: string) {
    return await apiClient.get<RoutingModelDetail>('/aigateway/routing-model', { id })
  },

  async getRoutingModels() {
    return await apiClient.get<RoutingModelSummary[]>('/aigateway/routing-model/list')
  },

  async createRoutingModel(payload: RoutingModelFormModel) {
    return await apiClient.post('/aigateway/routing-model', {
      name: payload.name,
      modelId: payload.modelId,
      isActive: payload.isActive
    })
  },

  async updateRoutingModel(payload: RoutingModelFormModel) {
    return await apiClient.put('/aigateway/routing-model', {
      id: payload.id,
      name: payload.name,
      modelId: payload.modelId,
      isActive: payload.isActive
    })
  },

  async activateRoutingModel(id: string) {
    return await apiClient.put('/aigateway/routing-model/activate', { id })
  },

  async deleteRoutingModel(id: string) {
    return await apiClient.delete('/aigateway/routing-model', { id })
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
