import { ChunkType, type ChatChunk } from '@/types/protocols'
import type { ChatMessage, FunctionCallChunk } from '@/types/models'

export interface ParsedWidgetPayload {
  widget: unknown
  remainingText: string
}

export function parseWidgetFromTextChunk(
  message: ChatMessage,
  chunk: ChatChunk
): ParsedWidgetPayload | null {
  if (chunk.type !== ChunkType.Text) {
    return null
  }

  const content = chunk.content.trim()
  if (!content.includes('"visual_decision"') && !content.includes('"VisualDecision"')) {
    return null
  }

  try {
    const jsonMatch = content.match(/(\{[\s\S]*"visual_decision"[\s\S]*?\})/)
    if (!jsonMatch) {
      return null
    }

    const payload = JSON.parse(jsonMatch[0]) as Record<string, unknown>
    const decision = payload.visual_decision || payload.VisualDecision
    if (!decision) {
      return null
    }

    return {
      widget: { ...payload, data: resolveWidgetData(message, payload, decision) },
      remainingText: content.replace(jsonMatch[0], '').trim()
    }
  } catch {
    return null
  }
}

function resolveWidgetData(
  message: ChatMessage,
  payload: Record<string, unknown>,
  decision: unknown
) {
  if (Array.isArray(payload.data) && payload.data.length > 0) {
    return payload.data
  }

  if (
    typeof decision === 'object' &&
    decision !== null &&
    Array.isArray((decision as { data?: unknown[] }).data) &&
    ((decision as { data?: unknown[] }).data?.length ?? 0) > 0
  ) {
    return (decision as { data: unknown[] }).data
  }

  for (let index = message.chunks.length - 1; index >= 0; index -= 1) {
    const existingChunk = message.chunks[index]
    if (!existingChunk || existingChunk.type !== ChunkType.FunctionCall) {
      continue
    }

    const functionCall = (existingChunk as FunctionCallChunk).functionCall
    if (functionCall.status !== 'completed' || !functionCall.result) {
      continue
    }

    try {
      const parsed = JSON.parse(functionCall.result)
      if (Array.isArray(parsed) && parsed.length > 0) {
        return parsed
      }
    } catch {
      continue
    }
  }

  return []
}
