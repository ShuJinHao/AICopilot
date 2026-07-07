import { computed, reactive } from 'vue'
import { defineStore } from 'pinia'
import { ChunkType, type ChatChunk, type ChatErrorPayload } from '@/types/protocols'
import type { ChatMessage } from '@/types/models'
import { resolveChatErrorMessage } from './chatErrorStore'
import {
  createSessionScopedState,
  type ChatRunPhase,
  type ChatRunStatus,
  type SessionScopedState
} from './sessionScopedState'
import { useSessionStore } from './sessionStore'

const runningPhases = new Set<ChatRunPhase>(['understanding', 'querying', 'answering'])

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function parseJson(value: unknown): unknown {
  if (typeof value !== 'string') {
    return value
  }

  try {
    return JSON.parse(value)
  } catch (error) {
    console.error('Failed to parse chat run status payload.', error)
    return value
  }
}

function getNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function getString(value: unknown) {
  return typeof value === 'string' ? value : undefined
}

function elapsedFrom(startedAt: string, now = Date.now()) {
  const startedAtMs = Date.parse(startedAt)
  if (!Number.isFinite(startedAtMs)) {
    return 0
  }

  return Math.max(0, now - startedAtMs)
}

function extractRowsFromPayload(payload: unknown): number | undefined {
  const parsed = parseJson(payload)

  if (Array.isArray(parsed)) {
    return parsed.length
  }

  if (!isObject(parsed)) {
    return undefined
  }

  const directCount =
    getNumber(parsed.returnedRowCount) ??
    getNumber(parsed.returnedRows) ??
    getNumber(parsed.rowCount) ??
    getNumber(parsed.totalRows) ??
    getNumber(parsed.totalCount) ??
    getNumber(parsed.count)

  if (directCount !== undefined) {
    return directCount
  }

  for (const key of ['rows', 'items', 'data', 'records', 'result']) {
    const value = parsed[key]
    const count = Array.isArray(value)
      ? value.length
      : typeof value === 'string'
        ? extractRowsFromPayload(value)
        : undefined

    if (count !== undefined) {
      return count
    }
  }

  return undefined
}

export function extractReturnedRowsFromFunctionResult(chunk: ChatChunk): number | undefined {
  const parsed = parseJson(chunk.content)

  if (!isObject(parsed)) {
    return undefined
  }

  return extractRowsFromPayload(parsed.result ?? parsed)
}

export function getChatRunMessageKey(
  message: Pick<ChatMessage, 'messageId' | 'sequence' | 'timestamp'>
) {
  if (typeof message.messageId === 'number' && message.messageId > 0) {
    return `message:${message.messageId}`
  }

  if (typeof message.sequence === 'number' && message.sequence > 0) {
    return `sequence:${message.sequence}`
  }

  return `local:${message.timestamp}`
}

function getFunctionName(chunk: ChatChunk) {
  const parsed = parseJson(chunk.content)
  if (!isObject(parsed)) {
    return ''
  }

  return getString(parsed.name) ?? getString(parsed.toolName) ?? ''
}

function isCloudReadonlyQuery(chunk: ChatChunk) {
  const haystack = `${chunk.source} ${getFunctionName(chunk)} ${chunk.content}`.toLowerCase()
  return (
    haystack.includes('dataanalysis') ||
    haystack.includes('cloudreadonly') ||
    haystack.includes('cloud_readonly') ||
    haystack.includes('business_database') ||
    haystack.includes('devicelog') ||
    haystack.includes('device_logs') ||
    haystack.includes('query')
  )
}

function parseErrorPayload(chunk: ChatChunk): ChatErrorPayload {
  const parsed = parseJson(chunk.content)
  return isObject(parsed) ? parsed as ChatErrorPayload : {}
}

function parseAgentEvent(chunk: ChatChunk) {
  const parsed = parseJson(chunk.content)
  return isObject(parsed) ? parsed : null
}

function hasDataAnalysisIntent(chunk: ChatChunk) {
  const parsed = parseJson(chunk.content)
  if (!Array.isArray(parsed)) {
    return false
  }

  return parsed.some((item) => {
    if (!isObject(item)) {
      return false
    }

    const intent = getString(item.intent)?.toLowerCase() ?? ''
    return intent.includes('analysis') || intent.includes('devicelog')
  })
}

