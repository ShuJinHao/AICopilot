<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { Plus, RefreshCw } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiModal from '@/components/ai/AiModal.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useAccessStore } from '@/stores/accessStore'
import type { RoleSummary, UserSummary } from '@/types/app'

const store = useAccessStore()

const enabledUsers = computed(() => store.users.filter((user) => user.isEnabled).length)
const roleOptions = computed(() => store.roles.map((role) => ({ label: role.roleName, value: role.roleName })))

onMounted(() => {
  void store.refresh()
})

function openRole(role: RoleSummary) {
  store.openEditRoleDialog(role)
}

function openUserRole(user: UserSummary) {
  store.openChangeUserRoleDialog(user)
}

function hasAuditMetadata(row: { metadata?: Record<string, string> }) {
  return Boolean(row.metadata && Object.keys(row.metadata).length > 0)
}

function auditMetadataEntries(row: { metadata?: Record<string, string> }) {
  return Object.entries(row.metadata ?? {}).filter(([, value]) => Boolean(value))
}

function cloudIdentityLabel(row: { metadata?: Record<string, string> }) {
  const metadata = row.metadata ?? {}
  return metadata.cloudEmployeeNo || metadata.cloudUserId || metadata.identityProvider || ''
}

function setPermission(code: string, checked: boolean) {
  const next = new Set(store.currentRoleForm.permissions)
  if (checked) next.add(code)
  else next.delete(code)
  store.currentRoleForm.permissions = [...next]
}
</script>

