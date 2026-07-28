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
  confirmExistingCloudOidcAccount: vi.fn(),
  cancelCloudOidcAccountConfirmation: vi.fn(),
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

  it('exposes the existing-account confirmation state from the backend problem contract', async () => {
    const error = new ApiError('API Error: 401', 401, {
      code: 'external_identity_confirmation_required',
      detail: '请输入本地 AI 账号密码完成绑定。'
    })
    identityServiceMock.finalizeCloudOidcLogin.mockRejectedValue(error)
    const store = useAuthStore()

    await expect(store.finalizeCloudOidcLogin()).rejects.toBe(error)

    expect(store.isCloudAccountConfirmationRequired).toBe(true)
    expect(store.errorMessage).toBe('请输入本地 AI 账号密码完成绑定。')
  })

  it('keeps confirmation available after a rejected password without persisting the password', async () => {
    const error = new ApiError('API Error: 401', 401, {
      code: 'invalid_credentials',
      detail: '本地 AI 账号密码无效，请重新输入。'
    })
    identityServiceMock.confirmExistingCloudOidcAccount.mockRejectedValue(error)
    const store = useAuthStore()

    await expect(store.confirmExistingCloudOidcAccount('Local-Password-1!')).rejects.toBe(error)

    expect(identityServiceMock.confirmExistingCloudOidcAccount).toHaveBeenCalledWith('Local-Password-1!')
    expect(store.isCloudAccountConfirmationRequired).toBe(true)
    expect(store.errorMessage).toBe('本地 AI 账号密码无效，请重新输入。')
    expect(Object.values(sessionStorage)).not.toContain('Local-Password-1!')
    expect(JSON.stringify(store.$state)).not.toContain('Local-Password-1!')
  })

  it('closes confirmation and displays the precise backend reason for an unrecoverable binding conflict', async () => {
    identityServiceMock.finalizeCloudOidcLogin.mockRejectedValue(new ApiError('API Error: 401', 401, {
      code: 'external_identity_confirmation_required'
    }))
    const conflict = new ApiError('API Error: 401', 401, {
      code: 'external_identity_conflict',
      detail: '该 AICopilot 本地账号已绑定到另一个 Cloud 身份，拒绝覆盖。'
    })
    identityServiceMock.confirmExistingCloudOidcAccount.mockRejectedValue(conflict)
    const store = useAuthStore()
    await expect(store.finalizeCloudOidcLogin()).rejects.toBeInstanceOf(ApiError)

    await expect(store.confirmExistingCloudOidcAccount('Local-Password-1!')).rejects.toBe(conflict)

    expect(store.isCloudAccountConfirmationRequired).toBe(false)
    expect(store.errorMessage).toBe('该 AICopilot 本地账号已绑定到另一个 Cloud 身份，拒绝覆盖。')
    expect(JSON.stringify(store.$state)).not.toContain('Local-Password-1!')
  })

  it('shows an expired external session as a stable Cloud login error without opening confirmation', async () => {
    const expired = new ApiError('API Error: 401', 401, {
      code: 'cloud_oidc_invalid_principal',
      detail: 'Cloud 登录态无效或已过期，请重新从 Cloud 登录。'
    })
    identityServiceMock.finalizeCloudOidcLogin.mockRejectedValue(expired)
    const store = useAuthStore()

    await expect(store.finalizeCloudOidcLogin()).rejects.toBe(expired)

    expect(store.isCloudAccountConfirmationRequired).toBe(false)
    expect(store.errorMessage).toBe('Cloud 登录态无效或已过期，请重新登录。')
  })

  it('cancels the pending Cloud account confirmation and clears its state', async () => {
    identityServiceMock.finalizeCloudOidcLogin.mockRejectedValue(new ApiError('API Error: 401', 401, {
      code: 'external_identity_confirmation_required'
    }))
    identityServiceMock.cancelCloudOidcAccountConfirmation.mockResolvedValue(undefined)
    const store = useAuthStore()
    await expect(store.finalizeCloudOidcLogin()).rejects.toBeInstanceOf(ApiError)

    await store.cancelCloudOidcAccountConfirmation()

    expect(identityServiceMock.cancelCloudOidcAccountConfirmation).toHaveBeenCalledOnce()
    expect(store.isCloudAccountConfirmationRequired).toBe(false)
  })
})
