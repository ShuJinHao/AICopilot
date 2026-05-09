import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useRagStore } from '@/stores/ragStore'

const ragServiceMock = vi.hoisted(() => ({
  getEmbeddingModels: vi.fn(),
  getKnowledgeBases: vi.fn(),
  getDocuments: vi.fn(),
  getEmbeddingModel: vi.fn(),
  createEmbeddingModel: vi.fn(),
  updateEmbeddingModel: vi.fn(),
  deleteEmbeddingModel: vi.fn(),
  getKnowledgeBase: vi.fn(),
  createKnowledgeBase: vi.fn(),
  updateKnowledgeBase: vi.fn(),
  deleteKnowledgeBase: vi.fn(),
  uploadDocument: vi.fn(),
  deleteDocument: vi.fn(),
  updateDocumentGovernance: vi.fn(),
  searchKnowledgeBase: vi.fn()
}))

vi.mock('@/services/ragService', () => ({
  ragService: ragServiceMock
}))

vi.mock('@/stores/authStore', () => ({
  useAuthStore: () => ({
    hasAnyPermission: () => true,
    hasPermission: () => true
  })
}))

function resetRagServiceMocks() {
  vi.clearAllMocks()
  ragServiceMock.getEmbeddingModels.mockResolvedValue([])
  ragServiceMock.getKnowledgeBases.mockResolvedValue([])
  ragServiceMock.getDocuments.mockResolvedValue([])
  ragServiceMock.uploadDocument.mockResolvedValue(undefined)
  ragServiceMock.deleteDocument.mockResolvedValue(undefined)
  ragServiceMock.updateDocumentGovernance.mockResolvedValue(undefined)
  ragServiceMock.searchKnowledgeBase.mockResolvedValue([])
}

describe('ragStore facade', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetRagServiceMocks()
  })

  it('keeps the public RAG action surface while delegating to domain stores', () => {
    const store = useRagStore()

    expect(typeof store.saveEmbeddingModel).toBe('function')
    expect(typeof store.selectKnowledgeBase).toBe('function')
    expect(typeof store.uploadDocument).toBe('function')
    expect(typeof store.saveDocumentGovernance).toBe('function')
    expect(typeof store.searchKnowledgeBase).toBe('function')
  })

  it('preserves knowledge base selection when refreshed list still contains it', async () => {
    ragServiceMock.getKnowledgeBases.mockResolvedValueOnce([
      { id: 'kb-1', name: 'A' },
      { id: 'kb-2', name: 'B' }
    ])
    const store = useRagStore()
    store.selectedKnowledgeBaseId = 'kb-2'

    await store.refreshKnowledgeBases()

    expect(store.selectedKnowledgeBaseId).toBe('kb-2')

    ragServiceMock.getKnowledgeBases.mockResolvedValueOnce([{ id: 'kb-1', name: 'A' }])
    await store.refreshKnowledgeBases()

    expect(store.selectedKnowledgeBaseId).toBe('kb-1')
  })

  it('clears search state and refreshes documents when selecting a knowledge base', async () => {
    ragServiceMock.getDocuments.mockResolvedValue([{ id: 1, name: 'doc.pdf' }])
    const store = useRagStore()
    store.searchResults = [{ documentId: 2, documentName: 'old', score: 0.8, text: 'old' }]
    store.actionErrors.search = 'old error'

    await store.selectKnowledgeBase('kb-1')

    expect(store.searchResults).toEqual([])
    expect(store.actionErrors.search).toBe('')
    expect(ragServiceMock.getDocuments).toHaveBeenCalledWith('kb-1')
    expect(store.documents).toEqual([{ id: 1, name: 'doc.pdf' }])
  })

  it('keeps upload/delete/governance behavior behind the facade', async () => {
    const store = useRagStore()

    await store.uploadDocument({ name: 'doc.pdf' } as File)
    expect(store.actionErrors.document).toBe('请先选择一个知识库。')
    expect(ragServiceMock.uploadDocument).not.toHaveBeenCalled()

    store.selectedKnowledgeBaseId = 'kb-1'
    await store.uploadDocument({ name: 'doc.pdf' } as File)
    expect(ragServiceMock.uploadDocument).toHaveBeenCalledWith(
      'kb-1',
      { name: 'doc.pdf' },
      store.uploadGovernanceForm
    )

    await store.deleteDocument(42)
    expect(ragServiceMock.deleteDocument).toHaveBeenCalledWith(42)

    store.currentDocumentGovernance.id = 42
    await store.saveDocumentGovernance()
    expect(ragServiceMock.updateDocumentGovernance).toHaveBeenCalledWith(
      expect.objectContaining({ id: 42 })
    )
  })

  it('keeps ragStore.ts as a facade without direct service CRUD implementations', () => {
    const sourcePath = fileURLToPath(new URL('../../src/stores/ragStore.ts', import.meta.url))
    const source = readFileSync(sourcePath, 'utf8')

    expect(source).toContain('useEmbeddingModelDomain')
    expect(source).toContain('useKnowledgeBaseDomain')
    expect(source).not.toContain('useDialogCrud({')
    expect(source).not.toContain('ragService.')
  })
})
