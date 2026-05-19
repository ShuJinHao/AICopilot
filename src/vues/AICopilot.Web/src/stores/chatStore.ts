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
  const agentApprovals = ref<AgentApprovalRequest[]>([])
  const agentAuditSummary = ref<AgentTaskAuditSummary[]>([])
  const uploadedFiles = ref<UploadRecord[]>([])
  const currentWorkspace = ref<ArtifactWorkspace | null>(null)
  const chartPreview = ref<AgentChartPreview | null>(null)
  const isAgentBusy = ref(false)
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
        chartPreview.value = null
      }
      await loadAgentApprovals(agentTasks.value[0]?.id ?? null)
      await loadAgentAuditSummary(agentTasks.value[0]?.id ?? null)
    } catch {
      agentTasks.value = []
      agentApprovals.value = []
      agentAuditSummary.value = []
      currentWorkspace.value = null
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

  async function refreshWorkspace(task: AgentTask) {
    if (!task.workspaceCode) {
      currentWorkspace.value = null
      chartPreview.value = null
      return
    }

    currentWorkspace.value = await chatService.getWorkspace(task.workspaceCode)
    await refreshChartPreview()
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
      }
      chartPreview.value = {
        labels: Array.isArray(payload.labels) ? payload.labels.map(String) : [],
        values: Array.isArray(payload.values) ? payload.values.map((value) => Number(value) || 0) : [],
        source: typeof payload.source === 'string' ? payload.source : undefined,
        sourceMode: typeof payload.sourceMode === 'string' ? payload.sourceMode : undefined,
        sourceLabel: typeof payload.sourceLabel === 'string' ? payload.sourceLabel : undefined,
        isSimulation: typeof payload.isSimulation === 'boolean' ? payload.isSimulation : undefined
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
        knowledgeBaseIds: []
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
    await Promise.all([sessionStore.loadSessions(), loadChatModels()])

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
    agentApprovals.value = []
    agentAuditSummary.value = []
    uploadedFiles.value = []
    currentWorkspace.value = null
    chartPreview.value = null
    agentErrorMessage.value = null
    isAgentBusy.value = false
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
    latestAgentTask,
    agentApprovals,
    pendingAgentApprovals,
    agentAuditSummary,
    uploadedFiles,
    currentWorkspace,
    chartPreview,
    isAgentBusy,
    agentErrorMessage,
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
    runAgentTask,
    decideAgentApproval,
    loadAgentAuditSummary,
    submitFinalReview,
    finalizeWorkspace,
    downloadArtifact,
    sendMessage,
    submitApproval,
    reset
  }
})
