import { describe, expect, it } from 'vitest'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ChatMessage, FunctionCallChunk } from '@/types/models'
import { parseWidgetFromTextChunk } from '@/protocol/widgetPayloadParser'

function createMessage(chunks: ChatMessage['chunks'] = []): ChatMessage {
  return {
    sessionId: 'session-1',
    role: MessageRole.Assistant,
    chunks,
    isStreaming: false,
    timestamp: 1
  }
}

describe('widgetPayloadParser', () => {
  it('uses explicit widget data when the visual decision payload includes data', () => {
    const payload = {
      visual_decision: 'DataTable',
      data: [{ id: 1 }]
    }
    const parsed = parseWidgetFromTextChunk(createMessage(), {
      source: 'assistant',
      type: ChunkType.Text,
      content: JSON.stringify(payload)
    })

    expect(parsed?.widget).toMatchObject({ data: [{ id: 1 }] })
    expect(parsed?.remainingText).toBe('')
  })

  it('falls back to the latest completed function result when widget data is missing', () => {
    const previousResult: FunctionCallChunk = {
      source: 'tool',
      type: ChunkType.FunctionCall,
      content: '{}',
      functionCall: {
        id: 'call-1',
        name: 'query',
        args: '{}',
        result: JSON.stringify([{ name: 'device-a' }]),
        status: 'completed'
      }
    }

    const parsed = parseWidgetFromTextChunk(createMessage([previousResult]), {
      source: 'assistant',
      type: ChunkType.Text,
      content: JSON.stringify({ visual_decision: 'DataTable' })
    })

    expect(parsed?.widget).toMatchObject({ data: [{ name: 'device-a' }] })
  })

  it('ignores non-text chunks', () => {
    const parsed = parseWidgetFromTextChunk(createMessage(), {
      source: 'assistant',
      type: ChunkType.Intent,
      content: '{"visual_decision":"DataTable"}'
    })

    expect(parsed).toBeNull()
  })
})
