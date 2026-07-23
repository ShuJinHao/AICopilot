import { fetchEventSource } from '@microsoft/fetch-event-source'
import { baseUrl } from '@/appsetting'
import { apiClient, ApiError, getAccessToken, getProblemDetails } from './apiClient'
import type {
  ChatHistoryPage,
  KnowledgeBaseSummary,
  SessionTimelinePage,
  StreamCallbacks,
} from '@/types/app'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskRuntimeSnapshot,
  AgentTaskAuditSummary,
  ArtifactWorkspace,
  ChatChunk,
  FunctionApprovalRequest,
  Session,
  UploadRecord,
} from '@/types/protocols'

const CHAT_READ_REQUEST_OPTIONS = { timeoutMs: 30_000 } as const
const CHAT_MUTATION_REQUEST_OPTIONS = { timeoutMs: 60_000 } as const
const CHAT_TRANSFER_REQUEST_OPTIONS = { timeoutMs: 120_000 } as const
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
        if (response.ok) {
          return
        }

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
        // These endpoints mutate chat/task/approval state and do not carry an idempotency key.
        // Throwing disables fetch-event-source's built-in reconnect; retry must be user initiated.
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

function postAgentTaskAction(path: string, id: string, extra: Record<string, unknown> = {}) {
  return apiClient.post<AgentTask>(
    path,
    { id, ...extra },
    CHAT_MUTATION_REQUEST_OPTIONS,
  )
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

  async getTimeline(
    sessionId: string,
    options: {
      count?: number
      beforeSequence?: number | null
      afterSequence?: number | null
    } = {},
  ) {
    return await apiClient.get<SessionTimelinePage>(
      '/aigateway/session/timeline',
      {
        sessionId,
        count: options.count ?? 200,
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

  async updateSessionSafetyAttestation(
    sessionId: string,
    isOnsiteConfirmed: boolean,
    expiresInMinutes?: number,
  ) {
    return await apiClient.put<Session>(
      '/aigateway/session/safety-attestation',
      {
        sessionId,
        isOnsiteConfirmed,
        expiresInMinutes,
      },
      CHAT_MUTATION_REQUEST_OPTIONS,
    )
  },

  async getPendingApprovals(sessionId: string) {
    return await apiClient.get<FunctionApprovalRequest[]>(
      '/aigateway/approval/pending',
      {
        sessionId,
      },
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async sendMessageStream(
    sessionId: string,
    message: string,
    callbacks: StreamCallbacks,
    referencedAgentTaskId?: string | null,
  ) {
    await sendEventStream(
      '/aigateway/chat',
      {
        sessionId,
        message,
        ...(referencedAgentTaskId ? { referencedAgentTaskId } : {}),
      },
      callbacks,
    )
  },

  async sendApprovalDecisionStream(
    sessionId: string,
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    request: FunctionApprovalRequest,
    callbacks: StreamCallbacks,
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
        toolName: request.toolName,
      },
      callbacks,
    )
  },

  async uploadFile(
    scope: 'SessionTemp' | 'AgentInput',
    file: File,
    options: {
      sessionId?: string | null
      agentTaskId?: string | null
    } = {},
  ) {
    const form = new FormData()
    form.set('scope', scope)
    form.set('file', file)
    if (options.sessionId) form.set('sessionId', options.sessionId)
    if (options.agentTaskId) form.set('agentTaskId', options.agentTaskId)
    return await apiClient.postForm<UploadRecord>(
      '/aigateway/upload',
      form,
      CHAT_TRANSFER_REQUEST_OPTIONS,
    )
  },

  async planAgentTaskStream(
    payload: {
      sessionId: string
      goal: string
      taskType: string
      uploadIds?: string[]
      knowledgeBaseIds?: string[]
      artifactTargets?: string[]
      pluginSelectionMode?: 'BuiltInOnly' | 'ExplicitAllowlist'
      selectedPluginIds?: string[]
      capabilitySelectionMode?: 'InferredFromGoal' | 'ExplicitAllowlist'
      requestedCapabilityCodes?: string[]
    },
    callbacks: StreamCallbacks,
  ) {
    await sendEventStream('/aigateway/agent/task/plan-stream', payload, callbacks)
  },

  async approveAgentTaskPlan(id: string) {
    return await postAgentTaskAction('/aigateway/agent/task/approve-plan', id)
  },

  async rejectAgentTaskPlan(id: string) {
    return await postAgentTaskAction(
      '/aigateway/agent/task/reject-plan',
      id,
      { reason: 'Plan rejected from primary PlanDraft action.' },
    )
  },

  async runAgentTask(id: string) {
    return await postAgentTaskAction('/aigateway/agent/task/run', id)
  },

  async retryAgentTask(id: string) {
    return await postAgentTaskAction('/aigateway/agent/task/retry', id)
  },

  async cancelAgentTask(id: string) {
    return await postAgentTaskAction('/aigateway/agent/task/cancel', id)
  },

  async getAgentTasksBySession(sessionId: string) {
    return await apiClient.get<AgentTask[]>(
      '/aigateway/agent/task/by-session',
      { sessionId },
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getPendingAgentApprovals() {
    return await apiClient.get<AgentApprovalRequest[]>(
      '/aigateway/agent/approval/pending',
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getAgentTaskApprovals(taskId: string) {
    return await apiClient.get<AgentApprovalRequest[]>(
      `/aigateway/agent/task/${encodeURIComponent(taskId)}/approvals`,
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getAgentTaskAuditSummary(taskId: string) {
    return await apiClient.get<AgentTaskAuditSummary[]>(
      `/aigateway/agent/task/${encodeURIComponent(taskId)}/audit-summary`,
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getAgentTaskRuntimeSnapshot(taskId: string) {
    return await apiClient.get<AgentTaskRuntimeSnapshot>(
      `/aigateway/agent/task/${encodeURIComponent(taskId)}/runtime-snapshot`,
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async decideAgentApproval(id: string, decision: 'approve' | 'reject', comment?: string | null) {
    return await apiClient.post<AgentApprovalRequest>(
      `/aigateway/agent/approval/${encodeURIComponent(id)}/${decision}`,
      { comment },
      CHAT_MUTATION_REQUEST_OPTIONS,
    )
  },

  async getWorkspace(code: string) {
    return await apiClient.get<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}`,
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async getArtifactPreview(id: string) {
    return await apiClient.get<AgentArtifactPreview>(
      `/aigateway/artifact/${encodeURIComponent(id)}/preview`,
      undefined,
      CHAT_READ_REQUEST_OPTIONS,
    )
  },

  async finalizeWorkspace(code: string) {
    return await apiClient.post<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}/finalize`,
      {},
      CHAT_TRANSFER_REQUEST_OPTIONS,
    )
  },

  async submitFinalReview(code: string) {
    return await apiClient.post<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}/submit-final-review`,
      {},
      CHAT_TRANSFER_REQUEST_OPTIONS,
    )
  },

  async downloadArtifact(downloadUrl: string) {
    return await apiClient.download(downloadUrl, undefined, CHAT_TRANSFER_REQUEST_OPTIONS)
  },
}
