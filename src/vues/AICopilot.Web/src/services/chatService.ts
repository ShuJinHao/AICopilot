import { fetchEventSource } from '@microsoft/fetch-event-source'
import { baseUrl } from '@/appsetting'
import { apiClient, ApiError, getAccessToken, getProblemDetails } from './apiClient'
import type { ChatHistoryPage, KnowledgeBaseSummary, StreamCallbacks } from '@/types/app'
import type {
  AgentSessionModeResponse,
  ChatChunk,
  FunctionApprovalRequest,
  Session,
} from '@/types/protocols'

const CHAT_READ_REQUEST_OPTIONS = { timeoutMs: 30_000 } as const
const CHAT_MUTATION_REQUEST_OPTIONS = { timeoutMs: 60_000 } as const
const CHAT_STREAM_REQUEST_TIMEOUT_MS = 10 * 60_000

async function sendEventStream(path: string, payload: unknown, callbacks: StreamCallbacks) {
  const controller = new AbortController()
  let streamTimedOut = false
  const timeoutId = globalThis.setTimeout(() => {
    streamTimedOut = true
    controller.abort()
  }, CHAT_STREAM_REQUEST_TIMEOUT_MS)

  try {
    await fetchEventSource(`${baseUrl}${path}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(getAccessToken() ? { Authorization: `Bearer ${getAccessToken()}` } : {}),
      },
      body: JSON.stringify(payload),
      signal: controller.signal,
      openWhenHidden: true,
      async onopen(response) {
        if (response.ok) return

        let details: unknown
        try {
          details = await response.json()
        } catch (error) {
          console.error('Failed to parse stream open error response.', error)
          details = undefined
        }
        throw new ApiError(`Stream open failed: ${response.status}`, response.status, details)
      },
      onmessage(event) {
        if (!event.data || event.data === '[DONE]') return
        callbacks.onChunkReceived(JSON.parse(event.data) as ChatChunk)
      },
      onclose() {
        callbacks.onComplete()
      },
      onerror(error) {
        // Chat mutations have no idempotency key; reconnecting could duplicate a turn.
        throw error
      },
    })

    if (streamTimedOut) {
      throw new ApiError('AICopilot stream request timed out.', 408, {
        code: 'client_stream_timeout',
        detail: '对话连接长时间无响应，请重试。',
      })
    }
  } catch (error) {
    if (error instanceof ApiError) {
      const problem = getProblemDetails(error.details)
      if (problem?.detail) {
        callbacks.onError(
          new ApiError('AICopilot stream request failed.', error.status, error.details),
        )
        return
      }
    }
    callbacks.onError(error)
  } finally {
    globalThis.clearTimeout(timeoutId)
  }
}

export const chatService = {
  async getSessions() {
    return await apiClient.get<Session[]>(
      '/aigateway/session/list',
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async createSession() {
    return await apiClient.post<Session>('/aigateway/session', {}, CHAT_MUTATION_REQUEST_OPTIONS)
  },

  async getSession(id: string) {
    return await apiClient.get<Session>('/aigateway/session', { id }, CHAT_READ_REQUEST_OPTIONS)
  },

  async updateAgentMode(sessionId: string, mode: 'plan' | 'execute', expectedVersion: number) {
    return await apiClient.put<AgentSessionModeResponse>(
      `/aigateway/session/${encodeURIComponent(sessionId)}/agent-mode`,
      { mode, expectedVersion },
      CHAT_MUTATION_REQUEST_OPTIONS,
    )
  },

  async deleteSession(id: string) {
    return await apiClient.delete('/aigateway/session', { id }, CHAT_MUTATION_REQUEST_OPTIONS)
  },

  async getHistory(
    sessionId: string,
    options: {
      count?: number
      beforeSequence?: number | null
      afterSequence?: number | null
    } = {},
  ) {
    return await apiClient.get<ChatHistoryPage>(
      '/aigateway/chat-message/list',
      {
        sessionId,
        count: options.count ?? 100,
        isDesc: false,
        beforeSequence: options.beforeSequence ?? undefined,
        afterSequence: options.afterSequence ?? undefined,
      },
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getKnowledgeBases() {
    return await apiClient.get<KnowledgeBaseSummary[]>(
      '/rag/knowledge-base/list',
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getPendingApprovals(sessionId: string) {
    return await apiClient.get<FunctionApprovalRequest[]>(
      '/aigateway/approval/pending',
      { sessionId },
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async sendMessageStream(sessionId: string, message: string, callbacks: StreamCallbacks) {
    await sendEventStream('/aigateway/chat', { sessionId, message }, callbacks)
  },

  async sendApprovalDecisionStream(
    sessionId: string,
    callId: string,
    decision: 'approved' | 'rejected',
    callbacks: StreamCallbacks,
  ) {
    await sendEventStream(
      '/aigateway/approval/decision',
      { sessionId, callId, decision },
      callbacks,
    )
  },
}
