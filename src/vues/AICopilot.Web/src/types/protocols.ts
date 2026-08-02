export interface Session {
  id: string
  title: string
  agentMode?: 'plan' | 'execute' | null
  agentSessionVersion?: number | null
  agentSessionStatus?: 'Ready' | 'Running' | 'Interrupted' | 'ResetRequired' | string | null
  agentSessionResetRequired?: boolean
  hasPendingApproval?: boolean
}

export interface AgentSessionModeResponse {
  sessionId: string
  mode: 'plan' | 'execute'
  version: number
}

export enum MessageRole {
  User = 'User',
  Assistant = 'Assistant',
}

export enum ChunkType {
  Error = 'Error',
  Text = 'Text',
  Metadata = 'Metadata',
  Widget = 'Widget',
  FunctionResult = 'FunctionResult',
  FunctionCall = 'FunctionCall',
  ApprovalRequest = 'ApprovalRequest',
  AgentEvent = 'AgentEvent',
}

export interface ChatChunk {
  source: string
  type: ChunkType
  content: string
}

export interface ChatModelMetadataPayload {
  finalModelId?: string | null
  finalModelName?: string | null
  contextWindowTokens?: number | null
  maxOutputTokens?: number | null
}

export interface ChatErrorPayload {
  code?: string
  detail?: string
  userFacingMessage?: string | null
}

export interface FunctionApprovalRequest {
  callId: string
  name: string
  runtimeName?: string | null
  targetType?: string | null
  targetName?: string | null
  toolName?: string | null
  args: string | Record<string, unknown>
}

export interface Widget {
  id: string
  type: string
  title: string
  description: string
  data: unknown
}

export interface AgentEventPayload {
  stage: string
  code?: string | null
  detail: string
  recoverable: boolean
  suggestedAction?: string | null
  metadata: Record<string, string>
  sessionId?: string
  mode?: 'plan' | 'execute'
  status?: 'Ready' | 'Running' | 'Interrupted' | string
  version?: number
  pendingApproval?: boolean
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
