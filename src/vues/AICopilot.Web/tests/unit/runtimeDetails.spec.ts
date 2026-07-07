import { describe, expect, it } from 'vitest'
import { buildRuntimeDetails, summarizeFunctionArgs, summarizeFunctionResult } from '@/protocol/runtimeDetails'
import { ChunkType, MessageRole } from '@/types/protocols'
import type {
  AgentEventChunk,
  ChatMessage,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'
import type { ChatRunStatus } from '@/stores/sessionScopedState'

function createAssistantMessage(chunks: ChatMessage['chunks'] = []): ChatMessage {
  return {
    sessionId: 'session-1',
    role: MessageRole.Assistant,
    chunks,
    isStreaming: false,
    timestamp: 1,
    finalModelName: 'deepseek-v4-pro',
    routingModelName: 'deepseek-v4-flash',
    contextWindowTokens: 128000,
    maxOutputTokens: 4096
  }
}

describe('runtimeDetails', () => {
  it('builds folded runtime facts from assistant chunks and run status', () => {
    const message = createAssistantMessage([
      {
        source: 'DataAnalysisExecutor',
        type: ChunkType.AgentEvent,
        content: '{}',
        event: {
          stage: 'capability_discovery',
          detail: 'Discovering readonly capability.',
          recoverable: true,
          suggestedAction: null,
          metadata: { executesCloudQuery: 'false' }
        }
      } as AgentEventChunk,
      {
        source: 'IntentClassifier',
        type: ChunkType.Intent,
        content: '[]',
        intents: [{ intent: 'DataAnalysis', confidence: 0.91 }]
      } as IntentChunk,
      {
        source: 'DataAnalysisExecutor',
        type: ChunkType.FunctionCall,
        content: '{}',
        functionCall: {
          id: 'call-1',
          name: 'queryDeviceLogs',
          status: 'completed',
          args: JSON.stringify({ deviceCode: 'DEV-001', startTime: '2026-06-30T00:00:00Z', limit: 20 }),
          result: JSON.stringify({ rows: [{ id: 1 }, { id: 2 }], isTruncated: true })
        }
      } as FunctionCallChunk,
      {
        source: 'DataAnalysisExecutor',
        type: ChunkType.Widget,
        content: '{}',
        widget: {
          id: 'table-1',
          type: 'DataTable',
          title: '设备日志证据表',
          description: '',
          data: {
            columns: [],
            rows: [{ id: 1 }, { id: 2 }]
          }
        }
      } as WidgetChunk
    ])
    const status: ChatRunStatus = {
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'completed',
      startedAt: '2026-07-01T00:00:00Z',
      completedAt: '2026-07-01T00:00:12Z',
      elapsedMs: 12000,
      summary: '回答已完成',
      queryCount: 1,
      returnedRows: 2
    }

    const details = buildRuntimeDetails(message, status)

    expect(details.count).toBeGreaterThan(0)
    expect(details.status).toMatchObject({
      phaseLabel: '已完成',
      elapsedText: '12s',
      summary: '回答已完成'
    })
    expect(details.modelBadges.map((item) => item.text)).toContain('回答模型：deepseek-v4-pro')
    expect(details.events[0]).toMatchObject({ label: '发现能力', statusText: '可继续' })
    expect(details.intents[0]).toMatchObject({ name: 'DataAnalysis', confidenceText: '91%' })
    expect(details.tools[0]).toMatchObject({
      name: 'queryDeviceLogs',
      argsSummary: '设备：DEV-001 · 开始：2026-06-30T00:00:00Z · 限制：20',
      resultSummary: '返回 2 条记录，结果已截断'
    })
    expect(details.widgets[0]).toMatchObject({
      typeLabel: '数据表',
      title: '设备日志证据表',
      summary: '展示 2 行证据'
    })
  })

  it('redacts unsafe tool arguments and raw result content', () => {
    const message = createAssistantMessage([
      {
        source: 'DataAnalysisExecutor',
        type: ChunkType.FunctionCall,
        content: '{}',
        functionCall: {
          id: 'call-1',
          name: 'queryDeviceLogs',
          status: 'completed',
          args: JSON.stringify({
            deviceCode: 'DEV-001',
            sql: 'SELECT * FROM DeviceLogs',
            password: 'secret',
            sourceName: 'CloudReadonly',
            nested: {
              connectionString: 'Server=prod;Password=secret'
            }
          }),
          result: JSON.stringify({
            rows: [
              {
                message: 'Motor overload at station 1',
                connectionString: 'Server=prod;Password=secret'
              }
            ]
          })
        }
      } as FunctionCallChunk
    ])

    const details = buildRuntimeDetails(message)
    const serialized = JSON.stringify(details)

    expect(details.tools[0]?.argsSummary).toBe('设备：DEV-001')
    expect(details.tools[0]?.resultSummary).toBe('返回 1 条记录')
    expect(serialized).not.toContain('SELECT')
    expect(serialized).not.toContain('password')
    expect(serialized).not.toContain('CloudReadonly')
    expect(serialized).not.toContain('connectionString')
    expect(serialized).not.toContain('Motor overload')
  })

  it('redacts unsafe run status summary and error text', () => {
    const message = createAssistantMessage()
    const failedStatus: ChatRunStatus = {
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'failed',
      startedAt: '2026-07-01T00:00:00Z',
      completedAt: '2026-07-01T00:00:12Z',
      elapsedMs: 12000,
      error: {
        code: 'model_provider_unavailable',
        message: 'Provider endpoint http://model.internal.example failed with token=secret and SQL SELECT * FROM device_logs'
      }
    }
    const runningStatus: ChatRunStatus = {
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'querying',
      startedAt: '2026-07-01T00:00:00Z',
      elapsedMs: 12000,
      summary: 'sourceName=device_logs tableName=DeviceLogs endpoint=/internal/model'
    }

    const failedDetails = buildRuntimeDetails(message, failedStatus)
    const runningDetails = buildRuntimeDetails(message, runningStatus)
    const serialized = JSON.stringify([failedDetails, runningDetails])

    expect(failedDetails.status?.summary).toBe('运行失败，原始错误未在详情中展开')
    expect(runningDetails.status?.summary).toBe('运行状态已记录，原始详情未在详情中展开')
    expect(failedDetails.status?.facts.map((fact) => fact.text)).toContain('model_provider_unavailable')
    expect(serialized).not.toContain('model.internal.example')
    expect(serialized).not.toContain('token=secret')
    expect(serialized).not.toContain('SELECT')
    expect(serialized).not.toContain('device_logs')
    expect(serialized).not.toContain('sourceName')
    expect(serialized).not.toContain('tableName')
    expect(serialized).not.toContain('/internal/model')
  })

  it('keeps unstructured parameters and results folded without raw text', () => {
    expect(summarizeFunctionArgs('SELECT * FROM DeviceLogs')).toBe('参数已记录，因无法结构化解析未展开')
    expect(summarizeFunctionResult('internal endpoint failed with password=secret', 'completed')).toBe(
      '工具结果已记录，未在详情中展开原文'
    )
  })

  it('does not build runtime details for user messages', () => {
    const details = buildRuntimeDetails({
      ...createAssistantMessage(),
      role: MessageRole.User,
      finalModelName: 'should-not-render'
    })

    expect(details.count).toBe(0)
    expect(details.modelBadges).toHaveLength(0)
  })
})
