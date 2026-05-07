import { describe, expect, it, vi } from 'vitest'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage, FunctionCallChunk } from '@/types/models'
import { getErrorCode, processChunk } from '@/protocol/chunkReducer'

function createMessage(): ChatMessage {
  return {
    sessionId: 'session-1',
    role: MessageRole.Assistant,
    chunks: [],
    isStreaming: true,
    timestamp: 1
  }
}

describe('chunkReducer', () => {
  it('merges adjacent text chunks from the same source', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(message, { source: 'assistant', type: ChunkType.Text, content: 'hello' }, callbacks)
    processChunk(message, { source: 'assistant', type: ChunkType.Text, content: ' world' }, callbacks)

    expect(message.chunks).toHaveLength(1)
    expect(message.chunks[0]?.content).toBe('hello world')
  })

  it('matches function results back to the original function call', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'tool',
        type: ChunkType.FunctionCall,
        content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}' })
      },
      callbacks
    )
    processChunk(
      message,
      {
        source: 'tool',
        type: ChunkType.FunctionResult,
        content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}', result: '[1]' })
      },
      callbacks
    )

    const callChunk = message.chunks[0] as FunctionCallChunk
    expect(callChunk.functionCall.status).toBe('completed')
    expect(callChunk.functionCall.result).toBe('[1]')
  })

  it('adds approval requests as pending chunks and notifies approval state', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'executor',
        type: ChunkType.ApprovalRequest,
        content: JSON.stringify({
          callId: 'approval-1',
          name: 'controlled tool',
          targetType: 'McpServer',
          targetName: 'cloud-read',
          toolName: 'queryDeviceLogs',
          args: {},
          requiresOnsiteAttestation: false
        })
      },
      callbacks
    )

    const approvalChunk = message.chunks[0] as ApprovalChunk
    expect(approvalChunk.status).toBe('pending')
    expect(approvalChunk.request.callId).toBe('approval-1')
    expect(callbacks.onApprovalChunk).toHaveBeenCalledWith('session-1')
  })

  it('extracts structured error codes', () => {
    expect(
      getErrorCode({
        source: 'executor',
        type: ChunkType.Error,
        content: JSON.stringify({ code: 'approval_pending' })
      })
    ).toBe('approval_pending')
    expect(getErrorCode({ source: 'executor', type: ChunkType.Error, content: 'not-json' })).toBeNull()
  })
})
