import { baseUrl } from '@/appsetting'

const TOKEN_KEY = 'aicopilot.auth.token'

let unauthorizedHandler: ((problem: ApiProblemDetails | null) => void | Promise<void>) | null = null

type QueryValue = string | number | boolean | null | undefined
type QueryParams = Record<string, QueryValue>

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
  handler: ((problem: ApiProblemDetails | null) => void | Promise<void>) | null
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
  } catch {
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
      : undefined
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

function buildUrl(endpoint: string, query?: QueryParams) {
  const url = new URL(`${baseUrl}${endpoint}`, window.location.origin)

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

async function request<T>(endpoint: string, init: RequestInit = {}, query?: QueryParams): Promise<T> {
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

  const response = await fetch(buildUrl(endpoint, query), {
    ...init,
    headers
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
}

export const apiClient = {
  request,
  get<T>(endpoint: string, query?: QueryParams) {
    return request<T>(endpoint, { method: 'GET' }, query)
  },
  post<T>(endpoint: string, body: unknown) {
    return request<T>(endpoint, {
      method: 'POST',
      body: JSON.stringify(body)
    })
  },
  postForm<T>(endpoint: string, body: FormData) {
    return request<T>(endpoint, {
      method: 'POST',
      body
    })
  },
  put<T>(endpoint: string, body: unknown) {
    return request<T>(endpoint, {
      method: 'PUT',
      body: JSON.stringify(body)
    })
  },
  delete<T>(endpoint: string, body?: unknown) {
    return request<T>(
      endpoint,
      {
        method: 'DELETE',
        body: body === undefined ? undefined : JSON.stringify(body)
      }
    )
  }
}
