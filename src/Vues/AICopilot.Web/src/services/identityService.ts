import { apiClient } from './apiClient'
import type {
  AuditLogListResponse,
  AuditLogQuery,
  CurrentUserProfile,
  InitializationStatus,
  LoginRequest,
  LoginResponse,
  PermissionDefinition,
  RoleSummary,
  UserSummary
} from '@/types/app'

export const identityService = {
  async getInitializationStatus() {
    return await apiClient.get<InitializationStatus>('/identity/initialization-status')
  },

  async login(payload: LoginRequest) {
    return await apiClient.post<LoginResponse>('/identity/login', payload)
  },

  async getCurrentUserProfile() {
    return await apiClient.get<CurrentUserProfile>('/identity/me')
  },

  async getAuditLogs(query: AuditLogQuery) {
    return await apiClient.get<AuditLogListResponse>('/identity/audit-log/list', { ...query })
  },

  async getPermissions() {
    return await apiClient.get<PermissionDefinition[]>('/identity/permission/list')
  },

  async getRoles() {
    return await apiClient.get<RoleSummary[]>('/identity/role/list')
  },

  async createRole(payload: { roleName: string; permissions: string[] }) {
    return await apiClient.post<RoleSummary>('/identity/role', payload)
  },

  async updateRole(payload: { roleId: string; permissions: string[] }) {
    return await apiClient.put<RoleSummary>('/identity/role', payload)
  },

  async deleteRole(payload: { roleId: string }) {
    return await apiClient.delete<void>('/identity/role', payload)
  },

  async getUsers() {
    return await apiClient.get<UserSummary[]>('/identity/user/list')
  },

  async createUser(payload: { userName: string; password: string; roleName: string }) {
    return await apiClient.post<UserSummary>('/identity/user', payload)
  },

  async updateUserRole(payload: { userId: string; roleName: string }) {
    return await apiClient.put<UserSummary>('/identity/user/role', payload)
  },

  async disableUser(payload: { userId: string }) {
    return await apiClient.put<UserSummary>('/identity/user/disable', payload)
  },

  async enableUser(payload: { userId: string }) {
    return await apiClient.put<UserSummary>('/identity/user/enable', payload)
  },

  async resetUserPassword(payload: { userId: string; newPassword: string }) {
    return await apiClient.put<void>('/identity/user/password/reset', payload)
  }
}
