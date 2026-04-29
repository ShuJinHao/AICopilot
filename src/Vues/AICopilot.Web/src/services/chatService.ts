import { fetchEventSource } from '@microsoft/fetch-event-source'
import { baseUrl } from '@/appsetting'
import { apiClient, ApiError, getAccessToken, getProblemDetails } from './apiClient'
import type { ChatHistoryMessage, StreamCallbacks } from '@/types/app'
import type { ChatChunk, Session } from '@/types/protocols'

async function sendEventStream(
  path: string,
  payload: unknown,
  callbacks: StreamCallbacks
) {
  const maxAttempts = 2

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const controller = new AbortController()

    try {
      await fetchEventSource(`${baseUrl}${path}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(getAccessToken() ? { Authorization: `Bearer ${getAccessToken()}` } : {})
        },
        body: JSON.stringify(payload),
        signal: controller.signal,
        openWhenHidden: true,
        async onopen(response) {
          if (response.ok) {
            return
          }

          let details: unknown
          try {
            details = await response.json()
          } catch {
            details = undefined
          }

          throw new ApiError(`Stream open failed: ${response.status}`, response.status, details)
        },
        onmessage(event) {
          if (!event.data || event.data === '[DONE]') {
            return
          }

          const chunk = JSON.parse(event.data) as ChatChunk
          callbacks.onChunkReceived(chunk)
        },
        onclose() {
          callbacks.onComplete()
        },
        onerror(error) {
          throw error
        }
      })

      return
    } catch (error) {
      const isRetryable = !(error instanceof ApiError) && attempt < maxAttempts
      if (isRetryable) {
        await new Promise((resolve) => window.setTimeout(resolve, 800 * attempt))
        continue
      }

      if (error instanceof ApiError) {
        const problem = getProblemDetails(error.details)
        if (problem?.detail) {
          callbacks.onError(new ApiError(problem.detail, error.status, error.details))
          return
        }
      }

      callbacks.onError(error)
      return
    }
  }
}

export const chatService = {
  async getSessions() {
    return await apiClient.get<Session[]>('/aigateway/session/list')
  },

  async createSession() {
    return await apiClient.post<Session>('/aigateway/session', {})
  },

  async getHistory(sessionId: string, count = 100) {
    return await apiClient.get<ChatHistoryMessage[]>(
      `/aigateway/chat-message/list?sessionId=${sessionId}&count=${count}&isDesc=false`
    )
  },

  async updateSessionSafetyAttestation(
    sessionId: string,
    isOnsiteConfirmed: boolean,
    expiresInMinutes?: number
  ) {
    return await apiClient.put<Session>('/aigateway/session/safety-attestation', {
      sessionId,
      isOnsiteConfirmed,
      expiresInMinutes
    })
  },

  async sendMessageStream(sessionId: string, message: string, callbacks: StreamCallbacks) {
    await sendEventStream('/aigateway/chat', { sessionId, message }, callbacks)
  },

  async sendApprovalDecisionStream(
    sessionId: string,
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    callbacks: StreamCallbacks
  ) {
    await sendEventStream(
      '/aigateway/approval/decision',
      {
        sessionId,
        callId,
        decision,
        onsiteConfirmed
      },
      callbacks
    )
  }
}
