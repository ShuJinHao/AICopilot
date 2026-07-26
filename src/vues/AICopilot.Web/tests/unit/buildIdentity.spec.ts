import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  API_BUILD_SERVICE_NAME,
  BUILD_IDENTITY_SCHEMA_VERSION,
  WEB_BUILD_SERVICE_NAME,
  formatBuildIdentity,
  loadApiBuildIdentity,
  resolveBuildIdentity,
  resolveWebBuildIdentity,
} from '@/services/buildIdentityService'

const apiClientMock = vi.hoisted(() => ({
  get: vi.fn(),
}))

vi.mock('@/services/apiClient', () => ({
  apiClient: apiClientMock,
}))

const webCommit = '1111111111111111111111111111111111111111'
const apiCommit = '2222222222222222222222222222222222222222'

describe('build identity facts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('keeps Web and API build identities independent when they diverge', () => {
    const web = resolveWebBuildIdentity(webCommit, `sha-${webCommit}`)
    const api = resolveBuildIdentity(
      {
        schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
        serviceName: API_BUILD_SERVICE_NAME,
        releaseTag: `sha-${apiCommit}`,
        sourceCommit: apiCommit,
        available: true,
      },
      API_BUILD_SERVICE_NAME,
    )

    expect(formatBuildIdentity(web)).toBe('sha-111111111111')
    expect(formatBuildIdentity(api)).toBe('sha-222222222222')
    expect(web.sourceCommit).not.toBe(api.sourceCommit)
  })

  it('does not use another source when one identity is missing or inconsistent', () => {
    const web = resolveWebBuildIdentity(undefined, undefined)
    const api = resolveBuildIdentity(
      {
        schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
        serviceName: API_BUILD_SERVICE_NAME,
        releaseTag: `sha-${webCommit}`,
        sourceCommit: apiCommit,
        available: true,
      },
      API_BUILD_SERVICE_NAME,
    )

    expect(web).toMatchObject({
      serviceName: WEB_BUILD_SERVICE_NAME,
      available: false,
      releaseTag: null,
      sourceCommit: null,
    })
    expect(api).toMatchObject({
      available: false,
      releaseTag: null,
      sourceCommit: null,
    })
  })

  it('loads a valid anonymous API build identity', async () => {
    apiClientMock.get.mockResolvedValue({
      schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
      serviceName: API_BUILD_SERVICE_NAME,
      releaseTag: `sha-${apiCommit}`,
      sourceCommit: apiCommit,
      available: true,
    })

    const result = await loadApiBuildIdentity()

    expect(apiClientMock.get).toHaveBeenCalledWith('/system/build-identity', undefined, {
      timeoutMs: 5000,
    })
    expect(result.status).toBe('ready')
    expect(result.fact.sourceCommit).toBe(apiCommit)
  })

  it('turns API request failure into a non-blocking unavailable state', async () => {
    apiClientMock.get.mockRejectedValue(new Error('network unavailable'))

    const result = await loadApiBuildIdentity()

    expect(result).toMatchObject({
      status: 'error',
      fact: {
        serviceName: API_BUILD_SERVICE_NAME,
        available: false,
        releaseTag: null,
        sourceCommit: null,
      },
    })
  })
})
