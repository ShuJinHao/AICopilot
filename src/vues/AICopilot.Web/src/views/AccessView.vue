<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import { ElMessage } from 'element-plus'
import AppShell from '@/components/layout/AppShell.vue'
import { useAccessStore } from '@/stores/accessStore'
import { useAuthStore } from '@/stores/authStore'
import type { RoleSummary } from '@/types/app'

const authStore = useAuthStore()
const accessStore = useAccessStore()

const canReadUsers = computed(() => authStore.hasPermission('Identity.GetListUsers'))
const canCreateUsers = computed(() => authStore.hasPermission('Identity.CreateUser'))
const canUpdateUserRole = computed(() => authStore.hasPermission('Identity.UpdateUserRole'))
const canDisableUsers = computed(() => authStore.hasPermission('Identity.DisableUser'))
const canEnableUsers = computed(() => authStore.hasPermission('Identity.EnableUser'))
const canResetUserPassword = computed(() => authStore.hasPermission('Identity.ResetUserPassword'))

const canReadRoles = computed(() => authStore.hasPermission('Identity.GetListRoles'))
const canCreateRoles = computed(() => authStore.hasPermission('Identity.CreateRole'))
const canUpdateRoles = computed(() => authStore.hasPermission('Identity.UpdateRole'))
const canDeleteRoles = computed(() => authStore.hasPermission('Identity.DeleteRole'))

const canReadPermissions = computed(() => authStore.hasPermission('Identity.GetListPermissions'))
const canViewAuditLogs = computed(() => authStore.hasPermission('Identity.GetListAuditLogs'))

const showUserSection = computed(
  () =>
    canReadUsers.value ||
    canCreateUsers.value ||
    canUpdateUserRole.value ||
    canDisableUsers.value ||
    canEnableUsers.value ||
    canResetUserPassword.value
)

const showRoleSection = computed(
  () =>
    canReadRoles.value ||
    canCreateRoles.value ||
    canUpdateRoles.value ||
    canDeleteRoles.value
)

const showPermissionSection = computed(
  () => canReadPermissions.value || canCreateRoles.value || canUpdateRoles.value
)

const userFormRef = ref<FormInstance>()
const userRoleFormRef = ref<FormInstance>()
const roleFormRef = ref<FormInstance>()
const resetPasswordFormRef = ref<FormInstance>()

const roleOptions = computed(() =>
  accessStore.roles.map((item) => ({
    label: item.roleName,
    value: item.roleName
  }))
)

const actionGroupOptions = [
  { label: '全部分组', value: '' },
  { label: '身份治理', value: 'Identity' },
  { label: '配置治理', value: 'Config' },
  { label: '审批治理', value: 'Approval' }
]

const auditResultOptions = [
  { label: '全部结果', value: '' },
  { label: '成功', value: 'Succeeded' },
  { label: '拒绝', value: 'Rejected' }
]

const userRules: FormRules = {
  userName: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入初始密码', trigger: 'blur' }],
  roleName: [{ required: true, message: '请选择初始角色', trigger: 'change' }]
}

const userRoleRules: FormRules = {
  roleName: [{ required: true, message: '请选择新的角色', trigger: 'change' }]
}

const roleRules: FormRules = {
  roleName: [{ required: true, message: '请输入角色名称', trigger: 'blur' }],
  permissions: [
    {
      validator: (_rule, value, callback) => {
        if (!Array.isArray(value) || value.length === 0) {
          callback(new Error('请至少选择一项权限'))
          return
        }

        callback()
      },
      trigger: 'change'
    }
  ]
}

const resetPasswordRules: FormRules = {
  newPassword: [{ required: true, message: '请输入新密码', trigger: 'blur' }],
  confirmPassword: [
    { required: true, message: '请再次输入新密码', trigger: 'blur' },
    {
      validator: (_rule, value, callback) => {
        if (value !== accessStore.currentResetPasswordForm.confirmPassword) {
          callback(new Error('确认密码状态异常，请重新输入'))
          return
        }

        if (value !== accessStore.currentResetPasswordForm.newPassword) {
          callback(new Error('两次输入的密码不一致'))
          return
        }

        callback()
      },
      trigger: 'blur'
    }
  ]
}

