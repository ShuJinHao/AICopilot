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
    timestamp: 1,
  }
}

function callbacks() {
  return {
    setSessionError: vi.fn(),
    onApprovalChunk: vi.fn(),
    onAgentSessionState: vi.fn(),
  }
}

describe('chunkReducer current Harness protocol', () => {
  it('merges adjacent sanitized text chunks from the same source', () => {
    const message = createMessage()
    const handlers = callbacks()

    processChunk(
      message,
      { source: 'HarnessAgent', type: ChunkType.Text, content: '<think>private</think>hello' },
      handlers,
    )
    processChunk(
      message,
      { source: 'HarnessAgent', type: ChunkType.Text, content: ' world' },
      handlers,
    )

    expect(message.chunks).toHaveLength(1)
    expect(message.chunks[0]?.content).toBe('hello world')
  })

  it('keeps model-authored widget JSON as ordinary text', () => {
    const message = createMessage()
    const content = JSON.stringify({ type: 'Chart', data: [{ secret: 'model-authored' }] })

    processChunk(message, { source: 'HarnessAgent', type: ChunkType.Text, content }, callbacks())

    expect(message.chunks).toContainEqual(
      expect.objectContaining({ type: ChunkType.Text, content }),
    )
    expect(message.chunks).not.toContainEqual(expect.objectContaining({ type: ChunkType.Widget }))
  })

  it('renders only an explicit trusted server widget chunk as a widget', () => {
    const message = createMessage()

    processChunk(
      message,
      {
        source: 'BusinessQuery',
        type: ChunkType.Widget,
        content: JSON.stringify({ type: 'Chart', data: [{ label: '设备 A', value: 3 }] }),
      },
      callbacks(),
    )

    expect(message.chunks).toContainEqual(
      expect.objectContaining({
        type: ChunkType.Widget,
        widget: expect.objectContaining({ type: 'Chart' }),
      }),
    )
  })

  it('matches function results back to the original function call', () => {
    const message = createMessage()
    const handlers = callbacks()

    processChunk(
      message,
      {
        source: 'HarnessAgent',
        type: ChunkType.FunctionCall,
        content: JSON.stringify({ id: 'call-1', name: 'queryDeviceLogs', args: '{}' }),
      },
      handlers,
    )
    processChunk(
      message,
      {
        source: 'HarnessAgent',
        type: ChunkType.FunctionResult,
        content: JSON.stringify({ id: 'call-1', result: '[1]' }),
      },
      handlers,
    )

    const callChunk = message.chunks[0] as FunctionCallChunk
    expect(callChunk.functionCall.status).toBe('completed')
    expect(callChunk.functionCall.result).toBe('[1]')
  })

  it('updates only actual answer-model provenance without adding a visible chunk', () => {
    const message = createMessage()

    processChunk(
      message,
      {
        source: 'HarnessAgent',
        type: ChunkType.Metadata,
        content: JSON.stringify({
          finalModelId: 'model-final',
          finalModelName: 'deepseek-v4-pro',
          contextWindowTokens: 128000,
          maxOutputTokens: 4096,
        }),
      },
      callbacks(),
    )

    expect(message.chunks).toHaveLength(0)
    expect(message).toMatchObject({
      finalModelId: 'model-final',
      finalModelName: 'deepseek-v4-pro',
      contextWindowTokens: 128000,
      maxOutputTokens: 4096,
    })
  })

  it('projects authoritative persisted AgentSession events', () => {
    const message = createMessage()
    const handlers = callbacks()

    processChunk(
      message,
      {
        source: 'HarnessAgent',
        type: ChunkType.AgentEvent,
        content: JSON.stringify({
          stage: 'agent_session_state',
          detail: 'persisted',
          recoverable: true,
          metadata: {},
          sessionId: 'session-1',
          mode: 'execute',
          status: 'Ready',
          version: 4,
          pendingApproval: false,
        }),
      },
      handlers,
    )

    expect(handlers.onAgentSessionState).toHaveBeenCalledWith(
      expect.objectContaining({
        sessionId: 'session-1',
        mode: 'execute',
        status: 'Ready',
        version: 4,
      }),
    )
  })

  it('adds a single approval request as a pending card', () => {
    const message = createMessage()
    const handlers = callbacks()

    processChunk(
      message,
      {
        source: 'HarnessAgent',
        type: ChunkType.ApprovalRequest,
        content: JSON.stringify({
          callId: 'approval-1',
          name: 'controlled tool',
          targetType: 'McpServer',
          targetName: 'cloud-read',
          toolName: 'queryDeviceLogs',
          args: {},
        }),
      },
      handlers,
    )

    const approvalChunk = message.chunks[0] as ApprovalChunk
    expect(approvalChunk.status).toBe('pending')
    expect(approvalChunk.request.callId).toBe('approval-1')
    expect(handlers.onApprovalChunk).toHaveBeenCalledWith('session-1')
  })

  it('reports malformed approval and event payloads without creating authority', () => {
    const message = createMessage()
    const handlers = callbacks()

    processChunk(
      message,
      { source: 'HarnessAgent', type: ChunkType.ApprovalRequest, content: 'not-json' },
      handlers,
    )
    processChunk(
      message,
      { source: 'HarnessAgent', type: ChunkType.AgentEvent, content: 'not-json' },
      handlers,
    )

    expect(handlers.onApprovalChunk).not.toHaveBeenCalled()
    expect(handlers.onAgentSessionState).not.toHaveBeenCalled()
    expect(handlers.setSessionError).toHaveBeenCalledTimes(2)
  })

  it('extracts structured error codes', () => {
    expect(
      getErrorCode({
        source: 'HarnessAgent',
        type: ChunkType.Error,
        content: JSON.stringify({ code: 'approval_pending' }),
      }),
    ).toBe('approval_pending')
    expect(
      getErrorCode({ source: 'HarnessAgent', type: ChunkType.Error, content: 'not-json' }),
    ).toBeNull()
  })

  it('renders the safe primary message and forwards complete safe error fields', () => {
    const safeMessage = createMessage()
    const safeHandlers = callbacks()
    processChunk(
      safeMessage,
      {
        source: 'HarnessAgent',
        type: ChunkType.Error,
        content: JSON.stringify({
          code: 'model_request_timeout',
          detail: 'Model provider did not return in time.',
          userFacingMessage: '模型这次响应超时，请稍后重试。',
        }),
      },
      safeHandlers,
    )

    const unknownMessage = createMessage()
    const unknownHandlers = callbacks()
    processChunk(
      unknownMessage,
      {
        source: 'HarnessAgent',
        type: ChunkType.Error,
        content: JSON.stringify({
          code: 'unknown_backend_code',
          detail: '工具调用未通过服务端安全校验。',
        }),
      },
      unknownHandlers,
    )

    expect(safeMessage.chunks[0]?.content).toBe('模型这次响应超时，请稍后重试。')
    expect(unknownMessage.chunks[0]?.content).toBe('请求失败，请稍后重试。')
    expect(JSON.stringify(unknownMessage)).not.toContain('工具调用未通过服务端安全校验。')
    expect(safeHandlers.setSessionError).toHaveBeenCalledWith('session-1', {
      code: 'model_request_timeout',
      detail: 'Model provider did not return in time.',
      userFacingMessage: '模型这次响应超时，请稍后重试。',
    })
    expect(unknownHandlers.setSessionError).toHaveBeenCalledWith('session-1', {
      code: 'unknown_backend_code',
      detail: '工具调用未通过服务端安全校验。',
    })
  })
})
