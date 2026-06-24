import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage } from '@/types/models'
import { processChunk, getErrorCode } from '@/protocol/chunkReducer'
import { getApprovalFailureStatus } from '@/protocol/approvalProtocol'
import { useApprovalStore } from './approvalStore'
import { resolveChatErrorMessage, useChatErrorStore, toFriendlyMessage } from './chatErrorStore'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useStreamStore } from './streamStore'
import type {
  AgentPlannerToolSummary,
  KnowledgeBaseSummary,
  SessionTimelineEvent,
  SkillDefinition
} from '@/types/app'
import type {
  AgentApprovalRequest,
  AgentArtifactPreview,
  AgentTask,
  AgentTaskAuditSummary,
  ArtifactRecord,
  ArtifactWorkspace,
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

interface HistoryCursorState {
  beforeSequence: number | null
  afterSequence: number | null
  hasMoreBefore: boolean
  hasMoreAfter: boolean
}

export const useChatStore = defineStore('chat', () => {
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
  const agentTasks = ref<AgentTask[]>([])
  const agentApprovals = ref<AgentApprovalRequest[]>([])
  const agentAuditSummary = ref<AgentTaskAuditSummary[]>([])
  const timelineEvents = ref<SessionTimelineEvent[]>([])
  const availableSkills = ref<SkillDefinition[]>([])
  const selectedSkillCode = ref<string | null>(null)
  const availablePluginTools = ref<AgentPlannerToolSummary[]>([])
  const selectedToolCodes = ref<string[]>([])
  const isLoadingPluginTools = ref(false)
  const availableKnowledgeBases = ref<KnowledgeBaseSummary[]>([])
  const selectedKnowledgeBaseId = ref<string | null>(null)
  const uploadedFiles = ref<UploadRecord[]>([])
  const currentWorkspace = ref<ArtifactWorkspace | null>(null)
  const currentArtifactPreview = ref<AgentArtifactPreview | null>(null)
  const chartPreview = ref<AgentChartPreview | null>(null)
  const isAgentBusy = ref(false)
  const agentErrorMessage = ref<string | null>(null)
  const historyCursors = ref<Record<string, HistoryCursorState>>({})
  const isLoadingOlderHistory = ref(false)
  const latestAgentTask = computed(() => agentTasks.value[0] ?? null)
  const selectedSkill = computed(() =>
    selectedSkillCode.value
      ? availableSkills.value.find((skill) => skill.skillCode === selectedSkillCode.value) ?? null
      : null
  )
  const selectedPluginTools = computed(() =>
    availablePluginTools.value.filter((tool) => selectedToolCodes.value.includes(tool.toolCode))
  )
  const isSkillAutoMode = computed(() => !selectedSkillCode.value)
  const selectedSkillSupportsKnowledge = computed(() =>
    isSkillAutoMode.value ||
    selectedSkill.value?.allowedKnowledgeScopes?.some((scope) => scope === 'SelectedKnowledgeBase') ||
    false
  )
  const selectedKnowledgeBase = computed(() =>
    availableKnowledgeBases.value.find((knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value) ?? null
  )
  const selectedKnowledgeBaseIdsForPlan = computed(() =>
    selectedSkillSupportsKnowledge.value && selectedKnowledgeBaseId.value ? [selectedKnowledgeBaseId.value] : []
  )
  const pendingAgentApprovals = computed(() =>
    agentApprovals.value.filter((approval) => approval.status === 'Pending')
  )
  const hasMoreHistoryBefore = computed(() => {
    const sessionId = sessionStore.currentSessionId
    return Boolean(sessionId && historyCursors.value[sessionId]?.hasMoreBefore)
  })

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  async function loadSkills() {
    try {
      availableSkills.value = await chatService.getSkills()
      if (selectedSkillCode.value && !availableSkills.value.some((skill) => skill.skillCode === selectedSkillCode.value)) {
        selectedSkillCode.value = null
      }
    } catch {
      availableSkills.value = []
      selectedSkillCode.value = null
    }
  }

  function selectSkill(skillCode: string | null) {
    if (!skillCode) {
      selectedSkillCode.value = null
      void loadPluginTools()
      return
    }

    selectedSkillCode.value = availableSkills.value.some((skill) => skill.skillCode === skillCode)
      ? skillCode
      : null
    void loadPluginTools()
  }

  function addPlanConversationMessages(sessionId: string, goal: string) {
    messageStore.addMessage(sessionId, {
      sessionId,
      role: MessageRole.User,
      chunks: [
        {
          source: 'User',
          type: ChunkType.Text,
          content: goal
        }
      ],
      isStreaming: false,
      timestamp: Date.now()
    })

    return messageStore.addMessage(sessionId, {
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
  }

  function appendPlanStreamError(message: ChatMessage, content: string) {
    message.chunks.push({
      source: 'PlanAgentTaskStream',
      type: ChunkType.Text,
      content
    })
  }

  async function loadPluginTools() {
    isLoadingPluginTools.value = true
    try {
      const catalog = await chatService.getToolCatalog(selectedSkillCode.value)
      availablePluginTools.value = catalog.tools
      const availableCodes = new Set(catalog.tools.map((tool) => tool.toolCode))
      selectedToolCodes.value = selectedToolCodes.value.filter((code) => availableCodes.has(code))
    } catch {
      availablePluginTools.value = []
      selectedToolCodes.value = []
    } finally {
      isLoadingPluginTools.value = false
    }
  }

  function togglePluginTool(toolCode: string) {
    if (!availablePluginTools.value.some((tool) => tool.toolCode === toolCode)) {
      return
    }

    selectedToolCodes.value = selectedToolCodes.value.includes(toolCode)
      ? selectedToolCodes.value.filter((code) => code !== toolCode)
      : [...selectedToolCodes.value, toolCode]
  }

  function clearPluginTools() {
    selectedToolCodes.value = []
  }

  async function loadKnowledgeBases() {
    try {
      availableKnowledgeBases.value = await chatService.getKnowledgeBases()
      if (
        !selectedKnowledgeBaseId.value ||
        !availableKnowledgeBases.value.some((knowledgeBase) => knowledgeBase.id === selectedKnowledgeBaseId.value)
      ) {
        selectedKnowledgeBaseId.value = null
      }
    } catch {
      availableKnowledgeBases.value = []
      selectedKnowledgeBaseId.value = null
    }
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    if (!knowledgeBaseId) {
      selectedKnowledgeBaseId.value = null
      return
    }

    selectedKnowledgeBaseId.value = availableKnowledgeBases.value.some(
      (knowledgeBase) => knowledgeBase.id === knowledgeBaseId
    )
      ? knowledgeBaseId
      : null
  }

  async function loadHistory(sessionId: string, force = false) {
    if (!force && messageStore.messagesMap[sessionId]?.length) {
      await approvalStore.refreshPendingApprovals(sessionId)
      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      return
    }

    sessionStore.isLoadingHistory = true
    try {
      const history = await chatService.getHistory(sessionId)
      messageStore.setHistory(sessionId, history.items)
      updateHistoryCursor(sessionId, history)
      await approvalStore.refreshPendingApprovals(sessionId)
      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
    } finally {
      sessionStore.isLoadingHistory = false
    }
  }

  async function loadOlderHistory(sessionId = sessionStore.currentSessionId) {
    if (!sessionId || isLoadingOlderHistory.value) {
      return false
    }

    const cursor = historyCursors.value[sessionId]
    if (!cursor?.hasMoreBefore || !cursor.beforeSequence) {
      return false
    }

    isLoadingOlderHistory.value = true
    try {
      const history = await chatService.getHistory(sessionId, {
        beforeSequence: cursor.beforeSequence
      })
      messageStore.prependHistory(sessionId, history.items)
      updateHistoryCursor(sessionId, history)
      return history.items.length > 0
    } finally {
      isLoadingOlderHistory.value = false
    }
  }

  function updateHistoryCursor(
    sessionId: string,
    page: {
      beforeSequence?: number | null
      afterSequence?: number | null
      hasMoreBefore?: boolean
      hasMoreAfter?: boolean
    }
  ) {
    historyCursors.value[sessionId] = {
      beforeSequence: page.beforeSequence ?? null,
      afterSequence: page.afterSequence ?? null,
      hasMoreBefore: Boolean(page.hasMoreBefore),
      hasMoreAfter: Boolean(page.hasMoreAfter)
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
      const latestTask = agentTasks.value[0] ?? null
      if (latestTask?.workspaceCode) {
        await refreshWorkspace(latestTask)
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

  async function loadTimeline(sessionId: string) {
    try {
      const timeline = await chatService.getTimeline(sessionId)
      timelineEvents.value = timeline.items
    } catch {
      timelineEvents.value = []
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

  async function refreshAgentTaskSnapshot(taskId: string) {
    if (sessionStore.currentSessionId) {
      await loadAgentTasks(sessionStore.currentSessionId)
      await loadTimeline(sessionStore.currentSessionId)
    } else {
      await loadAgentApprovals(taskId)
      await loadAgentAuditSummary(taskId)
    }
  }

  function findPendingPlanApproval(taskId: string) {
    return agentApprovals.value.find((approval) =>
      approval.taskId === taskId &&
      approval.type === 'Plan' &&
      approval.status === 'Pending'
    ) ?? null
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
    if (!sessionStore.currentSessionId || isAgentBusy.value || streamStore.isStreaming) {
      return null
    }

    const sessionId = sessionStore.currentSessionId
    const assistantMessage = addPlanConversationMessages(sessionId, goal)
    isAgentBusy.value = true
    agentErrorMessage.value = null
    streamStore.start()
    let plannedTask: AgentTask | null = null
    let streamErrorMessage: string | null = null
    try {
      await chatService.planAgentTaskStream({
        sessionId,
        goal,
        taskType: 'ReportGeneration',
        uploadIds: uploadedFiles.value.map((item) => item.id),
        knowledgeBaseIds: selectedKnowledgeBaseIdsForPlan.value,
        plannerMode: 'Auto',
        skillCode: selectedSkillCode.value || null,
        preferredToolCodes: selectedToolCodes.value
      }, {
        onChunkReceived(chunk) {
          if (chunk.type === ChunkType.Error) {
            try {
              streamErrorMessage = resolveChatErrorMessage(JSON.parse(chunk.content))
            } catch {
              streamErrorMessage = '请求失败，请稍后重试。'
            }
          }

          processChunk(assistantMessage, chunk, {
            setSessionError: errorStore.setSessionError,
            onApprovalChunk: approvalStore.sync,
            onAgentTaskChunk: (_sessionId, task) => {
              plannedTask = task
              upsertAgentTask(task)
            }
          })
        },
        onComplete() {
          streamStore.stop()
          assistantMessage.isStreaming = false
          approvalStore.sync(sessionId)
        },
        onError(error) {
          streamStore.stop()
          assistantMessage.isStreaming = false
          streamErrorMessage = toFriendlyMessage(error)
          agentErrorMessage.value = streamErrorMessage
          appendPlanStreamError(assistantMessage, streamErrorMessage)
          approvalStore.sync(sessionId)
        }
      })

      const completedTask = plannedTask as AgentTask | null
      if (!completedTask) {
        if (streamErrorMessage) {
          agentErrorMessage.value = streamErrorMessage
        }
        return null
      }

      await loadAgentApprovals(completedTask.id)
      await loadAgentAuditSummary(completedTask.id)
      await refreshWorkspace(completedTask)
      await loadTimeline(sessionId)
      return completedTask
    } catch (error) {
      const message = toFriendlyMessage(error)
      agentErrorMessage.value = message
      appendPlanStreamError(assistantMessage, message)
      return null
    } finally {
      streamStore.stop()
      assistantMessage.isStreaming = false
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
        await loadTimeline(sessionStore.currentSessionId)
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
      if (sessionStore.currentSessionId) {
        await loadTimeline(sessionStore.currentSessionId)
      }
      return updated
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      await refreshAgentTaskSnapshot(taskId)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function retryAgentTask(taskId: string) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    try {
      const updated = await chatService.retryAgentTask(taskId)
      upsertAgentTask(updated)
      await loadAgentApprovals(taskId)
      await loadAgentAuditSummary(taskId)
      await refreshWorkspace(updated)
      if (sessionStore.currentSessionId) {
        await loadTimeline(sessionStore.currentSessionId)
      }
      return updated
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      await refreshAgentTaskSnapshot(taskId)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function approveAndRunAgentTask(taskId: string) {
    if (isAgentBusy.value) {
      return null
    }

    isAgentBusy.value = true
    agentErrorMessage.value = null
    let planApproved = false
    try {
      const pendingPlanApproval = findPendingPlanApproval(taskId)
      if (pendingPlanApproval) {
        await chatService.decideAgentApproval(
          pendingPlanApproval.id,
          'approve',
          'Approved from primary plan CTA'
        )
      } else {
        const approved = await chatService.approveAgentTaskPlan(taskId)
        upsertAgentTask(approved)
      }

      planApproved = true
      const updated = await chatService.runAgentTask(taskId)
      upsertAgentTask(updated)
      await loadAgentApprovals(taskId)
      await loadAgentAuditSummary(taskId)
      await refreshWorkspace(updated)
      if (sessionStore.currentSessionId) {
        await loadTimeline(sessionStore.currentSessionId)
      }
      return updated
    } catch (error) {
      agentErrorMessage.value = toFriendlyMessage(error)
      await refreshAgentTaskSnapshot(taskId)
      return planApproved ? (agentTasks.value.find((task) => task.id === taskId) ?? null) : null
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
        await loadTimeline(sessionStore.currentSessionId)
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
        await loadTimeline(sessionStore.currentSessionId)
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
    await Promise.all([sessionStore.loadSessions(), loadSkills(), loadKnowledgeBases()])
    await loadPluginTools()

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

  async function deleteSession(id: string) {
    const wasCurrent = sessionStore.currentSessionId === id
    await sessionStore.deleteSession(id)
    delete messageStore.messagesMap[id]
    delete historyCursors.value[id]
    if (wasCurrent) {
      agentTasks.value = []
      agentApprovals.value = []
      agentAuditSummary.value = []
      timelineEvents.value = []
      currentWorkspace.value = null
      currentArtifactPreview.value = null
      chartPreview.value = null
      if (sessionStore.currentSessionId) {
        await selectSession(sessionStore.currentSessionId, true)
      } else {
        await createNewSession()
      }
    }
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

    streamStore.start()
    let streamErrorCode: string | null = null

    try {
      await chatService.sendMessageStream(sessionId, input, {
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
    agentTasks.value = []
    agentApprovals.value = []
    agentAuditSummary.value = []
    timelineEvents.value = []
    availableSkills.value = []
    selectedSkillCode.value = null
    availablePluginTools.value = []
    selectedToolCodes.value = []
    isLoadingPluginTools.value = false
    availableKnowledgeBases.value = []
    selectedKnowledgeBaseId.value = null
    uploadedFiles.value = []
    currentWorkspace.value = null
    currentArtifactPreview.value = null
    chartPreview.value = null
    agentErrorMessage.value = null
    historyCursors.value = {}
    isLoadingOlderHistory.value = false
    isAgentBusy.value = false
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    currentMessages,
    isStreaming,
    isWaitingForApproval,
    isLoadingHistory,
    isLoadingOlderHistory,
    hasMoreHistoryBefore,
    agentTasks,
    latestAgentTask,
    agentApprovals,
    pendingAgentApprovals,
    agentAuditSummary,
    timelineEvents,
    availableSkills,
    selectedSkillCode,
    selectedSkill,
    isSkillAutoMode,
    availablePluginTools,
    selectedToolCodes,
    selectedPluginTools,
    isLoadingPluginTools,
    selectedSkillSupportsKnowledge,
    availableKnowledgeBases,
    selectedKnowledgeBaseId,
    selectedKnowledgeBase,
    uploadedFiles,
    currentWorkspace,
    currentArtifactPreview,
    chartPreview,
    isAgentBusy,
    agentErrorMessage,
    errorMessage,
    initialize,
    loadSkills,
    selectSkill,
    loadPluginTools,
    togglePluginTool,
    clearPluginTools,
    loadKnowledgeBases,
    selectKnowledgeBase,
    loadTimeline,
    loadOlderHistory,
    createNewSession,
    selectSession,
    deleteSession,
    confirmOnsitePresence,
    clearOnsitePresence,
    uploadSessionFile,
    planAgentTask,
    runAgentTask,
    retryAgentTask,
    approveAndRunAgentTask,
    decideAgentApproval,
    refreshAgentTaskSnapshot,
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
