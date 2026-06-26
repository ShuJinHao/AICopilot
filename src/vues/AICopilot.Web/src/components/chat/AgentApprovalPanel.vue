<script setup lang="ts">
import { Check, X } from 'lucide-vue-next'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import type { AgentApprovalRequest } from '@/types/protocols'

const store = useChatStore()
const { approvalGroups, pendingAgentApprovals } = useAgentWorkbench()

function approvalTypeLabel(type?: string | null) {
  if (type === 'Plan') return '计划'
  if (type === 'Tool') return '工具'
  if (type === 'ToolCall') return '工具'
  if (type === 'FinalOutput') return '最终输出'
  if (type === 'Artifact') return '产物'
  return '审批'
}

function approvalDisplayTitle(approval: AgentApprovalRequest) {
  if (approval.type === 'Plan') return '确认执行计划'
  if (approval.type === 'FinalOutput') return '确认最终输出'
  if (approval.type === 'Artifact') return '确认产物'
  if (approval.type === 'Tool' || approval.type === 'ToolCall') return '需要确认后继续'
  return '人工审批请求'
}

function approvalRiskLabel(riskLevel?: string | null) {
  const normalized = riskLevel?.toLowerCase()
  if (normalized === 'critical') return '关键风险'
  if (normalized === 'high') return '高风险'
  if (normalized === 'medium') return '中风险'
  if (normalized === 'low') return '低风险'
  return riskLevel || '待评估'
}

function approvalMetaLine(approval: AgentApprovalRequest) {
  return `${approvalRiskLabel(approval.riskLevel)} · ${approval.reason || '等待人工复核'}`
}

async function approveAgentApproval(approvalId: string) {
  const approval = pendingAgentApprovals.value.find((item) => item.id === approvalId)
  if (!approval) return
  if (approval.type === 'Plan') {
    await store.approveAndRunAgentTask(approval.taskId)
    return
  }

  await store.decideAgentApproval(approval, 'approve', 'Approved from inline approval card')
}

async function rejectAgentApproval(approvalId: string) {
  const approval = pendingAgentApprovals.value.find((item) => item.id === approvalId)
  if (!approval) return
  const reason = window.prompt('请输入驳回原因', '需要修改计划或产物')
  if (reason === null) return
  await store.decideAgentApproval(approval, 'reject', reason)
}
</script>

<template>
  <section v-if="approvalGroups.length" class="runtime-section-block" data-testid="inline-approval-card">
    <div class="runtime-section-title">
      <strong>确认项</strong>
      <span>{{ pendingAgentApprovals.length }} 项待处理</span>
    </div>
    <div v-for="group in approvalGroups" :key="group.label" class="approval-group">
      <div v-for="approval in group.approvals" :key="approval.id" class="approval-row">
        <div>
          <strong>{{ approvalDisplayTitle(approval) }}</strong>
          <span>{{ approvalMetaLine(approval) }}</span>
          <details class="approval-detail-fold" data-testid="approval-detail-fold">
            <summary>审批详情</summary>
            <dl class="approval-detail-grid">
              <div>
                <dt>类型</dt>
                <dd>{{ approvalTypeLabel(approval.type) }}</dd>
              </div>
              <div>
                <dt>对象</dt>
                <dd class="mono">{{ approval.targetName }}</dd>
              </div>
              <div>
                <dt>目标 ID</dt>
                <dd class="mono">{{ approval.targetId }}</dd>
              </div>
              <div v-if="approval.workspaceCode">
                <dt>工作区</dt>
                <dd class="mono">{{ approval.workspaceCode }}</dd>
              </div>
            </dl>
          </details>
        </div>
        <div class="approval-actions">
          <button type="button" aria-label="批准审批" :disabled="store.isAgentBusy" @click="approveAgentApproval(approval.id)">
            <Check :size="16" />
          </button>
          <button type="button" aria-label="驳回审批" :disabled="store.isAgentBusy" @click="rejectAgentApproval(approval.id)">
            <X :size="16" />
          </button>
        </div>
      </div>
    </div>
  </section>
</template>
