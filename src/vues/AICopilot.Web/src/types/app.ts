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
  routingModelId?: string | null
  routingModelName?: string | null
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

export interface SessionTimelineEvent {
  sequence: number
  eventType: string
  createdAt: string
  messageId?: number | null
  agentTaskId?: string | null
  agentTaskTitle?: string | null
  agentTaskGoal?: string | null
  agentTaskStatus?: string | null
  agentStepId?: string | null
  agentStepIndex?: number | null
  agentStepTitle?: string | null
  agentStepStatus?: string | null
  agentStepToolCode?: string | null
  approvalRequestId?: string | null
  approvalType?: string | null
  approvalStatus?: string | null
  approvalTargetName?: string | null
  approvalDecidedAt?: string | null
  artifactWorkspaceId?: string | null
  workspaceCode?: string | null
  workspaceStatus?: string | null
  artifactId?: string | null
  artifactName?: string | null
  artifactType?: string | null
  artifactStatus?: string | null
  artifactRelativePath?: string | null
  artifactDownloadUrl?: string | null
  agentStepOutputKind?: string | null
  agentStepResultCount?: number | null
  agentStepLowConfidence?: boolean | null
  agentStepSources?: SessionTimelineStepSource[]
}

export interface SessionTimelineStepSource {
  knowledgeBaseId?: string | null
  documentId?: number | null
  documentName?: string | null
  chunkIndex?: number | null
  score?: number | null
  isLowConfidence?: boolean | null
  lowConfidenceReason?: string | null
  textPreview?: string | null
}

export interface SessionTimelinePage {
  items: SessionTimelineEvent[]
  beforeSequence?: number | null
  afterSequence?: number | null
  hasMore: boolean
  hasMoreBefore: boolean
  hasMoreAfter: boolean
}

export interface SkillDefinition {
  id: string
  skillCode: string
  displayName: string
  description: string
  allowedToolCodes: string[]
  riskLevel: string
  approvalPolicy: string
  allowedDataSourceModes: string[]
  allowedKnowledgeScopes: string[]
  outputComponentTypes: string[]
  isEnabled: boolean
  isBuiltIn: boolean
  version: number
  createdAt: string
  updatedAt: string
}

export interface PlannerToolPropertySummary {
  name: string
  type: string
  enum: string[]
  required: boolean
}

export interface PlannerToolSchemaSummary {
  type: string
  required: string[]
  properties: PlannerToolPropertySummary[]
  itemsType?: string | null
  isTruncated: boolean
}

export interface AgentPlannerToolSummary {
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
  inputSchema?: PlannerToolSchemaSummary | null
  outputSchema?: PlannerToolSchemaSummary | null
  category: string
  businessDomains?: string[] | null
  dataBoundary: string
  isVisibleToPlanner: boolean
  isExecutableByAgent: boolean
  schemaVersion: number
  catalogVersion: number
  approvalPolicy: string
  providerKind: string
  isMock: boolean
}

export interface ToolRegistryCatalog {
  version: number
  availableToolCount: number
  riskSummary: Record<string, number>
  tools: AgentPlannerToolSummary[]
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

export interface LanguageModelDetail extends LanguageModelSummary {}

export type LanguageModelApiKeyAction = 'keep' | 'replace' | 'clear'
export type LanguageModelUsage = 'Chat' | 'Routing' | 'Planner' | 'Embedding'

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

export interface RoutingModelSummary {
  id: string
  name: string
  modelId: string
  modelName: string
  modelProvider: string
  isActive: boolean
}

export interface RoutingModelDetail extends RoutingModelSummary {}

export interface RoutingModelFormModel {
  id?: string
  name: string
  modelId: string
  isActive: boolean
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
