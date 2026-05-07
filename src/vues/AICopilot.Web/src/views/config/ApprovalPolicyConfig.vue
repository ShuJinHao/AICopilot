<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'
import { approvalTargetTypeLabel } from '@/views/configLabels'
import type { ApprovalPolicySummary } from '@/types/app'

const store = useConfigStore()

const approvalToolNamesText = computed({
  get: () => store.currentApprovalPolicy.toolNames.join('\n'),
  set: (value: string) => {
    store.currentApprovalPolicy.toolNames = value
      .split(/\r?\n|,/)
      .map((item) => item.trim())
      .filter(Boolean)
  }
})

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(message, title, { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' })
  await action()
  ElMessage.success('操作已完成')
}

function policyTarget(policy: ApprovalPolicySummary) {
  return `${approvalTargetTypeLabel(policy.targetType)} / ${policy.targetName}`
}
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">审批策略</h2>
        <p class="panel-subtitle">定义工具调用的 HITL 和现场复核要求。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateApprovalPolicyDialog()">新增策略</el-button>
    </div>
    <el-table :data="store.approvalPolicies" stripe>
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column label="目标" min-width="180">
        <template #default="{ row }">{{ policyTarget(row) }}</template>
      </el-table-column>
      <el-table-column label="现场复核" width="110">
        <template #default="{ row }">
          <el-tag :type="row.requiresOnsiteAttestation ? 'warning' : 'info'">
            {{ row.requiresOnsiteAttestation ? '需要' : '不需要' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button link type="primary" @click="store.openEditApprovalPolicyDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除策略', `确认删除 ${row.name}？`, () => store.deleteApprovalPolicy(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.approvalPolicy" size="560px" title="审批策略">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentApprovalPolicy.name" /></el-form-item>
      <el-form-item label="说明"><el-input v-model="store.currentApprovalPolicy.description" /></el-form-item>
      <el-form-item label="目标类型">
        <el-select v-model="store.currentApprovalPolicy.targetType">
          <el-option label="插件" value="Plugin" />
          <el-option label="MCP 服务" value="McpServer" />
        </el-select>
      </el-form-item>
      <el-form-item label="目标名称"><el-input v-model="store.currentApprovalPolicy.targetName" /></el-form-item>
      <el-form-item label="工具名">
        <el-input v-model="approvalToolNamesText" type="textarea" :rows="5" placeholder="每行一个工具名" />
      </el-form-item>
      <el-form-item label="需要现场复核">
        <el-switch v-model="store.currentApprovalPolicy.requiresOnsiteAttestation" />
      </el-form-item>
      <el-form-item label="启用"><el-switch v-model="store.currentApprovalPolicy.isEnabled" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeApprovalPolicyDialog()">取消</el-button>
      <el-button
        type="primary"
        :loading="store.submittingStates.approvalPolicy"
        @click="store.saveApprovalPolicy()"
      >
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
