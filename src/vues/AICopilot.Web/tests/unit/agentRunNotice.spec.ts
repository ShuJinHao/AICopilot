import { describe, expect, it } from 'vitest'
import { resolveAgentRunNotice } from '@/composables/useAgentWorkbench'
import type { AgentTask } from '@/types/protocols'

function createTask(overrides: Partial<AgentTask> = {}): AgentTask {
  return {
    id: 'task-1',
    taskCode: 'AGT-1',
    sessionId: 'session-1',
    title: '设备日志分析',
    goal: '查看 DEV-001 最近 24 小时设备日志',
    taskType: 'CloudDataReport',
    status: 'PlanApproved',
    riskLevel: 'Low',
    modelId: null,
    workspaceId: null,
    workspaceCode: null,
    planJson: '{}',
    finalSummary: null,
    createdAt: '2026-06-22T07:00:00Z',
    updatedAt: '2026-06-22T07:00:00Z',
    completedAt: null,
    steps: [],
    pendingApprovalCount: 0,
    lastFailureReason: null,
    canRun: true,
    canRetry: false,
    canSubmitFinalReview: false,
    canApproveFinal: false,
    failureSummary: null,
    activeRunAttemptId: null,
    runAttemptCount: 0,
    isRunInProgress: false,
    queuedRunId: null,
    runQueueStatus: null,
    isRunQueued: false,
    ...overrides
  }
}

describe('resolveAgentRunNotice', () => {
  it('shows queued worker wait state with queue id evidence', () => {
    const notice = resolveAgentRunNotice(createTask({
      isRunQueued: true,
      queuedRunId: '12345678-abcd-4abc-8abc-1234567890ab',
      runQueueStatus: 'Queued'
    }))

    expect(notice).toEqual({
      tone: 'warning',
      title: '任务已入队，等待 Worker 接手',
      detail: '队列状态：Queued · 运行单 12345678'
    })
  })

  it('shows leased worker state when the worker has picked up the run', () => {
    const notice = resolveAgentRunNotice(createTask({
      isRunQueued: true,
      runQueueStatus: 'Leased'
    }))

    expect(notice?.tone).toBe('success')
    expect(notice?.title).toBe('Worker 已接手执行')
    expect(notice?.detail).toBe('队列状态：Leased')
  })

  it('shows active execution state while steps are running', () => {
    const notice = resolveAgentRunNotice(createTask({
      status: 'Running',
      isRunInProgress: true
    }))

    expect(notice?.tone).toBe('warning')
    expect(notice?.title).toBe('正在执行计划步骤')
    expect(notice?.detail).toContain('自动刷新')
  })

  it('surfaces failed task code and safe detail', () => {
    const notice = resolveAgentRunNotice(createTask({
      status: 'Failed',
      canRun: false,
      canRetry: true,
      failureSummary: {
        stepIndex: 2,
        toolCode: 'query_cloud_data_readonly',
        errorCode: 'cloud_readonly_tool_disabled',
        safeMessage: 'Cloud readonly tool is disabled.',
        canRetry: true,
        nextAction: '检查模型和工具配置后重试。'
      }
    }))

    expect(notice).toEqual({
      tone: 'danger',
      title: 'cloud_readonly_tool_disabled',
      detail: 'Cloud readonly tool is disabled.'
    })
  })
})
