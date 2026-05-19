export interface Session {
  id: string
  title: string
  onsiteConfirmedAt?: string | null
  onsiteConfirmedBy?: string | null
  onsiteConfirmationExpiresAt?: string | null
}

export enum MessageRole {
  User = 'User',
  Assistant = 'Assistant'
}

export enum ChunkType {
  Error = 'Error',
  Text = 'Text',
  Metadata = 'Metadata',
  Intent = 'Intent',
  Widget = 'Widget',
  FunctionResult = 'FunctionResult',
  FunctionCall = 'FunctionCall',
  ApprovalRequest = 'ApprovalRequest'
}

export interface ChatChunk {
  source: string
  type: ChunkType
  content: string
}

export interface ChatModelMetadataPayload {
  finalModelId?: string | null
  finalModelName?: string | null
  routingModelId?: string | null
  routingModelName?: string | null
  contextWindowTokens?: number | null
  maxOutputTokens?: number | null
}

export interface ChatErrorPayload {
  code?: string
  detail?: string
  userFacingMessage?: string | null
}

export interface IntentResult {
  intent: string
  confidence: number
  reasoning?: string
  query?: string
}

export interface FunctionApprovalRequest {
  callId: string
  name: string
  runtimeName?: string | null
  targetType?: string | null
  targetName?: string | null
  toolName?: string | null
  args: string | Record<string, unknown>
  requiresOnsiteAttestation: boolean
  attestationExpiresAt?: string | null
}

export interface Widget {
  id: string
  type: string
  title: string
  description: string
  data: unknown
}

export interface UploadRecord {
  id: string
  scope: 'SessionTemp' | 'AgentInput' | 'KnowledgeBase' | string
  sessionId?: string | null
  agentTaskId?: string | null
  knowledgeBaseId?: string | null
  ragDocumentId?: number | null
  fileName: string
  contentType: string
  fileSize: number
  sha256: string
  status: string
  createdAt: string
}

export interface AgentStep {
  id: string
  stepIndex: number
  title: string
  description: string
  stepType: string
  status: string
  toolCode?: string | null
  requiresApproval: boolean
  errorMessage?: string | null
}

export interface AgentTask {
  id: string
  taskCode: string
  sessionId: string
  title: string
  goal: string
  taskType: string
  status: string
  riskLevel: string
  modelId?: string | null
  workspaceId?: string | null
  workspaceCode?: string | null
  planJson: string
  finalSummary?: string | null
  createdAt: string
  updatedAt: string
  completedAt?: string | null
  steps: AgentStep[]
  pendingApprovalCount?: number
  lastFailureReason?: string | null
  canRetry?: boolean
}

export interface AgentApprovalRequest {
  id: string
  taskId: string
  workspaceCode?: string | null
  type: string
  targetId: string
  targetName: string
  riskLevel: string
  status: string
  reason?: string | null
  requestedAt: string
  decidedAt?: string | null
  decidedBy?: string | null
}

export interface AgentTaskAuditSummary {
  id: string
  taskId: string
  workspaceCode?: string | null
  actionCode: string
  targetType: string
  targetName: string
  result: string
  summary: string
  createdAt: string
  metadata: Record<string, string>
}

export interface ArtifactRecord {
  id: string
  name: string
  type: string
  status: string
  relativePath: string
  fileSize: number
  mimeType: string
  version: number
  updatedAt: string
  previewKind: 'chart' | 'json' | 'table' | 'markdown' | 'html' | 'pdf' | 'spreadsheet' | 'download' | string
  downloadUrl: string
  generatedByStepOrder?: number | null
  requiresApproval?: boolean
  approvalStatus?: string
  finalizedAt?: string | null
}

export interface ArtifactWorkspaceFile {
  name: string
  relativePath: string
  isDirectory: boolean
  fileSize: number
  updatedAt: string
}

export interface ArtifactWorkspace {
  id: string
  workspaceCode: string
  taskId: string
  status: string
  files: ArtifactWorkspaceFile[]
  artifacts: ArtifactRecord[]
}

export interface ChartWidget extends Widget {
  type: 'Chart'
  data: {
    category: 'Bar' | 'Line' | 'Pie'
    dataset: {
      dimensions: string[]
      source: Array<Record<string, unknown>>
    }
    encoding: {
      x: string
      y: string[]
      seriesName?: string
    }
  }
}

export interface StatsCardWidget extends Widget {
  type: 'StatsCard'
  data: {
    label: string
    value: string | number
    unit?: string
  }
}

export interface DataTableWidget extends Widget {
  type: 'DataTable'
  data: {
    columns: Array<{
      key: string
      label: string
      dataType: 'string' | 'number' | 'date' | 'boolean'
    }>
    rows: Array<Record<string, unknown>>
  }
}
