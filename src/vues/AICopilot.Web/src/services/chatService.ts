import { fetchEventSource } from '@microsoft/fetch-event-source'
import { baseUrl } from '@/appsetting'
import { apiClient, ApiError, getAccessToken, getProblemDetails } from './apiClient'
import type { ChatHistoryMessage, SelectableChatModel, StreamCallbacks } from '@/types/app'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskAuditSummary,
  AgentTrialScenario,
  ArtifactWorkspace,
  ChatChunk,
  CloudReadonlySandboxControlledPlan,
  CloudReadonlyPilotConfigPackage,
  CloudReadonlyPilotContractRehearsal,
  CloudReadonlyPilotReadinessStatus,
  CloudReadonlyProductionPilotScenarioResult,
  CloudReadonlyProductionPilotStatus,
  CloudReadonlyProductionPilotWindow,
  CloudSandboxGoalTimeRange,
  FunctionApprovalRequest,
  PilotApprovalRehearsal,
  PilotReadinessAssessment,
  Session,
  TrialCampaign,
  TrialEvidencePackage,
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
    dataSourceIds?: string[]
    businessDomains?: string[]
    queryMode?: string | null
    requiresDataApproval?: boolean
    artifactTypes?: string[]
    plannerMode?: 'Auto' | 'DynamicOnly' | 'StaticOnly'
  }) {
    return await apiClient.post<AgentTask>('/aigateway/agent/task/plan', payload)
  },

  async getAgentTrialScenarios() {
    return await apiClient.get<AgentTrialScenario[]>('/aigateway/agent/trial-scenarios')
  },

  async createAgentTaskFromTrialScenario(payload: {
    sessionId: string
    scenarioId: string
    promptOverride?: string | null
    artifactTypes?: string[]
    dataSourceIds?: string[]
    plannerMode?: 'Auto' | 'DynamicOnly' | 'StaticOnly'
  }) {
    return await apiClient.post<AgentTask>('/aigateway/agent/trial-scenarios/create-task', payload)
  },

  async getTrialCampaigns() {
    return await apiClient.get<TrialCampaign[]>('/aigateway/trial-operations/campaigns')
  },

  async getTrialCampaignDetail(id: string) {
    return await apiClient.get<TrialCampaign>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(id)}`
    )
  },

  async createTrialCampaign(payload: {
    name: string
    allowedSourceModes?: string[]
    ownerDepartment?: string | null
    startAt?: string | null
    endAt?: string | null
    summary?: string | null
  }) {
    return await apiClient.post<TrialCampaign>('/aigateway/trial-operations/campaigns', payload)
  },

  async updateTrialCampaignStatus(id: string, status: string) {
    return await apiClient.request<TrialCampaign>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(id)}/status`,
      {
        method: 'PATCH',
        body: JSON.stringify({ status })
      }
    )
  },

  async attachAgentTaskToTrialCampaign(
    campaignId: string,
    payload: {
      taskId: string
      scenarioId?: string | null
      trialMode?: string | null
    }
  ) {
    return await apiClient.post<TrialCampaign>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(campaignId)}/attach-task`,
      payload
    )
  },

  async upsertTrialRiskIssue(
    campaignId: string,
    payload: {
      issueId?: string | null
      severity: string
      category: string
      status: string
      owner?: string | null
      sourceRef?: string | null
      resolutionHash?: string | null
    }
  ) {
    return await apiClient.post<TrialCampaign>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(campaignId)}/risks`,
      payload
    )
  },

  async runPilotReadinessEvaluation(campaignId: string) {
    return await apiClient.post<PilotReadinessAssessment>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(campaignId)}/readiness`,
      {}
    )
  },

  async generateTrialEvidencePackage(campaignId: string) {
    return await apiClient.post<TrialEvidencePackage>(
      `/aigateway/trial-operations/campaigns/${encodeURIComponent(campaignId)}/evidence-package`,
      {}
    )
  },

  async getCloudReadonlyPilotReadiness() {
    return await apiClient.get<CloudReadonlyPilotReadinessStatus>(
      '/aigateway/cloud-readonly/readiness/pilot-readiness'
    )
  },

  async createCloudReadonlyPilotConfigPackage(campaignId: string) {
    return await apiClient.post<CloudReadonlyPilotConfigPackage>(
      '/aigateway/cloud-readonly/readiness/pilot-readiness/config-package',
      {
        campaignId
      }
    )
  },

  async runCloudReadonlyPilotGateEvaluation(campaignId: string) {
    return await apiClient.post<CloudReadonlyPilotReadinessStatus>(
      '/aigateway/cloud-readonly/readiness/pilot-readiness/gate',
      {
        campaignId
      }
    )
  },

  async runCloudReadonlyPilotApprovalRehearsal(packageId: string) {
    return await apiClient.post<PilotApprovalRehearsal>(
      '/aigateway/cloud-readonly/readiness/pilot-readiness/approval-rehearsal',
      {
        packageId
      }
    )
  },

  async runCloudReadonlyPilotContractRehearsal(packageId: string) {
    return await apiClient.post<CloudReadonlyPilotContractRehearsal>(
      '/aigateway/cloud-readonly/readiness/pilot-readiness/contract-rehearsal',
      {
        packageId,
        endpointCodes: ['devices', 'capacity_summary', 'device_logs', 'pass_station_records']
      }
    )
  },

  async getCloudReadonlyProductionPilotStatus() {
    return await apiClient.get<CloudReadonlyProductionPilotStatus>(
      '/aigateway/cloud-readonly/readiness/production-pilot'
    )
  },

  async createCloudReadonlyProductionPilotWindow() {
    return await apiClient.post<CloudReadonlyProductionPilotWindow>(
      '/aigateway/cloud-readonly/readiness/production-pilot/window',
      {}
    )
  },

  async approveCloudReadonlyProductionPilotWindow(windowId: string) {
    return await apiClient.post<CloudReadonlyProductionPilotWindow>(
      '/aigateway/cloud-readonly/readiness/production-pilot/window/status',
      {
        windowId,
        status: 'Approved'
      }
    )
  },

  async runCloudReadonlyProductionPilotGate() {
    return await apiClient.post<CloudReadonlyProductionPilotStatus>(
      '/aigateway/cloud-readonly/readiness/production-pilot/gate',
      {}
    )
  },

  async runCloudReadonlyProductionPilotScenario(scenarioId: string) {
    return await apiClient.post<CloudReadonlyProductionPilotScenarioResult>(
      '/aigateway/cloud-readonly/readiness/production-pilot/run',
      {
        scenarioId,
        artifactTypes: ['Markdown', 'Html'],
        maxRows: 20,
        timeoutMs: 5000
      }
    )
  },

  async createCloudSandboxControlledPlan(payload: {
    sessionId: string
    goal: string
    modelId?: string | null
    artifactTypes?: string[]
    timeRange?: CloudSandboxGoalTimeRange | null
    maxRows?: number | null
    plannerMode?: 'Auto' | 'DynamicOnly' | 'StaticOnly'
  }) {
    return await apiClient.post<CloudReadonlySandboxControlledPlan>(
      '/aigateway/agent/cloud-sandbox-controlled-trial/plan',
      payload
    )
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

  async getArtifactPreview(id: string) {
    return await apiClient.get<AgentArtifactPreview>(
      `/aigateway/artifact/${encodeURIComponent(id)}/preview`
    )
  },

  async finalizeWorkspace(code: string) {
    return await apiClient.post<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}/finalize`,
      {}
    )
  },

  async submitFinalReview(code: string) {
    return await apiClient.post<ArtifactWorkspace>(
      `/aigateway/workspace/${encodeURIComponent(code)}/submit-final-review`,
      {}
    )
  },

  async downloadArtifact(downloadUrl: string) {
    return await apiClient.download(downloadUrl)
  }
}
