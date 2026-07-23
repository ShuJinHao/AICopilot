<script setup lang="ts">
import { computed } from 'vue'
import { TriangleAlert } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useAgentPlanPreview } from '@/composables/useAgentPlanPreview'

const { latestTask, blockedStep } = useAgentWorkbench()
const {
  latestPlan,
  latestPlanCapabilityGaps,
  latestPlanIsSimulation,
  isPlanDraftTask,
  previewPlanSteps,
  totalPreviewPlanStepCount,
  hiddenPlanStepCount
} = useAgentPlanPreview()

const visibleBlockedStep = computed(() => {
  const task = latestTask.value
  const step = blockedStep.value
  if (!task || !step || isPlanDraftTask.value || task.isRunQueued || task.isRunInProgress) {
    return null
  }

  return ['WaitingApproval', 'Failed'].includes(step.status) ? step : null
})

function planStepTag(step: { requiresApproval: boolean }) {
  if (isPlanDraftTask.value) {
    return { tone: 'neutral' as const, label: '建议' }
  }

  return step.requiresApproval
    ? { tone: 'warning' as const, label: '需确认' }
    : { tone: 'neutral' as const, label: '只读' }
}
</script>

<template>
  <section v-if="latestTask" class="agent-plan" data-testid="inline-plan-card">
    <div class="section-title">
      <strong>{{ latestTask.title || '计划摘要' }}</strong>
      <AiTag v-if="latestPlanIsSimulation" tone="warning">Simulation · 只读模拟</AiTag>
      <span>{{ previewPlanSteps.length }} / {{ totalPreviewPlanStepCount }} 个步骤</span>
    </div>
    <p class="agent-plan-goal">{{ latestTask.goal }}</p>
    <div v-if="latestPlanCapabilityGaps.length" class="planner-warning capability-gap-list">
      <TriangleAlert :size="15" />
      <div>
        <strong>当前只是计划草案，确认执行前还有能力缺口</strong>
        <span v-for="gap in latestPlanCapabilityGaps.slice(0, 3)" :key="gap">{{ gap }}</span>
      </div>
    </div>
    <ol v-if="previewPlanSteps.length" class="agent-step-preview" data-testid="plan-steps-preview">
      <li v-for="step in previewPlanSteps" :key="step.id">
        <i>{{ step.stepIndex }}</i>
        <span>{{ step.title }}</span>
        <AiTag :tone="planStepTag(step).tone">
          {{ planStepTag(step).label }}
        </AiTag>
      </li>
      <li v-if="hiddenPlanStepCount" class="agent-step-more">
        <i>…</i>
        <span>还有 {{ hiddenPlanStepCount }} 个步骤在思考与执行记录中</span>
      </li>
    </ol>
    <div v-if="visibleBlockedStep" class="agent-blocked-step">
      <TriangleAlert :size="15" />
      <span>当前阻塞：{{ visibleBlockedStep.title }}</span>
    </div>
  </section>
</template>
