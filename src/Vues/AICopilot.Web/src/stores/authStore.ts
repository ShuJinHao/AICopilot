import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { identityService } from '@/services/identityService'
import {
  getProblemCode,
  getProblemDetail,
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
import type { CurrentUserProfile, InitializationStatus, LoginRequest } from '@/types/app'

const TOKEN_KEY = 'aicopilot.auth.token'

export const useAuthStore = defineStore('auth', () => {
  const token = ref(sessionStorage.getItem(TOKEN_KEY) ?? '')
  const currentUser = ref<CurrentUserProfile | null>(null)
  const initializationStatus = ref<InitializationStatus | null>(null)
  const isInitializationLoaded = ref(false)
  const isInitializing = ref(false)
  const isSubmitting = ref(false)
  const isProfileLoading = ref(false)
  const isProfileLoaded = ref(false)
  const errorMessage = ref('')

  const isAuthenticated = computed(() => token.value.length > 0)
  const isInitialized = computed(() => initializationStatus.value?.isInitialized ?? false)
  const userName = computed(() => currentUser.value?.userName ?? '')
  const roleName = computed(() => currentUser.value?.roleName ?? '')
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
        return problem.detail || '账号已禁用，请联系管理员恢复启用。'
      case 'user_missing':
        return problem.detail || '当前用户不存在，请重新登录。'
      case 'session_revoked':
        return problem.detail || '登录态已失效，请重新登录。'
      default:
        return problem?.detail || '登录态已失效，请重新登录。'
    }
  }

  function resolveLoginErrorMessage(error: unknown) {
    const apiError = error as ApiError | undefined
    if (apiError?.status === 401) {
      const detail = getProblemDetail(apiError.details)
      const code = getProblemCode(apiError.details)
      if (code === 'account_disabled') {
        return detail || '账号已禁用，请联系管理员恢复启用。'
      }

      if (code === 'invalid_credentials') {
        return detail || '登录失败，请检查用户名和密码。'
      }
    }

    return '登录失败，请检查用户名和密码。'
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
    isInitializationLoaded,
    isInitializing,
    isSubmitting,
    isProfileLoading,
    isProfileLoaded,
    errorMessage,
    isAuthenticated,
    isInitialized,
    userName,
    roleName,
    permissions,
    canUseChat,
    canViewConfig,
    canManageKnowledge,
    canManageAccess,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    ensureInitialized,
    ensureCurrentUser,
    refreshCurrentUser,
    login,
    resolveUnauthorizedMessage,
    clearAuth
  }
})
