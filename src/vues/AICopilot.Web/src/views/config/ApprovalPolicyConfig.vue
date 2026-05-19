<script setup lang="ts">
import { computed } from 'vue'
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useConfigStore } from '@/stores/configStore'
import { approvalTargetTypeLabel } from '@/views/configLabels'
import type { ApprovalPolicySummary } from '@/types/app'

const store = useConfigStore()

const targetTypeOptions = [
  { label: '插件', value: 'Plugin' },
  { label: 'MCP 服务', value: 'McpServer' }
]

const approvalToolNamesText = computed({
  get: () => store.currentApprovalPolicy.toolNames.join('\n'),
  set: (value: string | null) => {
    store.currentApprovalPolicy.toolNames = String(value ?? '')
      .split(/\r?\n|,/)
      .map((item) => item.trim())
      .filter(Boolean)
  }
})

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, title))) return
  await action()
  showAiToast('success', '操作已完成')
}

function policyTarget(policy: ApprovalPolicySummary) {
  return `${approvalTargetTypeLabel(policy.targetType)} / ${policy.targetName}`
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>审批策略</h2>
        <p>定义工具调用的 HITL 和现场复核要求。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateApprovalPolicyDialog()">
        <Plus class="h-4 w-4" />
        新增策略
      </AiButton>
    </div>
    <AiTableCard :empty="store.approvalPolicies.length === 0" empty-text="暂无审批策略">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>目标</th>
            <th>现场复核</th>
            <th>状态</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.approvalPolicies" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ policyTarget(row) }}</td>
            <td>
              <AiTag :tone="row.requiresOnsiteAttestation ? 'warning' : 'neutral'">
                {{ row.requiresOnsiteAttestation ? '需要' : '不需要' }}
              </AiTag>
            </td>
            <td><AiTag :tone="row.isEnabled ? 'success' : 'neutral'">{{ row.isEnabled ? '启用' : '停用' }}</AiTag></td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" @click="store.openEditApprovalPolicyDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除策略', `确认删除 ${row.name}？`, () => store.deleteApprovalPolicy(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.approvalPolicy" title="审批策略" width="580px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentApprovalPolicy.name" /></label>
      <label><span>说明</span><AiInput v-model="store.currentApprovalPolicy.description" /></label>
      <label><span>目标类型</span><AiSelect v-model="store.currentApprovalPolicy.targetType" :options="targetTypeOptions" /></label>
      <label><span>目标名称</span><AiInput v-model="store.currentApprovalPolicy.targetName" /></label>
      <label><span>工具名</span><AiTextarea v-model="approvalToolNamesText" :rows="5" placeholder="每行一个工具名" /></label>
      <div class="form-row"><span>需要现场复核</span><AiSwitch v-model="store.currentApprovalPolicy.requiresOnsiteAttestation" /></div>
      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentApprovalPolicy.isEnabled" /></div>
      <footer>
        <AiButton @click="store.closeApprovalPolicyDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.approvalPolicy" @click="store.saveApprovalPolicy()">
          {{ store.submittingStates.approvalPolicy ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';
</style>