<template>
  <AppShell>
    <AiDataPage eyebrow="访问治理" title="权限治理" description="管理用户、角色、权限矩阵和审计日志，确保工具链入口可追踪。">
      <template #actions>
        <AiButton :disabled="store.isLoading" @click="store.refresh()">
          <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isLoading }" />
          刷新
        </AiButton>
      </template>

      <div class="metric-strip">
        <AiCard class="metric" tone="violet"><span>用户</span><strong>{{ store.users.length }}</strong></AiCard>
        <AiCard class="metric" tone="blue"><span>启用用户</span><strong>{{ enabledUsers }}</strong></AiCard>
        <AiCard class="metric" tone="lime"><span>角色</span><strong>{{ store.roles.length }}</strong></AiCard>
        <AiCard class="metric" tone="teal"><span>权限点</span><strong>{{ store.permissions.length }}</strong></AiCard>
      </div>

      <div v-if="store.errorMessage" class="error-note">{{ store.errorMessage }}</div>

      <div class="access-grid">
        <AiTableCard title="用户" description="账号状态、角色分配和密码重置。" :empty="store.users.length === 0" empty-text="暂无用户">
          <template #actions>
            <AiButton variant="primary" @click="store.openCreateUserDialog()">
              <Plus class="h-4 w-4" />
              新增用户
            </AiButton>
          </template>
          <table class="ai-table">
            <thead>
              <tr>
                <th>用户名</th>
                <th>角色</th>
                <th>状态</th>
                <th class="right">操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in store.users" :key="row.userId">
                <td>{{ row.userName }}</td>
                <td>{{ row.roleName }}</td>
                <td><AiTag :tone="row.isEnabled ? 'success' : 'neutral'">{{ row.isEnabled ? '启用' : '停用' }}</AiTag></td>
                <td>
                  <AiActionGroup>
                    <AiButton size="sm" @click="openUserRole(row)">角色</AiButton>
                    <AiButton size="sm" @click="store.openResetPasswordDialog(row)">重置密码</AiButton>
                    <AiButton v-if="row.isEnabled" size="sm" variant="danger" @click="store.openDisableUserDialog(row)">停用</AiButton>
                    <AiButton v-else size="sm" variant="lime" @click="store.openEnableUserDialog(row)">启用</AiButton>
                  </AiActionGroup>
                </td>
              </tr>
            </tbody>
          </table>
        </AiTableCard>

        <AiTableCard title="角色" description="角色对应权限集合和用户数量。" :empty="store.roles.length === 0" empty-text="暂无角色">
          <template #actions>
            <AiButton variant="primary" @click="store.openCreateRoleDialog()">
              <Plus class="h-4 w-4" />
              新增角色
            </AiButton>
          </template>
          <table class="ai-table">
            <thead>
              <tr>
                <th>角色</th>
                <th>用户数</th>
                <th>系统角色</th>
                <th>权限数</th>
                <th class="right">操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in store.roles" :key="row.roleId">
                <td>{{ row.roleName }}</td>
                <td>{{ row.assignedUserCount }}</td>
                <td><AiTag :tone="row.isSystemRole ? 'warning' : 'neutral'">{{ row.isSystemRole ? '是' : '否' }}</AiTag></td>
                <td>{{ row.permissions.length }}</td>
                <td>
                  <AiActionGroup>
                    <AiButton size="sm" @click="openRole(row)">编辑</AiButton>
                    <AiButton size="sm" variant="danger" :disabled="row.isSystemRole" @click="store.openDeleteRoleDialog(row)">删除</AiButton>
                  </AiActionGroup>
                </td>
              </tr>
            </tbody>
          </table>
        </AiTableCard>
      </div>

      <AiCard class="permission-card">
        <div class="panel-header">
          <div>
            <h2>权限矩阵</h2>
            <p>按权限组查看当前系统可分配权限。</p>
          </div>
        </div>
        <div class="permission-groups">
          <section v-for="group in store.permissionGroups" :key="group.group">
            <h3>{{ group.group }}</h3>
            <div>
              <AiTag v-for="permission in group.items" :key="permission.code" tone="neutral">
                {{ permission.displayName || permission.code }}
              </AiTag>
            </div>
          </section>
        </div>
      </AiCard>

      <AiTableCard title="审计日志" description="查看关键配置、权限和工具链操作记录。" :empty="store.auditLogs.length === 0" empty-text="暂无审计日志">
        <template #actions>
          <AiButton :disabled="store.isAuditLoading" @click="store.loadAuditLogs()">
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isAuditLoading }" />
            刷新审计
          </AiButton>
        </template>
        <table class="ai-table">
          <thead>
            <tr>
              <th>时间</th>
              <th>分组</th>
              <th>动作</th>
              <th>对象</th>
              <th>操作人</th>
              <th>身份快照</th>
              <th>结果</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in store.auditLogs" :key="`${row.createdAt}-${row.actionCode}-${row.targetName}`">
              <td>{{ row.createdAt }}</td>
              <td>{{ row.actionGroup }}</td>
              <td>{{ row.actionCode }}</td>
              <td>{{ row.targetName }}</td>
              <td>{{ row.operatorUserName }}</td>
              <td>
                <details v-if="hasAuditMetadata(row)" class="audit-details">
                  <summary>
                    <AiTag tone="neutral">{{ row.metadata?.identityProvider || 'Identity' }}</AiTag>
                    <span>{{ cloudIdentityLabel(row) }}</span>
                  </summary>
                  <dl>
                    <template v-for="[key, value] in auditMetadataEntries(row)" :key="key">
                      <dt>{{ key }}</dt>
                      <dd>{{ value }}</dd>
                    </template>
                  </dl>
                </details>
                <span v-else class="muted">-</span>
              </td>
              <td><AiTag :tone="row.result === 'Succeeded' ? 'success' : 'danger'">{{ row.result }}</AiTag></td>
            </tr>
          </tbody>
        </table>
      </AiTableCard>

      <AiDrawer v-model="store.userDialogVisible" title="新增用户" width="480px">
        <div class="ai-form">
          <label><span>用户名</span><AiInput v-model="store.currentUserForm.userName" /></label>
          <label><span>密码</span><AiInput v-model="store.currentUserForm.password" type="password" autocomplete="new-password" /></label>
          <label><span>角色</span><AiSelect v-model="store.currentUserForm.roleName" :options="roleOptions" /></label>
          <footer>
            <AiButton @click="store.closeCreateUserDialog()">取消</AiButton>
            <AiButton variant="primary" :disabled="store.isSubmittingUser" @click="store.createUser()">
              {{ store.isSubmittingUser ? '保存中' : '保存' }}
            </AiButton>
          </footer>
        </div>
      </AiDrawer>

      <AiDrawer v-model="store.userRoleDialogVisible" title="调整角色" width="440px">
        <div class="ai-form">
          <label><span>用户</span><AiInput v-model="store.currentUserRoleForm.userName" disabled /></label>
          <label><span>角色</span><AiSelect v-model="store.currentUserRoleForm.roleName" :options="roleOptions" /></label>
          <footer>
            <AiButton @click="store.closeChangeUserRoleDialog()">取消</AiButton>
            <AiButton variant="primary" :disabled="store.isSubmittingUserRole" @click="store.updateUserRole()">
              {{ store.isSubmittingUserRole ? '保存中' : '保存' }}
            </AiButton>
          </footer>
        </div>
      </AiDrawer>

      <AiDrawer v-model="store.roleDialogVisible" title="角色权限" width="660px">
        <div class="ai-form">
          <label><span>角色名</span><AiInput v-model="store.currentRoleForm.roleName" :disabled="store.roleDialogMode === 'edit'" /></label>
          <div class="permission-checks">
            <section v-for="group in store.permissionGroups" :key="group.group">
              <h3>{{ group.group }}</h3>
              <AiCheckbox
                v-for="permission in group.items"
                :key="permission.code"
                :model-value="store.currentRoleForm.permissions.includes(permission.code)"
                @update:model-value="(value) => setPermission(permission.code, value)"
              >
                {{ permission.displayName || permission.code }}
              </AiCheckbox>
            </section>
          </div>
          <footer>
            <AiButton @click="store.closeRoleDialog()">取消</AiButton>
            <AiButton variant="primary" :disabled="store.isSubmittingRole" @click="store.saveRole()">
              {{ store.isSubmittingRole ? '保存中' : '保存' }}
            </AiButton>
          </footer>
        </div>
      </AiDrawer>

      <AiModal v-model="store.userStatusDialogVisible" title="账号状态" width="420px">
        <p class="modal-text">
          确认{{ store.currentUserStatusAction.mode === 'disable' ? '停用' : '启用' }}
          {{ store.currentUserStatusAction.userName }}？
        </p>
        <div class="modal-actions">
          <AiButton @click="store.closeUserStatusDialog()">取消</AiButton>
          <AiButton variant="primary" :disabled="store.isSubmittingUserStatus" @click="store.saveUserStatusAction()">确认</AiButton>
        </div>
      </AiModal>

      <AiModal v-model="store.resetPasswordDialogVisible" title="重置密码" width="460px">
        <div class="ai-form">
          <label><span>用户</span><AiInput v-model="store.currentResetPasswordForm.userName" disabled /></label>
          <label><span>新密码</span><AiInput v-model="store.currentResetPasswordForm.newPassword" type="password" autocomplete="new-password" /></label>
          <footer>
            <AiButton @click="store.closeResetPasswordDialog()">取消</AiButton>
            <AiButton variant="primary" :disabled="store.isSubmittingResetPassword" @click="store.resetUserPassword()">
              {{ store.isSubmittingResetPassword ? '保存中' : '保存' }}
            </AiButton>
          </footer>
        </div>
      </AiModal>

      <AiModal v-model="store.deleteRoleDialogVisible" title="删除角色" width="420px">
        <p class="modal-text">确认删除角色 {{ store.currentDeleteRoleForm.roleName }}？</p>
        <div class="modal-actions">
          <AiButton @click="store.closeDeleteRoleDialog()">取消</AiButton>
          <AiButton variant="danger" :disabled="store.isSubmittingDeleteRole" @click="store.deleteRole()">删除</AiButton>
        </div>
      </AiModal>
    </AiDataPage>
  </AppShell>
