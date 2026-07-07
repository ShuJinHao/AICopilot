import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import { useAuthStore } from '@/stores/authStore'

const identityServiceMock = vi.hoisted(() => ({
  getInitializationStatus: vi.fn(),
  login: vi.fn(),
  getCloudOidcStatus: vi.fn(),
  getCloudOidcChallengeUrl: vi.fn(),
  finalizeCloudOidcLogin: vi.fn(),
  getCurrentUserProfile: vi.fn()
}))

vi.mock('@/services/identityService', () => ({
  identityService: identityServiceMock
}))

function createSessionStorageMock(initial: Record<string, string> = {}) {
  const state = new Map<string, string>(Object.entries(initial))

  return {
    getItem(key: string) {
      return state.get(key) ?? null
    },
    setItem(key: string, value: string) {
      state.set(key, value)
    },
    removeItem(key: string) {
      state.delete(key)
    },
    clear() {
      state.clear()
    }
  }
}

describe('authStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(globalThis, 'sessionStorage', {
      value: createSessionStorageMock({
        'aicopilot.auth.token': 'token-1',
        'aicopilot.chat.currentSessionId': 'session-1'
      }),
      configurable: true
    })
    setActivePinia(createPinia())
  })

  it('clears invalid auth and keeps a visible login message when current user loading fails', async () => {
    const error = new ApiError('API Error: 401', 401, {
      code: 'session_revoked'
    })
    identityServiceMock.getCurrentUserProfile.mockRejectedValue(error)
    const store = useAuthStore()

    await expect(store.ensureCurrentUser(true)).rejects.toBe(error)

    expect(store.token).toBe('')
    expect(store.currentUser).toBeNull()
    expect(store.isProfileLoaded).toBe(false)
    expect(store.errorMessage).toBe('登录态已失效，请重新登录。')
    expect(sessionStorage.getItem('aicopilot.auth.token')).toBeNull()
    expect(sessionStorage.getItem('aicopilot.chat.currentSessionId')).toBeNull()
  })

  it('shows a generic visible login message when profile loading fails unexpectedly', async () => {
    const error = new ApiError('API Error: 500', 500, {
      detail: 'raw provider failure with /internal/identity endpoint'
    })
    identityServiceMock.getCurrentUserProfile.mockRejectedValue(error)
    const store = useAuthStore()

    await expect(store.ensureCurrentUser(true)).rejects.toBe(error)

    expect(store.token).toBe('')
    expect(store.errorMessage).toBe('无法获取当前用户信息，请重新登录。')
  })

  it('shows Cloud OIDC status load failures without treating them as not configured', async () => {
    const error = new ApiError('API Error: 500', 500, {
      detail: 'raw oidc discovery failure'
    })
    identityServiceMock.getCloudOidcStatus.mockRejectedValue(error)
    const store = useAuthStore()

    const status = await store.ensureCloudOidcStatus(true)

    expect(status).toEqual({ isEnabled: false })
    expect(store.errorMessage).toBe('无法获取 Cloud 登录状态，请稍后重试或使用本地 AI 账号登录。')
  })

  it('keeps the Cloud OIDC status failure message when Cloud login is requested', async () => {
    const error = new ApiError('API Error: 500', 500, {
      detail: 'raw oidc discovery failure'
    })
    identityServiceMock.getCloudOidcStatus.mockRejectedValue(error)
    const store = useAuthStore()

    await store.startCloudOidcLogin()

    expect(store.errorMessage).toBe('无法获取 Cloud 登录状态，请稍后重试或使用本地 AI 账号登录。')
    expect(identityServiceMock.getCloudOidcChallengeUrl).not.toHaveBeenCalled()
  })
})
