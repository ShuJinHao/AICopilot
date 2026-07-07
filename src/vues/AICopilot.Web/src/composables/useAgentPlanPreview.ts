import { computed } from 'vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import type { AgentStep } from '@/types/protocols'

type AgentPlanPreviewStep = Partial<AgentStep> & {
  title?: string | null
  description?: string | null
  stepType?: string | null
  toolCode?: string | null
  inputJson?: unknown
}

export type AgentPlanPreview = {
  planKind?: string
  isExecutable?: boolean
  capabilityGaps?: string[]
  plannerMode?: string
  plannerFallbackReason?: string | null
  plannerToolCatalogVersion?: number
  plannerAvailableToolCount?: number
  toolCatalogVersion?: number
  visibleToolCount?: number
  toolRiskSummary?: Record<string, number>
  approvalCheckpoints?: string[]
  forcedStepCodes?: string[]
  queryMode?: string | null
  skillCode?: string | null
  skillName?: string | null
  taskType?: string | null
  skillRoutingReason?: string | null
  dataSourceSummaries?: Array<{
    name?: string
    sourceMode?: string
    sourceLabel?: string
  }>
  plannerSafetySummary?: {
    planSource?: string
    plannerMode?: string
    plannerModelSummary?: string | null
    plannerToolCatalogVersion?: number
    availableToolCount?: number
    toolRiskSummary?: Record<string, number>
  }
  steps?: AgentPlanPreviewStep[]
}

export function parseAgentPlan(planJson?: string | null): AgentPlanPreview | null {
  if (!planJson) return null
  try {
    const parsed = JSON.parse(planJson) as AgentPlanPreview
    return parsed && typeof parsed === 'object' ? parsed : null
  } catch (error) {
    console.error('Failed to parse agent plan preview JSON.', error)
    return null
  }
}

export function sourceModeLabel(value?: string | null) {
  if (!value) {
    return '只读分析'
  }

  if (value === 'SimulationBusiness' || value === 'Simulation') {
    return 'AI 独立模拟业务库'
  }

  if (value.includes('CloudReadonly') || value.includes('CloudReadOnly')) {
    return 'Cloud 只读'
  }

  if (value === 'FreeGoal') {
    return '自由目标'
  }

  if (value === 'workspace' || value === 'Workspace') {
    return '工作区'
  }

  if (value === 'UnknownSource') {
    return '未知来源'
  }

  return value
}

export function statusTone(type?: string) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger' || type === 'error') return 'danger'
  return 'neutral'
}

export function useAgentPlanPreview() {
  const { latestTask, taskSteps } = useAgentWorkbench()

  const latestPlan = computed<AgentPlanPreview | null>(() => parseAgentPlan(latestTask.value?.planJson))
  const latestPlanDataSource = computed(() => latestPlan.value?.dataSourceSummaries?.[0] ?? null)
  const latestPlanToolCatalogVersion = computed(
    () => latestPlan.value?.toolCatalogVersion ?? latestPlan.value?.plannerToolCatalogVersion ?? latestPlan.value?.plannerSafetySummary?.plannerToolCatalogVersion
  )
  const latestPlanVisibleToolCount = computed(
    () => latestPlan.value?.visibleToolCount ?? latestPlan.value?.plannerAvailableToolCount ?? latestPlan.value?.plannerSafetySummary?.availableToolCount ?? 0
  )
  const latestPlanRiskSummary = computed(
    () => latestPlan.value?.toolRiskSummary ?? latestPlan.value?.plannerSafetySummary?.toolRiskSummary ?? {}
  )
  const latestPlanRiskLine = computed(() => {
    const entries = Object.entries(latestPlanRiskSummary.value)
    return entries.length ? entries.map(([risk, count]) => `${risk}:${count}`).join(' / ') : '无风险摘要'
  })
  const latestPlanCapabilityGaps = computed(() =>
    (latestPlan.value?.capabilityGaps ?? [])
      .filter((item) => item && item.trim().length > 0)
  )
  const latestPlanKindLabel = computed(() => {
    if (!latestPlan.value) return '计划草案'
    if (latestPlan.value.planKind === 'ExecutablePlan' || latestPlan.value.isExecutable) {
      return '可执行计划'
    }

    return '计划草案'
  })
  const isPlanDraftTask = computed(() =>
    latestTask.value?.status === 'Draft' ||
    latestPlan.value?.planKind === 'PlanDraft' ||
    latestPlan.value?.isExecutable === false
  )
  const draftPlanSteps = computed<AgentStep[]>(() =>
    (latestPlan.value?.steps ?? []).map((step, index) => ({
      id: step.id || `${latestTask.value?.id ?? 'plan'}:${index + 1}`,
      stepIndex: step.stepIndex || index + 1,
      title: step.title || step.description || `计划步骤 ${index + 1}`,
      description: step.description || '',
      stepType: step.stepType || 'PlanDraft',
      status: step.status || 'Draft',
      toolCode: step.toolCode ?? null,
      requiresApproval: step.requiresApproval ?? false,
      errorMessage: step.errorMessage ?? null
    }))
  )
  const displayPlanSteps = computed(() =>
    taskSteps.value.length > 0 ? taskSteps.value : draftPlanSteps.value
  )
  const latestPlanSource = computed(() =>
    sourceModeLabel(
      latestPlan.value?.skillName ||
      latestPlan.value?.skillCode ||
      latestPlanDataSource.value?.sourceLabel ||
      latestPlan.value?.plannerSafetySummary?.planSource ||
      latestPlanDataSource.value?.sourceMode ||
      'FreeGoal'
    )
  )
  const latestPlanIsCloudReadonly = computed(() =>
    latestPlan.value?.queryMode?.includes('CloudReadonly') ||
    latestPlanDataSource.value?.sourceMode?.includes('CloudReadonly') ||
    false
  )
  const previewPlanSteps = computed(() => displayPlanSteps.value.slice(0, 4))
  const totalPreviewPlanStepCount = computed(() => displayPlanSteps.value.length)
  const hiddenPlanStepCount = computed(() => Math.max(0, totalPreviewPlanStepCount.value - previewPlanSteps.value.length))

  return {
    latestPlan,
    latestPlanDataSource,
    latestPlanToolCatalogVersion,
    latestPlanVisibleToolCount,
    latestPlanRiskLine,
    latestPlanCapabilityGaps,
    latestPlanKindLabel,
    latestPlanSource,
    latestPlanIsCloudReadonly,
    isPlanDraftTask,
    previewPlanSteps,
    totalPreviewPlanStepCount,
    hiddenPlanStepCount
  }
}
