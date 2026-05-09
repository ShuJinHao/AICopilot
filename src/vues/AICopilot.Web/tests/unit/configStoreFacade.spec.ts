import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useConfigStore } from '@/stores/configStore'

const configServiceMock = vi.hoisted(() => ({
  getLanguageModels: vi.fn(),
  getProviderReliability: vi.fn(),
  getConversationTemplates: vi.fn(),
  getApprovalPolicies: vi.fn(),
  getBusinessDatabases: vi.fn(),
  getMcpServers: vi.fn(),
  getSemanticSourceStatuses: vi.fn(),
  getLanguageModel: vi.fn(),
  createLanguageModel: vi.fn(),
  updateLanguageModel: vi.fn(),
  deleteLanguageModel: vi.fn(),
  getConversationTemplate: vi.fn(),
  createConversationTemplate: vi.fn(),
  updateConversationTemplate: vi.fn(),
  deleteConversationTemplate: vi.fn(),
  getApprovalPolicy: vi.fn(),
  createApprovalPolicy: vi.fn(),
  updateApprovalPolicy: vi.fn(),
  deleteApprovalPolicy: vi.fn(),
  getBusinessDatabase: vi.fn(),
  createBusinessDatabase: vi.fn(),
  updateBusinessDatabase: vi.fn(),
  deleteBusinessDatabase: vi.fn(),
  getMcpServer: vi.fn(),
  createMcpServer: vi.fn(),
  updateMcpServer: vi.fn(),
  deleteMcpServer: vi.fn()
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
  configServiceMock.getProviderReliability.mockResolvedValue(null)
  configServiceMock.getConversationTemplates.mockResolvedValue([])
  configServiceMock.getApprovalPolicies.mockResolvedValue([])
  configServiceMock.getBusinessDatabases.mockResolvedValue([])
  configServiceMock.getMcpServers.mockResolvedValue([])
  configServiceMock.getSemanticSourceStatuses.mockResolvedValue([])
  configServiceMock.createLanguageModel.mockResolvedValue(undefined)
  configServiceMock.updateBusinessDatabase.mockResolvedValue(undefined)
  configServiceMock.createMcpServer.mockResolvedValue(undefined)
}

describe('configStore facade', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetConfigServiceMocks()
  })

  it('keeps the public config action surface while delegating to domain stores', () => {
    const store = useConfigStore()

    expect(typeof store.saveLanguageModel).toBe('function')
    expect(typeof store.openEditBusinessDatabaseDialog).toBe('function')
    expect(typeof store.saveMcpServer).toBe('function')
    expect(typeof store.refreshProviderReliability).toBe('function')
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

  it('preserves business database provider snapshot when edit connection string is blank', async () => {
    configServiceMock.getBusinessDatabase.mockResolvedValue({
      id: 'db-1',
      name: 'MES',
      description: '',
      provider: 2,
      isEnabled: true,
      externalSystemType: 2,
      readOnlyCredentialVerified: true,
      hasConnectionString: true,
      connectionStringMasked: '***'
    })
    const store = useConfigStore()

    await store.openEditBusinessDatabaseDialog('db-1')
    store.currentBusinessDatabase.connectionString = ''
    store.currentBusinessDatabase.provider = 1

    await store.saveBusinessDatabase()

    expect(configServiceMock.updateBusinessDatabase).toHaveBeenCalledWith(
      expect.objectContaining({
        id: 'db-1',
        provider: 2,
        connectionString: '',
        isReadOnly: true
      })
    )
  })

  it('preserves MCP allowed tool normalization through the facade', async () => {
    const store = useConfigStore()

    store.openCreateMcpServerDialog()
    store.currentMcpServer.name = ' Runtime MCP '
    store.currentMcpServer.description = ' Tools '
    store.currentMcpServer.command = ' dotnet '
    store.currentMcpServer.arguments = ' run '
    store.currentMcpServer.allowedTools = [
      { toolName: ' queryData ', readOnlyDeclared: true },
      { toolName: 'QueryData', readOnlyDeclared: false },
      { toolName: ' ' }
    ]

    await store.saveMcpServer()

    expect(configServiceMock.createMcpServer).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Runtime MCP',
        description: 'Tools',
        command: 'dotnet',
        arguments: 'run',
        allowedTools: [
          expect.objectContaining({
            toolName: 'queryData',
            readOnlyDeclared: true
          })
        ]
      })
    )
  })

  it('keeps configStore.ts as a facade without direct CRUD implementations', () => {
    const sourcePath = fileURLToPath(new URL('../../src/stores/configStore.ts', import.meta.url))
    const source = readFileSync(sourcePath, 'utf8')

    expect(source).toContain('useLanguageModelConfigDomain')
    expect(source).toContain('useBusinessDatabaseConfigDomain')
    expect(source).not.toContain('useDialogCrud({')
    expect(source).not.toContain('configService.')
  })
})
