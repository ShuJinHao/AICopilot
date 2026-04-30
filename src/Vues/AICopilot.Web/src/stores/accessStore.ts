import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  ApiError,
  getMissingPermissions,
  getProblemDetail
} from '@/services/apiClient'
import { identityService } from '@/services/identityService'
import { useAuthStore } from '@/stores/authStore'
import type {
  AuditLogQuery,
  AuditLogSummary,
  PermissionDefinition,
  RoleSummary,
  UserSummary
} from '@/types/app'

interface UserCreateForm {
  userName: string
  password: string
  roleName: string
}

interface UserRoleForm {
  userId: string
  userName: string
  roleName: string
}

interface RoleForm {
  roleId?: string
  roleName: string
  permissions: string[]
}

interface UserStatusActionForm {
  userId: string
  userName: string
  status: UserSummary['status']
  mode: 'disable' | 'enable'
}

interface ResetPasswordForm {
  userId: string
  userName: string
  newPassword: string
  confirmPassword: string
}

interface DeleteRoleForm {
  roleId: string
  roleName: string
}

function createUserCreateForm(): UserCreateForm {
  return {
    userName: '',
    password: '',
    roleName: ''
  }
}

function createUserRoleForm(): UserRoleForm {
  return {
    userId: '',
    userName: '',
    roleName: ''
  }
}

function createRoleForm(): RoleForm {
  return {
    roleName: '',
    permissions: []
  }
}

function createUserStatusActionForm(): UserStatusActionForm {
  return {
    userId: '',
    userName: '',
    status: 'Enabled',
    mode: 'disable'
  }
}

function createResetPasswordForm(): ResetPasswordForm {
  return {
    userId: '',
    userName: '',
    newPassword: '',
    confirmPassword: ''
  }
}

function createDeleteRoleForm(): DeleteRoleForm {
  return {
    roleId: '',
    roleName: ''
  }
}

function createAuditQuery(): AuditLogQuery {
  return {
    page: 1,
    pageSize: 10,
    actionGroup: '',
    actionCode: '',
    targetType: '',
    targetName: '',
    operatorUserName: '',
    result: '',
    from: '',
    to: ''
  }
}

function collectMessageTexts(value: unknown): string[] {
  if (typeof value === 'string') {
    return value.trim() ? [value.trim()] : []
  }

  if (Array.isArray(value)) {
    return value.flatMap((item) => collectMessageTexts(item))
  }

  if (!value || typeof value !== 'object') {
    return []
  }

  const candidate = value as Record<string, unknown>
  const detail = typeof candidate.description === 'string'
    ? candidate.description
    : typeof candidate.message === 'string'
      ? candidate.message
      : typeof candidate.errorMessage === 'string'
        ? candidate.errorMessage
        : undefined

  return detail && detail.trim() ? [detail.trim()] : []
}

function getValidationMessages(details: unknown) {
  if (!details || typeof details !== 'object') {
    return []
  }

  const errors = (details as Record<string, unknown>).errors
  if (Array.isArray(errors)) {
    return [...new Set(errors.flatMap((item) => collectMessageTexts(item)))]
  }

  if (!errors || typeof errors !== 'object') {
    return []
  }

  return [
    ...new Set(
      Object.values(errors as Record<string, unknown>).flatMap((item) => collectMessageTexts(item))
    )
  ]
}

function buildForbiddenMessage(baseMessage: string, details: unknown) {
  const detail = getProblemDetail(details)
  const missingPermissions = getMissingPermissions(details)

  if (detail && missingPermissions.length > 0) {
    return `${detail} 缺少权限：${missingPermissions.join('、')}`
  }

  if (detail) {
    return detail
  }

  if (missingPermissions.length > 0) {
    return `${baseMessage} 缺少权限：${missingPermissions.join('、')}`
  }

  return baseMessage
}

function toErrorMessage(error: unknown, fallback: string, forbiddenMessage: string) {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.status === 403) {
    return buildForbiddenMessage(forbiddenMessage, error.details)
  }

  const detail = getProblemDetail(error.details)
  if (detail) {
    return detail
  }

  const validationMessages = getValidationMessages(error.details)
  if (validationMessages.length > 0) {
    return validationMessages.join('；')
  }

  return fallback
}

