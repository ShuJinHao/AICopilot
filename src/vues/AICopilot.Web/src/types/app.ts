export interface InitializationStatus {
  hasAdminRole: boolean
  hasUserRole: boolean
  bootstrapAdminConfigured: boolean
  hasEnabledAdminUser: boolean
  isInitialized: boolean
}

export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  userName: string
  token: string
}

export interface CloudOidcStatus {
  isEnabled: boolean
}

export type CloudReadonlyRuntimeStatus =
  | 'Disabled'
  | 'Simulation'
  | 'RealReady'
  | 'RealMissingBaseUrl'
  | 'RealMissingToken'
  | 'RealNotAllowed'
  | string

export interface CloudReadonlyStatus {
  mode: string
  status: CloudReadonlyRuntimeStatus
  baseUrlConfigured: boolean
  tokenConfigured: boolean
  productionReadAllowed: boolean
  message: string
}

export interface CurrentUserProfile {
  userId: string
  userName: string
  roleName?: string | null
  permissions: string[]
  identityProvider: 'Local' | 'Cloud' | string
  cloudTenantId?: string | null
  cloudEmployeeNo?: string | null
  cloudStatusVersion?: string | null
}

export interface PermissionDefinition {
  code: string
  group: string
  displayName: string
  description: string
}

export interface RoleSummary {
  roleId: string
  roleName: string
  permissions: string[]
  isSystemRole: boolean
  assignedUserCount: number
}

export interface UserSummary {
  userId: string
  userName: string
  roleName?: string | null
  isEnabled: boolean
  status: 'Enabled' | 'Disabled'
}

export interface AuditLogSummary {
  id: string
  actionGroup: string
  actionCode: string
  targetType: string
  targetId?: string | null
  targetName?: string | null
  operatorUserName?: string | null
  operatorRoleName?: string | null
  result: 'Succeeded' | 'Rejected'
  summary: string
  changedFields: string[]
  metadata: Record<string, string>
  createdAt: string
}

export interface AuditLogListResponse {
  items: AuditLogSummary[]
  page: number
  pageSize: number
  totalCount: number
}

export interface AuditLogQuery {
  page: number
  pageSize: number
  actionGroup?: string
  actionCode?: string
  targetType?: string
  targetName?: string
  operatorUserName?: string
  result?: 'Succeeded' | 'Rejected' | ''
  from?: string
  to?: string
}

export interface AuthState {
  token: string
  userName: string
  isAuthenticated: boolean
}

export interface ApiErrorPayload {
  errors?: string[] | Record<string, string[]>
}

export interface ChatHistoryMessage {
  messageId?: number
  sequence?: number
  sessionId: string
  role: 'User' | 'Assistant'
  content: string
  createdAt: string
  renderChunks?: import('@/types/protocols').ChatChunk[] | null
  finalModelId?: string | null
  finalModelName?: string | null
  contextWindowTokens?: number | null
  maxOutputTokens?: number | null
}

export interface ChatHistoryPage {
  items: ChatHistoryMessage[]
  beforeSequence?: number | null
  afterSequence?: number | null
  hasMore: boolean
  hasMoreBefore: boolean
  hasMoreAfter: boolean
}

export interface ToolRegistrationSummary {
  toolCode: string
  displayName: string
  description: string
  providerType: string
  targetType: string
  targetName: string
  inputSchemaJson: string
  requiresApproval: boolean
  riskLevel: string
  timeoutSeconds: number
  auditLevel: string
  runtimeAvailable: boolean
  category: string
  businessDomains?: string[] | null
  dataBoundary: string
  isExecutableByAgent: boolean
  schemaVersion: number
  catalogVersion: number
}

export interface ToolRegistryCatalog {
  version: number
  availableToolCount: number
  riskSummary: Record<string, number>
  tools: ToolRegistrationSummary[]
}

export interface StreamCallbacks {
  onChunkReceived: (chunk: import('@/types/protocols').ChatChunk) => void
  onComplete: () => void
  onError: (err: unknown) => void
}

export interface LanguageModelSummary {
  id: string
  provider: string
  protocolType: string
  name: string
  baseUrl: string
  maxTokens: number
  contextWindowTokens: number
  maxOutputTokens: number
  temperature: number
  isEnabled: boolean
  usages: LanguageModelUsage[]
  hasApiKey: boolean
  apiKeyMasked?: string | null
  connectivityStatus: 'Unknown' | 'Succeeded' | 'Failed' | string
  connectivityCheckedAt?: string | null
  connectivityError?: string | null
}

export type LanguageModelDetail = LanguageModelSummary

