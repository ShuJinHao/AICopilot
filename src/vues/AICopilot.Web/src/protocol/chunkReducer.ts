import {
  ChunkType,
  type AgentEventPayload,
  type AgentTask,
  type ChatChunk,
  type ChatErrorPayload,
  type ChatModelMetadataPayload,
  type FunctionApprovalRequest,
  type IntentResult
} from '@/types/protocols'
import type {
  ApprovalChunk,
  AgentEventChunk,
  ChatMessage,
  FunctionCall,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'
import { resolveChatErrorMessage } from '@/stores/chatErrorStore'
import { stripThinkingTags } from './modelOutputSanitizer'
import { parseWidgetFromTextChunk } from './widgetPayloadParser'
import { formatPlanDraftFailure } from './agentEventDisplay'

export interface ChunkReducerCallbacks {
  setSessionError: (sessionId: string, message: string) => void
  onApprovalChunk: (sessionId: string) => void
  onAgentTaskChunk?: (sessionId: string, task: AgentTask) => void
}

export function processChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  const parsedWidget = parseWidgetFromTextChunk(message, chunk)
  if (parsedWidget) {
    addWidgetChunk(message, chunk, parsedWidget.widget)
    if (parsedWidget.remainingText) {
      addTextChunk(message, { ...chunk, content: parsedWidget.remainingText })
    }
    return
  }

  switch (chunk.type) {
    case ChunkType.Text:
      addTextChunk(message, chunk)
      break
    case ChunkType.Metadata:
      applyMetadataChunk(message, chunk)
      break
    case ChunkType.Intent:
      addIntentChunk(message, chunk)
      break
    case ChunkType.FunctionCall:
      addFunctionCallChunk(message, chunk)
      break
    case ChunkType.FunctionResult:
      addFunctionResultChunk(message, chunk)
      break
    case ChunkType.Widget:
      try {
        addWidgetChunk(message, chunk, JSON.parse(chunk.content))
      } catch (error) {
        console.error('Failed to parse widget chunk payload.', error)
        addTextChunk(message, chunk)
      }
      break
    case ChunkType.ApprovalRequest:
      addApprovalRequestChunk(message, chunk, callbacks)
      break
    case ChunkType.AgentEvent:
      addAgentEventChunk(message, chunk, callbacks)
      break
    case ChunkType.AgentTask:
      addAgentTaskChunk(message, chunk, callbacks)
      break
    case ChunkType.Error:
      addErrorChunk(message, chunk, callbacks)
      break
  }
}

function applyMetadataChunk(message: ChatMessage, chunk: ChatChunk) {
  try {
    const metadata = JSON.parse(chunk.content) as ChatModelMetadataPayload

    if (metadata.finalModelId !== undefined) {
      message.finalModelId = metadata.finalModelId
    }

    if (typeof metadata.finalModelName === 'string' && metadata.finalModelName.trim()) {
      message.finalModelName = metadata.finalModelName.trim()
    } else if (!message.finalModelName) {
      message.finalModelName = '未知'
    }

    if (metadata.routingModelId !== undefined) {
      message.routingModelId = metadata.routingModelId
    }

    if (typeof metadata.routingModelName === 'string' && metadata.routingModelName.trim()) {
      message.routingModelName = metadata.routingModelName.trim()
    }

    if (metadata.contextWindowTokens !== undefined) {
      message.contextWindowTokens = metadata.contextWindowTokens
    }

    if (metadata.maxOutputTokens !== undefined) {
      message.maxOutputTokens = metadata.maxOutputTokens
    }
  } catch (error) {
    console.error('Failed to parse metadata chunk payload.', error)
    if (!message.finalModelName) {
      message.finalModelName = '未知'
    }
  }
}

export function getErrorCode(chunk: ChatChunk) {
  try {
    const payload = JSON.parse(chunk.content) as ChatErrorPayload
    return payload.code ?? null
  } catch (error) {
    console.error('Failed to parse chat error code payload.', error)
    return null
  }
}

