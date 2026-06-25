import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { identityService } from '@/services/identityService'
import {
  getProblemCode,
  setAccessToken,
  type ApiError,
  type ApiProblemDetails
} from '@/services/apiClient'
import {
  ACCESS_MANAGEMENT_PERMISSIONS,
  CHAT_REQUIRED_PERMISSIONS,
  collectConfigReadPermissions,
  collectKnowledgeReadPermissions
} from '@/security/permissions'
import type { CloudOidcStatus, CurrentUserProfile, InitializationStatus, LoginRequest } from '@/types/app'

const TOKEN_KEY = 'aicopilot.auth.token'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(sessionStorage.getItem(TOKEN_KEY) ?? '')
  const currentUser = ref<CurrentUserProfile | null>(null)
  const initializationStatus = ref<InitializationStatus | null>(null)
  const cloudOidcStatus = ref<CloudOidcStatus | null>(null)
  const isInitializationLoaded = ref(false)
  const isInitializing = ref(false)
  const isSubmitting = ref(false)
  const isCloudOidcStatusLoading = ref(false)
  const isCloudLoginSubmitting = ref(false)
  const isProfileLoading = ref(false)
  const isProfileLoaded = ref(false)
  const errorMessage = ref('')

  const isAuthenticated = computed(() => token.value.length > 0)
  const isInitialized = computed(() => initializationStatus.value?.isInitialized ?? false)
  const isCloudOidcEnabled = computed(() => cloudOidcStatus.value?.isEnabled ?? false)
  const userName = computed(() => currentUser.value?.userName ?? '')
  const roleName = computed(() => currentUser.value?.roleName ?? '')
  const loginSource = computed(() => currentUser.value?.identityProvider === 'Cloud' ? 'Cloud OIDC' : '本地 AI 账号')
  const cloudEmployeeNo = computed(() => currentUser.value?.cloudEmployeeNo ?? '')
  const cloudStatusVersion = computed(() => currentUser.value?.cloudStatusVersion ?? '')
  const permissions = computed(() => currentUser.value?.permissions ?? [])

  function persistAuth() {
    if (token.value) {
      sessionStorage.setItem(TOKEN_KEY, token.value)
      setAccessToken(token.value)
      return
    }

    sessionStorage.removeItem(TOKEN_KEY)
    setAccessToken(null)
  }

  function hasPermission(permission: string) {
    return permissions.value.includes(permission)
  }

  function hasAnyPermission(permissionCodes: readonly string[]) {
    return permissionCodes.some((permission) => hasPermission(permission))
  }

  function hasAllPermissions(permissionCodes: readonly string[]) {
    return permissionCodes.every((permission) => hasPermission(permission))
  }

  const canUseChat = computed(() => hasAllPermissions(CHAT_REQUIRED_PERMISSIONS))
  const canViewConfig = computed(() => hasAnyPermission(collectConfigReadPermissions()))
  const canManageKnowledge = computed(() => hasAnyPermission(collectKnowledgeReadPermissions()))
  const canManageAccess = computed(() => hasAnyPermission(ACCESS_MANAGEMENT_PERMISSIONS))

  function resolveUnauthorizedMessage(problem?: ApiProblemDetails | null) {
    switch (problem?.code) {
      case 'account_disabled':
        return '账号已禁用，请联系管理员恢复启用。'
      case 'user_missing':
        return '当前用户不存在，请重新登录。'
      case 'session_revoked':
        return '登录态已失效，请重新登录。'
      default:
        return '登录态已失效，请重新登录。'
    }
  }

  function resolveLoginErrorMessage(error: unknown) {
    const apiError = error as ApiError | undefined
    if (apiError?.status === 401) {
      const code = getProblemCode(apiError.details)
      if (code === 'account_disabled') {
        return '账号已禁用，请联系管理员恢复启用。'
      }

      if (code === 'invalid_credentials') {
        return '登录失败，请检查用户名和密码。'
      }
    }

    return '登录失败，请检查用户名和密码。'
  }

  function resolveCloudLoginErrorMessage(error: unknown) {
    const apiError = error as ApiError | undefined
    const code = getProblemCode(apiError?.details)

    switch (code) {
      case 'cloud_oidc_not_configured':
        return 'Cloud 登录尚未配置。'
      case 'cloud_oidc_invalid_principal':
        return 'Cloud 登录态无效或已过期，请重新登录。'
      case 'cloud_identity_inactive':
        return 'Cloud 账号或员工状态无效，无法登录 AICopilot。'
      case 'external_identity_conflict':
        return 'Cloud 身份与现有 AI 账号存在冲突，请联系 AI 管理员处理。'
      case 'account_disabled':
        return 'AICopilot 本地账号已禁用，请联系 AI 管理员。'
      default:
        return 'Cloud 登录失败，请重新从 Cloud 登录。'
    }
  }

  async function ensureInitialized(force = false) {
    if (isInitializationLoaded.value && !force) {
      return initializationStatus.value
    }

    isInitializing.value = true
    errorMessage.value = ''

    try {
      const status = await identityService.getInitializationStatus()
      initializationStatus.value = status
      isInitializationLoaded.value = true
      return status
    } catch (error) {
      errorMessage.value = '无法获取系统初始化状态，请稍后重试。'
      throw error
    } finally {
      isInitializing.value = false
    }
  }

  async function ensureCloudOidcStatus(force = false) {
    if (cloudOidcStatus.value && !force) {
      return cloudOidcStatus.value
    }

    isCloudOidcStatusLoading.value = true

    try {
      const status = await identityService.getCloudOidcStatus()
      cloudOidcStatus.value = status
      return status
    } catch {
      cloudOidcStatus.value = { isEnabled: false }
      return cloudOidcStatus.value
    } finally {
      isCloudOidcStatusLoading.value = false
    }
  }

  async function ensureCurrentUser(force = false) {
    if (!token.value) {
      currentUser.value = null
      isProfileLoaded.value = false
      return null
    }

    if (isProfileLoaded.value && !force) {
      return currentUser.value
    }

    isProfileLoading.value = true

    try {
      const profile = await identityService.getCurrentUserProfile()
      currentUser.value = profile
      isProfileLoaded.value = true
      return profile
    } catch (error) {
      isProfileLoaded.value = false
      throw error
    } finally {
      isProfileLoading.value = false
    }
  }

  async function login(payload: LoginRequest) {
    isSubmitting.value = true
    errorMessage.value = ''

    try {
      const response = await identityService.login(payload)
      token.value = response.token
      persistAuth()
      await ensureCurrentUser(true)
      return response
    } catch (error) {
      errorMessage.value = resolveLoginErrorMessage(error)
      throw error
    } finally {
      isSubmitting.value = false
    }
  }

  async function startCloudOidcLogin() {
    isCloudLoginSubmitting.value = true
    errorMessage.value = ''

    try {
      const status = await ensureCloudOidcStatus(true)
      if (!status.isEnabled) {
        errorMessage.value = 'Cloud 登录尚未配置。'
        return
      }

      window.location.assign(identityService.getCloudOidcChallengeUrl())
    } finally {
      isCloudLoginSubmitting.value = false
    }
  }

  async function finalizeCloudOidcLogin() {
    isCloudLoginSubmitting.value = true
    errorMessage.value = ''

    try {
      const response = await identityService.finalizeCloudOidcLogin()
      token.value = response.token
      persistAuth()
      await ensureCurrentUser(true)
      return response
    } catch (error) {
      errorMessage.value = resolveCloudLoginErrorMessage(error)
      throw error
    } finally {
      isCloudLoginSubmitting.value = false
    }
  }

  async function refreshCurrentUser() {
    return await ensureCurrentUser(true)
  }

  function clearAuth(message?: string) {
    token.value = ''
    currentUser.value = null
    isProfileLoaded.value = false
    errorMessage.value = message ?? ''
    persistAuth()
    sessionStorage.removeItem('aicopilot.chat.currentSessionId')
  }

  persistAuth()

  return {
    token,
    currentUser,
    initializationStatus,
    cloudOidcStatus,
    isInitializationLoaded,
    isInitializing,
    isSubmitting,
    isCloudOidcStatusLoading,
    isCloudLoginSubmitting,
    isProfileLoading,
    isProfileLoaded,
    errorMessage,
    isAuthenticated,
    isInitialized,
    isCloudOidcEnabled,
    userName,
    roleName,
    loginSource,
    cloudEmployeeNo,
    cloudStatusVersion,
    permissions,
    canUseChat,
    canViewConfig,
    canManageKnowledge,
    canManageAccess,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    ensureInitialized,
    ensureCloudOidcStatus,
    ensureCurrentUser,
    refreshCurrentUser,
    login,
    startCloudOidcLogin,
    finalizeCloudOidcLogin,
    resolveUnauthorizedMessage,
    clearAuth
  }
})
