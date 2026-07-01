import { describe, expect, it } from 'vitest'
import { createSessionScopedState, resetSessionScopedState } from '@/stores/sessionScopedState'

describe('sessionScopedState', () => {
  it('resets every session-scoped field through one entry point', () => {
    const state = createSessionScopedState()

    state.agentTasks = [{ id: 'task-1' } as never]
    state.agentApprovals = [{ id: 'approval-1' } as never]
    state.agentAuditSummary = [{ id: 'audit-1' } as never]
    state.timelineEvents = [{ sequence: 1 } as never]
    state.uploadedFiles = [{ id: 'upload-1' } as never]
    state.currentWorkspace = { workspaceCode: 'ws-1' } as never
    state.currentArtifactPreview = { artifactId: 'artifact-1' } as never
    state.chartPreview = { labels: ['A'], values: [1] }
    state.isAgentBusy = true
    state.chatRunStatus = {
      sessionId: 'session-1',
      messageKey: 'message-1',
      phase: 'querying',
      startedAt: '2026-07-01T00:00:00Z',
      elapsedMs: 1200,
      summary: '正在查询 Cloud 只读数据'
    }

    resetSessionScopedState(state)

    expect(state).toEqual(createSessionScopedState())
  })
})
