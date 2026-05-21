import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'
import { processChunk, getErrorCode } from '@/protocol/chunkReducer'
import { getApprovalFailureStatus } from '@/protocol/approvalProtocol'
import { useApprovalStore } from './approvalStore'
import { useChatErrorStore, toFriendlyMessage } from './chatErrorStore'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useStreamStore } from './streamStore'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskAuditSummary,
  AgentTrialScenario,
  ArtifactRecord,
  ArtifactWorkspace,
  CloudReadonlyPilotConfigPackage,
  CloudReadonlyPilotContractRehearsal,
  CloudReadonlyPilotReadinessStatus,
  CloudReadonlyProductionControlledPilotResult,
  CloudReadonlyProductionControlledPilotStatus,
  CloudReadonlyProductionControlledPlan,
  CloudReadonlyProductionPilotScenarioResult,
  CloudReadonlyProductionPilotStatus,
  CloudReadonlyProductionPilotWindow,
  PilotApprovalRehearsal,
  PilotReadinessAssessment,
  TrialCampaign,
  TrialEvidencePackage,
  UploadRecord
} from '@/types/protocols'

interface AgentChartPreview {
  labels: string[]
  values: number[]
  source?: string
  sourceMode?: string
  sourceLabel?: string
  isSimulation?: boolean
  queryHash?: string
}

