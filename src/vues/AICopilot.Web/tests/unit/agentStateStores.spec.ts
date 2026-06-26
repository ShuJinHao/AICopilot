import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAgentCatalogStore } from '@/stores/agentCatalogStore'
import { useAgentTaskStore } from '@/stores/agentTaskStore'
import { useArtifactWorkspaceStore } from '@/stores/artifactWorkspaceStore'

describe('agent state stores', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('resets task runtime state through agentTaskStore', () => {
    const store = useAgentTaskStore()

    store.agentTasks = [{ id: 'task-1' } as never]
    store.agentApprovals = [{ id: 'approval-1', status: 'Pending' } as never]
    store.agentAuditSummary = [{ id: 'audit-1' } as never]
    store.timelineEvents = [{ sequence: 1, eventType: 'AgentTaskPlanCreated' } as never]
    store.isAgentBusy = true

    store.reset()

    expect(store.agentTasks).toEqual([])
    expect(store.agentApprovals).toEqual([])
    expect(store.agentAuditSummary).toEqual([])
    expect(store.timelineEvents).toEqual([])
    expect(store.isAgentBusy).toBe(false)
  })

  it('resets artifact workspace state through artifactWorkspaceStore', () => {
    const store = useArtifactWorkspaceStore()

    store.uploadedFiles = [{ id: 'upload-1' } as never]
    store.currentWorkspace = { workspaceCode: 'ws-1' } as never
    store.currentArtifactPreview = { artifactId: 'artifact-1' } as never
    store.chartPreview = { labels: ['A'], values: [1] }

    store.reset()

    expect(store.uploadedFiles).toEqual([])
    expect(store.currentWorkspace).toBeNull()
    expect(store.currentArtifactPreview).toBeNull()
    expect(store.chartPreview).toBeNull()
  })

  it('clears session plan selections without clearing cached catalog data', () => {
    const store = useAgentCatalogStore()

    store.availableSkills = [{ skillCode: 'general_report' } as never]
    store.availablePluginTools = [{ toolCode: 'rag_search' } as never]
    store.availableKnowledgeBases = [{ id: 'kb-1' } as never]
    store.selectedSkillCode = 'general_report'
    store.selectedToolCodes = ['rag_search']
    store.selectedKnowledgeBaseId = 'kb-1'

    store.resetSelections()

    expect(store.availableSkills).toHaveLength(1)
    expect(store.availablePluginTools).toHaveLength(1)
    expect(store.availableKnowledgeBases).toHaveLength(1)
    expect(store.selectedSkillCode).toBeNull()
    expect(store.selectedToolCodes).toEqual([])
    expect(store.selectedKnowledgeBaseId).toBeNull()
  })
})
