<script setup lang="ts">
import { computed } from 'vue'
import { ListChecks, ShieldCheck } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { useAgentPlanPreview, statusTone } from '@/composables/useAgentPlanPreview'
import { useAgentTimelineDisplay, formatTimelineScore } from '@/composables/useAgentTimelineDisplay'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { useChatStore } from '@/stores/chatStore'
import ArtifactWorkspacePanel from './ArtifactWorkspacePanel.vue'

const store = useChatStore()
const {
  latestTask,
  taskSteps,
  completedStepCount,
  draftArtifactCount,
  finalArtifactCount,
  finalArtifacts,
  taskArtifacts,
  auditSummary,
  agentStageCards,
  onsiteStatus
} = useAgentWorkbench()
const {
  latestPlan,
  latestPlanRiskLine,
  latestPlanSource,
  latestPlanSchemaVersion,
  latestPlanTopologyProfile,
  latestPlanIntegrityStatus,
  latestPlanIsSimulation
} = useAgentPlanPreview()
const {
  timelineEventItems,
  latestTimelineSummary
} = useAgentTimelineDisplay()
const runtimeSnapshot = computed(() => store.agentRuntimeSnapshot)
const runtimeNodes = computed(() => runtimeSnapshot.value?.nodes ?? [])
const runtimeEvidence = computed(() => runtimeSnapshot.value?.evidence ?? [])
const runtimeMetrics = computed(() => runtimeSnapshot.value?.metrics ?? [])

const visibleEvidenceMetadataKeys = new Set([
  'stepOrder',
  'sourceMode',
  'isSimulation',
  'rowCount',
  'isTruncated',
])

function formatAuditTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '-'
    : date.toLocaleTimeString('zh-CN', { hour12: false })
}

function evidenceMetadataEntries(metadata: Record<string, string>) {
  return Object.entries(metadata).filter(
    ([key, value]) => visibleEvidenceMetadataKeys.has(key) && Boolean(value),
  )
}

function auditMetadataLabel(key: string) {
  const labels: Record<string, string> = {
    stepOrder: '步骤顺序',
    sourceMode: '数据模式',
    isSimulation: 'Simulation',
    rowCount: '行数',
    isTruncated: '是否截断',
  }
  return labels[key] ?? '记录'
}

function auditTitle(actionCode: string, targetType: string) {
  if (actionCode === 'Agent.ToolExecutionRecord') return '受控工具执行'
  if (actionCode === 'Agent.FailureSummary') return '任务失败摘要'
  if (targetType === 'AgentTask') return '任务状态记录'
  return '受控审计记录'
}

function formatDuration(value?: number | null) {
  if (value === null || value === undefined) return '-'
  if (value < 1000) return `${Math.round(value)} ms`
  return `${(value / 1000).toFixed(1)} s`
}

function formatRuntimeTime(value?: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '-'
    : date.toLocaleString('zh-CN', { hour12: false })
}

type RuntimeTone = 'success' | 'info' | 'warning' | 'neutral' | 'danger'

const runtimeTones: Record<string, RuntimeTone> = {
  'truth:ObservedFact': 'success', 'truth:DerivedFact': 'info', 'truth:ModelPrediction': 'warning',
  'truth:LlmInference': 'neutral', 'truth:Recommendation': 'warning', 'node:Completed': 'success',
  'node:Succeeded': 'success', 'node:Failed': 'danger', 'node:OutcomeUnknown': 'danger', 'node:Running': 'info',
  'node:WaitingApproval': 'warning', 'metric:Healthy': 'success', 'metric:Recorded': 'success',
  'metric:DerivedFromRuntimeRecord': 'success', 'metric:Warning': 'warning', 'metric:Unavailable': 'warning',
  'metric:ProviderUsageUnavailable': 'warning', 'metric:NotRecorded': 'warning', 'metric:Failed': 'danger',
}

const runtimeLabels: Record<string, string> = {
  'node:Pending': '待调度', 'node:Runnable': '可执行', 'node:Claimed': '已领取', 'node:Running': '执行中',
  'node:WaitingApproval': '暂停待审批', 'node:Completed': '已完成', 'node:Succeeded': '已成功',
  'node:Failed': '已失败', 'node:Cancelled': '已取消', 'node:OutcomeUnknown': '结果待核对',
  'metric:Recorded': '已记录', 'metric:DerivedFromRuntimeRecord': '由运行记录确定', 'metric:ObservedByTelemetryOnly': '仅遥测观测',
  'metric:ProviderUsageUnavailable': '供应商未回传', 'metric:Pending': '待记录', 'metric:NotStarted': '未开始',
  'metric:NotRecorded': '未记录', 'metric:Failed': '不一致', 'evidence:futureHeartbeatCount': '时钟超前心跳',
  'evidence:missingHeartbeatCount': '缺失心跳', 'evidence:reportedIssueStatusCount': '明确异常状态',
  'evidence:staleHeartbeatCount': '陈旧心跳', 'evidence:totalDeviceCount': '评估设备数',
  'evidence:unknownRuntimeStatusCount': '未知运行状态', 'evidence:rowCount': '记录数',
  'evidence:fileCount': '文件数', 'evidence:itemCount': '条目数', 'evidence:artifactCount': '产物数',
}

