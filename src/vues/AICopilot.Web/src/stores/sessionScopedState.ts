import { reactive } from 'vue'
import type { SessionTimelineEvent } from '@/types/app'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskAuditSummary,
  ArtifactWorkspace,
  UploadRecord
} from '@/types/protocols'

export interface AgentChartPreview {
  labels: string[]
  values: number[]
  source?: string
  sourceMode?: string
  sourceLabel?: string
  isSimulation?: boolean
  queryHash?: string
}

export type ChatRunPhase =
  | 'understanding'
  | 'querying'
  | 'answering'
  | 'completed'
  | 'failed'

export interface ChatRunStatus {
  sessionId: string
  messageKey?: string
  messageId?: string
  phase: ChatRunPhase
  startedAt: string
  completedAt?: string
  elapsedMs: number
  summary?: string
  queryCount?: number
  returnedRows?: number
  error?: {
    code?: string
    message: string
  }
}

export interface SessionScopedState {
  agentTasks: AgentTask[]
  agentApprovals: AgentApprovalRequest[]
  agentAuditSummary: AgentTaskAuditSummary[]
  timelineEvents: SessionTimelineEvent[]
  uploadedFiles: UploadRecord[]
  currentWorkspace: ArtifactWorkspace | null
  currentArtifactPreview: AgentArtifactPreview | null
  chartPreview: AgentChartPreview | null
  isAgentBusy: boolean
  chatRunStatus: ChatRunStatus | null
}

export function createSessionScopedState(): SessionScopedState {
  return {
    agentTasks: [],
    agentApprovals: [],
    agentAuditSummary: [],
    timelineEvents: [],
    uploadedFiles: [],
    currentWorkspace: null,
    currentArtifactPreview: null,
    chartPreview: null,
    isAgentBusy: false,
    chatRunStatus: null
  }
}

export function createReactiveSessionScopedState() {
  return reactive(createSessionScopedState())
}

export function resetSessionScopedState(state: SessionScopedState) {
  Object.assign(state, createSessionScopedState())
}