export const useChatRunStatusStore = defineStore('chatRunStatus', () => {
  const sessionStore = useSessionStore()
  const sessionStates = reactive<Record<string, SessionScopedState>>({})
  let timerId: ReturnType<typeof setInterval> | null = null

  const currentRunStatus = computed(() => {
    const sessionId = sessionStore.currentSessionId
    if (!sessionId) {
      return null
    }

    return sessionStates[sessionId]?.chatRunStatus ?? null
  })

  function ensureSessionState(sessionId: string) {
    if (!sessionStates[sessionId]) {
      sessionStates[sessionId] = createSessionScopedState()
    }

    return sessionStates[sessionId]!
  }

  function hasRunningStatus() {
    return Object.values(sessionStates).some((state) =>
      state.chatRunStatus ? runningPhases.has(state.chatRunStatus.phase) : false
    )
  }

  function refreshElapsed(now = Date.now()) {
    for (const state of Object.values(sessionStates)) {
      const status = state.chatRunStatus
      if (!status || !runningPhases.has(status.phase)) {
        continue
      }

      state.chatRunStatus = {
        ...status,
        elapsedMs: elapsedFrom(status.startedAt, now)
      }
    }
  }

  function stopTimerIfIdle() {
    if (timerId !== null && !hasRunningStatus()) {
      clearInterval(timerId)
      timerId = null
    }
  }

  function startTimer() {
    if (timerId !== null) {
      return
    }

    timerId = setInterval(() => {
      refreshElapsed()
      stopTimerIfIdle()
    }, 1000)
    ;(timerId as { unref?: () => void }).unref?.()
  }

  function getStatus(sessionId: string, messageKey: string) {
    const status = sessionStates[sessionId]?.chatRunStatus ?? null
    return status?.messageKey === messageKey ? status : null
  }

  function setStatus(sessionId: string, status: ChatRunStatus) {
    ensureSessionState(sessionId).chatRunStatus = status
    if (runningPhases.has(status.phase)) {
      startTimer()
    } else {
      stopTimerIfIdle()
    }
  }

  function updateStatus(
    sessionId: string,
    messageKey: string,
    updater: (status: ChatRunStatus) => ChatRunStatus
  ) {
    const status = getStatus(sessionId, messageKey)
    if (!status || !runningPhases.has(status.phase)) {
      return
    }

    setStatus(sessionId, updater(status))
  }

  function startRun(sessionId: string, messageKey: string, summary = '正在理解问题') {
    const now = Date.now()
    setStatus(sessionId, {
      sessionId,
      messageKey,
      messageId: messageKey,
      phase: 'understanding',
      startedAt: new Date(now).toISOString(),
      elapsedMs: 0,
      summary,
      queryCount: 0
    })
  }

  function advancePhase(
    sessionId: string,
    messageKey: string,
    phase: ChatRunPhase,
    summary: string,
    extra: Partial<Pick<ChatRunStatus, 'queryCount' | 'returnedRows'>> = {}
  ) {
    updateStatus(sessionId, messageKey, (status) => ({
      ...status,
      ...extra,
      phase,
      summary,
      elapsedMs: elapsedFrom(status.startedAt)
    }))
  }

  function advanceFromChunk(sessionId: string, messageKey: string, chunk: ChatChunk) {
    if (!getStatus(sessionId, messageKey)) {
      return
    }

    switch (chunk.type) {
      case ChunkType.Intent:
        advancePhase(
          sessionId,
          messageKey,
          'querying',
          hasDataAnalysisIntent(chunk) ? '正在准备 Cloud 只读查询' : '正在处理请求'
        )
        return
      case ChunkType.FunctionCall:
        advancePhase(
          sessionId,
          messageKey,
          'querying',
          isCloudReadonlyQuery(chunk) ? '正在查询 Cloud 只读数据' : '正在调用只读工具'
        )
        return
      case ChunkType.FunctionResult: {
        const rows = extractReturnedRowsFromFunctionResult(chunk)
        updateStatus(sessionId, messageKey, (status) => ({
          ...status,
          phase: 'querying',
          summary: isCloudReadonlyQuery(chunk) ? '正在查询 Cloud 只读数据' : '正在处理工具结果',
          queryCount: (status.queryCount ?? 0) + 1,
          returnedRows:
            rows === undefined
              ? status.returnedRows
              : (status.returnedRows ?? 0) + rows,
          elapsedMs: elapsedFrom(status.startedAt)
        }))
        return
      }
      case ChunkType.AgentEvent: {
        const event = parseAgentEvent(chunk)
        const stage = getString(event?.stage)?.toLowerCase() ?? ''
        const metadataText = isObject(event?.metadata)
          ? Object.values(event.metadata).join(' ').toLowerCase()
          : ''

        if (stage.includes('data') || metadataText.includes('cloudreadonly')) {
          advancePhase(sessionId, messageKey, 'querying', '正在查询 Cloud 只读数据')
        } else if (stage.includes('intent') || stage.includes('understand')) {
          advancePhase(sessionId, messageKey, 'understanding', '正在理解问题')
        }
        return
      }
      case ChunkType.Text:
      case ChunkType.Widget:
      case ChunkType.AgentTask:
        advancePhase(sessionId, messageKey, 'answering', '正在生成回答')
        return
      case ChunkType.Error: {
        const payload = parseErrorPayload(chunk)
        failRun(sessionId, messageKey, resolveChatErrorMessage(payload), payload.code)
        return
      }
      default:
        return
    }
  }

  function completeRun(sessionId: string, messageKey: string) {
    const status = getStatus(sessionId, messageKey)
    if (!status || status.phase === 'failed') {
      return
    }

    const completedAt = new Date().toISOString()
    setStatus(sessionId, {
      ...status,
      phase: 'completed',
      completedAt,
      elapsedMs: elapsedFrom(status.startedAt),
      summary: '回答已完成'
    })
  }

  function failRun(sessionId: string, messageKey: string, message = '请求失败，请稍后重试。', code?: string) {
    const status = getStatus(sessionId, messageKey)
    if (!status || status.phase === 'completed') {
      return
    }

    const completedAt = new Date().toISOString()
    setStatus(sessionId, {
      ...status,
      phase: 'failed',
      completedAt,
      elapsedMs: elapsedFrom(status.startedAt),
      summary: message,
      error: {
        code,
        message
      }
    })
  }

  function clearRunStatus(sessionId: string, messageKey?: string) {
    const state = sessionStates[sessionId]
    const status = state?.chatRunStatus
    if (!status) {
      return
    }

    if (!messageKey || status.messageKey === messageKey) {
      state.chatRunStatus = null
      stopTimerIfIdle()
    }
  }

  function clearSession(sessionId: string) {
    delete sessionStates[sessionId]
    stopTimerIfIdle()
  }

  function reset() {
    if (timerId !== null) {
      clearInterval(timerId)
      timerId = null
    }

    for (const sessionId of Object.keys(sessionStates)) {
      delete sessionStates[sessionId]
    }
  }

  return {
    currentRunStatus,
    startRun,
    advanceFromChunk,
    completeRun,
    failRun,
    clearRunStatus,
    clearSession,
    getStatus,
    reset
  }
})