function runtimeTone(scope: string, value: string) {
  return runtimeTones[`${scope}:${value}`] ?? 'neutral'
}

function runtimeLabel(scope: string, value: string, fallback: string) {
  return runtimeLabels[`${scope}:${value}`] ?? fallback
}

function evidenceFreshnessLabel(freshness: string) {
  const labels: Record<string, string> = {
    'source-as-of': '按来源时点',
    'checkpoint-current': '检查点当前',
    'parent-evidence-current': '父证据当前',
    'parent-evidence-truncated': '父证据已截断',
    'derived-from-parent': '派生于父证据',
  }
  return labels[freshness] ?? '来源时效已记录'
}

function evidenceQualityWarnings(flags: string[]) {
  const labels: Record<string, string> = {
    'source-truncated': '输入数据已截断', 'parent-evidence-truncated': '父证据已截断', 'source-stale': '数据可能陈旧',
    'missing-data': '存在缺失数据', 'low-confidence': '低置信度', 'simulation-source': 'Simulation 数据',
    'simulation-evidence': 'Simulation 证据', 'metric-conflict': '同名指标存在冲突', 'potential-evidence-conflict': '证据可能冲突',
    'optional-evidence-missing': '可选证据缺失',
    'no-results': '来源未返回记录',
    'data-insufficient': '数据不足',
  }
  return [...new Set(
    flags
      .map((flag) => labels[flag])
      .filter((label): label is string => Boolean(label)),
  )]
}

function formatQualityRatio(value?: number | null) {
  if (value === null || value === undefined) return '-'
  const ratio = value <= 1 ? value * 100 : value
  return `${ratio.toFixed(1)}%`
}

function formatMetric(value?: number | null, unit?: string) {
  if (value === null || value === undefined) return '未记录'
  const formatted = Number.isInteger(value) ? value.toString() : value.toFixed(2)
  return unit ? `${formatted} ${unit}` : formatted
}

</script>

