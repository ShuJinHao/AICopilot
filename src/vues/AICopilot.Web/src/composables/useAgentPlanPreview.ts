import { computed } from 'vue'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import type { AgentStep, AgentTask } from '@/types/protocols'

type AgentPlanPreviewStep = Partial<AgentStep> & {
  title?: string | null
  description?: string | null
  stepType?: string | null
  toolCode?: string | null
  inputJson?: unknown
}

export type AgentPlanPreview = {
  schemaVersion?: string
  planDigest?: string
  topologyProfile?: string
  planKind?: string
  isExecutable?: boolean
  capabilityGaps?: string[]
  requestedCapabilityCodes?: string[]
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
  taskType?: string | null
  skillCode?: string | null
  skillName?: string | null
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
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null
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

export function isAgentPlanConfirmable(
  task: Pick<AgentTask, 'planSchemaVersion' | 'planDigest' | 'topologyProfile' | 'planIntegrityStatus' | 'isPlanExecutable'> | null | undefined,
  plan: AgentPlanPreview | null | undefined,
  capabilityGaps: string[],
) {
  const hasCanonicalStringArray = (value: unknown, requireNonEmpty: boolean) => {
    if (!Array.isArray(value) || value.some(item => typeof item !== 'string' || item.length === 0 || item.trim() !== item)) {
      return false
    }
    const canonical = [...new Set(value)].sort((left, right) => left < right ? -1 : left > right ? 1 : 0)
    return (!requireNonEmpty || value.length > 0) &&
      canonical.length === value.length &&
      canonical.every((item, index) => item === value[index])
  }
  const hasExecutableStepShape = Array.isArray(plan?.steps) &&
    plan.steps.length > 0 &&
    plan.steps.every(step => step !== null &&
      typeof step === 'object' &&
      typeof step.title === 'string' && step.title.trim().length > 0 &&
      typeof step.description === 'string' && step.description.trim().length > 0 &&
      typeof step.stepType === 'string' && step.stepType.trim().length > 0 &&
      typeof step.toolCode === 'string' && step.toolCode.trim().length > 0)
  return task?.planIntegrityStatus === 'ValidV2' &&
    task.isPlanExecutable === false &&
    task.planSchemaVersion === '2.0' &&
    plan?.schemaVersion === '2.0' &&
    task.topologyProfile === 'LinearV1' &&
    plan?.topologyProfile === 'LinearV1' &&
    /^[0-9a-f]{64}$/.test(task.planDigest ?? '') &&
    task.planDigest === plan?.planDigest &&
    plan?.planKind === 'PlanDraft' &&
    plan?.isExecutable === false &&
    hasCanonicalStringArray(plan?.capabilityGaps, false) &&
    plan?.capabilityGaps?.length === 0 &&
    hasCanonicalStringArray(plan?.requestedCapabilityCodes, true) &&
    hasExecutableStepShape &&
    capabilityGaps.length === 0
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
    !Array.isArray(latestPlan.value?.capabilityGaps) ||
    latestPlan.value.capabilityGaps.some(item => typeof item !== 'string' || item.trim().length === 0)
      ? ['plan_contract_incomplete']
      : latestPlan.value.capabilityGaps
  )
  const latestPlanSchemaVersion = computed(() =>
    latestTask.value?.planSchemaVersion ?? latestPlan.value?.schemaVersion ?? null
  )
  const latestPlanDigest = computed(() =>
    latestTask.value?.planDigest ?? latestPlan.value?.planDigest ?? null
  )
  const latestPlanTopologyProfile = computed(() =>
    latestTask.value?.topologyProfile ?? latestPlan.value?.topologyProfile ?? null
  )
  const latestPlanIntegrityStatus = computed(() =>
    latestTask.value?.planIntegrityStatus ?? 'Invalid'
  )
  const isPlanConfirmable = computed(() => isAgentPlanConfirmable(
    latestTask.value,
    latestPlan.value,
    latestPlanCapabilityGaps.value,
  ))
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
    latestPlanSchemaVersion,
    latestPlanDigest,
    latestPlanTopologyProfile,
    latestPlanIntegrityStatus,
    isPlanConfirmable,
    latestPlanKindLabel,
    latestPlanSource,
    latestPlanIsCloudReadonly,
    isPlanDraftTask,
    previewPlanSteps,
    totalPreviewPlanStepCount,
    hiddenPlanStepCount
  }
}