const userStatusDialogTitle = computed(() =>
  accessStore.currentUserStatusAction.mode === 'disable' ? '禁用用户' : '恢复启用用户'
)

const userStatusConfirmText = computed(() =>
  accessStore.currentUserStatusAction.mode === 'disable'
    ? `确认禁用用户 ${accessStore.currentUserStatusAction.userName} 吗？禁用后该用户无法登录，已有登录态会在下一次请求时失效。`
    : `确认恢复启用用户 ${accessStore.currentUserStatusAction.userName} 吗？恢复后该用户可以使用新状态重新登录。`
)

async function validate(formRef: FormInstance | undefined) {
  if (!formRef) {
    return false
  }

  return await formRef.validate().catch(() => false)
}

function formatAuditTime(value?: string | null) {
  if (!value) {
    return '-'
  }

  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return value
  }

  return parsed.toLocaleString('zh-CN', { hour12: false })
}

function getUserStatusTagType(isEnabled: boolean) {
  return isEnabled ? 'success' : 'warning'
}

function getRoleDeleteReason(role: RoleSummary) {
  if (role.isSystemRole) {
    return '系统基线角色不可删除'
  }

  if (role.assignedUserCount > 0) {
    return `已绑定 ${role.assignedUserCount} 个用户`
  }

  return '可删除'
}

function canDeleteRoleItem(role: RoleSummary) {
  return canDeleteRoles.value && !role.isSystemRole && role.assignedUserCount === 0
}

async function saveUser() {
  const valid = await validate(userFormRef.value)
  if (!valid) {
    return
  }

  try {
    await accessStore.createUser()
    ElMessage.success('用户已创建')
  } catch {
    // Store 内已处理错误
  }
}

async function saveUserRole() {
  const valid = await validate(userRoleFormRef.value)
  if (!valid) {
    return
  }

  try {
    await accessStore.updateUserRole()
    ElMessage.success('用户角色已更新')
  } catch {
    // Store 内已处理错误
  }
}

async function saveRole() {
  const valid = await validate(roleFormRef.value)
  if (!valid) {
    return
  }

  try {
    await accessStore.saveRole()
    ElMessage.success(accessStore.roleDialogMode === 'create' ? '角色已创建' : '角色权限已更新')
  } catch {
    // Store 内已处理错误
  }
}

async function saveUserStatusAction() {
  try {
    await accessStore.saveUserStatusAction()
    ElMessage.success(
      accessStore.currentUserStatusAction.mode === 'disable' ? '用户已禁用' : '用户已恢复启用'
    )
  } catch {
    // Store 内已处理错误
  }
}

async function saveResetPassword() {
  const valid = await validate(resetPasswordFormRef.value)
  if (!valid) {
    return
  }

  try {
    await accessStore.resetUserPassword()
    ElMessage.success('密码已重置')
  } catch {
    // Store 内已处理错误
  }
}

async function saveDeleteRole() {
  try {
    await accessStore.deleteRole()
    ElMessage.success('角色已删除')
  } catch {
    // Store 内已处理错误
  }
}

async function applyAuditFilters() {
  try {
    await accessStore.applyAuditFilters()
  } catch {
    // Store 内已处理错误
  }
}

async function resetAuditFilters() {
  try {
    await accessStore.resetAuditFilters()
  } catch {
    // Store 内已处理错误
  }
}

async function changeAuditPage(page: number) {
  try {
    await accessStore.loadAuditLogs(page)
  } catch {
    // Store 内已处理错误
  }
}

onMounted(async () => {
  try {
    await accessStore.refresh()
  } catch {
    // 页面顶部错误会展示具体信息
  }
})
</script>

