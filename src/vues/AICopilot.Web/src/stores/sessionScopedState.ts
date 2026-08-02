import { reactive } from 'vue'

export type ChatRunPhase = 'understanding' | 'querying' | 'answering' | 'completed' | 'failed'

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
  chatRunStatus: ChatRunStatus | null
}

export function createSessionScopedState(): SessionScopedState {
  return { chatRunStatus: null }
}

export function createReactiveSessionScopedState() {
  return reactive(createSessionScopedState())
}

export function resetSessionScopedState(state: SessionScopedState) {
  Object.assign(state, createSessionScopedState())
}
