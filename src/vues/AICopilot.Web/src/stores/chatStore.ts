import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage } from '@/types/models'
import { processChunk, getErrorCode } from '@/protocol/chunkReducer'
import { getApprovalFailureStatus } from '@/protocol/approvalProtocol'
import { useApprovalStore } from './approvalStore'
import { useAgentCatalogStore } from './agentCatalogStore'
import { useAgentTaskStore } from './agentTaskStore'
import { useArtifactWorkspaceStore } from './artifactWorkspaceStore'
import { resolveChatErrorMessage, useChatErrorStore, toFriendlyMessage } from './chatErrorStore'
import { getChatRunMessageKey, useChatRunStatusStore } from './chatRunStatusStore'
import { useMessageStore } from './messageStore'
import { useSessionStore } from './sessionStore'
import { useStreamStore } from './streamStore'
import type {
  AgentApprovalRequest,
  AgentTask,
  ArtifactRecord
} from '@/types/protocols'

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
  const catalogStore = useAgentCatalogStore()
  const agentTaskStore = useAgentTaskStore()
  const artifactWorkspaceStore = useArtifactWorkspaceStore()
  const errorStore = useChatErrorStore()
  const runStatusStore = useChatRunStatusStore()

  const sessions = computed(() => sessionStore.sessions)
  const currentSessionId = computed(() => sessionStore.currentSessionId)
  const currentSession = computed(() => sessionStore.currentSession)
  const currentMessages = computed(() => messageStore.currentMessages)
  const isStreaming = computed(() => streamStore.isStreaming)
  const isWaitingForApproval = computed(() => approvalStore.isWaitingForApproval)
  const isLoadingHistory = computed(() => sessionStore.isLoadingHistory)
  const errorMessage = computed(() => errorStore.errorMessage)
  const agentTasks = computed({
    get: () => agentTaskStore.agentTasks,
    set: (value) => {
      agentTaskStore.agentTasks = value
    }
  })
  const agentApprovals = computed({
    get: () => agentTaskStore.agentApprovals,
    set: (value) => {
      agentTaskStore.agentApprovals = value
    }
  })
  const agentAuditSummary = computed({
    get: () => agentTaskStore.agentAuditSummary,
    set: (value) => {
      agentTaskStore.agentAuditSummary = value
    }
  })
  const timelineEvents = computed({
    get: () => agentTaskStore.timelineEvents,
    set: (value) => {
      agentTaskStore.timelineEvents = value
    }
  })
  const availableSkills = computed(() => catalogStore.availableSkills)
  const selectedSkillCode = computed(() => catalogStore.selectedSkillCode)
  const availablePluginTools = computed(() => catalogStore.availablePluginTools)
  const selectedToolCodes = computed(() => catalogStore.selectedToolCodes)
  const isLoadingPluginTools = computed(() => catalogStore.isLoadingPluginTools)
  const availableKnowledgeBases = computed(() => catalogStore.availableKnowledgeBases)
  const selectedKnowledgeBaseId = computed(() => catalogStore.selectedKnowledgeBaseId)
  const uploadedFiles = computed({
    get: () => artifactWorkspaceStore.uploadedFiles,
    set: (value) => {
      artifactWorkspaceStore.uploadedFiles = value
    }
  })
  const currentWorkspace = computed({
    get: () => artifactWorkspaceStore.currentWorkspace,
    set: (value) => {
      artifactWorkspaceStore.currentWorkspace = value
    }
  })
  const currentArtifactPreview = computed({
    get: () => artifactWorkspaceStore.currentArtifactPreview,
    set: (value) => {
      artifactWorkspaceStore.currentArtifactPreview = value
    }
  })
  const chartPreview = computed({
    get: () => artifactWorkspaceStore.chartPreview,
    set: (value) => {
      artifactWorkspaceStore.chartPreview = value
    }
  })
  const isAgentBusy = computed({
    get: () => agentTaskStore.isAgentBusy,
    set: (value: boolean) => {
      agentTaskStore.isAgentBusy = value
    }
  })
  const currentRunStatus = computed(() => runStatusStore.currentRunStatus)
  const historyCursors = ref<Record<string, HistoryCursorState>>({})
  const isLoadingOlderHistory = ref(false)
  const latestAgentTask = computed(() => agentTaskStore.latestAgentTask)
  const selectedSkill = computed(() => catalogStore.selectedSkill)
  const selectedPluginTools = computed(() => catalogStore.selectedPluginTools)
  const isSkillAutoMode = computed(() => catalogStore.isSkillAutoMode)
  const selectedSkillSupportsKnowledge = computed(() => catalogStore.selectedSkillSupportsKnowledge)
  const selectedKnowledgeBase = computed(() => catalogStore.selectedKnowledgeBase)
  const selectedKnowledgeBaseIdsForPlan = computed(() => catalogStore.selectedKnowledgeBaseIdsForPlan)
  const pendingAgentApprovals = computed(() =>
    agentTaskStore.pendingAgentApprovals
  )
  const hasMoreHistoryBefore = computed(() => {
    const sessionId = sessionStore.currentSessionId
    return Boolean(sessionId && historyCursors.value[sessionId]?.hasMoreBefore)
  })

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  function clearCurrentSessionError() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
    errorStore.clearSessionError(sessionStore.currentSessionId)
  }

  function setCurrentSessionError(message: string) {
    const sessionId = sessionStore.currentSessionId
    if (sessionId) {
      errorStore.bindCurrentSession(sessionId)
      errorStore.setSessionError(sessionId, message)
    }
  }

  function createSessionErrorReporter(sessionId = sessionStore.currentSessionId) {
    const targetSessionId = sessionId

    return (message: string) => {
      if (!targetSessionId) {
        return
      }

      errorStore.setSessionError(targetSessionId, message)
      errorStore.bindCurrentSession(sessionStore.currentSessionId)
    }
  }

  function resetCurrentSessionState() {
    agentTaskStore.reset()
    artifactWorkspaceStore.reset()
    catalogStore.resetSelections()
  }

  function getRunStatusForMessage(message: ChatMessage) {
    return runStatusStore.getStatus(message.sessionId, getChatRunMessageKey(message))
  }

  async function loadSkills() {
    await catalogStore.loadSkills(createSessionErrorReporter())
  }

  function selectSkill(skillCode: string | null) {
    catalogStore.selectSkill(skillCode, createSessionErrorReporter())
  }

  async function loadPluginTools() {
    await catalogStore.loadPluginTools(createSessionErrorReporter())
  }

  function togglePluginTool(toolCode: string) {
    catalogStore.togglePluginTool(toolCode)
  }

  function clearPluginTools() {
    catalogStore.clearPluginTools()
  }

  async function loadKnowledgeBases() {
    await catalogStore.loadKnowledgeBases(createSessionErrorReporter())
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    catalogStore.selectKnowledgeBase(knowledgeBaseId)
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
    agentTaskStore.upsertAgentTask(task)
  }

  async function loadAgentTasks(sessionId: string) {
    const reportError = createSessionErrorReporter(sessionId)
    const latestTask = await agentTaskStore.loadAgentTasks(sessionId, reportError)
    if (latestTask?.workspaceCode) {
      await refreshWorkspace(latestTask, sessionId)
    } else {
      artifactWorkspaceStore.reset()
    }
    await loadAgentApprovals(latestTask?.id ?? null, sessionId)
    await loadAgentAuditSummary(latestTask?.id ?? null, sessionId)
  }

  async function loadTimeline(sessionId: string) {
    await agentTaskStore.loadTimeline(sessionId, createSessionErrorReporter(sessionId))
  }

  async function loadAgentApprovals(
    taskId: string | null = null,
    sessionId = sessionStore.currentSessionId
  ) {
    await agentTaskStore.loadAgentApprovals(taskId, createSessionErrorReporter(sessionId))
  }

  async function loadAgentAuditSummary(
    taskId: string | null = null,
    sessionId = sessionStore.currentSessionId
  ) {
    await agentTaskStore.loadAgentAuditSummary(taskId, createSessionErrorReporter(sessionId))
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
    return agentTaskStore.findPendingPlanApproval(taskId)
  }

  async function refreshWorkspace(task: AgentTask, sessionId = sessionStore.currentSessionId) {
    await artifactWorkspaceStore.refreshWorkspace(task, createSessionErrorReporter(sessionId))
  }

  async function loadArtifactPreview(artifactId: string) {
    return artifactWorkspaceStore.loadArtifactPreview(artifactId, createSessionErrorReporter())
  }

  async function refreshChartPreview() {
    await artifactWorkspaceStore.refreshChartPreview(createSessionErrorReporter())
  }

  async function downloadArtifact(artifact: ArtifactRecord) {
    if (!artifact.downloadUrl) {
      setCurrentSessionError('后端未返回产物下载地址，前端不会自行拼接下载路径。')
      return
    }

    try {
      await artifactWorkspaceStore.downloadArtifact(artifact)
    } catch (error) {
      setCurrentSessionError(`下载产物失败：${toFriendlyMessage(error)}`)
    }
  }

  async function uploadSessionFile(file: File) {
    if (!sessionStore.currentSessionId) {
      return null
    }

    clearCurrentSessionError()
    try {
      return await artifactWorkspaceStore.uploadSessionFile(sessionStore.currentSessionId, file)
    } catch (error) {
      setCurrentSessionError(`上传附件失败：${toFriendlyMessage(error)}`)
      return null
    }
  }

  async function planAgentTask(goal: string) {
    if (!sessionStore.currentSessionId || isAgentBusy.value || streamStore.isStreaming) {
      return null
    }

    const sessionId = sessionStore.currentSessionId
    const assistantMessage = addPlanConversationMessages(sessionId, goal)
    const runMessageKey = getChatRunMessageKey(assistantMessage)
    isAgentBusy.value = true
    clearCurrentSessionError()
    runStatusStore.startRun(sessionId, runMessageKey)
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
          runStatusStore.advanceFromChunk(sessionId, runMessageKey, chunk)
          if (chunk.type === ChunkType.Error) {
            try {
              streamErrorMessage = resolveChatErrorMessage(JSON.parse(chunk.content))
            } catch (error) {
              console.error('Failed to parse chat stream error chunk.', error)
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
          runStatusStore.completeRun(sessionId, runMessageKey)
          approvalStore.sync(sessionId)
        },
        onError(error) {
          streamStore.stop()
          assistantMessage.isStreaming = false
          streamErrorMessage = toFriendlyMessage(error)
          runStatusStore.failRun(sessionId, runMessageKey, streamErrorMessage)
          setCurrentSessionError(streamErrorMessage)
          appendPlanStreamError(assistantMessage, streamErrorMessage)
          approvalStore.sync(sessionId)
        }
      })

      const completedTask = plannedTask as AgentTask | null
      if (!completedTask) {
        if (streamErrorMessage) {
          setCurrentSessionError(streamErrorMessage)
        }
        return null
      }

      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      return agentTasks.value.find((task) => task.id === completedTask.id) ?? latestAgentTask.value ?? completedTask
    } catch (error) {
      const message = toFriendlyMessage(error)
      runStatusStore.failRun(sessionId, runMessageKey, message)
      setCurrentSessionError(message)
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
    clearCurrentSessionError()
    try {
      artifactWorkspaceStore.currentWorkspace = await chatService.submitFinalReview(code)
      await refreshChartPreview()
      if (sessionStore.currentSessionId) {
        await loadAgentTasks(sessionStore.currentSessionId)
        await loadTimeline(sessionStore.currentSessionId)
      }
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      setCurrentSessionError(toFriendlyMessage(error))
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
    clearCurrentSessionError()
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
      setCurrentSessionError(toFriendlyMessage(error))
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
    clearCurrentSessionError()
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
      setCurrentSessionError(toFriendlyMessage(error))
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
    clearCurrentSessionError()
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
      setCurrentSessionError(toFriendlyMessage(error))
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
    clearCurrentSessionError()
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
      setCurrentSessionError(toFriendlyMessage(error))
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
    clearCurrentSessionError()
    try {
      artifactWorkspaceStore.currentWorkspace = await chatService.finalizeWorkspace(code)
      await refreshChartPreview()
      if (sessionStore.currentSessionId) {
        await loadAgentTasks(sessionStore.currentSessionId)
        await loadTimeline(sessionStore.currentSessionId)
      }
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      setCurrentSessionError(toFriendlyMessage(error))
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
    resetCurrentSessionState()
    const newSession = await sessionStore.createSession()
    messageStore.messagesMap[newSession.id] = []
    streamStore.stop()
    approvalStore.sync(newSession.id)
    bindErrorSession()
    return newSession
  }

  async function selectSession(id: string, forceReload = false) {
    streamStore.stop()
    resetCurrentSessionState()
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
    runStatusStore.clearSession(id)
    if (wasCurrent) {
      resetCurrentSessionState()
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
    const runMessageKey = getChatRunMessageKey(assistantMessage)

    runStatusStore.startRun(sessionId, runMessageKey)
    streamStore.start()
    let streamErrorCode: string | null = null

    try {
      await chatService.sendMessageStream(sessionId, input, {
        onChunkReceived(chunk) {
          runStatusStore.advanceFromChunk(sessionId, runMessageKey, chunk)
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
            runStatusStore.clearRunStatus(sessionId, runMessageKey)
            messageStore.removeMessages(sessionId, userMessage, assistantMessage)
            void approvalStore.refreshPendingApprovals(sessionId)
          } else {
            runStatusStore.completeRun(sessionId, runMessageKey)
          }

          approvalStore.sync(sessionId)
        },
        onError(error) {
          streamStore.stop()
          assistantMessage.isStreaming = false
          const message = toFriendlyMessage(error)
          runStatusStore.failRun(sessionId, runMessageKey, message)
          errorStore.setSessionError(sessionId, message)
          approvalStore.sync(sessionId)
        }
      })
    } catch (error) {
      const message = toFriendlyMessage(error)
      runStatusStore.failRun(sessionId, runMessageKey, message)
      errorStore.setSessionError(sessionId, message)
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
    runStatusStore.reset()
    catalogStore.reset()
    resetCurrentSessionState()
    historyCursors.value = {}
    isLoadingOlderHistory.value = false
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
    currentRunStatus,
    errorMessage,
    getRunStatusForMessage,
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
    clearCurrentSessionError,
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