<template>
  <AppShell>
    <div class="access-page">
      <section class="hero">
        <div>
          <h1>权限与治理</h1>
          <p>维护用户、角色、权限目录，并收口最小治理审计能力。</p>
        </div>
        <div class="hero-metrics">
          <el-statistic title="当前角色" :value="authStore.roleName || '未分配'" />
          <el-statistic title="当前权限数" :value="authStore.permissions.length" />
          <el-statistic title="审计记录数" :value="accessStore.auditTotalCount" />
        </div>
      </section>

      <el-alert
        v-if="accessStore.errorMessage"
        :title="accessStore.errorMessage"
        type="error"
        show-icon
        :closable="false"
      />

      <div v-if="accessStore.isLoading" class="loading-panel">
        <el-skeleton :rows="8" animated />
      </div>

      <template v-else>
        <section class="summary-grid">
          <el-card class="summary-card" shadow="hover">
            <template #header>
              <span>当前账号</span>
            </template>
            <dl class="summary-list">
              <div>
                <dt>用户名</dt>
                <dd>{{ authStore.userName || '未登录' }}</dd>
              </div>
              <div>
                <dt>角色</dt>
                <dd>{{ authStore.roleName || '未分配角色' }}</dd>
              </div>
              <div>
                <dt>权限数量</dt>
                <dd>{{ authStore.permissions.length }}</dd>
              </div>
            </dl>
          </el-card>

          <el-card class="summary-card" shadow="hover">
            <template #header>
              <span>权限目录分组</span>
            </template>
            <div class="chip-list">
              <el-tag v-for="group in accessStore.permissionGroups" :key="group.group" type="info">
                {{ group.group }} / {{ group.items.length }}
              </el-tag>
            </div>
          </el-card>
        </section>

        <section class="section-grid">
          <el-card v-if="showUserSection" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>用户列表</span>
                  <el-tag type="info">{{ accessStore.users.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateUsers"
                  type="primary"
                  plain
                  size="small"
                  @click="accessStore.openCreateUserDialog()"
                >
                  新建用户
                </el-button>
              </div>
            </template>

            <div v-if="!canReadUsers" class="section-empty">
              <el-empty description="当前账号没有查看用户列表权限，但仍可使用已授权的用户治理操作。" />
            </div>

            <el-table v-else :data="accessStore.users" size="small" empty-text="暂无用户">
              <el-table-column prop="userName" label="用户名" min-width="180" />
              <el-table-column label="当前角色" min-width="140">
                <template #default="{ row }">
                  <span v-if="row.roleName">{{ row.roleName }}</span>
                  <span v-else class="muted-text">未分配角色</span>
                </template>
              </el-table-column>
              <el-table-column label="状态" width="120">
                <template #default="{ row }">
                  <el-tag :type="getUserStatusTagType(row.isEnabled)">
                    {{ row.isEnabled ? '启用中' : '已禁用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" min-width="280" fixed="right">
                <template #default="{ row }">
                  <div class="action-links">
                    <el-button
                      v-if="canUpdateUserRole"
                      link
                      type="primary"
                      @click="accessStore.openChangeUserRoleDialog(row)"
                    >
                      调整角色
                    </el-button>
                    <el-button
                      v-if="row.isEnabled && canDisableUsers"
                      link
                      type="warning"
                      @click="accessStore.openDisableUserDialog(row)"
                    >
                      禁用
                    </el-button>
                    <el-button
                      v-if="!row.isEnabled && canEnableUsers"
                      link
                      type="success"
                      @click="accessStore.openEnableUserDialog(row)"
                    >
                      恢复启用
                    </el-button>
                    <el-button
                      v-if="canResetUserPassword"
                      link
                      type="danger"
                      @click="accessStore.openResetPasswordDialog(row)"
                    >
                      重置密码
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card v-if="showRoleSection" class="section-card" shadow="hover">
            <template #header>
              <div class="section-header">
                <div class="section-title">
                  <span>角色列表</span>
                  <el-tag type="info">{{ accessStore.roles.length }}</el-tag>
                </div>
                <el-button
                  v-if="canCreateRoles"
                  type="primary"
                  plain
                  size="small"
                  @click="accessStore.openCreateRoleDialog()"
                >
                  新建角色
                </el-button>
              </div>
            </template>

            <div v-if="!canReadRoles" class="section-empty">
              <el-empty description="当前账号没有查看角色列表权限，但仍可使用已授权的角色治理操作。" />
            </div>

            <el-table v-else :data="accessStore.roles" size="small" empty-text="暂无角色">
              <el-table-column label="角色名称" min-width="180">
                <template #default="{ row }">
                  <div class="role-name-cell">
                    <span>{{ row.roleName }}</span>
                    <el-tag size="small" :type="row.isSystemRole ? 'warning' : 'info'">
                      {{ row.isSystemRole ? '系统基线' : '自定义' }}
                    </el-tag>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="绑定用户数" width="120">
                <template #default="{ row }">
                  {{ row.assignedUserCount }}
                </template>
              </el-table-column>
              <el-table-column label="权限数" width="100">
                <template #default="{ row }">
                  {{ row.permissions.length }}
                </template>
              </el-table-column>
              <el-table-column label="删除规则" min-width="180">
                <template #default="{ row }">
                  <el-tag :type="canDeleteRoleItem(row) ? 'success' : 'info'">
                    {{ getRoleDeleteReason(row) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="权限" min-width="260">
                <template #default="{ row }">
                  <div class="chip-list">
                    <el-tag v-for="permission in row.permissions" :key="permission" size="small">
                      {{ permission }}
                    </el-tag>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="操作" min-width="180" fixed="right">
                <template #default="{ row }">
                  <div class="action-links">
                    <el-button
                      v-if="canUpdateRoles"
                      link
                      type="primary"
                      @click="accessStore.openEditRoleDialog(row)"
                    >
                      编辑权限
                    </el-button>
                    <el-button
                      v-if="canDeleteRoleItem(row)"
                      link
                      type="danger"
                      @click="accessStore.openDeleteRoleDialog(row)"
                    >
                      删除角色
                    </el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </section>

        <el-card v-if="showPermissionSection" class="section-card" shadow="hover">
          <template #header>
            <div class="section-header">
              <div class="section-title">
                <span>权限目录</span>
                <el-tag type="info">{{ accessStore.permissions.length }}</el-tag>
              </div>
            </div>
          </template>

          <div class="permission-groups">
            <section
              v-for="group in accessStore.permissionGroups"
              :key="group.group"
              class="permission-group"
            >
              <h3>{{ group.group }}</h3>
              <el-table :data="group.items" size="small" empty-text="暂无权限">
                <el-table-column prop="displayName" label="显示名称" min-width="160" />
                <el-table-column prop="code" label="权限码" min-width="240" />
                <el-table-column prop="description" label="说明" min-width="280" />
              </el-table>
            </section>
          </div>
        </el-card>

        <el-card v-if="canViewAuditLogs" class="section-card audit-card" shadow="hover">
          <template #header>
            <div class="section-header">
              <div class="section-title">
                <span>审计记录</span>
                <el-tag type="info">{{ accessStore.auditTotalCount }}</el-tag>
              </div>
            </div>
          </template>

          <div class="audit-filters">
            <el-select
              v-model="accessStore.auditQuery.actionGroup"
              placeholder="操作分组"
              clearable
            >
              <el-option
                v-for="option in actionGroupOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>

            <el-select v-model="accessStore.auditQuery.result" placeholder="结果" clearable>
              <el-option
                v-for="option in auditResultOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>

            <el-input v-model="accessStore.auditQuery.actionCode" placeholder="动作码" clearable />
            <el-input v-model="accessStore.auditQuery.targetType" placeholder="目标类型" clearable />
            <el-input v-model="accessStore.auditQuery.targetName" placeholder="目标名称" clearable />
            <el-input
              v-model="accessStore.auditQuery.operatorUserName"
              placeholder="操作者"
              clearable
            />

            <el-date-picker
              v-model="accessStore.auditQuery.from"
              type="datetime"
              placeholder="开始时间"
              value-format="YYYY-MM-DDTHH:mm:ss"
              clearable
            />

            <el-date-picker
              v-model="accessStore.auditQuery.to"
              type="datetime"
              placeholder="结束时间"
              value-format="YYYY-MM-DDTHH:mm:ss"
              clearable
            />

            <div class="filter-actions">
              <el-button type="primary" :loading="accessStore.isAuditLoading" @click="applyAuditFilters">
                查询
              </el-button>
              <el-button @click="resetAuditFilters">重置</el-button>
            </div>
          </div>

          <el-alert
            v-if="accessStore.auditErrorMessage"
            :title="accessStore.auditErrorMessage"
            type="error"
            show-icon
            :closable="false"
            class="dialog-alert"
          />

          <el-table
            :data="accessStore.auditLogs"
            size="small"
            empty-text="暂无审计记录"
            v-loading="accessStore.isAuditLoading"
          >
            <el-table-column prop="createdAt" label="时间" min-width="170">
              <template #default="{ row }">
                {{ formatAuditTime(row.createdAt) }}
              </template>
            </el-table-column>
            <el-table-column prop="actionGroup" label="分组" width="120" />
            <el-table-column prop="actionCode" label="动作码" min-width="220" />
            <el-table-column prop="targetType" label="目标类型" min-width="130" />
            <el-table-column prop="targetName" label="目标名称" min-width="180" />
            <el-table-column prop="operatorUserName" label="操作者" min-width="140" />
            <el-table-column prop="operatorRoleName" label="操作者角色" min-width="140" />
            <el-table-column prop="result" label="结果" width="110">
              <template #default="{ row }">
                <el-tag :type="row.result === 'Succeeded' ? 'success' : 'warning'">
                  {{ row.result === 'Succeeded' ? '成功' : '拒绝' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="summary" label="摘要" min-width="320" />
            <el-table-column label="变更字段" min-width="220">
              <template #default="{ row }">
                <div class="chip-list">
                  <el-tag
                    v-for="field in row.changedFields"
                    :key="field"
                    size="small"
                    type="info"
                  >
                    {{ field }}
                  </el-tag>
                  <span v-if="row.changedFields.length === 0" class="muted-text">无字段变更</span>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div class="pagination-wrap">
            <el-pagination
              background
              layout="prev, pager, next, total"
              :current-page="accessStore.auditQuery.page"
              :page-size="accessStore.auditQuery.pageSize"
              :total="accessStore.auditTotalCount"
              @current-change="changeAuditPage"
            />
          </div>
        </el-card>
      </template>
    </div>

    <el-dialog
      v-model="accessStore.userDialogVisible"
      title="新建用户"
      width="520px"
      destroy-on-close
      @closed="accessStore.closeCreateUserDialog()"
    >
      <el-alert
        v-if="accessStore.userDialogError"
        :title="accessStore.userDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form ref="userFormRef" :model="accessStore.currentUserForm" :rules="userRules" label-position="top">
        <el-form-item label="用户名" prop="userName">
          <el-input v-model="accessStore.currentUserForm.userName" placeholder="请输入用户名" />
        </el-form-item>
        <el-form-item label="初始密码" prop="password">
          <el-input
            v-model="accessStore.currentUserForm.password"
            type="password"
            show-password
            placeholder="请输入初始密码"
          />
        </el-form-item>
        <el-form-item label="初始角色" prop="roleName">
          <el-select
            v-model="accessStore.currentUserForm.roleName"
            placeholder="请选择角色"
            style="width: 100%"
          >
            <el-option
              v-for="item in roleOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeCreateUserDialog()">取消</el-button>
          <el-button type="primary" :loading="accessStore.isSubmittingUser" @click="saveUser">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="accessStore.userRoleDialogVisible"
      title="调整用户角色"
      width="520px"
      destroy-on-close
      @closed="accessStore.closeChangeUserRoleDialog()"
    >
      <el-alert
        v-if="accessStore.userRoleDialogError"
        :title="accessStore.userRoleDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="userRoleFormRef"
        :model="accessStore.currentUserRoleForm"
        :rules="userRoleRules"
        label-position="top"
      >
        <el-form-item label="用户名">
          <el-input :model-value="accessStore.currentUserRoleForm.userName" disabled />
        </el-form-item>
        <el-form-item label="新的角色" prop="roleName">
          <el-select
            v-model="accessStore.currentUserRoleForm.roleName"
            placeholder="请选择角色"
            style="width: 100%"
          >
            <el-option
              v-for="item in roleOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeChangeUserRoleDialog()">取消</el-button>
          <el-button type="primary" :loading="accessStore.isSubmittingUserRole" @click="saveUserRole">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="accessStore.roleDialogVisible"
      :title="accessStore.roleDialogMode === 'create' ? '新建角色' : '编辑角色权限'"
      width="760px"
      destroy-on-close
      @closed="accessStore.closeRoleDialog()"
    >
      <el-alert
        v-if="accessStore.roleDialogError"
        :title="accessStore.roleDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form ref="roleFormRef" :model="accessStore.currentRoleForm" :rules="roleRules" label-position="top">
        <el-form-item label="角色名称" prop="roleName">
          <el-input
            v-model="accessStore.currentRoleForm.roleName"
            placeholder="请输入角色名称"
            :disabled="accessStore.roleDialogMode === 'edit'"
          />
        </el-form-item>
        <el-form-item label="权限列表" prop="permissions">
          <div class="permission-selector">
            <section
              v-for="group in accessStore.permissionGroups"
              :key="group.group"
              class="permission-selector-group"
            >
              <h3>{{ group.group }}</h3>
              <el-checkbox-group v-model="accessStore.currentRoleForm.permissions">
                <el-checkbox
                  v-for="permission in group.items"
                  :key="permission.code"
                  :value="permission.code"
                >
                  <span class="permission-label">{{ permission.displayName }}</span>
                  <span class="permission-code">{{ permission.code }}</span>
                </el-checkbox>
              </el-checkbox-group>
            </section>
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeRoleDialog()">取消</el-button>
          <el-button type="primary" :loading="accessStore.isSubmittingRole" @click="saveRole">
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="accessStore.userStatusDialogVisible"
      :title="userStatusDialogTitle"
      width="480px"
      destroy-on-close
      @closed="accessStore.closeUserStatusDialog()"
    >
      <el-alert
        v-if="accessStore.userStatusDialogError"
        :title="accessStore.userStatusDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <div class="dialog-content">
        <el-tag :type="accessStore.currentUserStatusAction.mode === 'disable' ? 'warning' : 'success'">
          当前状态：{{ accessStore.currentUserStatusAction.status === 'Enabled' ? '启用中' : '已禁用' }}
        </el-tag>
        <p class="dialog-hint">{{ userStatusConfirmText }}</p>
      </div>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeUserStatusDialog()">取消</el-button>
          <el-button
            :type="accessStore.currentUserStatusAction.mode === 'disable' ? 'warning' : 'success'"
            :loading="accessStore.isSubmittingUserStatus"
            @click="saveUserStatusAction"
          >
            {{ accessStore.currentUserStatusAction.mode === 'disable' ? '确认禁用' : '确认恢复' }}
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="accessStore.resetPasswordDialogVisible"
      title="重置密码"
      width="520px"
      destroy-on-close
      @closed="accessStore.closeResetPasswordDialog()"
    >
      <el-alert
        v-if="accessStore.resetPasswordDialogError"
        :title="accessStore.resetPasswordDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <el-form
        ref="resetPasswordFormRef"
        :model="accessStore.currentResetPasswordForm"
        :rules="resetPasswordRules"
        label-position="top"
      >
        <el-form-item label="用户名">
          <el-input :model-value="accessStore.currentResetPasswordForm.userName" disabled />
        </el-form-item>
        <el-form-item label="新密码" prop="newPassword">
          <el-input
            v-model="accessStore.currentResetPasswordForm.newPassword"
            type="password"
            show-password
            placeholder="请输入新密码"
          />
        </el-form-item>
        <el-form-item label="确认新密码" prop="confirmPassword">
          <el-input
            v-model="accessStore.currentResetPasswordForm.confirmPassword"
            type="password"
            show-password
            placeholder="请再次输入新密码"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeResetPasswordDialog()">取消</el-button>
          <el-button
            type="danger"
            :loading="accessStore.isSubmittingResetPassword"
            @click="saveResetPassword"
          >
            确认重置
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="accessStore.deleteRoleDialogVisible"
      title="删除角色"
      width="480px"
      destroy-on-close
      @closed="accessStore.closeDeleteRoleDialog()"
    >
      <el-alert
        v-if="accessStore.deleteRoleDialogError"
        :title="accessStore.deleteRoleDialogError"
        type="error"
        show-icon
        :closable="false"
        class="dialog-alert"
      />

      <div class="dialog-content">
        <el-tag type="danger">角色删除不可恢复</el-tag>
        <p class="dialog-hint">
          确认删除角色 {{ accessStore.currentDeleteRoleForm.roleName }} 吗？删除成功后会写入治理审计。
        </p>
      </div>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="accessStore.closeDeleteRoleDialog()">取消</el-button>
          <el-button
            type="danger"
            :loading="accessStore.isSubmittingDeleteRole"
            @click="saveDeleteRole"
          >
            确认删除
          </el-button>
        </div>
      </template>
    </el-dialog>
  </AppShell>
</template>

<style scoped>
.access-page {
  display: grid;
  gap: 18px;
}

.hero {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  padding: 20px 24px;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.9);
  box-shadow: 0 10px 32px rgba(15, 23, 42, 0.08);
}

.hero h1 {
  font-size: 24px;
  font-weight: 700;
  color: #0f172a;
}

.hero p {
  margin-top: 8px;
  color: #64748b;
}

.hero-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 20px;
}

.loading-panel {
  padding: 24px;
  border-radius: 24px;
  background: rgba(255, 255, 255, 0.88);
}

.summary-grid,
.section-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 18px;
}

.summary-card,
.section-card {
  border-radius: 20px;
}

.audit-card {
  grid-column: 1 / -1;
}

.summary-list {
  display: grid;
  gap: 12px;
}

.summary-list dt {
  font-size: 12px;
  color: #64748b;
}

.summary-list dd {
  margin: 4px 0 0;
  font-weight: 600;
  color: #0f172a;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
}

.chip-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.section-empty {
  padding: 12px 0;
}

.action-links {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 12px;
}

.role-name-cell {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.permission-groups {
  display: grid;
  gap: 18px;
}

.permission-group {
  display: grid;
  gap: 10px;
}

.permission-group h3,
.permission-selector-group h3 {
  font-size: 15px;
  font-weight: 700;
  color: #0f172a;
}

.permission-selector {
  display: grid;
  gap: 18px;
  width: 100%;
}

.permission-selector-group {
  display: grid;
  gap: 12px;
  padding: 16px;
  border-radius: 16px;
  background: #f8fafc;
}

.permission-selector-group :deep(.el-checkbox-group) {
  display: grid;
  gap: 10px;
}

.permission-selector-group :deep(.el-checkbox) {
  height: auto;
  margin-right: 0;
  align-items: flex-start;
}

.permission-label {
  display: block;
  font-weight: 600;
  color: #0f172a;
}

.permission-code {
  display: block;
  font-size: 12px;
  color: #64748b;
}

.audit-filters {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}

.filter-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.pagination-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

.dialog-alert {
  margin-bottom: 16px;
}

.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

.dialog-content {
  display: grid;
  gap: 14px;
}

.dialog-hint {
  color: #475569;
  line-height: 1.6;
}

.muted-text {
  color: #94a3b8;
  font-size: 12px;
}

@media (max-width: 1200px) {
  .audit-filters {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .filter-actions {
    grid-column: 1 / -1;
  }
}

@media (max-width: 1080px) {
  .summary-grid,
  .section-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 768px) {
  .hero {
    flex-direction: column;
  }

  .hero-metrics {
    justify-content: space-between;
  }

  .audit-filters {
    grid-template-columns: 1fr;
  }
}
</style>