<template>
  <details class="agent-thinking-details" data-testid="inline-runtime-card">
    <summary data-testid="inline-runtime-summary">
      <span class="timeline-summary-main">
        <ListChecks :size="18" />
        <span>
          <strong>运行详情</strong>
          <small>{{ completedStepCount }}/{{ taskSteps.length }} 步完成 · {{ latestTimelineSummary }}</small>
        </span>
      </span>
      <AiTag tone="neutral">展开</AiTag>
    </summary>

    <div class="runtime-sections">
      <section class="runtime-section-block">
        <div class="runtime-section-title">
          <strong>运行状态</strong>
          <span>阶段、审批和工作区</span>
        </div>
        <div class="status-grid compact-status-grid">
          <div v-for="item in agentStageCards" :key="item.label" class="status-tile">
            <span>{{ item.label }}</span>
            <AiTag :tone="statusTone(item.type)">{{ item.value }}</AiTag>
          </div>
          <div class="status-tile">
            <span>草稿 / 正式</span>
            <strong class="ai-number">{{ draftArtifactCount }}/{{ finalArtifactCount }}</strong>
          </div>
        </div>
      </section>

      <section class="runtime-section-block">
        <div class="runtime-section-title">
          <strong>安全边界</strong>
          <span>AICopilot 只读边界和现场声明</span>
        </div>
        <div class="boundary-runtime" data-testid="inline-boundary-row">
          <ShieldCheck :size="20" />
          <div>
            <strong>{{ latestPlanIsSimulation ? 'Simulation 只读边界' : 'Cloud 只读边界' }}</strong>
            <span v-if="latestPlanIsSimulation">AI 独立模拟业务库 · 不回退、不冒充 Cloud 正式数据</span>
            <span v-else>现场确认：{{ onsiteStatus.label }}</span>
          </div>
          <button v-if="!latestPlanIsSimulation" type="button" :disabled="!store.resolvedSessionId || store.isSessionTransitionBlocked" @click="store.confirmOnsitePresence(30)">确认在岗</button>
        </div>
      </section>

      <section v-if="latestTask" class="runtime-section-block" data-testid="plan-technical-details">
        <div class="runtime-section-title">
          <strong>计划详情</strong>
          <span>来源、风险和审批点</span>
        </div>
        <div class="plan-grid">
          <div>
            <span>来源</span>
            <strong>{{ latestPlanSource }}</strong>
          </div>
          <div>
            <span>审批点</span>
            <strong>{{ latestPlan?.approvalCheckpoints?.length ?? 0 }}</strong>
          </div>
          <div>
            <span>风险</span>
            <strong>{{ latestPlanRiskLine }}</strong>
          </div>
          <div>
            <span>计划契约</span>
            <strong>{{ latestPlanSchemaVersion ?? '-' }}</strong>
          </div>
          <div>
            <span>执行拓扑</span>
            <strong>{{ latestPlanTopologyProfile ?? '-' }}</strong>
          </div>
          <div>
            <span>完整性</span>
            <strong>{{ latestPlanIntegrityStatus }}</strong>
          </div>
        </div>
      </section>

      <section class="runtime-section-block">
        <div class="runtime-section-title">
          <strong>步骤</strong>
          <span>{{ completedStepCount }}/{{ taskSteps.length }} 已完成</span>
        </div>
        <div v-if="taskSteps.length" class="step-list">
          <div v-for="step in taskSteps" :key="step.id" class="step-row">
            <i>{{ step.stepIndex }}</i>
            <div>
              <strong>{{ step.title }}</strong>
              <span v-if="step.errorMessage" class="danger-text">{{ step.errorMessage }}</span>
              <details v-if="step.description" class="step-detail-fold">
                <summary>详情</summary>
                <span>{{ step.description }}</span>
              </details>
            </div>
            <AiTag :tone="step.status === 'Completed' ? 'success' : step.status === 'Failed' ? 'danger' : step.requiresApproval ? 'warning' : 'neutral'">
              {{ step.status }}
            </AiTag>
          </div>
        </div>
        <div v-else class="panel-empty">暂无步骤</div>
      </section>

      <section v-if="runtimeNodes.length" class="runtime-section-block" data-testid="agent-runtime-nodes">
        <div class="runtime-section-title">
          <strong>节点执行</strong>
          <span>{{ runtimeNodes.length }} 个真实运行节点</span>
        </div>
        <div class="timeline-list">
          <div v-for="node in runtimeNodes" :key="node.nodeId" class="timeline-row">
            <time>{{ formatDuration(node.durationMs) }}</time>
            <div class="timeline-row-main">
              <strong>{{ node.label }}</strong>
              <span>
                {{ node.kind }} · 依赖 {{ node.dependencyCount }} ·
                尝试 {{ node.attemptNo }}/{{ node.maxAttempts }}
                <template v-if="node.retryCount"> · 重试 {{ node.retryCount }}</template>
              </span>
              <span v-if="node.safeMessage" :class="{ 'danger-text': node.status === 'Failed' }">
                {{ node.safeMessage }}
              </span>
            </div>
            <AiTag :tone="runtimeTone('node', node.status)">{{ runtimeLabel('node', node.status, '未知状态') }}</AiTag>
          </div>
        </div>
      </section>

      <section v-if="timelineEventItems.length" class="runtime-section-block">
        <div class="runtime-section-title">
          <strong>事件</strong>
          <span>最新：{{ latestTimelineSummary }}</span>
        </div>
        <div class="timeline-list">
          <div v-for="item in timelineEventItems" :key="item.key" class="timeline-row">
            <time>{{ item.time }}</time>
            <div class="timeline-row-main">
              <strong>{{ item.title }}</strong>
              <span>{{ item.subtitle }}</span>
              <details v-if="item.outputKind === 'RagSearch' && item.sources.length" class="timeline-result-fold">
                <summary>
                  检索结果 · {{ item.resultCount }} 条
                  <template v-if="item.lowConfidence"> · 低置信度</template>
                </summary>
                <div class="timeline-source-list">
                  <article v-for="source in item.sources" :key="`${item.key}:${source.documentId}:${source.chunkIndex}`" class="timeline-source-item">
                    <strong>{{ source.documentName || `文档 ${source.documentId || '-'}` }}</strong>
                    <small>
                      {{ formatTimelineScore(source.score) }}
                      <template v-if="source.isLowConfidence"> · 低置信度</template>
                      <template v-if="source.lowConfidenceReason"> · {{ source.lowConfidenceReason }}</template>
                    </small>
                    <em v-if="source.textPreview">{{ source.textPreview }}</em>
                  </article>
                </div>
              </details>
            </div>
            <AiTag :tone="item.tone">{{ item.status }}</AiTag>
          </div>
        </div>
      </section>

      <section v-if="runtimeEvidence.length" class="runtime-section-block" data-testid="agent-evidence-summary">
        <div class="runtime-section-title">
          <strong>证据与真值类型</strong>
          <span>{{ runtimeEvidence.length }} 份受控证据</span>
        </div>
        <div class="timeline-list">
          <div v-for="evidence in runtimeEvidence" :key="`${evidence.nodeId}:${evidence.evidenceKind}`" class="timeline-row">
            <time>{{ formatRuntimeTime(evidence.asOfUtc) }}</time>
            <div class="timeline-row-main">
              <strong>{{ evidence.nodeLabel }} · {{ evidence.sourceLabel }}</strong>
              <span>{{ evidence.safeSummary }}</span>
              <details class="step-detail-fold">
                <summary>证据质量</summary>
                <span>
                  {{ evidence.sourceMode }}
                  <template v-if="evidence.isSimulation"> · Simulation</template>
                  · {{ evidenceFreshnessLabel(evidence.quality.freshness) }}
                  · {{ evidence.quality.rowCount ?? '行数未记录' }}
                  <template v-if="evidence.quality.isTruncated"> · 已截断</template>
                </span>
                <span v-if="evidence.timeRangeStartUtc || evidence.timeRangeEndUtc">
                  时间范围：{{ formatRuntimeTime(evidence.timeRangeStartUtc) }} —
                  {{ formatRuntimeTime(evidence.timeRangeEndUtc) }}
                </span>
                <span v-if="evidence.quality.confidence !== null && evidence.quality.confidence !== undefined">
                  置信度：{{ formatQualityRatio(evidence.quality.confidence) }}
                </span>
                <span v-if="evidence.quality.missingRate !== null && evidence.quality.missingRate !== undefined">
                  缺失率：{{ formatQualityRatio(evidence.quality.missingRate) }}
                </span>
                <span v-if="evidence.citationCount">引用：{{ evidence.citationCount }} 条</span>
                <span v-for="warning in evidenceQualityWarnings(evidence.quality.flags)" :key="warning">
                  质量提示：{{ warning }}
                </span>
                <span v-for="finding in evidence.findings" :key="finding">{{ finding }}</span>
                <span v-for="(value, name) in evidence.typedMetrics" :key="name">
                  {{ runtimeLabel('evidence', name, '确定性指标') }}：{{ formatMetric(value) }}
                </span>
              </details>
            </div>
            <AiTag
              :tone="runtimeTone('truth', evidence.truthClass)"
            >{{ evidence.truthLabel }}</AiTag>
          </div>
        </div>
      </section>

      <section v-if="runtimeMetrics.length" class="runtime-section-block" data-testid="agent-runtime-metrics">
        <div class="runtime-section-title">
          <strong>运行指标</strong>
          <span>仅展示后端已记录的真实数值</span>
        </div>
        <div class="status-grid compact-status-grid">
          <div v-for="metric in runtimeMetrics" :key="metric.code" class="status-tile">
            <span>{{ metric.label }}</span>
            <strong class="ai-number">{{ formatMetric(metric.value, metric.unit) }}</strong>
            <AiTag :tone="runtimeTone('metric', metric.status)">{{ runtimeLabel('metric', metric.status, '状态未记录') }}</AiTag>
          </div>
        </div>
      </section>

      <section v-if="auditSummary.length" class="runtime-section-block" data-testid="agent-audit-summary">
        <div class="runtime-section-title">
          <strong>审计摘要</strong>
          <span>{{ auditSummary.length }} 条真实审计记录</span>
        </div>
        <div class="timeline-list">
          <div v-for="audit in auditSummary" :key="audit.id" class="timeline-row">
            <time>{{ formatAuditTime(audit.createdAt) }}</time>
            <div class="timeline-row-main">
              <strong>{{ auditTitle(audit.actionCode, audit.targetType) }}</strong>
              <span>{{ audit.summary }}</span>
              <details v-if="evidenceMetadataEntries(audit.metadata).length" class="step-detail-fold">
                <summary>记录范围</summary>
                <span v-for="entry in evidenceMetadataEntries(audit.metadata)" :key="`${audit.id}:${entry[0]}`">
                  {{ auditMetadataLabel(entry[0]) }}：{{ entry[1] }}
                </span>
              </details>
            </div>
            <AiTag :tone="audit.result === 'Succeeded' ? 'success' : 'danger'">
              {{ audit.result }}
            </AiTag>
          </div>
        </div>
      </section>

      <section v-if="latestTask?.finalSummary || finalArtifacts.length" class="runtime-section-block" data-testid="agent-final-result">
        <div class="runtime-section-title">
          <strong>最终结果</strong>
          <span>{{ finalArtifacts.length }} 个正式产物</span>
        </div>
        <div class="plan-grid">
          <div>
            <span>任务结论</span>
            <strong>{{ latestTask?.finalSummary || '后端未返回最终摘要。' }}</strong>
          </div>
          <div>
            <span>数据边界</span>
            <strong>{{ latestPlanIsSimulation ? 'Simulation · 只读模拟' : latestPlanSource }}</strong>
          </div>
        </div>
      </section>

      <ArtifactWorkspacePanel v-if="store.currentWorkspace || taskArtifacts.length" />
    </div>
  </details>
</template>
