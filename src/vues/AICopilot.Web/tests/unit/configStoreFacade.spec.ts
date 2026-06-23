import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useConfigStore } from '@/stores/configStore'

const configServiceMock = vi.hoisted(() => ({
  getLanguageModels: vi.fn(),
  getConversationTemplates: vi.fn(),
  getRoutingModels: vi.fn(),
  getLanguageModel: vi.fn(),
  createLanguageModel: vi.fn(),
  updateLanguageModel: vi.fn(),
  deleteLanguageModel: vi.fn(),
  getRoutingModel: vi.fn(),
  createRoutingModel: vi.fn(),
  updateRoutingModel: vi.fn(),
  deleteRoutingModel: vi.fn(),
  getConversationTemplate: vi.fn(),
  createConversationTemplate: vi.fn(),
  updateConversationTemplate: vi.fn(),
  deleteConversationTemplate: vi.fn()
}))

vi.mock('@/services/configService', () => ({
  configService: configServiceMock
}))

vi.mock('@/stores/authStore', () => ({
  useAuthStore: () => ({
    hasAnyPermission: () => true,
    hasPermission: () => true
  })
}))

function resetConfigServiceMocks() {
  vi.clearAllMocks()
  configServiceMock.getLanguageModels.mockResolvedValue([])
  configServiceMock.getRoutingModels.mockResolvedValue([])
  configServiceMock.getConversationTemplates.mockResolvedValue([])
  configServiceMock.createLanguageModel.mockResolvedValue(undefined)
  configServiceMock.createRoutingModel.mockResolvedValue(undefined)
  configServiceMock.createConversationTemplate.mockResolvedValue(undefined)
}

describe('configStore facade', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetConfigServiceMocks()
  })

  it('keeps the public config action surface while delegating to domain stores', () => {
    const store = useConfigStore()

    expect(typeof store.saveLanguageModel).toBe('function')
    expect(typeof store.saveRoutingModel).toBe('function')
    expect(typeof store.saveConversationTemplate).toBe('function')
    expect(typeof store.refreshAgentSlots).toBe('function')
    expect('saveMcpServer' in store).toBe(false)
    expect('openEditBusinessDatabaseDialog' in store).toBe(false)
  })

  it('preserves language model payload trimming through the facade', async () => {
    const store = useConfigStore()

    store.openCreateLanguageModelDialog()
    store.currentLanguageModel.provider = ' OpenAI '
    store.currentLanguageModel.name = ' Primary '
    store.currentLanguageModel.baseUrl = ' https://api.example.com '
    store.currentLanguageModel.apiKey = ' secret '

    await store.saveLanguageModel()

    expect(configServiceMock.createLanguageModel).toHaveBeenCalledWith(
      expect.objectContaining({
        provider: 'OpenAI',
        name: 'Primary',
        baseUrl: 'https://api.example.com',
        apiKey: 'secret'
      })
    )
  })

  it('refreshes only fixed agent slot domains', async () => {
    const store = useConfigStore()

    await store.refresh()

    expect(configServiceMock.getLanguageModels).toHaveBeenCalledOnce()
    expect(configServiceMock.getRoutingModels).toHaveBeenCalledOnce()
    expect(configServiceMock.getConversationTemplates).toHaveBeenCalledOnce()
  })

  it('keeps configStore.ts as a facade without direct CRUD implementations', () => {
    const sourcePath = fileURLToPath(new URL('../../src/stores/configStore.ts', import.meta.url))
    const source = readFileSync(sourcePath, 'utf8')

    expect(source).toContain('useLanguageModelConfigDomain')
    expect(source).toContain('useRoutingModelConfigDomain')
    expect(source).toContain('useConversationTemplateConfigDomain')
    expect(source).not.toContain('useBusinessDatabaseConfigDomain')
    expect(source).not.toContain('useMcpServerConfigDomain')
    expect(source).not.toContain('useProviderReliabilityConfigDomain')
    expect(source).not.toContain('useDialogCrud({')
    expect(source).not.toContain('configService.')
  })
})
