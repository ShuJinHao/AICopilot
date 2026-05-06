<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { Plus, Refresh } from '@element-plus/icons-vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useAccessStore } from '@/stores/accessStore'
import type { RoleSummary, UserSummary } from '@/types/app'

const store = useAccessStore()

const enabledUsers = computed(() => store.users.filter((user) => user.isEnabled).length)

onMounted(() => {
  void store.refresh()
})

function openRole(role: RoleSummary) {
  store.openEditRoleDialog(role)
}

function openUserRole(user: UserSummary) {
  store.openChangeUserRoleDialog(user)
}
</script>

<template>
  <AppShell>
    <div class="page access-page">
      <header class="page-header">
        <div>
          <p class="page-kicker">Access Governance</p>
          <h1 class="page-title">权限治理</h1>
          <p class="page-description">管理用户、角色、权限矩阵和审计日志，确保工具链入口可追踪。</p>
        </div>
        <el-button :icon="Refresh" :loading="store.isLoading" @click="store.refresh()">刷新</el-button>
      </header>

      <div class="metric-strip">
        <div class="metric">
          <span class="metric-label">用户</span>
          <strong class="metric-value">{{ store.users.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">启用用户</span>
          <strong class="metric-value">{{ enabledUsers }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">角色</span>
          <strong class="metric-value">{{ store.roles.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">权限点</span>
          <strong class="metric-value">{{ store.permissions.length }}</strong>
        </div>
      </div>

      <el-alert v-if="store.errorMessage" type="error" show-icon :closable="false" :title="store.errorMessage" />

      <div class="access-grid">
        <section class="panel">
          <div class="panel-header">
            <div>
              <h2 class="panel-title">用户</h2>
              <p class="panel-subtitle">账号状态、角色分配和密码重置。</p>
            </div>
            <el-button type="primary" :icon="Plus" @click="store.openCreateUserDialog()">新增用户</el-button>
          </div>
          <el-table :data="store.users" stripe>
            <el-table-column prop="userName" label="用户名" min-width="160" />
            <el-table-column prop="roleName" label="角色" min-width="140" />
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="260" fixed="right">
              <template #default="{ row }">
                <div class="table-actions">
                  <el-button link type="primary" @click="openUserRole(row)">角色</el-button>
                  <el-button link @click="store.openResetPasswordDialog(row)">重置密码</el-button>
                  <el-button
                    v-if="row.isEnabled"
                    link
                    type="warning"
                    @click="store.openDisableUserDialog(row)"
                  >
                    停用
                  </el-button>
                  <el-button v-else link type="success" @click="store.openEnableUserDialog(row)">启用</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </section>

        <section class="panel">
          <div class="panel-header">
            <div>
              <h2 class="panel-title">角色</h2>
              <p class="panel-subtitle">角色对应权限集合和用户数量。</p>
            </div>
            <el-button type="primary" :icon="Plus" @click="store.openCreateRoleDialog()">新增角色</el-button>
          </div>
          <el-table :data="store.roles" stripe>
            <el-table-column prop="roleName" label="角色" min-width="150" />
            <el-table-column prop="assignedUserCount" label="用户数" width="90" />
            <el-table-column label="系统角色" width="100">
              <template #default="{ row }">
                <el-tag :type="row.isSystemRole ? 'warning' : 'info'">{{ row.isSystemRole ? '是' : '否' }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="权限数" width="90">
              <template #default="{ row }">{{ row.permissions.length }}</template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <div class="table-actions">
                  <el-button link type="primary" @click="openRole(row)">编辑</el-button>
                  <el-button
                    link
                    type="danger"
                    :disabled="row.isSystemRole"
                    @click="store.openDeleteRoleDialog(row)"
                  >
                    删除
                  </el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </section>
      </div>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">权限矩阵</h2>
            <p class="panel-subtitle">按权限组查看当前系统可分配权限。</p>
          </div>
        </div>
        <div class="permission-groups">
          <section v-for="group in store.permissionGroups" :key="group.group">
            <h3>{{ group.group }}</h3>
            <div>
              <el-tag v-for="permission in group.items" :key="permission.code" type="info">
                {{ permission.displayName || permission.code }}
              </el-tag>
            </div>
          </section>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">审计日志</h2>
            <p class="panel-subtitle">查看关键配置、权限和工具链操作记录。</p>
          </div>
          <el-button :loading="store.isAuditLoading" @click="store.loadAuditLogs()">刷新审计</el-button>
        </div>
        <el-table :data="store.auditLogs" stripe>
          <el-table-column prop="createdAt" label="时间" min-width="170" />
          <el-table-column prop="actionGroup" label="分组" width="130" />
          <el-table-column prop="actionCode" label="动作" min-width="220" show-overflow-tooltip />
          <el-table-column prop="targetName" label="对象" min-width="160" show-overflow-tooltip />
          <el-table-column prop="operatorUserName" label="操作人" width="140" />
          <el-table-column label="结果" width="100">
            <template #default="{ row }">
              <el-tag :type="row.result === 'Succeeded' ? 'success' : 'danger'">{{ row.result }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
      </section>

      <el-drawer v-model="store.userDialogVisible" size="460px" title="新增用户">
        <el-form label-position="top">
          <el-form-item label="用户名"><el-input v-model="store.currentUserForm.userName" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="store.currentUserForm.password" type="password" show-password /></el-form-item>
          <el-form-item label="角色"><el-select v-model="store.currentUserForm.roleName"><el-option v-for="role in store.roles" :key="role.roleId" :label="role.roleName" :value="role.roleName" /></el-select></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeCreateUserDialog()">取消</el-button>
          <el-button type="primary" :loading="store.isSubmittingUser" @click="store.createUser()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.userRoleDialogVisible" size="420px" title="调整角色">
        <el-form label-position="top">
          <el-form-item label="用户"><el-input v-model="store.currentUserRoleForm.userName" disabled /></el-form-item>
          <el-form-item label="角色"><el-select v-model="store.currentUserRoleForm.roleName"><el-option v-for="role in store.roles" :key="role.roleId" :label="role.roleName" :value="role.roleName" /></el-select></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeChangeUserRoleDialog()">取消</el-button>
          <el-button type="primary" :loading="store.isSubmittingUserRole" @click="store.updateUserRole()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.roleDialogVisible" size="620px" title="角色权限">
        <el-form label-position="top">
          <el-form-item label="角色名"><el-input v-model="store.currentRoleForm.roleName" :disabled="store.roleDialogMode === 'edit'" /></el-form-item>
          <el-form-item label="权限">
            <el-checkbox-group v-model="store.currentRoleForm.permissions" class="permission-checks">
              <section v-for="group in store.permissionGroups" :key="group.group">
                <h3>{{ group.group }}</h3>
                <el-checkbox v-for="permission in group.items" :key="permission.code" :label="permission.code">
                  {{ permission.displayName || permission.code }}
                </el-checkbox>
              </section>
            </el-checkbox-group>
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeRoleDialog()">取消</el-button>
          <el-button type="primary" :loading="store.isSubmittingRole" @click="store.saveRole()">保存</el-button>
        </template>
      </el-drawer>

      <el-dialog v-model="store.userStatusDialogVisible" width="420px" title="账号状态">
        <p>
          确认{{ store.currentUserStatusAction.mode === 'disable' ? '停用' : '启用' }}
          {{ store.currentUserStatusAction.userName }}？
        </p>
        <template #footer>
          <el-button @click="store.closeUserStatusDialog()">取消</el-button>
          <el-button type="primary" :loading="store.isSubmittingUserStatus" @click="store.saveUserStatusAction()">确认</el-button>
        </template>
      </el-dialog>

      <el-dialog v-model="store.resetPasswordDialogVisible" width="460px" title="重置密码">
        <el-form label-position="top">
          <el-form-item label="用户"><el-input v-model="store.currentResetPasswordForm.userName" disabled /></el-form-item>
          <el-form-item label="新密码"><el-input v-model="store.currentResetPasswordForm.newPassword" type="password" show-password /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeResetPasswordDialog()">取消</el-button>
          <el-button type="primary" :loading="store.isSubmittingResetPassword" @click="store.resetUserPassword()">保存</el-button>
        </template>
      </el-dialog>

      <el-dialog v-model="store.deleteRoleDialogVisible" width="420px" title="删除角色">
        <p>确认删除角色 {{ store.currentDeleteRoleForm.roleName }}？</p>
        <template #footer>
          <el-button @click="store.closeDeleteRoleDialog()">取消</el-button>
          <el-button type="danger" :loading="store.isSubmittingDeleteRole" @click="store.deleteRole()">删除</el-button>
        </template>
      </el-dialog>
    </div>
  </AppShell>
</template>

<style scoped>
.access-page {
  display: grid;
  align-content: start;
  gap: 14px;
  height: 100%;
  overflow: auto;
}

.access-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 14px;
}

.permission-groups {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  padding: 16px;
}

.permission-groups section,
.permission-checks section {
  display: grid;
  gap: 8px;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 12px;
  background: var(--app-surface-muted);
}

.permission-groups h3,
.permission-checks h3 {
  margin: 0;
  font-size: 14px;
  font-weight: 750;
}

.permission-groups div {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.permission-checks {
  display: grid;
  gap: 12px;
}

@media (max-width: 1080px) {
  .access-grid,
  .permission-groups {
    grid-template-columns: 1fr;
  }
}
</style>
