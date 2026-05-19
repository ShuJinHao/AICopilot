import { fetchEventSource } from '@microsoft/fetch-event-source'
import { baseUrl } from '@/appsetting'
import { apiClient, ApiError, getAccessToken, getProblemDetails } from './apiClient'
import type { ChatHistoryMessage, SelectableChatModel, StreamCallbacks } from '@/types/app'
import type {
  AgentApprovalRequest,
  AgentTask,
  AgentTaskAuditSummary,
  ArtifactWorkspace,
  ChatChunk,
  FunctionApprovalRequest,
  Session,
  UploadRecord
} from '@/types/protocols'

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

  async getSelectableChatModels() {
    return await apiClient.get<SelectableChatModel[]>('/aigateway/language-model/chat-options')
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

  async getPendingApprovals(sessionId: string) {
    return await apiClient.get<FunctionApprovalRequest[]>('/aigateway/approval/pending', {
      sessionId
    })
  },

  async sendMessageStream(
    sessionId: string,
    message: string,
    finalModelId: string | null,
    callbacks: StreamCallbacks
  ) {
    await sendEventStream('/aigateway/chat', { sessionId, message, finalModelId }, callbacks)
  },

  async sendApprovalDecisionStream(
    sessionId: string,
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    request: FunctionApprovalRequest,
    callbacks: StreamCallbacks
  ) {
    await sendEventStream(
      '/aigateway/approval/decision',
      {
        sessionId,
        callId,
        decision,
        onsiteConfirmed,
        targetType: request.targetType,
        targetName: request.targetName,
        toolName: request.toolName
      },
      callbacks
    )
  },

  async uploadFile(
    scope: 'SessionTemp' | 'AgentInput' | 'KnowledgeBase',
    file: File,
    options: {
      sessionId?: string | null
      agentTaskId?: string | null
      knowledgeBaseId?: string | null
    } = {}
  ) {
    const form = new FormData()
    form.set('scope', scope)
    form.set('file', file)
    if (options.sessionId) form.set('sessionId', options.sessionId)
    if (options.agentTaskId) form.set('agentTaskId', options.agentTaskId)
    if (options.knowledgeBaseId) form.set('knowledgeBaseId', options.knowledgeBaseId)
    return await apiClient.postForm<UploadRecord>('/aigateway/upload', form)
  },

  async planAgentTask(payload: {
    sessionId: string
    goal: string
    taskType: string
    modelId?: string | null
    uploadIds?: string[]
    knowledgeBaseIds?: string[]
  }) {
    return await apiClient.post<AgentTask>('/aigateway/agent/task/plan', payload)
  },

  async approveAgentTaskPlan(id: string) {
    return await apiClient.post<AgentTask>('/aigateway/agent/task/approve-plan', { id })
  },

  async runAgentTask(id: string) {
    return await apiClient.post<AgentTask>('/aigateway/agent/task/run', { id })
  },

  async getAgentTasksBySession(sessionId: string) {
    return await apiClient.get<AgentTask[]>('/aigateway/agent/task/by-session', { sessionId })
  },

  async getPendingAgentApprovals() {
    return await apiClient.get<AgentApprovalRequest[]>('/aigateway/agent/approval/pending')
  },

  async getAgentTaskApprovals(taskId: string) {
    return await apiClient.get<AgentApprovalRequest[]>(
      `/aigateway/agent/task/${encodeURIComponent(taskId)}/approvals`
    )
  },

  async getAgentTaskAuditSummary(taskId: string) {
    return await apiClient.get<AgentTaskAuditSummary[]>(
      `/aigateway/agent/task/${encodeURIComponent(taskId)}/audit-summary`
    )
  },

  async decideAgentApproval(id: string, decision: 'approve' | 'reject', comment?: string | null) {
    return await apiClient.post<AgentApprovalRequest>(
      `/aigateway/agent/approval/${encodeURIComponent(id)}/${decision}`,
      { comment }
    )
  },

  async getWorkspace(code: string) {
    return await apiClient.get<ArtifactWorkspace>(`/aigateway/workspace/${encodeURIComponent(code)}`)
  },

  async finalizeWorkspace(code: string) {
    return await apiClient.post<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}/finalize`,
      {}
    )
  },

  async downloadArtifact(id: string) {
    return await apiClient.download(`/aigateway/artifact/${encodeURIComponent(id)}/download`)
  }
}
