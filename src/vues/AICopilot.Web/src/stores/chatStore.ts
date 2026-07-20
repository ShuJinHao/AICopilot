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
import type { AgentApprovalRequest, AgentTask, ArtifactRecord } from '@/types/protocols'

interface HistoryCursorState {
  beforeSequence: number | null
  afterSequence: number | null
  hasMoreBefore: boolean
  hasMoreAfter: boolean
}

interface SessionActivationOptions {
  forceReload?: boolean
  preserveComposerSelections?: boolean
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
  const composerSessionId = computed(() => sessionStore.activeSession?.id ?? null)
  const resolvedSessionId = computed(() =>
    sessionStore.isSessionActivating ? null : composerSessionId.value,
  )
  const isSessionActivating = computed(() => sessionStore.isSessionActivating)
  const currentMessages = computed(() => messageStore.currentMessages)
  const isStreaming = computed(() => streamStore.isStreaming)
  const isWaitingForApproval = computed(() => approvalStore.isWaitingForApproval)
  const hasPendingApproval = computed(() => approvalStore.hasPendingApproval)
  const isApprovalAuthorityUnknown = computed(() => approvalStore.isApprovalAuthorityUnknown)
  const isLoadingHistory = computed(() => sessionStore.isLoadingHistory)
  const initializationErrors = ref<Record<string, string>>({})
  const isInitializationHydrating = ref(false)
  const errorMessage = computed(
    () =>
      errorStore.errorMessage ||
      Object.values(initializationErrors.value).filter(Boolean).join('；'),
  )
  const agentTasks = computed({
    get: () => agentTaskStore.agentTasks,
    set: (value) => {
      agentTaskStore.agentTasks = value
    },
  })
  const agentApprovals = computed({
    get: () => agentTaskStore.agentApprovals,
    set: (value) => {
      agentTaskStore.agentApprovals = value
    },
  })
  const agentAuditSummary = computed({
    get: () => agentTaskStore.agentAuditSummary,
    set: (value) => {
      agentTaskStore.agentAuditSummary = value
    },
  })
  const timelineEvents = computed({
    get: () => agentTaskStore.timelineEvents,
    set: (value) => {
      agentTaskStore.timelineEvents = value
    },
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
    },
  })
  const currentWorkspace = computed({
    get: () => artifactWorkspaceStore.currentWorkspace,
    set: (value) => {
      artifactWorkspaceStore.currentWorkspace = value
    },
  })
  const currentArtifactPreview = computed({
    get: () => artifactWorkspaceStore.currentArtifactPreview,
    set: (value) => {
      artifactWorkspaceStore.currentArtifactPreview = value
    },
  })
  const chartPreview = computed({
    get: () => artifactWorkspaceStore.chartPreview,
    set: (value) => {
      artifactWorkspaceStore.chartPreview = value
    },
  })
  const isAgentBusy = computed({
    get: () => agentTaskStore.isAgentBusy,
    set: (value: boolean) => {
      agentTaskStore.isAgentBusy = value
    },
  })
  const currentRunStatus = computed(() => runStatusStore.currentRunStatus)
  const historyCursors = ref<Record<string, HistoryCursorState>>({})
  const isLoadingOlderHistory = ref(false)
  const sessionOperationCount = ref(0)
  const preserveComposerContextDuringActivation = ref(false)
  const isSessionOperationInFlight = computed(
    () => sessionOperationCount.value > 0 || isLoadingOlderHistory.value,
  )
  const isSessionTransitionBlocked = computed(
    () =>
      sessionStore.isSessionActivating ||
      agentTaskStore.isAgentBusy ||
      streamStore.isStreaming ||
      isSessionOperationInFlight.value,
  )
  const canEditComposerContext = computed(
    () =>
      !agentTaskStore.isAgentBusy &&
      (!sessionStore.isSessionActivating || preserveComposerContextDuringActivation.value),
  )
  const latestAgentTask = computed(() => agentTaskStore.latestAgentTask)
  const selectedSkill = computed(() => catalogStore.selectedSkill)
  const selectedPluginTools = computed(() => catalogStore.selectedPluginTools)
  const isSkillAutoMode = computed(() => catalogStore.isSkillAutoMode)
  const selectedSkillSupportsKnowledge = computed(() => catalogStore.selectedSkillSupportsKnowledge)
  const selectedKnowledgeBase = computed(() => catalogStore.selectedKnowledgeBase)
  const selectedKnowledgeBaseIdsForPlan = computed(
    () => catalogStore.selectedKnowledgeBaseIdsForPlan,
  )
  const pendingAgentApprovals = computed(() => agentTaskStore.pendingAgentApprovals)
  const isAgentApprovalAuthorityUnknown = computed(
    () => agentTaskStore.isAgentApprovalAuthorityUnknown,
  )
  const hasMoreHistoryBefore = computed(() => {
    const sessionId = sessionStore.currentSessionId
    return Boolean(sessionId && historyCursors.value[sessionId]?.hasMoreBefore)
  })

  function bindErrorSession() {
    errorStore.bindCurrentSession(sessionStore.currentSessionId)
  }

  function clearInitializationErrors(...sources: string[]) {
    if (sources.length === 0) {
      initializationErrors.value = {}
      return
    }

    const remaining = { ...initializationErrors.value }
    for (const source of sources) {
      delete remaining[source]
    }
    initializationErrors.value = remaining
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
      return
    }

    initializationErrors.value = {
      ...initializationErrors.value,
      runtime: message,
    }
  }

  function createSessionErrorReporter(
    sessionId = sessionStore.currentSessionId,
    initializationSource = 'runtime',
  ) {
    const targetSessionId = sessionId

    return (message: string) => {
      if (isInitializationHydrating.value || !targetSessionId) {
        initializationErrors.value = {
          ...initializationErrors.value,
          [initializationSource]: message,
        }
      }

      if (targetSessionId) {
        errorStore.setSessionError(targetSessionId, message)
        errorStore.bindCurrentSession(sessionStore.currentSessionId)
      }
    }
  }

  function resetSessionRuntimeState() {
    agentTaskStore.reset()
    artifactWorkspaceStore.reset()
  }

  function captureAgentProjectionSnapshot() {
    return {
      agentTasks: [...agentTaskStore.agentTasks],
      agentApprovals: [...agentTaskStore.agentApprovals],
      agentAuditSummary: [...agentTaskStore.agentAuditSummary],
      approvalAuthorityUnknownTaskIds: new Set(agentTaskStore.approvalAuthorityUnknownTaskIds),
      currentWorkspace: artifactWorkspaceStore.currentWorkspace,
      currentArtifactPreview: artifactWorkspaceStore.currentArtifactPreview,
      chartPreview: artifactWorkspaceStore.chartPreview,
    }
  }

  function restoreAgentProjectionSnapshot(
    snapshot: ReturnType<typeof captureAgentProjectionSnapshot>,
  ) {
    agentTaskStore.agentTasks = snapshot.agentTasks
    agentTaskStore.agentApprovals = snapshot.agentApprovals
    agentTaskStore.agentAuditSummary = snapshot.agentAuditSummary
    agentTaskStore.approvalAuthorityUnknownTaskIds = new Set(
      snapshot.approvalAuthorityUnknownTaskIds,
    )
    artifactWorkspaceStore.currentWorkspace = snapshot.currentWorkspace
    artifactWorkspaceStore.currentArtifactPreview = snapshot.currentArtifactPreview
    artifactWorkspaceStore.chartPreview = snapshot.chartPreview
  }

  function captureSessionRuntimeSnapshot() {
    return {
      agentTasks: [...agentTaskStore.agentTasks],
      agentApprovals: [...agentTaskStore.agentApprovals],
      agentAuditSummary: [...agentTaskStore.agentAuditSummary],
      approvalAuthorityUnknownTaskIds: new Set(agentTaskStore.approvalAuthorityUnknownTaskIds),
      timelineEvents: [...agentTaskStore.timelineEvents],
      uploadedFiles: [...artifactWorkspaceStore.uploadedFiles],
      currentWorkspace: artifactWorkspaceStore.currentWorkspace,
      currentArtifactPreview: artifactWorkspaceStore.currentArtifactPreview,
      chartPreview: artifactWorkspaceStore.chartPreview,
      availableSkills: [...catalogStore.availableSkills],
      selectedSkillCode: catalogStore.selectedSkillCode,
      availablePluginTools: [...catalogStore.availablePluginTools],
      selectedToolCodes: [...catalogStore.selectedToolCodes],
      isLoadingPluginTools: catalogStore.isLoadingPluginTools,
      availableKnowledgeBases: [...catalogStore.availableKnowledgeBases],
      selectedKnowledgeBaseId: catalogStore.selectedKnowledgeBaseId,
    }
  }

  function restoreSessionRuntimeSnapshot(
    snapshot: ReturnType<typeof captureSessionRuntimeSnapshot>,
  ) {
    catalogStore.invalidatePluginToolRequests()
    agentTaskStore.agentTasks = snapshot.agentTasks
    agentTaskStore.agentApprovals = snapshot.agentApprovals
    agentTaskStore.agentAuditSummary = snapshot.agentAuditSummary
    agentTaskStore.approvalAuthorityUnknownTaskIds = new Set(
      snapshot.approvalAuthorityUnknownTaskIds,
    )
    agentTaskStore.timelineEvents = snapshot.timelineEvents
    artifactWorkspaceStore.uploadedFiles = snapshot.uploadedFiles
    artifactWorkspaceStore.currentWorkspace = snapshot.currentWorkspace
    artifactWorkspaceStore.currentArtifactPreview = snapshot.currentArtifactPreview
    artifactWorkspaceStore.chartPreview = snapshot.chartPreview
    catalogStore.availableSkills = snapshot.availableSkills
    catalogStore.selectedSkillCode = snapshot.selectedSkillCode
    catalogStore.availablePluginTools = snapshot.availablePluginTools
    catalogStore.selectedToolCodes = snapshot.selectedToolCodes
    catalogStore.isLoadingPluginTools = snapshot.isLoadingPluginTools
    catalogStore.availableKnowledgeBases = snapshot.availableKnowledgeBases
    catalogStore.selectedKnowledgeBaseId = snapshot.selectedKnowledgeBaseId
  }

  let pendingInitializationRuntimeSnapshot: ReturnType<
    typeof captureSessionRuntimeSnapshot
  > | null = null

  function resetCurrentSessionState() {
    resetSessionRuntimeState()
    catalogStore.resetSelections()
  }

  function resetForSessionActivation(preserveComposerSelections: boolean) {
    if (preserveComposerSelections) {
      resetSessionRuntimeState()
      return
    }

    resetCurrentSessionState()
  }

  async function runSessionOperation<T>(operation: () => Promise<T>): Promise<T> {
    sessionOperationCount.value += 1
    try {
      return await operation()
    } finally {
      sessionOperationCount.value = Math.max(0, sessionOperationCount.value - 1)
    }
  }

  function getRunStatusForMessage(message: ChatMessage) {
    return runStatusStore.getStatus(message.sessionId, getChatRunMessageKey(message))
  }

  async function loadSkills() {
    const loaded = await catalogStore.loadSkills(
      createSessionErrorReporter(sessionStore.currentSessionId, 'catalog-skills'),
    )
    if (loaded) {
      clearInitializationErrors('catalog-skills')
    }
  }

  async function selectSkill(skillCode: string | null) {
    if (!canEditComposerContext.value) {
      return false
    }

    const loaded = await runSessionOperation(
      async () =>
        await catalogStore.selectSkill(
          skillCode,
          createSessionErrorReporter(sessionStore.currentSessionId, 'catalog-selection'),
        ),
    )
    if (loaded) {
      clearInitializationErrors('catalog-selection')
    }
    return loaded
  }

  async function loadPluginTools() {
    const loaded = await catalogStore.loadPluginTools(
      createSessionErrorReporter(sessionStore.currentSessionId, 'catalog-tools'),
    )
    if (loaded) {
      clearInitializationErrors('catalog-tools')
    }
  }

  function togglePluginTool(toolCode: string) {
    if (!canEditComposerContext.value) {
      return
    }

    catalogStore.togglePluginTool(toolCode)
  }

  function clearPluginTools() {
    if (!canEditComposerContext.value) {
      return
    }

    catalogStore.clearPluginTools()
  }

  async function loadKnowledgeBases() {
    const loaded = await catalogStore.loadKnowledgeBases(
      createSessionErrorReporter(sessionStore.currentSessionId, 'catalog-knowledge'),
    )
    if (loaded) {
      clearInitializationErrors('catalog-knowledge')
    }
  }

  function selectKnowledgeBase(knowledgeBaseId: string | null) {
    if (!canEditComposerContext.value) {
      return
    }

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
          content: goal,
        },
      ],
      isStreaming: false,
      timestamp: Date.now(),
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
      timestamp: Date.now(),
    })
  }

  function appendPlanStreamError(message: ChatMessage, content: string) {
    message.chunks.push({
      source: 'PlanAgentTaskStream',
      type: ChunkType.Text,
      content,
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

  async function loadOlderHistory(sessionId = resolvedSessionId.value) {
    if (!sessionId || isSessionTransitionBlocked.value) {
      return false
    }

    const cursor = historyCursors.value[sessionId]
    if (!cursor?.hasMoreBefore || !cursor.beforeSequence) {
      return false
    }

    isLoadingOlderHistory.value = true
    try {
      const history = await chatService.getHistory(sessionId, {
        beforeSequence: cursor.beforeSequence,
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
    },
  ) {
    historyCursors.value[sessionId] = {
      beforeSequence: page.beforeSequence ?? null,
      afterSequence: page.afterSequence ?? null,
      hasMoreBefore: Boolean(page.hasMoreBefore),
      hasMoreAfter: Boolean(page.hasMoreAfter),
    }
  }

  function upsertAgentTask(task: AgentTask) {
    agentTaskStore.upsertAgentTask(task)
  }

  function rejectForeignSessionTarget() {
    setCurrentSessionError('操作目标不属于当前会话，已阻止请求。')
  }

  function rejectUnknownSessionTarget() {
    setCurrentSessionError('会话不在当前已加载列表中，已阻止操作。')
  }

  function ownsLoadedSession(sessionId: string) {
    return sessionStore.sessions.some((session) => session.id === sessionId)
  }

  function ownsCurrentTask(taskId: string, sessionId: string) {
    return agentTasks.value.some((task) => task.id === taskId && task.sessionId === sessionId)
  }

  function ownsCurrentApproval(approval: AgentApprovalRequest, sessionId: string) {
    const canonicalApproval = agentApprovals.value.find(
      (candidate) => candidate.id === approval.id && candidate.taskId === approval.taskId,
    )
    return (
      ownsCurrentTask(approval.taskId, sessionId) &&
      approval.status === 'Pending' &&
      canonicalApproval?.status === 'Pending' &&
      !agentTaskStore.isApprovalAuthorityUnknown(approval.taskId)
    )
  }

  function ownsCurrentFunctionApproval(callId: string, chunk: ApprovalChunk, sessionId: string) {
    return (
      callId === chunk.request.callId &&
      chunk.status === 'pending' &&
      messageStore
        .getApprovalChunks(sessionId)
        .some((candidate) => candidate === chunk && candidate.request.callId === callId)
    )
  }

  function ownsCurrentWorkspace(code: string, sessionId: string) {
    const workspace = currentWorkspace.value
    if (!workspace || workspace.workspaceCode !== code) {
      return false
    }

    return agentTasks.value.some(
      (task) =>
        task.id === workspace.taskId &&
        task.sessionId === sessionId &&
        task.workspaceCode === code &&
        !agentTaskStore.isApprovalAuthorityUnknown(task.id),
    )
  }

  function ownsCurrentArtifact(artifactId: string, sessionId: string) {
    const workspace = currentWorkspace.value
    if (!workspace || !workspace.artifacts.some((artifact) => artifact.id === artifactId)) {
      return false
    }

    return agentTasks.value.some(
      (task) =>
        task.id === workspace.taskId &&
        task.sessionId === sessionId &&
        task.workspaceCode === workspace.workspaceCode,
    )
  }

  async function loadAgentTasks(sessionId: string) {
    const projectionSnapshot = captureAgentProjectionSnapshot()
    const reportError = createSessionErrorReporter(sessionId)
    try {
      const latestTask = await agentTaskStore.loadAgentTasks(sessionId, reportError)
      if (latestTask?.workspaceCode) {
        await refreshWorkspace(latestTask, sessionId)
      } else {
        artifactWorkspaceStore.currentWorkspace = null
        artifactWorkspaceStore.currentArtifactPreview = null
        artifactWorkspaceStore.chartPreview = null
      }
      await loadAgentApprovals(latestTask?.id ?? null, sessionId)
      await loadAgentAuditSummary(latestTask?.id ?? null, sessionId)
    } catch (error) {
      const newlyUnknownApprovalTasks = new Set(agentTaskStore.approvalAuthorityUnknownTaskIds)
      restoreAgentProjectionSnapshot(projectionSnapshot)
      for (const taskId of newlyUnknownApprovalTasks) {
        agentTaskStore.markApprovalAuthorityUnknown(taskId)
      }
      throw error
    }
  }

  async function loadTimeline(sessionId: string) {
    if (
      !ownsLoadedSession(sessionId) ||
      sessionStore.currentSessionId !== sessionId ||
      (!sessionStore.isSessionActivating && resolvedSessionId.value !== sessionId)
    ) {
      rejectForeignSessionTarget()
      return false
    }

    await agentTaskStore.loadTimeline(sessionId, createSessionErrorReporter(sessionId))
    return true
  }

  async function loadAgentApprovals(
    taskId: string | null = null,
    sessionId = sessionStore.currentSessionId,
  ) {
    await agentTaskStore.loadAgentApprovals(taskId, createSessionErrorReporter(sessionId))
  }

  async function loadAgentAuditSummary(
    taskId: string | null = null,
    sessionId = sessionStore.currentSessionId,
  ) {
    if (
      !sessionId ||
      !ownsLoadedSession(sessionId) ||
      sessionStore.currentSessionId !== sessionId ||
      (taskId !== null && !ownsCurrentTask(taskId, sessionId))
    ) {
      rejectForeignSessionTarget()
      return false
    }

    await agentTaskStore.loadAgentAuditSummary(taskId, createSessionErrorReporter(sessionId))
    return true
  }

  async function refreshAgentTaskSnapshotForSession(sessionId: string) {
    await loadAgentTasks(sessionId)
    await loadTimeline(sessionId)
  }

  async function tryRefreshAgentTaskSnapshotForSession(sessionId: string) {
    try {
      await refreshAgentTaskSnapshotForSession(sessionId)
      return true
    } catch (error) {
      console.error('Failed to refresh the current agent task snapshot.', error)
      return false
    }
  }

  async function refreshAgentTaskSnapshot(_taskId: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return
    }

    await runSessionOperation(async () => await tryRefreshAgentTaskSnapshotForSession(sessionId))
  }

  function findPendingPlanApproval(taskId: string) {
    return agentTaskStore.findPendingPlanApproval(taskId)
  }

  async function refreshWorkspace(task: AgentTask, sessionId = sessionStore.currentSessionId) {
    await artifactWorkspaceStore.refreshWorkspace(task, createSessionErrorReporter(sessionId))
  }

  async function loadArtifactPreview(artifactId: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentArtifact(artifactId, sessionId)) {
      rejectForeignSessionTarget()
      return null
    }

    return await runSessionOperation(
      async () =>
        await artifactWorkspaceStore.loadArtifactPreview(artifactId, createSessionErrorReporter()),
    )
  }

  async function refreshChartPreview() {
    await artifactWorkspaceStore.refreshChartPreview(createSessionErrorReporter())
  }

  async function downloadArtifact(artifact: ArtifactRecord) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return
    }

    if (!artifact.downloadUrl) {
      setCurrentSessionError('后端未返回产物下载地址，前端不会自行拼接下载路径。')
      return
    }

    if (!ownsCurrentArtifact(artifact.id, sessionId)) {
      rejectForeignSessionTarget()
      return
    }

    const canonicalArtifact = currentWorkspace.value?.artifacts.find(
      (candidate) => candidate.id === artifact.id,
    )
    if (!canonicalArtifact?.downloadUrl) {
      setCurrentSessionError('后端未返回产物下载地址，前端不会自行拼接下载路径。')
      return
    }

    await runSessionOperation(async () => {
      try {
        await artifactWorkspaceStore.downloadArtifact(canonicalArtifact)
      } catch (error) {
        createSessionErrorReporter(sessionId)(`下载产物失败：${toFriendlyMessage(error)}`)
      }
    })
  }

  async function uploadSessionFile(file: File) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }

    clearCurrentSessionError()
    return await runSessionOperation(async () => {
      try {
        return await artifactWorkspaceStore.uploadSessionFile(sessionId, file)
      } catch (error) {
        createSessionErrorReporter(sessionId)(`上传附件失败：${toFriendlyMessage(error)}`)
        return null
      }
    })
  }

  async function planAgentTask(goal: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value || approvalStore.isWaitingForApproval) {
      return null
    }

    if (selectedSkillCode.value || selectedToolCodes.value.length > 0) {
      clearCurrentSessionError()
      setCurrentSessionError('当前计划入口不再接受 Skill 或工具选择，请清除旧选择后重试。')
      return null
    }

    const assistantMessage = addPlanConversationMessages(sessionId, goal)
    const runMessageKey = getChatRunMessageKey(assistantMessage)
    isAgentBusy.value = true
    clearCurrentSessionError()
    runStatusStore.startRun(sessionId, runMessageKey)
    streamStore.start()
    let plannedTask: AgentTask | null = null
    let streamErrorMessage: string | null = null
    try {
      await chatService.planAgentTaskStream(
        {
          sessionId,
          goal,
          taskType: 'ReportGeneration',
          uploadIds: uploadedFiles.value.map((item) => item.id),
          knowledgeBaseIds: selectedKnowledgeBaseIdsForPlan.value,
          plannerMode: 'Auto',
          pluginSelectionMode: 'BuiltInOnly',
          selectedPluginIds: [],
          capabilitySelectionMode: 'InferredFromGoal',
          requestedCapabilityCodes: [],
        },
        {
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
              },
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
          },
        },
      )

      const completedTask = plannedTask as AgentTask | null
      if (!completedTask) {
        if (streamErrorMessage) {
          setCurrentSessionError(streamErrorMessage)
        }
        return null
      }

      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      return (
        agentTasks.value.find((task) => task.id === completedTask.id) ??
        latestAgentTask.value ??
        completedTask
      )
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
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentWorkspace(code, sessionId)) {
      rejectForeignSessionTarget()
      return null
    }

    isAgentBusy.value = true
    clearCurrentSessionError()
    try {
      artifactWorkspaceStore.currentWorkspace = await chatService.submitFinalReview(code)
      await refreshChartPreview()
      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      await tryRefreshAgentTaskSnapshotForSession(sessionId)
      setCurrentSessionError(failureMessage)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function executeAgentTaskAction(
    taskId: string,
    execute: (id: string) => ReturnType<typeof chatService.runAgentTask>,
  ) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentTask(taskId, sessionId) || agentTaskStore.isApprovalAuthorityUnknown(taskId)) {
      rejectForeignSessionTarget()
      return null
    }

    isAgentBusy.value = true
    clearCurrentSessionError()
    try {
      const updated = await execute(taskId)
      upsertAgentTask(updated)
      await loadAgentApprovals(taskId)
      await loadAgentAuditSummary(taskId)
      await refreshWorkspace(updated)
      await loadTimeline(sessionId)
      return updated
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      await tryRefreshAgentTaskSnapshotForSession(sessionId)
      setCurrentSessionError(failureMessage)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function runAgentTask(taskId: string) {
    return executeAgentTaskAction(taskId, chatService.runAgentTask)
  }

  async function retryAgentTask(taskId: string) {
    return executeAgentTaskAction(taskId, chatService.retryAgentTask)
  }

  async function approveAndRunAgentTask(taskId: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentTask(taskId, sessionId) || agentTaskStore.isApprovalAuthorityUnknown(taskId)) {
      rejectForeignSessionTarget()
      return null
    }

    isAgentBusy.value = true
    clearCurrentSessionError()
    let planApproved = false
    let approvalMutationStarted = false
    try {
      const pendingPlanApproval = findPendingPlanApproval(taskId)
      if (pendingPlanApproval) {
        approvalMutationStarted = true
        agentTaskStore.markApprovalAuthorityUnknown(taskId)
        await chatService.decideAgentApproval(
          pendingPlanApproval.id,
          'approve',
          'Approved from primary plan CTA',
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
      await loadTimeline(sessionId)
      return updated
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      if (approvalMutationStarted) {
        agentTaskStore.markApprovalAuthorityUnknown(taskId)
      }
      await tryRefreshAgentTaskSnapshotForSession(sessionId)
      setCurrentSessionError(failureMessage)
      return planApproved ? (agentTasks.value.find((task) => task.id === taskId) ?? null) : null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function decideAgentApproval(
    approval: AgentApprovalRequest,
    decision: 'approve' | 'reject',
    comment?: string | null,
  ) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentApproval(approval, sessionId)) {
      rejectForeignSessionTarget()
      return null
    }

    isAgentBusy.value = true
    clearCurrentSessionError()
    try {
      agentTaskStore.markApprovalAuthorityUnknown(approval.taskId)
      const decided = await chatService.decideAgentApproval(approval.id, decision, comment)
      await loadAgentApprovals(approval.taskId)
      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      await loadAgentAuditSummary(approval.taskId)

      return decided
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      agentTaskStore.markApprovalAuthorityUnknown(approval.taskId)
      await tryRefreshAgentTaskSnapshotForSession(sessionId)
      setCurrentSessionError(failureMessage)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  async function finalizeWorkspace(code: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return null
    }
    if (!ownsCurrentWorkspace(code, sessionId)) {
      rejectForeignSessionTarget()
      return null
    }

    isAgentBusy.value = true
    clearCurrentSessionError()
    try {
      artifactWorkspaceStore.currentWorkspace = await chatService.finalizeWorkspace(code)
      await refreshChartPreview()
      await loadAgentTasks(sessionId)
      await loadTimeline(sessionId)
      await loadAgentApprovals(latestAgentTask.value?.id ?? null)
      await loadAgentAuditSummary(latestAgentTask.value?.id ?? null)
      return currentWorkspace.value
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      await tryRefreshAgentTaskSnapshotForSession(sessionId)
      setCurrentSessionError(failureMessage)
      return null
    } finally {
      isAgentBusy.value = false
    }
  }

  function prepareInitialization() {
    if (sessionStore.isSessionActivating) {
      return
    }

    pendingInitializationRuntimeSnapshot = captureSessionRuntimeSnapshot()
    initializationErrors.value = {}
    isInitializationHydrating.value = true
    preserveComposerContextDuringActivation.value = true
    sessionStore.beginSessionActivation()
    streamStore.stop()
    resetSessionRuntimeState()
    errorStore.clearSessionError()
  }

  async function initialize() {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    const runtimeSnapshot = pendingInitializationRuntimeSnapshot ?? captureSessionRuntimeSnapshot()
    if (!sessionStore.isSessionActivating) {
      prepareInitialization()
    }
    pendingInitializationRuntimeSnapshot = null
    try {
      const [sessionLoadResult] = await Promise.allSettled([
        sessionStore.loadSessions(),
        loadSkills(),
        loadKnowledgeBases(),
      ])
      if (sessionLoadResult.status === 'rejected') {
        throw sessionLoadResult.reason
      }
      await loadPluginTools()

      if (sessionStore.sessions.length === 0) {
        await createSession({ preserveComposerSelections: previousActiveSessionId === null })
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
        sessionStore.completeSessionActivation(null)
        return
      }

      const isSameSessionRehydration =
        previousActiveSessionId === null || initialSession.id === previousActiveSessionId
      await activateSession(initialSession.id, {
        preserveComposerSelections: isSameSessionRehydration,
      })
      if (isSameSessionRehydration && initialSession.id === previousActiveSessionId) {
        artifactWorkspaceStore.uploadedFiles = runtimeSnapshot.uploadedFiles
      }
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      restoreSessionRuntimeSnapshot(runtimeSnapshot)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      if (previousCurrentSessionId) {
        errorStore.setSessionError(previousCurrentSessionId, toFriendlyMessage(error))
      } else {
        initializationErrors.value = {
          ...initializationErrors.value,
          'session-list': toFriendlyMessage(error),
        }
      }
      throw error
    } finally {
      isInitializationHydrating.value = false
      if (!sessionStore.isSessionActivating) {
        preserveComposerContextDuringActivation.value = false
      }
    }
  }

  async function createSession(options: SessionActivationOptions = {}) {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    const runtimeSnapshot = captureSessionRuntimeSnapshot()
    preserveComposerContextDuringActivation.value = options.preserveComposerSelections ?? false
    errorStore.clearSessionError()
    sessionStore.beginSessionActivation(null)
    resetForSessionActivation(options.preserveComposerSelections ?? false)
    try {
      const newSession = await sessionStore.createSession()
      messageStore.messagesMap[newSession.id] = []
      streamStore.stop()
      approvalStore.sync(newSession.id)
      bindErrorSession()
      if (!(options.preserveComposerSelections ?? false)) {
        await catalogStore.loadPluginTools(createSessionErrorReporter(newSession.id))
      }
      clearInitializationErrors('session-list', 'session-create')
      sessionStore.completeSessionActivation(newSession.id)
      return newSession
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      restoreSessionRuntimeSnapshot(runtimeSnapshot)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      if (previousCurrentSessionId) {
        errorStore.setSessionError(previousCurrentSessionId, toFriendlyMessage(error))
      } else {
        initializationErrors.value = {
          ...initializationErrors.value,
          'session-create': toFriendlyMessage(error),
        }
      }
      throw error
    } finally {
      preserveComposerContextDuringActivation.value = false
    }
  }

  async function createNewSession() {
    if (isSessionTransitionBlocked.value) {
      return null
    }
    try {
      return await createSession()
    } catch (error) {
      console.error('Failed to create a new session.', error)
      return null
    }
  }

  async function activateSession(id: string, options: SessionActivationOptions = {}) {
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    const runtimeSnapshot = captureSessionRuntimeSnapshot()
    const preserveComposerSelections =
      options.preserveComposerSelections ?? composerSessionId.value === id
    const shouldReloadComposerCatalog =
      previousActiveSessionId !== id && !preserveComposerSelections
    preserveComposerContextDuringActivation.value = preserveComposerSelections
    sessionStore.beginSessionActivation(id)
    streamStore.stop()
    if (previousActiveSessionId !== id) {
      resetForSessionActivation(preserveComposerSelections)
    }
    try {
      sessionStore.persistCurrentSession(id)
      bindErrorSession()
      errorStore.clearSessionError(id)
      approvalStore.sync(id)
      if (shouldReloadComposerCatalog) {
        await catalogStore.loadPluginTools(createSessionErrorReporter(id))
      }
      await loadHistory(id, options.forceReload ?? false)
      sessionStore.completeSessionActivation(id)
    } catch (error) {
      sessionStore.persistCurrentSession(previousCurrentSessionId)
      sessionStore.failSessionActivation(previousActiveSessionId)
      restoreSessionRuntimeSnapshot(runtimeSnapshot)
      approvalStore.sync(previousActiveSessionId)
      bindErrorSession()
      errorStore.setSessionError(previousCurrentSessionId ?? id, toFriendlyMessage(error))
      throw error
    } finally {
      preserveComposerContextDuringActivation.value = false
    }
  }

  async function selectSession(id: string, forceReload = false) {
    if (isSessionTransitionBlocked.value) {
      return false
    }
    if (!ownsLoadedSession(id)) {
      rejectUnknownSessionTarget()
      return false
    }
    try {
      await activateSession(id, { forceReload })
      return true
    } catch (error) {
      console.error('Failed to select the requested session.', error)
      return false
    }
  }

  async function deleteSession(id: string) {
    if (isSessionTransitionBlocked.value) {
      return false
    }
    if (!ownsLoadedSession(id)) {
      rejectUnknownSessionTarget()
      return false
    }
    const previousCurrentSessionId = sessionStore.currentSessionId
    const previousActiveSessionId = sessionStore.activeSessionId
    const wasCurrent = sessionStore.currentSessionId === id
    preserveComposerContextDuringActivation.value = false
    sessionStore.beginSessionActivation(previousActiveSessionId)
    try {
      await sessionStore.deleteSession(id)
    } catch (error) {
      const failureMessage = toFriendlyMessage(error)
      try {
        await sessionStore.loadSessions()
      } catch (reconciliationError) {
        console.error(
          'Failed to reconcile a session deletion with unknown outcome.',
          reconciliationError,
        )
        const trustedCurrentSessionId =
          previousCurrentSessionId && ownsLoadedSession(previousCurrentSessionId)
            ? previousCurrentSessionId
            : null
        sessionStore.persistCurrentSession(trustedCurrentSessionId)
        sessionStore.failSessionActivation(null)
        bindErrorSession()
        if (trustedCurrentSessionId) {
          errorStore.setSessionError(
            trustedCurrentSessionId,
            `${failureMessage}；删除结果无法确认，已暂停会话操作，请刷新后重试。`,
          )
        } else {
          initializationErrors.value = {
            ...initializationErrors.value,
            'session-delete': `${failureMessage}；删除结果无法确认，已暂停会话操作，请刷新后重试。`,
          }
        }
        preserveComposerContextDuringActivation.value = false
        return false
      }

      if (ownsLoadedSession(id)) {
        const trustedCurrentSessionId =
          previousCurrentSessionId && ownsLoadedSession(previousCurrentSessionId)
            ? previousCurrentSessionId
            : (sessionStore.sessions[0]?.id ?? null)
        const trustedActiveSessionId =
          previousActiveSessionId && ownsLoadedSession(previousActiveSessionId)
            ? previousActiveSessionId
            : null
        sessionStore.persistCurrentSession(trustedCurrentSessionId)
        sessionStore.failSessionActivation(trustedActiveSessionId)
        bindErrorSession()
        if (trustedCurrentSessionId) {
          errorStore.setSessionError(trustedCurrentSessionId, failureMessage)
        }
        preserveComposerContextDuringActivation.value = false
        return false
      }

      const reconciledCurrentSessionId =
        previousCurrentSessionId &&
        previousCurrentSessionId !== id &&
        ownsLoadedSession(previousCurrentSessionId)
          ? previousCurrentSessionId
          : (sessionStore.sessions[0]?.id ?? null)
      sessionStore.persistCurrentSession(reconciledCurrentSessionId)
    }
    delete messageStore.messagesMap[id]
    delete historyCursors.value[id]
    runStatusStore.clearSession(id)
    if (wasCurrent) {
      resetCurrentSessionState()
      try {
        if (sessionStore.currentSessionId) {
          await activateSession(sessionStore.currentSessionId, { forceReload: true })
        } else {
          await createSession()
        }
      } catch (error) {
        console.error('Failed to reactivate a session after deletion.', error)
        return false
      }
      return true
    }

    sessionStore.completeSessionActivation(previousActiveSessionId)
    preserveComposerContextDuringActivation.value = false
    return true
  }

  async function confirmOnsitePresence(expiresInMinutes = 30) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return
    }

    errorStore.clearSessionError(sessionId)
    return await runSessionOperation(async () => {
      const updatedSession = await chatService.updateSessionSafetyAttestation(
        sessionId,
        true,
        expiresInMinutes,
      )
      sessionStore.upsertSession(updatedSession)
      await approvalStore.refreshPendingApprovals(updatedSession.id)
      return updatedSession
    })
  }

  async function clearOnsitePresence() {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return
    }

    errorStore.clearSessionError(sessionId)
    return await runSessionOperation(async () => {
      const updatedSession = await chatService.updateSessionSafetyAttestation(sessionId, false)
      sessionStore.upsertSession(updatedSession)
      await approvalStore.refreshPendingApprovals(updatedSession.id)
      return updatedSession
    })
  }

  async function sendMessage(input: string) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value || approvalStore.isWaitingForApproval) {
      return
    }

    return await runSessionOperation(async () => {
      clearCurrentSessionError()

      const userMessage = messageStore.addMessage(sessionId, {
        sessionId,
        role: MessageRole.User,
        chunks: [
          {
            source: 'User',
            type: ChunkType.Text,
            content: input,
          },
        ],
        isStreaming: false,
        timestamp: Date.now(),
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
        timestamp: Date.now(),
      })
      const runMessageKey = getChatRunMessageKey(assistantMessage)

      runStatusStore.startRun(sessionId, runMessageKey)
      streamStore.start()
      let streamErrorCode: string | null = null
      let refreshPendingApprovals = false

      try {
        await chatService.sendMessageStream(sessionId, input, {
          onChunkReceived(chunk) {
            runStatusStore.advanceFromChunk(sessionId, runMessageKey, chunk)
            if (chunk.type === ChunkType.Error) {
              streamErrorCode = getErrorCode(chunk)
            }

            processChunk(assistantMessage, chunk, {
              setSessionError: errorStore.setSessionError,
              onApprovalChunk: approvalStore.sync,
            })
          },
          onComplete() {
            streamStore.stop()
            assistantMessage.isStreaming = false
            if (streamErrorCode === 'approval_pending') {
              runStatusStore.clearRunStatus(sessionId, runMessageKey)
              messageStore.removeMessages(sessionId, userMessage, assistantMessage)
              refreshPendingApprovals = true
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
          },
        })
        if (refreshPendingApprovals) {
          await approvalStore.refreshPendingApprovals(sessionId)
        }
      } catch (error) {
        const message = toFriendlyMessage(error)
        runStatusStore.failRun(sessionId, runMessageKey, message)
        errorStore.setSessionError(sessionId, message)
      } finally {
        streamStore.stop()
        assistantMessage.isStreaming = false
        approvalStore.sync(sessionId)
      }
    })
  }

  async function submitApproval(
    callId: string,
    decision: 'approved' | 'rejected',
    onsiteConfirmed: boolean,
    chunk: ApprovalChunk,
  ) {
    const sessionId = resolvedSessionId.value
    if (!sessionId || isSessionTransitionBlocked.value) {
      return false
    }
    if (!ownsCurrentFunctionApproval(callId, chunk, sessionId)) {
      rejectForeignSessionTarget()
      return false
    }

    return await runSessionOperation(async () => {
      let approvalFailed = false
      let approvalErrorCode: string | null = null
      clearCurrentSessionError()
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
          timestamp: Date.now(),
        })
      } else {
        targetMessage.isStreaming = true
      }

      try {
        await chatService.sendApprovalDecisionStream(
          sessionId,
          callId,
          decision,
          onsiteConfirmed,
          chunk.request,
          {
            onChunkReceived(incomingChunk) {
              if (incomingChunk.type === ChunkType.Error) {
                approvalFailed = true
                approvalErrorCode = getErrorCode(incomingChunk)
              }

              processChunk(targetMessage!, incomingChunk, {
                setSessionError: errorStore.setSessionError,
                onApprovalChunk: approvalStore.sync,
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
            },
          },
        )
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
    })
  }

  function reset() {
    sessionStore.reset()
    messageStore.reset()
    streamStore.reset()
    approvalStore.reset()
    errorStore.reset()
    initializationErrors.value = {}
    isInitializationHydrating.value = false
    runStatusStore.reset()
    catalogStore.reset()
    resetCurrentSessionState()
    historyCursors.value = {}
    isLoadingOlderHistory.value = false
    sessionOperationCount.value = 0
    pendingInitializationRuntimeSnapshot = null
    preserveComposerContextDuringActivation.value = false
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    composerSessionId,
    resolvedSessionId,
    isSessionActivating,
    isSessionOperationInFlight,
    isSessionTransitionBlocked,
    canEditComposerContext,
    currentMessages,
    isStreaming,
    isWaitingForApproval,
    hasPendingApproval,
    isApprovalAuthorityUnknown,
    isLoadingHistory,
    isLoadingOlderHistory,
    hasMoreHistoryBefore,
    agentTasks,
    latestAgentTask,
    agentApprovals,
    pendingAgentApprovals,
    isAgentApprovalAuthorityUnknown,
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
    prepareInitialization,
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
    reset,
  }
})
