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

  it('updates assistant model metadata without adding a visible chunk', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'executor',
        type: ChunkType.Metadata,
        content: JSON.stringify({
          finalModelId: 'model-final',
          finalModelName: 'deepseek-v4-pro',
          routingModelId: 'model-routing',
          routingModelName: 'deepseek-v4-flash',
          contextWindowTokens: 1000000,
          maxOutputTokens: 4096
        })
      },
      callbacks
    )

    expect(message.chunks).toHaveLength(0)
    expect(message.finalModelId).toBe('model-final')
    expect(message.finalModelName).toBe('deepseek-v4-pro')
    expect(message.routingModelId).toBe('model-routing')
    expect(message.routingModelName).toBe('deepseek-v4-flash')
    expect(message.contextWindowTokens).toBe(1000000)
    expect(message.maxOutputTokens).toBe(4096)
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

  it('routes agent task chunks to the task callback without adding visible chunks', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn(),
      onAgentTaskChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'PlanAgentTaskStreamHandler',
        type: ChunkType.AgentTask,
        content: JSON.stringify({
          id: 'task-1',
          taskCode: 'AGT-001',
          sessionId: 'session-1',
          title: '设备日志分析',
          goal: '查看 DEV-001 最近 24 小时日志',
          taskType: 'CloudDataReport',
          status: 'Draft',
          riskLevel: 'Low',
          planJson: '{}',
          createdAt: '2026-06-24T00:00:00Z',
          updatedAt: '2026-06-24T00:00:00Z',
          steps: [],
          canRun: false,
          canSubmitFinalReview: false,
          canApproveFinal: false,
          isRunInProgress: false,
          isRunQueued: false
        })
      },
      callbacks
    )

    expect(message.chunks).toHaveLength(0)
    expect(callbacks.onAgentTaskChunk).toHaveBeenCalledWith(
      'session-1',
      expect.objectContaining({ id: 'task-1', status: 'Draft' })
    )
  })

  it('stores agent event chunks as structured runtime details', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'PlanAgentTaskStreamHandler',
        type: ChunkType.AgentEvent,
        content: JSON.stringify({
          stage: 'capability_discovery',
          detail: 'Discovering capabilities without execution.',
          recoverable: true,
          suggestedAction: null,
          metadata: { executesCloudQuery: 'false' }
        })
      },
      callbacks
    )

    expect(message.chunks).toHaveLength(1)
    expect(message.chunks[0]?.type).toBe(ChunkType.AgentEvent)
    expect(message.chunks[0]).toMatchObject({
      event: {
        stage: 'capability_discovery',
        metadata: { executesCloudQuery: 'false' }
      }
    })
  })

  it('promotes failed plan draft agent events into session errors', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'PlanAgentTaskStreamHandler',
        type: ChunkType.AgentEvent,
        content: JSON.stringify({
          stage: 'plan_draft_failed',
          code: 'planner_model_unavailable',
          detail: 'Planner model is not configured.',
          recoverable: true,
          suggestedAction: '请先配置模型。',
          metadata: {}
        })
      },
      callbacks
    )

    expect(callbacks.setSessionError).toHaveBeenCalledWith(
      'session-1',
      'Planner model is not configured. 请先配置模型。'
    )
  })

  it('strips model thinking tags from visible text chunks', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'assistant',
        type: ChunkType.Text,
        content: '<mm:think>内部推理</mm:think>最终回答'
      },
      callbacks
    )
    processChunk(
      message,
      {
        source: 'assistant',
        type: ChunkType.Text,
        content: '\nmm:think用户说了半句\n继续回答'
      },
      callbacks
    )

    expect(message.chunks).toHaveLength(1)
    expect(message.chunks[0]?.content).toBe('最终回答继续回答')
  })

  it('reports invalid agent task chunks as session errors', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn(),
      onAgentTaskChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'PlanAgentTaskStreamHandler',
        type: ChunkType.AgentTask,
        content: 'not-json'
      },
      callbacks
    )

    expect(callbacks.onAgentTaskChunk).not.toHaveBeenCalled()
    expect(callbacks.setSessionError).toHaveBeenCalledWith('session-1', '任务状态解析失败。')
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

  it('shows backend user-facing message from error chunks', () => {
    const message = createMessage()
    const callbacks = {
      setSessionError: vi.fn(),
      onApprovalChunk: vi.fn()
    }

    processChunk(
      message,
      {
        source: 'ChatStreamHandler',
        type: ChunkType.Error,
        content: JSON.stringify({
          code: 'model_request_timeout',
          detail: 'Model provider did not return in time.',
          userFacingMessage: '模型这次响应超时，请稍后重试。'
        })
      },
      callbacks
    )

    expect(message.chunks).toContainEqual(expect.objectContaining({
      type: ChunkType.Text,
      content: '模型这次响应超时，请稍后重试。'
    }))
    expect(callbacks.setSessionError).toHaveBeenCalledWith('session-1', '模型这次响应超时，请稍后重试。')
  })
})
