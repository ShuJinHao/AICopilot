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