</template>

<style scoped>
@import './config/shared-config.css';

.metric-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(150px, 1fr));
  gap: 12px;
}

.metric span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.metric strong {
  display: block;
  margin-top: 12px;
  color: var(--ai-text);
  font-size: 30px;
  font-weight: 950;
}

.access-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 14px;
}

.permission-card {
  display: grid;
  gap: 14px;
}

.permission-groups,
.permission-checks {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.permission-groups section,
.permission-checks section {
  display: grid;
  gap: 8px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 12px;
  background: var(--ai-surface-soft);
}

.permission-groups h3,
.permission-checks h3 {
  margin: 0;
  color: var(--ai-text);
  font-size: 14px;
  font-weight: 900;
}

.permission-groups div {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.audit-details summary {
  display: flex;
  cursor: pointer;
  align-items: center;
  gap: 8px;
}

.audit-details summary span {
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--ai-text-muted);
}

.audit-details dl {
  display: grid;
  grid-template-columns: minmax(110px, 0.45fr) minmax(0, 1fr);
  gap: 8px 10px;
  margin: 10px 0 0;
  font-size: 12px;
}

.audit-details dt {
  color: var(--ai-text-muted);
}

.audit-details dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.modal-text {
  margin: 0;
  color: var(--ai-text);
  font-weight: 750;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 18px;
}

@media (max-width: 1080px) {
  .access-grid,
  .permission-groups,
  .permission-checks {
    grid-template-columns: 1fr;
  }
}
</style>