export const useChatStore = defineStore('chat', () => {
  const selectedModelStorageKey = 'aicopilot.chat.selectedModelId'
  const sessionStore = useSessionStore()
  const messageStore = useMessageStore()
  const streamStore = useStreamStore()
  const approvalStore = useApprovalStore()
  const errorStore = useChatErrorStore()

  const sessions = computed(() => sessionStore.sessions)
  const currentSessionId = computed(() => sessionStore.currentSessionId)
  const currentSession = computed(() => sessionStore.currentSession)
  const currentMessages = computed(() => messageStore.currentMessages)
  const isStreaming = computed(() => streamStore.isStreaming)
  const isWaitingForApproval = computed(() => approvalStore.isWaitingForApproval)
  const isLoadingHistory = computed(() => sessionStore.isLoadingHistory)
  const errorMessage = computed(() => errorStore.errorMessage)
  const chatModels = ref<Awaited<ReturnType<typeof chatService.getSelectableChatModels>>>([])
  const agentTasks = ref<AgentTask[]>([])
  const agentTrialScenarios = ref<AgentTrialScenario[]>([])
  const agentApprovals = ref<AgentApprovalRequest[]>([])
  const agentAuditSummary = ref<AgentTaskAuditSummary[]>([])
  const uploadedFiles = ref<UploadRecord[]>([])
  const currentWorkspace = ref<ArtifactWorkspace | null>(null)
  const currentArtifactPreview = ref<AgentArtifactPreview | null>(null)
  const trialCampaigns = ref<TrialCampaign[]>([])
  const currentTrialCampaign = ref<TrialCampaign | null>(null)
  const currentPilotReadiness = ref<PilotReadinessAssessment | null>(null)
  const currentTrialEvidencePackage = ref<TrialEvidencePackage | null>(null)
  const currentCloudReadonlyPilotReadiness = ref<CloudReadonlyPilotReadinessStatus | null>(null)
  const currentCloudReadonlyPilotConfigPackage = ref<CloudReadonlyPilotConfigPackage | null>(null)
  const currentPilotApprovalRehearsal = ref<PilotApprovalRehearsal | null>(null)
  const currentPilotContractRehearsal = ref<CloudReadonlyPilotContractRehearsal | null>(null)
  const currentCloudReadonlyProductionPilotStatus = ref<CloudReadonlyProductionPilotStatus | null>(null)
  const currentCloudReadonlyProductionPilotWindow = ref<CloudReadonlyProductionPilotWindow | null>(null)
  const currentCloudReadonlyProductionPilotRun = ref<CloudReadonlyProductionPilotScenarioResult | null>(null)
  const currentCloudReadonlyProductionControlledStatus = ref<CloudReadonlyProductionControlledPilotStatus | null>(null)
  const currentCloudReadonlyProductionControlledPlan = ref<CloudReadonlyProductionControlledPlan | null>(null)
  const currentCloudReadonlyProductionControlledRun = ref<CloudReadonlyProductionControlledPilotResult | null>(null)
  const chartPreview = ref<AgentChartPreview | null>(null)
  const isAgentBusy = ref(false)
  const isLoadingAgentTrialScenarios = ref(false)
  const isLoadingTrialOperations = ref(false)
  const agentErrorMessage = ref<string | null>(null)
  const selectedModelId = ref<string | null>(
    typeof window === 'undefined' ? null : window.localStorage.getItem(selectedModelStorageKey)
  )
  const isLoadingChatModels = ref(false)
  const selectedModel = computed(() =>
    chatModels.value.find((model) => model.id === selectedModelId.value) ?? null
  )
  const latestAgentTask = computed(() => agentTasks.value[0] ?? null)
  const pendingAgentApprovals = computed(() =>
    agentApprovals.value.filter((approval) => approval.status === 'Pending')
  )

  async function loadChatModels() {
    isLoadingChatModels.value = true
    try {
      chatModels.value = await chatService.getSelectableChatModels()
      if (!chatModels.value.some((model) => model.id === selectedModelId.value)) {
        setSelectedModel(chatModels.value[0]?.id ?? null)
      }
    } finally {
      isLoadingChatModels.value = false
    }
  }

  function setSelectedModel(modelId: string | null) {
    selectedModelId.value = modelId
    if (typeof window !== 'undefined') {
      if (modelId) {
        window.localStorage.setItem(selectedModelStorageKey, modelId)
      } else {
        window.localStorage.removeItem(selectedModelStorageKey)
      }
    }
  }

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  async function loadHistory(sessionId: string, force = false) {
    if (!force && messageStore.messagesMap[sessionId]?.length) {
      await approvalStore.refreshPendingApprovals(sessionId)
      await loadAgentTasks(sessionId)
      return
    }

    sessionStore.isLoadingHistory = true
    try {
      const history = await chatService.getHistory(sessionId)
      messageStore.setHistory(sessionId, history)
      await approvalStore.refreshPendingApprovals(sessionId)
      await loadAgentTasks(sessionId)
    } finally {
      sessionStore.isLoadingHistory = false
    }
  }

  function upsertAgentTask(task: AgentTask) {
    const index = agentTasks.value.findIndex((item) => item.id === task.id)
    if (index >= 0) {
      agentTasks.value.splice(index, 1, task)
    } else {
      agentTasks.value.unshift(task)
    }
  }

  async function loadAgentTasks(sessionId: string) {
    try {
      agentTasks.value = await chatService.getAgentTasksBySession(sessionId)
      const taskWithWorkspace = agentTasks.value.find((task) => Boolean(task.workspaceCode))
      if (taskWithWorkspace) {
        await refreshWorkspace(taskWithWorkspace)
      } else {
        currentWorkspace.value = null
        currentArtifactPreview.value = null
        chartPreview.value = null
      }
      await loadAgentApprovals(agentTasks.value[0]?.id ?? null)
      await loadAgentAuditSummary(agentTasks.value[0]?.id ?? null)
    } catch {
      agentTasks.value = []
      agentApprovals.value = []
      agentAuditSummary.value = []
      currentWorkspace.value = null
      currentArtifactPreview.value = null
      chartPreview.value = null
    }
  }

  async function loadAgentApprovals(taskId: string | null = null) {
    try {
      agentApprovals.value = taskId
        ? await chatService.getAgentTaskApprovals(taskId)
        : await chatService.getPendingAgentApprovals()
    } catch {
      agentApprovals.value = []
    }
  }

  async function loadAgentAuditSummary(taskId: string | null = null) {
    if (!taskId) {
      agentAuditSummary.value = []
      return
    }

    try {
      agentAuditSummary.value = await chatService.getAgentTaskAuditSummary(taskId)
    } catch {
      agentAuditSummary.value = []
    }
  }

  async function loadAgentTrialScenarios() {
    isLoadingAgentTrialScenarios.value = true
    try {
      agentTrialScenarios.value = await chatService.getAgentTrialScenarios()
    } catch {
      agentTrialScenarios.value = []
    } finally {
      isLoadingAgentTrialScenarios.value = false
    }
  }

  async function loadTrialCampaigns() {
    isLoadingTrialOperations.value = true
    try {
      trialCampaigns.value = await chatService.getTrialCampaigns()
      currentTrialCampaign.value =
        trialCampaigns.value.find((campaign) => campaign.campaignId === currentTrialCampaign.value?.campaignId) ??
        trialCampaigns.value[0] ??
        null
    } catch {
      trialCampaigns.value = []
      currentTrialCampaign.value = null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function createTrialCampaign() {
    if (isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      const campaign = await chatService.createTrialCampaign({
        name: `内部试用 ${new Date().toLocaleDateString('zh-CN')}`,
        allowedSourceModes: ['SimulationBusiness', 'CloudReadonlySandbox'],
        ownerDepartment: 'AI 平台',
        summary: 'P10 内部试用运营台账，保留任务、产物、hash、审批和审计摘要引用。'
      })
      currentTrialCampaign.value = campaign
      currentPilotReadiness.value = null
      currentTrialEvidencePackage.value = null
      await loadTrialCampaigns()
      return campaign
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function attachLatestTaskToTrialCampaign() {
    const campaign = currentTrialCampaign.value ?? trialCampaigns.value[0] ?? (await createTrialCampaign())
    const task = latestAgentTask.value
    if (!campaign || !task || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentTrialCampaign.value = await chatService.attachAgentTaskToTrialCampaign(campaign.campaignId, {
        taskId: task.id,
        scenarioId: task.taskCode,
        trialMode: latestAgentTask.value?.planJson?.includes('CloudReadonlySandbox')
          ? 'CloudReadonlySandbox'
          : 'AgentTaskEvidence'
      })
      await loadTrialCampaigns()
      return currentTrialCampaign.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runPilotReadinessEvaluation() {
    const campaign = currentTrialCampaign.value
    if (!campaign || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentPilotReadiness.value = await chatService.runPilotReadinessEvaluation(campaign.campaignId)
      currentTrialCampaign.value = await chatService.getTrialCampaignDetail(campaign.campaignId)
      await loadTrialCampaigns()
      return currentPilotReadiness.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function generateTrialEvidencePackage() {
    const campaign = currentTrialCampaign.value
    if (!campaign || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentTrialEvidencePackage.value = await chatService.generateTrialEvidencePackage(campaign.campaignId)
      currentTrialCampaign.value = await chatService.getTrialCampaignDetail(campaign.campaignId)
      await loadTrialCampaigns()
      return currentTrialEvidencePackage.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function loadCloudReadonlyPilotReadiness() {
    try {
      currentCloudReadonlyPilotReadiness.value = await chatService.getCloudReadonlyPilotReadiness()
      currentCloudReadonlyPilotConfigPackage.value =
        currentCloudReadonlyPilotReadiness.value.configSummary ?? currentCloudReadonlyPilotConfigPackage.value
      return currentCloudReadonlyPilotReadiness.value
    } catch {
      currentCloudReadonlyPilotReadiness.value = null
      return null
    }
  }

  async function createCloudReadonlyPilotConfigPackage() {
    const campaign = currentTrialCampaign.value
    if (!campaign || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyPilotConfigPackage.value =
        await chatService.createCloudReadonlyPilotConfigPackage(campaign.campaignId)
      await loadCloudReadonlyPilotReadiness()
      return currentCloudReadonlyPilotConfigPackage.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runCloudReadonlyPilotGateEvaluation() {
    const campaign = currentTrialCampaign.value
    if (!campaign || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyPilotReadiness.value =
        await chatService.runCloudReadonlyPilotGateEvaluation(campaign.campaignId)
      currentCloudReadonlyPilotConfigPackage.value =
        currentCloudReadonlyPilotReadiness.value.configSummary ?? currentCloudReadonlyPilotConfigPackage.value
      return currentCloudReadonlyPilotReadiness.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runCloudReadonlyPilotApprovalRehearsal() {
    const packageId =
      currentCloudReadonlyPilotConfigPackage.value?.packageId ??
      currentCloudReadonlyPilotReadiness.value?.configSummary?.packageId
    if (!packageId || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentPilotApprovalRehearsal.value =
        await chatService.runCloudReadonlyPilotApprovalRehearsal(packageId)
      await loadCloudReadonlyPilotReadiness()
      return currentPilotApprovalRehearsal.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runCloudReadonlyPilotContractRehearsal() {
    const packageId =
      currentCloudReadonlyPilotConfigPackage.value?.packageId ??
      currentCloudReadonlyPilotReadiness.value?.configSummary?.packageId
    if (!packageId || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentPilotContractRehearsal.value =
        await chatService.runCloudReadonlyPilotContractRehearsal(packageId)
      await loadCloudReadonlyPilotReadiness()
      return currentPilotContractRehearsal.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function loadCloudReadonlyProductionPilotStatus() {
    try {
      currentCloudReadonlyProductionPilotStatus.value =
        await chatService.getCloudReadonlyProductionPilotStatus()
      return currentCloudReadonlyProductionPilotStatus.value
    } catch {
      currentCloudReadonlyProductionPilotStatus.value = null
      return null
    }
  }

  async function createCloudReadonlyProductionPilotWindow() {
    if (isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionPilotWindow.value =
        await chatService.createCloudReadonlyProductionPilotWindow()
      await loadCloudReadonlyProductionPilotStatus()
      return currentCloudReadonlyProductionPilotWindow.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function approveCloudReadonlyProductionPilotWindow() {
    const windowId =
      currentCloudReadonlyProductionPilotWindow.value?.windowId ??
      currentCloudReadonlyProductionPilotStatus.value?.pilotWindowId
    if (!windowId || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionPilotWindow.value =
        await chatService.approveCloudReadonlyProductionPilotWindow(windowId)
      await loadCloudReadonlyProductionPilotStatus()
      return currentCloudReadonlyProductionPilotWindow.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runCloudReadonlyProductionPilotGate() {
    if (isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionPilotStatus.value =
        await chatService.runCloudReadonlyProductionPilotGate()
      return currentCloudReadonlyProductionPilotStatus.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function runCloudReadonlyProductionPilotScenario(scenarioId = 'cloud-production-pilot-devices') {
    if (isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionPilotRun.value =
        await chatService.runCloudReadonlyProductionPilotScenario(scenarioId)
      await loadCloudReadonlyProductionPilotStatus()
      return currentCloudReadonlyProductionPilotRun.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function loadCloudReadonlyProductionControlledPilotStatus() {
    try {
      currentCloudReadonlyProductionControlledStatus.value =
        await chatService.getCloudReadonlyProductionControlledPilotStatus()
      return currentCloudReadonlyProductionControlledStatus.value
    } catch {
      currentCloudReadonlyProductionControlledStatus.value = null
      return null
    }
  }

  async function createCloudReadonlyProductionControlledPlan(goal: string) {
    if (isAgentBusy.value || isLoadingTrialOperations.value) {
      return null
    }

    const sessionId = currentSessionId.value
    if (!sessionId) {
      agentErrorMessage.value = '璇峰厛鍒涘缓鎴栭€夋嫨浼氳瘽'
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionControlledPlan.value =
        await chatService.createCloudReadonlyProductionControlledPlan({
          sessionId,
          goal,
          artifactTypes: ['Markdown', 'Html'],
          maxRows: 20,
          plannerMode: 'StaticOnly'
        })
      agentTasks.value = [currentCloudReadonlyProductionControlledPlan.value.task, ...agentTasks.value.filter((item) => item.id !== currentCloudReadonlyProductionControlledPlan.value?.task.id)]
      await loadCloudReadonlyProductionControlledPilotStatus()
      return currentCloudReadonlyProductionControlledPlan.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function runCloudReadonlyProductionControlledPilot() {
    const intentId =
      currentCloudReadonlyProductionControlledPlan.value?.intent.intentId ??
      currentCloudReadonlyProductionControlledRun.value?.intentId
    if (!intentId || isLoadingTrialOperations.value) {
      return null
    }

    isLoadingTrialOperations.value = true
    agentErrorMessage.value = null
    try {
      currentCloudReadonlyProductionControlledRun.value =
        await chatService.runCloudReadonlyProductionControlledPilot(intentId)
      await loadCloudReadonlyProductionControlledPilotStatus()
      return currentCloudReadonlyProductionControlledRun.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isLoadingTrialOperations.value = false
    }
  }

  async function refreshWorkspace(task: AgentTask) {
    if (!task.workspaceCode) {
      currentWorkspace.value = null
      currentArtifactPreview.value = null
      chartPreview.value = null
      return
    }

    currentWorkspace.value = await chatService.getWorkspace(task.workspaceCode)
    const firstArtifact = currentWorkspace.value.artifacts[0]
    currentArtifactPreview.value = firstArtifact
      ? await loadArtifactPreview(firstArtifact.id)
      : null
    await refreshChartPreview()
  }

  async function loadArtifactPreview(artifactId: string) {
    try {
      currentArtifactPreview.value = await chatService.getArtifactPreview(artifactId)
      return currentArtifactPreview.value
    } catch {
      currentArtifactPreview.value = null
      return null
    }
  }

  async function refreshChartPreview() {
    const chartArtifact = currentWorkspace.value?.artifacts.find((artifact) => artifact.previewKind === 'chart')
    if (!chartArtifact) {
      chartPreview.value = null
      return
    }

    try {
      const blob = await chatService.downloadArtifact(chartArtifact.downloadUrl)
      const payload = JSON.parse(await blob.text()) as {
        labels?: unknown
        values?: unknown
        source?: unknown
        sourceMode?: unknown
        sourceLabel?: unknown
        isSimulation?: unknown
        queryHash?: unknown
        sourceInfo?: {
          sourceMode?: unknown
          sourceLabel?: unknown
          isSimulation?: unknown
          queryHash?: unknown
        }
      }
      const sourceInfo = payload.sourceInfo
      chartPreview.value = {
        labels: Array.isArray(payload.labels) ? payload.labels.map(String) : [],
        values: Array.isArray(payload.values) ? payload.values.map((value) => Number(value) || 0) : [],
        source: typeof payload.source === 'string' ? payload.source : undefined,
        sourceMode:
          typeof payload.sourceMode === 'string'
            ? payload.sourceMode
            : typeof sourceInfo?.sourceMode === 'string'
              ? sourceInfo.sourceMode
              : undefined,
        sourceLabel:
          typeof payload.sourceLabel === 'string'
            ? payload.sourceLabel
            : typeof sourceInfo?.sourceLabel === 'string'
              ? sourceInfo.sourceLabel
              : undefined,
        isSimulation:
          typeof payload.isSimulation === 'boolean'
            ? payload.isSimulation
            : typeof sourceInfo?.isSimulation === 'boolean'
              ? sourceInfo.isSimulation
              : undefined,
        queryHash:
          typeof payload.queryHash === 'string'
            ? payload.queryHash
            : typeof sourceInfo?.queryHash === 'string'
              ? sourceInfo.queryHash
              : undefined
      }
    } catch {
      chartPreview.value = null
    }
  }

  async function downloadArtifact(artifact: ArtifactRecord) {
    if (!artifact.downloadUrl) {
      agentErrorMessage.value = '后端未返回产物下载地址，前端不会自行拼接下载路径。'
      return
    }

    const blob = await chatService.downloadArtifact(artifact.downloadUrl)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = artifact.name
    anchor.click()
    URL.revokeObjectURL(url)
  }

  async function uploadSessionFile(file: File) {
    if (!sessionStore.currentSessionId) {
      return null
    }

    agentErrorMessage.value = null
    const uploaded = await chatService.uploadFile('SessionTemp', file, {
      sessionId: sessionStore.currentSessionId
    })
    uploadedFiles.value.unshift(uploaded)
    return uploaded
  }

  async function planAgentTask(goal: string) {
    if (!sessionStore.currentSessionId || !selectedModelId.value || isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const task = await chatService.planAgentTask({
        sessionId: sessionStore.currentSessionId,
        goal,
        taskType: 'ReportGeneration',
        modelId: selectedModelId.value,
        uploadIds: uploadedFiles.value.map((item) => item.id),
        knowledgeBaseIds: [],
        plannerMode: 'Auto'
      })
      upsertAgentTask(task)
      await loadAgentApprovals(task.id)
      await loadAgentAuditSummary(task.id)
      await refreshWorkspace(task)
      return task
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function createAgentTaskFromTrialScenario(
    scenario: AgentTrialScenario,
    promptOverride?: string | null,
    artifactTypes?: string[]
  ) {
    if (!sessionStore.currentSessionId || isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const task = await chatService.createAgentTaskFromTrialScenario({
        sessionId: sessionStore.currentSessionId,
        scenarioId: scenario.id,
        promptOverride: promptOverride?.trim() || null,
        artifactTypes: artifactTypes?.length ? artifactTypes : scenario.defaultArtifactTypes,
        dataSourceIds: scenario.defaultDataSourceIds,
        plannerMode: 'Auto'
      })
      upsertAgentTask(task)
      await loadAgentApprovals(task.id)
      await loadAgentAuditSummary(task.id)
      await refreshWorkspace(task)
      return task
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function createCloudSandboxControlledPlan(goal: string, artifactTypes: string[] = ['Markdown', 'Html']) {
    if (!sessionStore.currentSessionId || isAgentBusy.value) {
      return null
    }

    const normalizedGoal = goal.trim()
    if (!normalizedGoal) {
      agentErrorMessage.value = '请输入 Cloud Sandbox Controlled Trial 目标'
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const result = await chatService.createCloudSandboxControlledPlan({
        sessionId: sessionStore.currentSessionId,
        goal: normalizedGoal,
        modelId: selectedModelId.value,
        artifactTypes,
        maxRows: 20,
        plannerMode: 'Auto'
      })
      upsertAgentTask(result.task)
      await loadAgentApprovals(result.task.id)
      await loadAgentAuditSummary(result.task.id)
      await refreshWorkspace(result.task)
      return result
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function submitFinalReview(code: string) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      currentWorkspace.value = await chatService.submitFinalReview(code)
      await refreshChartPreview()
      if (sessionStore.currentSessionId) {
        await loadAgentTasks(sessionStore.currentSessionId)
      }
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function runAgentTask(taskId: string) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const updated = await chatService.runAgentTask(taskId)
      upsertAgentTask(updated)
      await loadAgentApprovals(taskId)
      await loadAgentAuditSummary(taskId)
      await refreshWorkspace(updated)
      return updated
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function decideAgentApproval(
    approval: AgentApprovalRequest,
    decision: 'approve' | 'reject',
    comment?: string | null
  ) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const decided = await chatService.decideAgentApproval(approval.id, decision, comment)
      await loadAgentApprovals(approval.taskId)
      if (sessionStore.currentSessionId) {
        await loadAgentTasks(sessionStore.currentSessionId)
      }
      await loadAgentAuditSummary(approval.taskId)

      return decided
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function finalizeWorkspace(code: string) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      currentWorkspace.value = await chatService.finalizeWorkspace(code)
      await refreshChartPreview()
      if (sessionStore.currentSessionId) {
        await loadAgentTasks(sessionStore.currentSessionId)
      }
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function initialize() {
    errorStore.clearSessionError()
    await Promise.all([
      sessionStore.loadSessions(),
      loadChatModels(),
      loadAgentTrialScenarios(),
      loadTrialCampaigns(),
      loadCloudReadonlyPilotReadiness(),
      loadCloudReadonlyProductionPilotStatus(),
      loadCloudReadonlyProductionControlledPilotStatus()
    ])

    if (sessionStore.sessions.length === 0) {
      await createNewSession()
      return
    }

    const restoredSessionId = sessionStore.currentSessionId
    const initialSession =
      sessionStore.sessions.find((session) => session.id === restoredSessionId) ??
      sessionStore.sessions[0] ??
      null

    if (!initialSession) {
      sessionStore.persistCurrentSession(null)
      bindErrorSession()
      return
    }

    await selectSession(initialSession.id)
  }

  async function createNewSession() {
    errorStore.clearSessionError()
    const newSession = await sessionStore.createSession()
    messageStore.messagesMap[newSession.id] = []
    streamStore.stop()
    approvalStore.sync(newSession.id)
    bindErrorSession()
    return newSession
  }

  async function selectSession(id: string, forceReload = false) {
    sessionStore.persistCurrentSession(id)
    bindErrorSession()
    errorStore.clearSessionError(id)
    approvalStore.sync(id)
    await loadHistory(id, forceReload)
  }

  async function confirmOnsitePresence(expiresInMinutes = 30) {
    if (!sessionStore.currentSessionId) {
      return
    }

    errorStore.clearSessionError(sessionStore.currentSessionId)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      sessionStore.currentSessionId,
      true,
      expiresInMinutes
    )
    sessionStore.upsertSession(updatedSession)
    await approvalStore.refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function clearOnsitePresence() {
    if (!sessionStore.currentSessionId) {
      return
    }

    errorStore.clearSessionError(sessionStore.currentSessionId)
    const updatedSession = await chatService.updateSessionSafetyAttestation(
      sessionStore.currentSessionId,
      false
    )
    sessionStore.upsertSession(updatedSession)
    await approvalStore.refreshPendingApprovals(updatedSession.id)
    return updatedSession
  }

  async function sendMessage(input: string) {
    if (!sessionStore.currentSessionId || streamStore.isStreaming || approvalStore.isWaitingForApproval) {
      return
    }

    const sessionId = sessionStore.currentSessionId
    errorStore.clearSessionError(sessionId)
    if (!selectedModelId.value) {
      errorStore.setSessionError(sessionId, '当前没有可用的对话模型，请联系管理员启用 Chat 模型。')
      return
    }

    const userMessage = messageStore.addMessage(sessionId, {
      sessionId,
      role: MessageRole.User,
      chunks: [
        {
          source: 'User',
          type: ChunkType.Text,
          content: input
        }
      ],
      isStreaming: false,
      timestamp: Date.now()
    })

    const assistantMessage = messageStore.addMessage(sessionId, {
      sessionId,
      role: MessageRole.Assistant,
      finalModelId: selectedModelId.value,
      finalModelName: '未知',
      routingModelId: null,
      routingModelName: null,
      contextWindowTokens: null,
      maxOutputTokens: null,
      chunks: [],
      isStreaming: true,
      timestamp: Date.now()
    })

    streamStore.start()
    let streamErrorCode: string | null = null

    try {
      await chatService.sendMessageStream(sessionId, input, selectedModelId.value, {
        onChunkReceived(chunk) {
          if (chunk.type === ChunkType.Error) {
            streamErrorCode = getErrorCode(chunk)
          }

          processChunk(assistantMessage, chunk, {
            setSessionError: errorStore.setSessionError,
            onApprovalChunk: approvalStore.sync
          })
        },
        onComplete() {
          streamStore.stop()
          assistantMessage.isStreaming = false
          if (streamErrorCode === 'approval_pending') {
            messageStore.removeMessages(sessionId, userMessage, assistantMessage)
            void approvalStore.refreshPendingApprovals(sessionId)
          }

          approvalStore.sync(sessionId)
        },
        onError(error) {
          streamStore.stop()
          assistantMessage.isStreaming = false
          errorStore.setSessionError(sessionId, toFriendlyMessage(error))
          approvalStore.sync(sessionId)
        }
      })
    } catch (error) {
      errorStore.setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      streamStore.stop()
      assistantMessage.isStreaming = false
      approvalStore.sync(sessionId)
    }
  }

  async function submitApproval(
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    chunk: ApprovalChunk
  ) {
    if (!sessionStore.currentSessionId || streamStore.isStreaming) {
      return false
    }

    const sessionId = sessionStore.currentSessionId
    let approvalFailed = false
    let approvalErrorCode: string | null = null
    errorStore.clearSessionError(sessionId)
    streamStore.start()

    let targetMessage = messageStore.getLastAssistantMessage(sessionId)
    if (!targetMessage) {
      targetMessage = messageStore.addMessage(sessionId, {
        sessionId,
        role: MessageRole.Assistant,
        finalModelId: null,
        finalModelName: '未知',
        routingModelId: null,
        routingModelName: null,
        contextWindowTokens: null,
        maxOutputTokens: null,
        chunks: [],
        isStreaming: true,
        timestamp: Date.now()
      })
    } else {
      targetMessage.isStreaming = true
    }

    try {
      await chatService.sendApprovalDecisionStream(sessionId, callId, decision, onsiteConfirmed, chunk.request, {
        onChunkReceived(incomingChunk) {
          if (incomingChunk.type === ChunkType.Error) {
            approvalFailed = true
            approvalErrorCode = getErrorCode(incomingChunk)
          }

          processChunk(targetMessage!, incomingChunk, {
            setSessionError: errorStore.setSessionError,
            onApprovalChunk: approvalStore.sync
          })
        },
        onComplete() {
          streamStore.stop()
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = approvalFailed ? getApprovalFailureStatus(approvalErrorCode) : decision
          approvalStore.sync(sessionId)
        },
        onError(error) {
          approvalFailed = true
          streamStore.stop()
          if (targetMessage) {
            targetMessage.isStreaming = false
          }

          chunk.status = 'pending'
          errorStore.setSessionError(sessionId, toFriendlyMessage(error))
          approvalStore.sync(sessionId)
        }
      })
    } catch (error) {
      approvalFailed = true
      chunk.status = 'pending'
      errorStore.setSessionError(sessionId, toFriendlyMessage(error))
    } finally {
      streamStore.stop()
      if (targetMessage) {
        targetMessage.isStreaming = false
      }

      if (approvalFailed) {
        await approvalStore.refreshPendingApprovals(sessionId)
      }

      approvalStore.sync(sessionId)
    }

    return !approvalFailed
  }

  function reset() {
    sessionStore.reset()
    messageStore.reset()
    streamStore.reset()
    approvalStore.reset()
    errorStore.reset()
    chatModels.value = []
    agentTasks.value = []
    agentTrialScenarios.value = []
    agentApprovals.value = []
    agentAuditSummary.value = []
    uploadedFiles.value = []
    currentWorkspace.value = null
    currentArtifactPreview.value = null
    trialCampaigns.value = []
    currentTrialCampaign.value = null
    currentPilotReadiness.value = null
    currentTrialEvidencePackage.value = null
    currentCloudReadonlyPilotReadiness.value = null
    currentCloudReadonlyPilotConfigPackage.value = null
    currentPilotApprovalRehearsal.value = null
    currentPilotContractRehearsal.value = null
    currentCloudReadonlyProductionPilotStatus.value = null
    currentCloudReadonlyProductionPilotWindow.value = null
    currentCloudReadonlyProductionPilotRun.value = null
    currentCloudReadonlyProductionControlledStatus.value = null
    currentCloudReadonlyProductionControlledPlan.value = null
    currentCloudReadonlyProductionControlledRun.value = null
    chartPreview.value = null
    agentErrorMessage.value = null
    isAgentBusy.value = false
    isLoadingAgentTrialScenarios.value = false
    isLoadingTrialOperations.value = false
    selectedModelId.value = null
    isLoadingChatModels.value = false
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    currentMessages,
    isStreaming,
    isWaitingForApproval,
    isLoadingHistory,
    chatModels,
    selectedModelId,
    selectedModel,
    isLoadingChatModels,
    agentTasks,
    agentTrialScenarios,
    isLoadingAgentTrialScenarios,
    latestAgentTask,
    agentApprovals,
    pendingAgentApprovals,
    agentAuditSummary,
    uploadedFiles,
    currentWorkspace,
    currentArtifactPreview,
    trialCampaigns,
    currentTrialCampaign,
    currentPilotReadiness,
    currentTrialEvidencePackage,
    currentCloudReadonlyPilotReadiness,
    currentCloudReadonlyPilotConfigPackage,
    currentPilotApprovalRehearsal,
    currentPilotContractRehearsal,
    currentCloudReadonlyProductionPilotStatus,
    currentCloudReadonlyProductionPilotWindow,
    currentCloudReadonlyProductionPilotRun,
    currentCloudReadonlyProductionControlledStatus,
    currentCloudReadonlyProductionControlledPlan,
    currentCloudReadonlyProductionControlledRun,
    chartPreview,
    isAgentBusy,
    agentErrorMessage,
    isLoadingTrialOperations,
    errorMessage,
    initialize,
    loadChatModels,
    setSelectedModel,
    createNewSession,
    selectSession,
    confirmOnsitePresence,
    clearOnsitePresence,
    uploadSessionFile,
    planAgentTask,
    loadAgentTrialScenarios,
    loadTrialCampaigns,
    createTrialCampaign,
    attachLatestTaskToTrialCampaign,
    runPilotReadinessEvaluation,
    generateTrialEvidencePackage,
    loadCloudReadonlyPilotReadiness,
    createCloudReadonlyPilotConfigPackage,
    runCloudReadonlyPilotGateEvaluation,
    runCloudReadonlyPilotApprovalRehearsal,
    runCloudReadonlyPilotContractRehearsal,
    loadCloudReadonlyProductionPilotStatus,
    createCloudReadonlyProductionPilotWindow,
    approveCloudReadonlyProductionPilotWindow,
    runCloudReadonlyProductionPilotGate,
    runCloudReadonlyProductionPilotScenario,
    loadCloudReadonlyProductionControlledPilotStatus,
    createCloudReadonlyProductionControlledPlan,
    runCloudReadonlyProductionControlledPilot,
    createAgentTaskFromTrialScenario,
    createCloudSandboxControlledPlan,
    runAgentTask,
    decideAgentApproval,
    loadAgentAuditSummary,
    loadArtifactPreview,
    submitFinalReview,
    finalizeWorkspace,
    downloadArtifact,
    sendMessage,
    submitApproval,
    reset
  }
})
