import { FORM_DEFAULTS } from '@/constants/formDefaults'
import type {
  ApprovalPolicyDetail,
  ApprovalPolicyFormModel,
  BusinessDatabaseDetail,
  BusinessDatabaseFormModel,
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  LanguageModelDetail,
  LanguageModelFormModel,
  McpServerDetail,
  McpServerFormModel,
  RoutingModelDetail,
  RoutingModelFormModel
} from '@/types/app'

export function createEmptyLanguageModelForm(): LanguageModelFormModel {
  return {
    provider: FORM_DEFAULTS.languageModel.provider,
    protocolType: FORM_DEFAULTS.languageModel.protocolType,
    name: '',
    baseUrl: '',
    apiKey: '',
    apiKeyAction: 'replace',
    clearApiKey: false,
    maxTokens: FORM_DEFAULTS.languageModel.contextWindowTokens,
    contextWindowTokens: FORM_DEFAULTS.languageModel.contextWindowTokens,
    maxOutputTokens: FORM_DEFAULTS.languageModel.maxOutputTokens,
    temperature: FORM_DEFAULTS.languageModel.temperature,
    isEnabled: true,
    usages: ['Chat'],
    hasApiKey: false,
    apiKeyMasked: null
  }
}

export function createEmptyRoutingModelForm(): RoutingModelFormModel {
  return {
    name: '',
    modelId: '',
    isActive: false
  }
}

export function createEmptyConversationTemplateForm(): ConversationTemplateFormModel {
  return {
    name: '',
    description: '',
    systemPrompt: '',
    modelId: '',
    maxTokens: null,
    temperature: null,
    isEnabled: true
  }
}

export function createEmptyApprovalPolicyForm(): ApprovalPolicyFormModel {
  return {
    name: '',
    description: '',
    targetType: 'Plugin',
    targetName: '',
    toolNames: [],
    isEnabled: true,
    requiresOnsiteAttestation: false
  }
}

export function createEmptyBusinessDatabaseForm(): BusinessDatabaseFormModel {
  return {
    name: '',
    description: '',
    connectionString: '',
    provider: FORM_DEFAULTS.businessDatabase.provider,
    isEnabled: true,
    isReadOnly: true,
    externalSystemType: FORM_DEFAULTS.businessDatabase.externalSystemType,
    readOnlyCredentialVerified: false,
    hasConnectionString: false,
    connectionStringMasked: null,
    category: 'General',
    tags: [],
    ownerDepartment: '',
    businessDomain: '',
    sensitivityLevel: 'Internal',
    defaultQueryLimit: FORM_DEFAULTS.businessDatabase.defaultQueryLimit,
    maxQueryLimit: FORM_DEFAULTS.businessDatabase.maxQueryLimit,
    isSelectableInChat: true,
    isSelectableInAgent: true
  }
}

export function createEmptyMcpServerForm(): McpServerFormModel {
  return {
    name: '',
    description: '',
    transportType: FORM_DEFAULTS.mcpServer.transportType,
    command: FORM_DEFAULTS.mcpServer.command,
    arguments: '',
    chatExposureMode: FORM_DEFAULTS.mcpServer.chatExposureMode,
    allowedTools: [],
    externalSystemType: FORM_DEFAULTS.mcpServer.externalSystemType,
    capabilityKind: FORM_DEFAULTS.mcpServer.capabilityKind,
    riskLevel: FORM_DEFAULTS.mcpServer.riskLevel,
    isEnabled: true,
    hasArguments: false,
    argumentsMasked: null,
    originalTransportType: FORM_DEFAULTS.mcpServer.transportType
  }
}

export function toLanguageModelForm(detail: LanguageModelDetail): LanguageModelFormModel {
  return {
    id: detail.id,
    provider: detail.provider,
    protocolType: detail.protocolType,
    name: detail.name,
    baseUrl: detail.baseUrl,
    apiKey: '',
    apiKeyAction: 'keep',
    clearApiKey: false,
    maxTokens: detail.contextWindowTokens ?? detail.maxTokens,
    contextWindowTokens: detail.contextWindowTokens ?? detail.maxTokens,
    maxOutputTokens: detail.maxOutputTokens,
    temperature: detail.temperature,
    isEnabled: detail.isEnabled,
    usages: [...detail.usages],
    hasApiKey: detail.hasApiKey,
    apiKeyMasked: detail.apiKeyMasked
  }
}

export function toRoutingModelForm(detail: RoutingModelDetail): RoutingModelFormModel {
  return {
    id: detail.id,
    name: detail.name,
    modelId: detail.modelId,
    isActive: detail.isActive
  }
}

export function toConversationTemplateForm(
  detail: ConversationTemplateDetail
): ConversationTemplateFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    systemPrompt: detail.systemPrompt,
    modelId: detail.modelId,
    maxTokens: detail.maxTokens ?? null,
    temperature: detail.temperature ?? null,
    isEnabled: detail.isEnabled
  }
}

export function toApprovalPolicyForm(detail: ApprovalPolicyDetail): ApprovalPolicyFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description ?? '',
    targetType: detail.targetType,
    targetName: detail.targetName,
    toolNames: [...detail.toolNames],
    isEnabled: detail.isEnabled,
    requiresOnsiteAttestation: detail.requiresOnsiteAttestation
  }
}

export function toBusinessDatabaseForm(detail: BusinessDatabaseDetail): BusinessDatabaseFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    connectionString: '',
    provider: detail.provider,
    isEnabled: detail.isEnabled,
    isReadOnly: true,
    externalSystemType: detail.externalSystemType,
    readOnlyCredentialVerified: detail.readOnlyCredentialVerified,
    hasConnectionString: detail.hasConnectionString,
    connectionStringMasked: detail.connectionStringMasked,
    category: detail.category ?? 'General',
    tags: [...(detail.tags ?? [])],
    ownerDepartment: detail.ownerDepartment ?? '',
    businessDomain: detail.businessDomain ?? '',
    sensitivityLevel: detail.sensitivityLevel ?? 'Internal',
    defaultQueryLimit: detail.defaultQueryLimit ?? FORM_DEFAULTS.businessDatabase.defaultQueryLimit,
    maxQueryLimit: detail.maxQueryLimit ?? FORM_DEFAULTS.businessDatabase.maxQueryLimit,
    isSelectableInChat: detail.isSelectableInChat ?? true,
    isSelectableInAgent: detail.isSelectableInAgent ?? true
  }
}

export function toMcpServerForm(detail: McpServerDetail): McpServerFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    transportType: detail.transportType,
    command: detail.command ?? '',
    arguments: '',
    chatExposureMode: detail.chatExposureMode,
    allowedTools: detail.allowedTools.map((tool) => ({ ...tool })),
    externalSystemType: detail.externalSystemType,
    capabilityKind: detail.capabilityKind,
    riskLevel: detail.riskLevel,
    isEnabled: detail.isEnabled,
    hasArguments: detail.hasArguments,
    argumentsMasked: detail.argumentsMasked,
    originalTransportType: detail.transportType
  }
}
