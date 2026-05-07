import {
  ChunkType,
  type ChatChunk,
  type ChatErrorPayload,
  type FunctionApprovalRequest,
  type IntentResult
} from '@/types/protocols'
import type {
  ApprovalChunk,
  ChatMessage,
  FunctionCall,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'
import { resolveChatErrorMessage } from '@/stores/chatErrorStore'
import { parseWidgetFromTextChunk } from './widgetPayloadParser'

export interface ChunkReducerCallbacks {
  setSessionError: (sessionId: string, message: string) => void
  onApprovalChunk: (sessionId: string) => void
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
      } catch {
        addTextChunk(message, chunk)
      }
      break
    case ChunkType.ApprovalRequest:
      addApprovalRequestChunk(message, chunk, callbacks)
      break
    case ChunkType.Error:
      addErrorChunk(message, chunk, callbacks)
      break
  }
}

export function getErrorCode(chunk: ChatChunk) {
  try {
    const payload = JSON.parse(chunk.content) as ChatErrorPayload
    return payload.code ?? null
  } catch {
    return null
  }
}

function addTextChunk(message: ChatMessage, chunk: ChatChunk) {
  const previousChunk = message.chunks[message.chunks.length - 1]

  if (!previousChunk) {
    message.chunks.push(chunk)
    return
  }

  if (previousChunk.source === chunk.source && previousChunk.type === ChunkType.Text) {
    previousChunk.content += chunk.content
    return
  }

  message.chunks.push(chunk)
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
  } catch {
    addTextChunk(message, chunk)
  }
}

function addFunctionCallChunk(message: ChatMessage, chunk: ChatChunk) {
  try {
    const functionCall = JSON.parse(chunk.content) as FunctionCall
    functionCall.status = 'calling'
    message.chunks.push({ ...chunk, functionCall } as FunctionCallChunk)
  } catch {
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
  } catch {
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
  } catch {
    callbacks.setSessionError(message.sessionId, '审批请求解析失败。')
  }
}

function addErrorChunk(
  message: ChatMessage,
  chunk: ChatChunk,
  callbacks: ChunkReducerCallbacks
) {
  try {
    const payload = JSON.parse(chunk.content) as ChatErrorPayload
    const userFacingMessage = payload.userFacingMessage?.trim() || payload.detail?.trim()

    if (userFacingMessage) {
      addTextChunk(message, {
        ...chunk,
        type: ChunkType.Text,
        content: userFacingMessage
      })
    }

    callbacks.setSessionError(message.sessionId, resolveChatErrorMessage(payload))
  } catch {
    callbacks.setSessionError(message.sessionId, '请求失败，请稍后重试。')
  }
}
