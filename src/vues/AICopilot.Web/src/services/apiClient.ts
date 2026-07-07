import { baseUrl } from '@/appsetting'

const TOKEN_KEY = 'aicopilot.auth.token'

let unauthorizedHandler: ((problem: ApiProblemDetails | null) => void | Promise<void>) | null = null

type QueryValue = string | number | boolean | null | undefined
type QueryParams = Record<string, QueryValue>

export interface ApiRequestOptions {
  timeoutMs?: number
}

export interface ApiProblemDetails {
  title?: string
  detail?: string
  status?: number
  code?: string
  missingPermissions?: string[]
}

export class ApiError extends Error {
  status: number
  details?: unknown

  constructor(message: string, status: number, details?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.details = details
  }
}

export function setUnauthorizedHandler(
  handler: ((problem: ApiProblemDetails | null) => void | Promise<void>) | null,
) {
  unauthorizedHandler = handler
}

export function getAccessToken() {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function setAccessToken(token: string | null) {
  if (token) {
    sessionStorage.setItem(TOKEN_KEY, token)
    return
  }

  sessionStorage.removeItem(TOKEN_KEY)
}

async function parseError(response: Response) {
  try {
    return await response.json()
  } catch (error) {
    console.error('Failed to parse API error response.', error)
    return undefined
  }
}

export function getProblemDetails(details: unknown): ApiProblemDetails | null {
  if (!details || typeof details !== 'object') {
    return null
  }

  const candidate = details as Record<string, unknown>
  return {
    title: typeof candidate.title === 'string' ? candidate.title : undefined,
    detail: typeof candidate.detail === 'string' ? candidate.detail : undefined,
    status: typeof candidate.status === 'number' ? candidate.status : undefined,
    code: typeof candidate.code === 'string' ? candidate.code : undefined,
    missingPermissions: Array.isArray(candidate.missingPermissions)
      ? candidate.missingPermissions.filter((item): item is string => typeof item === 'string')
      : undefined,
  }
}

export function getProblemCode(details: unknown) {
  return getProblemDetails(details)?.code
}

export function getProblemDetail(details: unknown) {
  return getProblemDetails(details)?.detail
}

export function getMissingPermissions(details: unknown) {
  return getProblemDetails(details)?.missingPermissions ?? []
}

async function notifyUnauthorized(problem: ApiProblemDetails | null) {
  if (unauthorizedHandler) {
    await unauthorizedHandler(problem)
  }
}

function resolveEndpoint(endpoint: string) {
  const trimmed = endpoint.trim()
  if (/^https?:\/\//i.test(trimmed)) {
    const endpointUrl = new URL(trimmed)
    if (!isTrustedEndpointOrigin(endpointUrl)) {
      throw new ApiError('API endpoint origin is not trusted.', 400, {
        code: 'untrusted_api_endpoint',
        detail: 'API endpoints must use the current origin or the configured API origin.',
      })
    }

    return endpointUrl.toString()
  }

  if (trimmed === '/api' || trimmed.startsWith('/api/')) {
    return trimmed
  }

  const normalizedBase = baseUrl.replace(/\/$/, '')
  const normalizedEndpoint = trimmed.startsWith('/') ? trimmed : `/${trimmed}`
  return `${normalizedBase}${normalizedEndpoint}`
}

function isTrustedEndpointOrigin(url: URL) {
  const trustedOrigins = new Set<string>([window.location.origin])
  trustedOrigins.add(new URL(baseUrl, window.location.origin).origin)
  return trustedOrigins.has(url.origin)
}

function buildUrl(endpoint: string, query?: QueryParams) {
  const url = new URL(resolveEndpoint(endpoint), window.location.origin)

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === null || value === undefined || value === '') {
        continue
      }

      url.searchParams.set(key, String(value))
    }
  }

  return url.toString()
}

