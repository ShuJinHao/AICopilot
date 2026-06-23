import { FORM_DEFAULTS } from '@/constants/formDefaults'
import type {
  ConversationTemplateDetail,
  ConversationTemplateFormModel,
  LanguageModelDetail,
  LanguageModelFormModel,
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