function addTextChunk(message: ChatMessage, chunk: ChatChunk) {
  const sanitizedContent = stripThinkingTags(chunk.content)
  if (!sanitizedContent) {
    return
  }

  const sanitizedChunk = sanitizedContent === chunk.content
    ? chunk
    : { ...chunk, content: sanitizedContent }
  const previousChunk = message.chunks[message.chunks.length - 1]

  if (!previousChunk) {
    message.chunks.push(sanitizedChunk)
    return
  }

  if (previousChunk.source === sanitizedChunk.source && previousChunk.type === ChunkType.Text) {
    previousChunk.content += sanitizedChunk.content
    return
  }

  message.chunks.push(sanitizedChunk)
}

function addWidgetChunk(message: ChatMessage, chunk: ChatChunk, parsedWidget: unknown) {
  message.chunks.push({
    ...chunk,
    type: ChunkType.Widget,
    widget: parsedWidget
  } as WidgetChunk)
}

function addIntentChunk(message: ChatMessage, chunk: ChatChunk) {
  try {
    const intents = JSON.parse(chunk.content) as IntentResult[]
    message.chunks.push({ ...chunk, intents } as IntentChunk)
  } catch (error) {
    console.error('Failed to parse intent chunk payload.', error)
    addTextChunk(message, chunk)
  }
}

function addFunctionCallChunk(message: ChatMessage, chunk: ChatChunk) {
  try {
    const functionCall = JSON.parse(chunk.content) as FunctionCall
    functionCall.status = 'calling'
    message.chunks.push({ ...chunk, functionCall } as FunctionCallChunk)
  } catch (error) {
    console.error('Failed to parse function call chunk payload.', error)
    addTextChunk(message, chunk)
  }
}

function addFunctionResultChunk(message: ChatMessage, chunk: ChatChunk) {
  try {
    const functionResult = JSON.parse(chunk.content) as FunctionCall
    const functionCallChunks = message.chunks.filter(
      (item) => item.type === ChunkType.FunctionCall
    ) as FunctionCallChunk[]
    const functionCallChunk = functionCallChunks.find((item) => item.functionCall.id === functionResult.id)

    if (functionCallChunk) {
      functionCallChunk.functionCall.result = functionResult.result
      functionCallChunk.functionCall.status = 'completed'
    }
  } catch (error) {
    console.error('Failed to parse function result chunk payload.', error)
    return
  }
}

function addApprovalRequestChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const request = JSON.parse(chunk.content) as FunctionApprovalRequest
    message.chunks.push({
      ...chunk,
      request,
      status: 'pending'
    } as ApprovalChunk)
    callbacks.onApprovalChunk(message.sessionId)
  } catch (error) {
    console.error('Failed to parse approval request chunk payload.', error)
    callbacks.setSessionError(message.sessionId, '审批请求解析失败。')
  }
}

function addAgentEventChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const event = JSON.parse(chunk.content) as AgentEventPayload
    message.chunks.push({
      ...chunk,
      event
    } as AgentEventChunk)

    if (event.stage === 'plan_draft_failed') {
      callbacks.setSessionError(message.sessionId, formatPlanDraftFailure(event))
    }
  } catch (error) {
    console.error('Failed to parse agent event chunk payload.', error)
    callbacks.setSessionError(message.sessionId, '运行状态事件解析失败。')
  }
}

function addAgentTaskChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const task = JSON.parse(chunk.content) as AgentTask
    callbacks.onAgentTaskChunk?.(message.sessionId, task)
  } catch (error) {
    console.error('Failed to parse agent task chunk payload.', error)
    callbacks.setSessionError(message.sessionId, '任务状态解析失败。')
  }
}

function addErrorChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const payload = JSON.parse(chunk.content) as ChatErrorPayload
    const userFacingMessage = resolveChatErrorMessage(payload)

    if (userFacingMessage) {
      addTextChunk(message, {
        ...chunk,
        type: ChunkType.Text,
        content: userFacingMessage
      })
    }

    callbacks.setSessionError(message.sessionId, resolveChatErrorMessage(payload))
  } catch (error) {
    console.error('Failed to parse chat error chunk payload.', error)
    callbacks.setSessionError(message.sessionId, '请求失败，请稍后重试。')
  }
}