function createTimeoutSignal(timeoutMs: number | undefined, existingSignal?: AbortSignal | null) {
  if (
    existingSignal ||
    timeoutMs === undefined ||
    timeoutMs <= 0 ||
    typeof AbortController === 'undefined'
  ) {
    return null
  }

  const controller = new AbortController()
  const timeoutId = globalThis.setTimeout(() => controller.abort(), timeoutMs)

  return {
    signal: controller.signal,
    clear: () => globalThis.clearTimeout(timeoutId),
  }
}

async function request<T>(
  endpoint: string,
  init: RequestInit = {},
  query?: QueryParams,
  options: ApiRequestOptions = {},
): Promise<T> {
  const headers = new Headers(init.headers ?? {})
  headers.set('Accept', 'application/json')
  const isFormDataBody = typeof FormData !== 'undefined' && init.body instanceof FormData

  const token = getAccessToken()
  const hasAuthToken = Boolean(token)
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  if (init.body && !headers.has('Content-Type') && !isFormDataBody) {
    headers.set('Content-Type', 'application/json')
  }

  const timeout = createTimeoutSignal(options.timeoutMs, init.signal)

  try {
    const response = await fetch(buildUrl(endpoint, query), {
      ...init,
      headers,
      signal: init.signal ?? timeout?.signal,
    })

    if (!response.ok) {
      const details = await parseError(response)
      if (response.status === 401 && hasAuthToken) {
        await notifyUnauthorized(getProblemDetails(details))
      }

      throw new ApiError(`API Error: ${response.status}`, response.status, details)
    }

    if (response.status === 204) {
      return undefined as T
    }

    const text = await response.text()
    if (!text) {
      return undefined as T
    }

    return JSON.parse(text) as T
  } finally {
    timeout?.clear()
  }
}

async function download(
  endpoint: string,
  query?: QueryParams,
  options: ApiRequestOptions = {},
): Promise<Blob> {
  const headers = new Headers()
  const token = getAccessToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const timeout = createTimeoutSignal(options.timeoutMs)

  try {
    const response = await fetch(buildUrl(endpoint, query), {
      method: 'GET',
      headers,
      signal: timeout?.signal,
    })

    if (!response.ok) {
      const details = await parseError(response)
      if (response.status === 401 && token) {
        await notifyUnauthorized(getProblemDetails(details))
      }

      throw new ApiError(`API Error: ${response.status}`, response.status, details)
    }

    return await response.blob()
  } finally {
    timeout?.clear()
  }
}

export const apiClient = {
  request,
  get<T>(endpoint: string, query?: QueryParams, options?: ApiRequestOptions) {
    return request<T>(endpoint, { method: 'GET' }, query, options)
  },
  post<T>(endpoint: string, body: unknown, options?: ApiRequestOptions) {
    return request<T>(
      endpoint,
      {
        method: 'POST',
        body: JSON.stringify(body),
      },
      undefined,
      options,
    )
  },
  postWithCredentials<T>(endpoint: string, body: unknown, options?: ApiRequestOptions) {
    return request<T>(
      endpoint,
      {
        method: 'POST',
        body: JSON.stringify(body),
        credentials: 'include',
      },
      undefined,
      options,
    )
  },
  postForm<T>(endpoint: string, body: FormData, options?: ApiRequestOptions) {
    return request<T>(
      endpoint,
      {
        method: 'POST',
        body,
      },
      undefined,
      options,
    )
  },
  put<T>(endpoint: string, body: unknown, options?: ApiRequestOptions) {
    return request<T>(
      endpoint,
      {
        method: 'PUT',
        body: JSON.stringify(body),
      },
      undefined,
      options,
    )
  },
  delete<T>(endpoint: string, body?: unknown, options?: ApiRequestOptions) {
    return request<T>(
      endpoint,
      {
        method: 'DELETE',
        body: body === undefined ? undefined : JSON.stringify(body),
      },
      undefined,
      options,
    )
  },
  download,
}
