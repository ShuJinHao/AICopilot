import { afterEach, describe, expect, it, vi } from 'vitest'

function installBrowserGlobals(origin = 'https://app.example.test') {
  const storage = new Map<string, string>()

  vi.stubGlobal('window', {
    location: { origin },
  })
  vi.stubGlobal('sessionStorage', {
    getItem: vi.fn((key: string) => storage.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => storage.set(key, value)),
    removeItem: vi.fn((key: string) => storage.delete(key)),
  })
}

async function loadApiClient(apiBaseUrl = '/api') {
  vi.resetModules()
  vi.doMock('@/appsetting', () => ({
    baseUrl: apiBaseUrl,
  }))

  return await import('@/services/apiClient')
}

function createJsonResponse() {
  return new Response('{}', {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('apiClient endpoint trust', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.doUnmock('@/appsetting')
    vi.resetModules()
  })

  it('sends authorization to same-origin absolute endpoints', async () => {
    installBrowserGlobals()
    const fetchMock = vi.fn(async () => createJsonResponse())
    vi.stubGlobal('fetch', fetchMock)
    const { apiClient, setAccessToken } = await loadApiClient()

    setAccessToken('same-origin-token')
    await apiClient.get('https://app.example.test/api/aigateway/session/list')

    const [, init] = fetchMock.mock.calls[0]
    expect(init).toBeDefined()
    expect((init!.headers as Headers).get('Authorization')).toBe('Bearer same-origin-token')
  })

  it('sends authorization to the configured API origin', async () => {
    installBrowserGlobals()
    const fetchMock = vi.fn(async () => createJsonResponse())
    vi.stubGlobal('fetch', fetchMock)
    const { apiClient, setAccessToken } = await loadApiClient('https://api.example.test/api')

    setAccessToken('configured-origin-token')
    await apiClient.get('https://api.example.test/api/aigateway/session/list')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('https://api.example.test/api/aigateway/session/list')
    expect(init).toBeDefined()
    expect((init!.headers as Headers).get('Authorization')).toBe('Bearer configured-origin-token')
  })

  it('rejects external absolute downloads before sending authorization', async () => {
    installBrowserGlobals()
    const fetchMock = vi.fn(async () => createJsonResponse())
    vi.stubGlobal('fetch', fetchMock)
    const { apiClient, setAccessToken } = await loadApiClient()

    setAccessToken('external-token')
    await expect(
      apiClient.download('https://evil.example.test/artifact.bin'),
    ).rejects.toMatchObject({
      status: 400,
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('adds an abort signal only when a request timeout is explicitly provided', async () => {
    installBrowserGlobals()
    const fetchMock = vi.fn(async () => createJsonResponse())
    vi.stubGlobal('fetch', fetchMock)
    const { apiClient } = await loadApiClient()

    await apiClient.get('/identity/me')
    expect(fetchMock.mock.calls[0][1]?.signal).toBeUndefined()

    await apiClient.get('/identity/me', undefined, { timeoutMs: 1000 })
    expect(fetchMock.mock.calls[1][1]?.signal).toBeInstanceOf(AbortSignal)
  })

  it('reads code and detail from ProblemDetails error payloads', async () => {
    installBrowserGlobals()
    const { getProblemDetails } = await loadApiClient()

    expect(getProblemDetails({
      title: 'Bad Request',
      status: 400,
      code: 'agent_plan_tool_denied',
      detail: 'toolCode is not allowed by the selected skill.',
      errors: ['legacy field should be ignored']
    })).toEqual({
      title: 'Bad Request',
      status: 400,
      code: 'agent_plan_tool_denied',
      detail: 'toolCode is not allowed by the selected skill.',
      missingPermissions: undefined
    })
  })
})