export const useAccessStore = defineStore('access', () => {
  const authStore = useAuthStore()

  const permissions = ref<PermissionDefinition[]>([])
  const roles = ref<RoleSummary[]>([])
  const users = ref<UserSummary[]>([])
  const auditLogs = ref<AuditLogSummary[]>([])

  const isLoading = ref(false)
  const errorMessage = ref('')

  const isAuditLoading = ref(false)
  const auditErrorMessage = ref('')
  const auditTotalCount = ref(0)
  const auditQuery = ref<AuditLogQuery>(createAuditQuery())

  const userDialogVisible = ref(false)
  const userRoleDialogVisible = ref(false)
  const roleDialogVisible = ref(false)
  const userStatusDialogVisible = ref(false)
  const resetPasswordDialogVisible = ref(false)
  const deleteRoleDialogVisible = ref(false)

  const isSubmittingUser = ref(false)
  const isSubmittingUserRole = ref(false)
  const isSubmittingRole = ref(false)
  const isSubmittingUserStatus = ref(false)
  const isSubmittingResetPassword = ref(false)
  const isSubmittingDeleteRole = ref(false)

  const userDialogError = ref('')
  const userRoleDialogError = ref('')
  const roleDialogError = ref('')
  const userStatusDialogError = ref('')
  const resetPasswordDialogError = ref('')
  const deleteRoleDialogError = ref('')

  const currentUserForm = ref<UserCreateForm>(createUserCreateForm())
  const currentUserRoleForm = ref<UserRoleForm>(createUserRoleForm())
  const currentRoleForm = ref<RoleForm>(createRoleForm())
  const currentUserStatusAction = ref<UserStatusActionForm>(createUserStatusActionForm())
  const currentResetPasswordForm = ref<ResetPasswordForm>(createResetPasswordForm())
  const currentDeleteRoleForm = ref<DeleteRoleForm>(createDeleteRoleForm())
  const roleDialogMode = ref<'create' | 'edit'>('create')

  const permissionGroups = computed(() => {
    const groups = new Map<string, PermissionDefinition[]>()
    for (const definition of permissions.value) {
      if (!groups.has(definition.group)) {
        groups.set(definition.group, [])
      }

      groups.get(definition.group)?.push(definition)
    }

    return [...groups.entries()]
      .sort(([leftGroup], [rightGroup]) => leftGroup.localeCompare(rightGroup))
      .map(([group, items]) => ({
        group,
        items: [...items].sort((left, right) => left.code.localeCompare(right.code))
      }))
  })

  function shouldLoadPermissions() {
    return (
      authStore.hasPermission('Identity.GetListPermissions') ||
      authStore.hasPermission('Identity.CreateRole') ||
      authStore.hasPermission('Identity.UpdateRole')
    )
  }

  function shouldLoadRoles() {
    return (
      authStore.hasPermission('Identity.GetListRoles') ||
      authStore.hasPermission('Identity.CreateUser') ||
      authStore.hasPermission('Identity.UpdateUserRole') ||
      authStore.hasPermission('Identity.CreateRole') ||
      authStore.hasPermission('Identity.UpdateRole') ||
      authStore.hasPermission('Identity.DeleteRole')
    )
  }

  function shouldLoadUsers() {
    return (
      authStore.hasPermission('Identity.GetListUsers') ||
      authStore.hasPermission('Identity.CreateUser') ||
      authStore.hasPermission('Identity.UpdateUserRole') ||
      authStore.hasPermission('Identity.DisableUser') ||
      authStore.hasPermission('Identity.EnableUser') ||
      authStore.hasPermission('Identity.ResetUserPassword')
    )
  }

  async function refreshPermissions() {
    if (!shouldLoadPermissions()) {
      permissions.value = []
      return
    }

    permissions.value = await identityService.getPermissions()
  }

  async function refreshRoles() {
    if (!shouldLoadRoles()) {
      roles.value = []
      return
    }

    roles.value = await identityService.getRoles()
  }

  async function refreshUsers() {
    if (!shouldLoadUsers()) {
      users.value = []
      return
    }

    users.value = await identityService.getUsers()
  }

  async function refresh() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      await Promise.all([refreshPermissions(), refreshRoles(), refreshUsers()])

      if (authStore.hasPermission('Identity.GetListAuditLogs')) {
        await loadAuditLogs()
      } else {
        auditLogs.value = []
        auditTotalCount.value = 0
        auditErrorMessage.value = ''
      }
    } catch (error) {
      errorMessage.value = toErrorMessage(
        error,
        '权限治理页面加载失败，请稍后重试。',
        '当前账号没有访问权限治理页面所需的权限。'
      )
      throw error
    } finally {
      isLoading.value = false
    }
  }

  async function loadAuditLogs(page = auditQuery.value.page) {
    if (!authStore.hasPermission('Identity.GetListAuditLogs')) {
      auditLogs.value = []
      auditTotalCount.value = 0
      auditErrorMessage.value = ''
      return
    }

    isAuditLoading.value = true
    auditErrorMessage.value = ''
    auditQuery.value.page = page

    try {
      const response = await identityService.getAuditLogs(auditQuery.value)
      auditLogs.value = response.items
      auditTotalCount.value = response.totalCount
      auditQuery.value.page = response.page
      auditQuery.value.pageSize = response.pageSize
    } catch (error) {
      auditErrorMessage.value = toErrorMessage(
        error,
        '审计记录加载失败，请稍后重试。',
        '当前账号没有查看审计记录的权限。'
      )
      throw error
    } finally {
      isAuditLoading.value = false
    }
  }

  async function applyAuditFilters() {
    await loadAuditLogs(1)
  }

  async function resetAuditFilters() {
    auditQuery.value = createAuditQuery()
    await loadAuditLogs(1)
  }

  function openCreateUserDialog() {
    currentUserForm.value = createUserCreateForm()
    userDialogError.value = ''
    userDialogVisible.value = true
  }

  function closeCreateUserDialog() {
    currentUserForm.value = createUserCreateForm()
    userDialogError.value = ''
    userDialogVisible.value = false
  }

  function openChangeUserRoleDialog(user: UserSummary) {
    currentUserRoleForm.value = {
      userId: user.userId,
      userName: user.userName,
      roleName: user.roleName ?? ''
    }
    userRoleDialogError.value = ''
    userRoleDialogVisible.value = true
  }

  function closeChangeUserRoleDialog() {
    currentUserRoleForm.value = createUserRoleForm()
    userRoleDialogError.value = ''
    userRoleDialogVisible.value = false
  }

  function openCreateRoleDialog() {
    roleDialogMode.value = 'create'
    currentRoleForm.value = createRoleForm()
    roleDialogError.value = ''
    roleDialogVisible.value = true
  }

  function openEditRoleDialog(role: RoleSummary) {
    roleDialogMode.value = 'edit'
    currentRoleForm.value = {
      roleId: role.roleId,
      roleName: role.roleName,
      permissions: [...role.permissions]
    }
    roleDialogError.value = ''
    roleDialogVisible.value = true
  }

  function closeRoleDialog() {
    roleDialogMode.value = 'create'
    currentRoleForm.value = createRoleForm()
    roleDialogError.value = ''
    roleDialogVisible.value = false
  }

  function openDisableUserDialog(user: UserSummary) {
    currentUserStatusAction.value = {
      userId: user.userId,
      userName: user.userName,
      status: user.status,
      mode: 'disable'
    }
    userStatusDialogError.value = ''
    userStatusDialogVisible.value = true
  }

  function openEnableUserDialog(user: UserSummary) {
    currentUserStatusAction.value = {
      userId: user.userId,
      userName: user.userName,
      status: user.status,
      mode: 'enable'
    }
    userStatusDialogError.value = ''
    userStatusDialogVisible.value = true
  }

  function closeUserStatusDialog() {
    currentUserStatusAction.value = createUserStatusActionForm()
    userStatusDialogError.value = ''
    userStatusDialogVisible.value = false
  }

  function openResetPasswordDialog(user: UserSummary) {
    currentResetPasswordForm.value = {
      userId: user.userId,
      userName: user.userName,
      newPassword: '',
      confirmPassword: ''
    }
    resetPasswordDialogError.value = ''
    resetPasswordDialogVisible.value = true
  }

  function closeResetPasswordDialog() {
    currentResetPasswordForm.value = createResetPasswordForm()
    resetPasswordDialogError.value = ''
    resetPasswordDialogVisible.value = false
  }

  function openDeleteRoleDialog(role: RoleSummary) {
    currentDeleteRoleForm.value = {
      roleId: role.roleId,
      roleName: role.roleName
    }
    deleteRoleDialogError.value = ''
    deleteRoleDialogVisible.value = true
  }

  function closeDeleteRoleDialog() {
    currentDeleteRoleForm.value = createDeleteRoleForm()
    deleteRoleDialogError.value = ''
    deleteRoleDialogVisible.value = false
  }

  async function createUser() {
    isSubmittingUser.value = true
    userDialogError.value = ''

    try {
      await identityService.createUser({
        userName: currentUserForm.value.userName.trim(),
        password: currentUserForm.value.password,
        roleName: currentUserForm.value.roleName
      })

      await refresh()
      closeCreateUserDialog()
    } catch (error) {
      userDialogError.value = toErrorMessage(
        error,
        '创建用户失败，请稍后重试。',
        '当前账号没有创建用户的权限。'
      )
      throw error
    } finally {
      isSubmittingUser.value = false
    }
  }

  async function updateUserRole() {
    isSubmittingUserRole.value = true
    userRoleDialogError.value = ''

    try {
      await identityService.updateUserRole({
        userId: currentUserRoleForm.value.userId,
        roleName: currentUserRoleForm.value.roleName
      })

      if (authStore.currentUser?.userId === currentUserRoleForm.value.userId) {
        await authStore.refreshCurrentUser()
      }

      await refresh()
      closeChangeUserRoleDialog()
    } catch (error) {
      userRoleDialogError.value = toErrorMessage(
        error,
        '更新用户角色失败，请稍后重试。',
        '当前账号没有调整用户角色的权限。'
      )
      throw error
    } finally {
      isSubmittingUserRole.value = false
    }
  }

  async function saveRole() {
    isSubmittingRole.value = true
    roleDialogError.value = ''

    try {
      if (roleDialogMode.value === 'create') {
        await identityService.createRole({
          roleName: currentRoleForm.value.roleName.trim(),
          permissions: [...currentRoleForm.value.permissions]
        })
      } else {
        await identityService.updateRole({
          roleId: currentRoleForm.value.roleId!,
          permissions: [...currentRoleForm.value.permissions]
        })

        if (authStore.roleName === currentRoleForm.value.roleName) {
          await authStore.refreshCurrentUser()
        }
      }

      await refresh()
      closeRoleDialog()
    } catch (error) {
      roleDialogError.value = toErrorMessage(
        error,
        '保存角色失败，请稍后重试。',
        '当前账号没有管理角色权限的权限。'
      )
      throw error
    } finally {
      isSubmittingRole.value = false
    }
  }

  async function saveUserStatusAction() {
    isSubmittingUserStatus.value = true
    userStatusDialogError.value = ''

    try {
      if (currentUserStatusAction.value.mode === 'disable') {
        await identityService.disableUser({
          userId: currentUserStatusAction.value.userId
        })
      } else {
        await identityService.enableUser({
          userId: currentUserStatusAction.value.userId
        })
      }

      await refresh()
      closeUserStatusDialog()
    } catch (error) {
      userStatusDialogError.value = toErrorMessage(
        error,
        currentUserStatusAction.value.mode === 'disable'
          ? '禁用用户失败，请稍后重试。'
          : '恢复启用用户失败，请稍后重试。',
        currentUserStatusAction.value.mode === 'disable'
          ? '当前账号没有禁用用户的权限。'
          : '当前账号没有恢复启用用户的权限。'
      )
      throw error
    } finally {
      isSubmittingUserStatus.value = false
    }
  }

  async function resetUserPassword() {
    isSubmittingResetPassword.value = true
    resetPasswordDialogError.value = ''

    try {
      await identityService.resetUserPassword({
        userId: currentResetPasswordForm.value.userId,
        newPassword: currentResetPasswordForm.value.newPassword
      })

      await refresh()
      closeResetPasswordDialog()
    } catch (error) {
      resetPasswordDialogError.value = toErrorMessage(
        error,
        '重置密码失败，请稍后重试。',
        '当前账号没有重置用户密码的权限。'
      )
      throw error
    } finally {
      isSubmittingResetPassword.value = false
    }
  }

  async function deleteRole() {
    isSubmittingDeleteRole.value = true
    deleteRoleDialogError.value = ''

    try {
      await identityService.deleteRole({
        roleId: currentDeleteRoleForm.value.roleId
      })

      await refresh()
      closeDeleteRoleDialog()
    } catch (error) {
      deleteRoleDialogError.value = toErrorMessage(
        error,
        '删除角色失败，请稍后重试。',
        '当前账号没有删除角色的权限。'
      )
      throw error
    } finally {
      isSubmittingDeleteRole.value = false
    }
  }

  return {
    permissions,
    roles,
    users,
    auditLogs,
    isLoading,
    errorMessage,
    isAuditLoading,
    auditErrorMessage,
    auditTotalCount,
    auditQuery,
    permissionGroups,
    userDialogVisible,
    userRoleDialogVisible,
    roleDialogVisible,
    userStatusDialogVisible,
    resetPasswordDialogVisible,
    deleteRoleDialogVisible,
    isSubmittingUser,
    isSubmittingUserRole,
    isSubmittingRole,
    isSubmittingUserStatus,
    isSubmittingResetPassword,
    isSubmittingDeleteRole,
    userDialogError,
    userRoleDialogError,
    roleDialogError,
    userStatusDialogError,
    resetPasswordDialogError,
    deleteRoleDialogError,
    currentUserForm,
    currentUserRoleForm,
    currentRoleForm,
    currentUserStatusAction,
    currentResetPasswordForm,
    currentDeleteRoleForm,
    roleDialogMode,
    refresh,
    loadAuditLogs,
    applyAuditFilters,
    resetAuditFilters,
    openCreateUserDialog,
    closeCreateUserDialog,
    openChangeUserRoleDialog,
    closeChangeUserRoleDialog,
    openCreateRoleDialog,
    openEditRoleDialog,
    closeRoleDialog,
    openDisableUserDialog,
    openEnableUserDialog,
    closeUserStatusDialog,
    openResetPasswordDialog,
    closeResetPasswordDialog,
    openDeleteRoleDialog,
    closeDeleteRoleDialog,
    createUser,
    updateUserRole,
    saveRole,
    saveUserStatusAction,
    resetUserPassword,
    deleteRole
  }
})