export type LanguageModelApiKeyAction = 'keep' | 'replace' | 'clear'
export type LanguageModelUsage = 'Chat'

export interface LanguageModelFormModel {
  id?: string
  provider: string
  protocolType: string
  name: string
  baseUrl: string
  apiKey: string
  apiKeyAction: LanguageModelApiKeyAction
  clearApiKey: boolean
  maxTokens: number
  contextWindowTokens: number
  maxOutputTokens: number
  temperature: number
  isEnabled: boolean
  usages: LanguageModelUsage[]
  hasApiKey: boolean
  apiKeyMasked?: string | null
}

export interface LanguageModelTestRequest {
  id?: string
  provider?: string
  protocolType?: string
  name?: string
  baseUrl?: string
  apiKey?: string
  clearApiKey?: boolean
  maxTokens?: number
  contextWindowTokens?: number
  maxOutputTokens?: number
  usages?: LanguageModelUsage[]
  temperature?: number
  persistResult?: boolean
}

export interface LanguageModelTestResult {
  success: boolean
  status: 'Succeeded' | 'Failed' | string
  message: string
  error?: string | null
  elapsedMilliseconds: number
  checkedAt: string
}

export interface ConversationTemplateSummary {
  id: string
  name: string
  code?: string | null
  description: string
  systemPrompt: string
  modelId: string
  scope?: string | null
  builtInVersion?: number
  isBuiltIn?: boolean
  maxTokens?: number | null
  temperature?: number | null
  isEnabled: boolean
}

export type ConversationTemplateDetail = ConversationTemplateSummary

export interface ConversationTemplateFormModel {
  id?: string
  name: string
  description: string
  systemPrompt: string
  modelId: string
  maxTokens?: number | null
  temperature?: number | null
  isEnabled: boolean
}

export type EmbeddingModelApiKeyAction = 'keep' | 'replace' | 'clear'

export interface EmbeddingModelSummary {
  id: string
  name: string
  provider: string
  baseUrl: string
  modelName: string
  dimensions: number
  maxTokens: number
  isEnabled: boolean
  hasApiKey: boolean
  apiKeyMasked?: string | null
}

export type EmbeddingModelDetail = EmbeddingModelSummary

export interface EmbeddingModelFormModel {
  id?: string
  name: string
  provider: string
  baseUrl: string
  apiKey: string
  apiKeyAction: EmbeddingModelApiKeyAction
  modelName: string
  dimensions: number
  maxTokens: number
  isEnabled: boolean
  hasApiKey: boolean
  apiKeyMasked?: string | null
}

export interface KnowledgeBaseSummary {
  id: string
  name: string
  description: string
  embeddingModelId: string
  documentCount: number
}

export type KnowledgeBaseDetail = KnowledgeBaseSummary

export interface KnowledgeBaseFormModel {
  id?: string
  name: string
  description: string
  embeddingModelId: string
}

export type KnowledgeDocumentStatus =
  | 'Pending'
  | 'Parsing'
  | 'Splitting'
  | 'Embedding'
  | 'Indexed'
  | 'Failed'
  | number

export type KnowledgeDocumentClassification =
  | 'Public'
  | 'Internal'
  | 'Sensitive'
  | 'Forbidden'
  | number

export type KnowledgeDocumentSourceType =
  | 'UserUploaded'
  | 'BusinessRule'
  | 'CloudReadOnlyApiDoc'
  | 'Runbook'
  | 'External'
  | number

export interface UploadDocumentGovernanceForm {
  classification: KnowledgeDocumentClassification
  sourceType: KnowledgeDocumentSourceType
  isSanitized: boolean
  allowedForFinalPrompt: boolean
}

export interface KnowledgeDocumentGovernanceForm extends UploadDocumentGovernanceForm {
  id: number
  effectiveFrom?: string | null
  effectiveTo?: string | null
  blockedReason?: string | null
}

export interface KnowledgeDocumentSummary {
  id: number
  knowledgeBaseId: string
  name: string
  extension: string
  status: KnowledgeDocumentStatus
  chunkCount: number
  errorMessage?: string | null
  createdAt: string
  processedAt?: string | null
  classification: KnowledgeDocumentClassification
  sourceType: KnowledgeDocumentSourceType
  isSanitized: boolean
  reviewedBy?: string | null
  reviewedAt?: string | null
  effectiveFrom?: string | null
  effectiveTo?: string | null
  allowedForFinalPrompt: boolean
  blockedReason?: string | null
}

export interface UploadDocumentResponse {
  id: number
  status: string
}

export interface SearchKnowledgeBaseResult {
  text: string
  score: number
  documentId: number
  documentName?: string | null
}

export type ConfigDialogMode = 'create' | 'edit'
