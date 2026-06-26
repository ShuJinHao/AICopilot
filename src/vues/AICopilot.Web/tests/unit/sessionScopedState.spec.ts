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

    resetSessionScopedState(state)

    expect(state).toEqual(createSessionScopedState())
  })
})
