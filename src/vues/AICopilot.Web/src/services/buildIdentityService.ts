import { apiClient } from './apiClient'

export const BUILD_IDENTITY_SCHEMA_VERSION = 'aicopilot-build-identity-v1'
export const API_BUILD_SERVICE_NAME = 'AICopilot.HttpApi'
export const WEB_BUILD_SERVICE_NAME = 'AICopilot.Web'

export interface BuildIdentityFact {
  schemaVersion: typeof BUILD_IDENTITY_SCHEMA_VERSION
  serviceName: string
  releaseTag: string | null
  sourceCommit: string | null
  available: boolean
}

export interface BuildIdentityLoadResult {
  status: 'ready' | 'unavailable' | 'error'
  fact: BuildIdentityFact
}

function unavailableBuildIdentity(serviceName: string): BuildIdentityFact {
  return {
    schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
    serviceName,
    releaseTag: null,
    sourceCommit: null,
    available: false,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function isFullGitCommit(value: string) {
  return /^[0-9a-f]{40}$/.test(value)
}

export function resolveBuildIdentity(
  payload: unknown,
  expectedServiceName: string,
): BuildIdentityFact {
  if (
    !isRecord(payload) ||
    payload.schemaVersion !== BUILD_IDENTITY_SCHEMA_VERSION ||
    payload.serviceName !== expectedServiceName ||
    typeof payload.available !== 'boolean'
  ) {
    return unavailableBuildIdentity(expectedServiceName)
  }

  if (!payload.available) {
    return unavailableBuildIdentity(expectedServiceName)
  }

  const sourceCommit =
    typeof payload.sourceCommit === 'string' ? payload.sourceCommit.trim().toLowerCase() : ''
  const releaseTag =
    typeof payload.releaseTag === 'string' ? payload.releaseTag.trim().toLowerCase() : ''

  if (!isFullGitCommit(sourceCommit) || releaseTag !== `sha-${sourceCommit}`) {
    return unavailableBuildIdentity(expectedServiceName)
  }

  return {
    schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
    serviceName: expectedServiceName,
    releaseTag,
    sourceCommit,
    available: true,
  }
}

export function resolveWebBuildIdentity(
  sourceCommit: string | undefined,
  releaseTag: string | undefined,
) {
  return resolveBuildIdentity(
    {
      schemaVersion: BUILD_IDENTITY_SCHEMA_VERSION,
      serviceName: WEB_BUILD_SERVICE_NAME,
      sourceCommit,
      releaseTag,
      available: true,
    },
    WEB_BUILD_SERVICE_NAME,
  )
}

export function formatBuildIdentity(fact: BuildIdentityFact) {
  return fact.available && fact.sourceCommit
    ? `sha-${fact.sourceCommit.slice(0, 12)}`
    : '未注入'
}

export async function loadApiBuildIdentity(): Promise<BuildIdentityLoadResult> {
  try {
    const payload = await apiClient.get<unknown>('/system/build-identity', undefined, {
      timeoutMs: 5000,
    })
    const fact = resolveBuildIdentity(payload, API_BUILD_SERVICE_NAME)
    return {
      status: fact.available ? 'ready' : 'unavailable',
      fact,
    }
  } catch (error) {
    void error
    return {
      status: 'error',
      fact: unavailableBuildIdentity(API_BUILD_SERVICE_NAME),
    }
  }
}
