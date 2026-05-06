export interface InitializationStatus {
  hasAdminRole: boolean
  hasUserRole: boolean
  bootstrapAdminConfigured: boolean
  hasAdminUser: boolean
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

export interface CurrentUserProfile {
  userId: string
  userName: string
  roleName?: string | null
  permissions: string[]
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
  sessionId: string
  role: 'User' | 'Assistant'
  content: string
  createdAt: string
}

export interface StreamCallbacks {
  onChunkReceived: (chunk: import('@/types/protocols').ChatChunk) => void
  onComplete: () => void
  onError: (err: unknown) => void
}

export interface LanguageModelSummary {
  id: string
  provider: string
  name: string
  baseUrl: string
  maxTokens: number
  temperature: number
  hasApiKey: boolean
  apiKeyMasked?: string | null
}

export interface LanguageModelDetail extends LanguageModelSummary {}

export type LanguageModelApiKeyAction = 'keep' | 'replace' | 'clear'

export interface LanguageModelFormModel {
  id?: string
  provider: string
  name: string
  baseUrl: string
  apiKey: string
  apiKeyAction: LanguageModelApiKeyAction
  clearApiKey: boolean
  maxTokens: number
  temperature: number
  hasApiKey: boolean
  apiKeyMasked?: string | null
}

export interface ConversationTemplateSummary {
  id: string
  name: string
  description: string
  systemPrompt: string
  modelId: string
  maxTokens?: number | null
  temperature?: number | null
  isEnabled: boolean
}

export interface ConversationTemplateDetail extends ConversationTemplateSummary {}

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

export type ApprovalTargetType = 'Plugin' | 'McpServer'

export interface ApprovalPolicySummary {
  id: string
  name: string
  description?: string | null
  targetType: ApprovalTargetType
  targetName: string
  toolNames: string[]
  isEnabled: boolean
  requiresOnsiteAttestation: boolean
}

export interface ApprovalPolicyDetail extends ApprovalPolicySummary {}

export interface ApprovalPolicyFormModel {
  id?: string
  name: string
  description: string
  targetType: ApprovalTargetType
  targetName: string
  toolNames: string[]
  isEnabled: boolean
  requiresOnsiteAttestation: boolean
}

export interface BusinessDatabaseSummary {
  id: string
  name: string
  description: string
  provider: number
  isEnabled: boolean
  isReadOnly: boolean
  externalSystemType: number
  readOnlyCredentialVerified: boolean
  createdAt: string
  hasConnectionString: boolean
  connectionStringMasked?: string | null
}

export interface BusinessDatabaseDetail extends BusinessDatabaseSummary {}

export interface BusinessDatabaseFormModel {
  id?: string
  name: string
  description: string
  connectionString: string
  provider: number
  isEnabled: boolean
  isReadOnly: boolean
  externalSystemType: number
  readOnlyCredentialVerified: boolean
  hasConnectionString: boolean
  connectionStringMasked?: string | null
}

export interface McpToolPolicySummary {
  toolName: string
  requiresApproval: boolean
  requiresOnsiteAttestation: boolean
}

export interface McpServerSummary {
  id: string
  name: string
  description: string
  transportType: number
  command?: string | null
  hasArguments: boolean
  argumentsMasked?: string | null
  chatExposureMode: number
  allowedToolNames: string[]
  externalSystemType: number
  capabilityKind: number
  riskLevel: number
  toolPolicySummaries: McpToolPolicySummary[]
  isEnabled: boolean
}

export interface McpServerDetail extends McpServerSummary {}

export interface McpServerFormModel {
  id?: string
  name: string
  description: string
  transportType: number
  command: string
  arguments: string
  chatExposureMode: number
  allowedToolNames: string[]
  externalSystemType: number
  capabilityKind: number
  riskLevel: number
  isEnabled: boolean
  hasArguments: boolean
  argumentsMasked?: string | null
  originalTransportType?: number
}

export interface SemanticSourceStatus {
  target: string
  databaseName?: string | null
  sourceName?: string | null
  effectiveSourceName?: string | null
  isEnabled: boolean
  isReadOnly: boolean
  sourceExists: boolean
  providerMatched: boolean
  missingRequiredFields: string[]
  status: string
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

export interface EmbeddingModelDetail extends EmbeddingModelSummary {}

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

export interface KnowledgeBaseDetail extends KnowledgeBaseSummary {}

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
