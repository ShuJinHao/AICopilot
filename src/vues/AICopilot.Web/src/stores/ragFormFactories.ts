import { FORM_DEFAULTS } from '@/constants/formDefaults'
import type {
  EmbeddingModelDetail,
  EmbeddingModelFormModel,
  KnowledgeBaseDetail,
  KnowledgeBaseFormModel,
  KnowledgeDocumentGovernanceForm,
  KnowledgeDocumentSummary,
  UploadDocumentGovernanceForm
} from '@/types/app'

export function createEmptyEmbeddingModelForm(): EmbeddingModelFormModel {
  return {
    name: '',
    provider: FORM_DEFAULTS.embeddingModel.provider,
    baseUrl: FORM_DEFAULTS.embeddingModel.baseUrl,
    apiKey: '',
    apiKeyAction: 'replace',
    modelName: FORM_DEFAULTS.embeddingModel.modelName,
    dimensions: FORM_DEFAULTS.embeddingModel.dimensions,
    maxTokens: FORM_DEFAULTS.embeddingModel.maxTokens,
    isEnabled: true,
    hasApiKey: false,
    apiKeyMasked: null
  }
}

export function createEmptyKnowledgeBaseForm(): KnowledgeBaseFormModel {
  return {
    name: '',
    description: '',
    embeddingModelId: ''
  }
}

export function createDefaultUploadGovernanceForm(): UploadDocumentGovernanceForm {
  return {
    classification: FORM_DEFAULTS.knowledgeDocument.classification,
    sourceType: FORM_DEFAULTS.knowledgeDocument.sourceType,
    isSanitized: false,
    allowedForFinalPrompt: true
  }
}

export function createEmptyDocumentGovernanceForm(): KnowledgeDocumentGovernanceForm {
  return {
    id: 0,
    classification: FORM_DEFAULTS.knowledgeDocument.classification,
    sourceType: FORM_DEFAULTS.knowledgeDocument.sourceType,
    isSanitized: false,
    allowedForFinalPrompt: true,
    effectiveFrom: null,
    effectiveTo: null,
    blockedReason: null
  }
}

export function toEmbeddingModelForm(detail: EmbeddingModelDetail): EmbeddingModelFormModel {
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

export function toKnowledgeBaseForm(detail: KnowledgeBaseDetail): KnowledgeBaseFormModel {
  return {
    id: detail.id,
    name: detail.name,
    description: detail.description,
    embeddingModelId: detail.embeddingModelId
  }
}

export function toDocumentGovernanceForm(
  document: KnowledgeDocumentSummary
): KnowledgeDocumentGovernanceForm {
  return {
    id: document.id,
    classification: document.classification,
    sourceType: document.sourceType,
    isSanitized: document.isSanitized,
    allowedForFinalPrompt: document.allowedForFinalPrompt,
    effectiveFrom: document.effectiveFrom ?? null,
    effectiveTo: document.effectiveTo ?? null,
    blockedReason: document.blockedReason ?? null
  }
}
