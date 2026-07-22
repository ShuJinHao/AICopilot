<script setup lang="ts">
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
  latestPlanToolCatalogVersion,
  latestPlanVisibleToolCount,
  latestPlanRiskLine,
  latestPlanSource,
  latestPlanSchemaVersion,
  latestPlanDigest,
  latestPlanTopologyProfile,
  latestPlanIntegrityStatus,
  latestPlanIsSimulation
} = useAgentPlanPreview()
const {
  timelineEventItems,
  latestTimelineSummary
} = useAgentTimelineDisplay()

const visibleEvidenceMetadataKeys = new Set([
  'stepOrder',
  'toolName',
  'artifactId',
  'sourceMode',
  'isSimulation',
  'queryHash',
  'resultHash',
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
</script>

<template>
  <details class="agent-thinking-details" data-testid="inline-runtime-card">
    <summary data-testid="inline-runtime-summary">
      <span class="timeline-summary-main">
        <ListChecks :size="18" />
        <span>
          <strong>思考与执行记录</strong>
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
          <span>Skill、目录版本和审批点</span>
        </div>
        <div class="plan-grid">
          <div>
            <span>来源</span>
            <strong>{{ latestPlanSource }}</strong>
          </div>
          <div>
            <span>工具目录</span>
            <strong>v{{ latestPlanToolCatalogVersion ?? '-' }} / {{ latestPlanVisibleToolCount }}</strong>
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
            <strong>{{ latestPlanSchemaVersion ?? '-' }} / {{ latestPlanTopologyProfile ?? '-' }}</strong>
          </div>
          <div>
            <span>完整性</span>
            <strong>{{ latestPlanIntegrityStatus }}</strong>
          </div>
          <div>
            <span>Digest</span>
            <strong>{{ latestPlanDigest ? latestPlanDigest.slice(0, 12) : '-' }}</strong>
          </div>
        </div>
        <div v-if="latestPlan?.forcedStepCodes?.length" class="chip-row">
          <span v-for="code in latestPlan.forcedStepCodes" :key="code">{{ code }}</span>
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
              <details v-if="step.toolCode || step.description" class="step-detail-fold">
                <summary>详情</summary>
                <span v-if="step.description">{{ step.description }}</span>
                <code v-if="step.toolCode">{{ step.toolCode }}</code>
              </details>
            </div>
            <AiTag :tone="step.status === 'Completed' ? 'success' : step.status === 'Failed' ? 'danger' : step.requiresApproval ? 'warning' : 'neutral'">
              {{ step.status }}
            </AiTag>
          </div>
        </div>
        <div v-else class="panel-empty">暂无步骤</div>
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

      <section v-if="auditSummary.length" class="runtime-section-block" data-testid="agent-evidence-summary">
        <div class="runtime-section-title">
          <strong>受控证据</strong>
          <span>{{ auditSummary.length }} 条真实审计记录</span>
        </div>
        <div class="timeline-list">
          <div v-for="audit in auditSummary" :key="audit.id" class="timeline-row">
            <time>{{ formatAuditTime(audit.createdAt) }}</time>
            <div class="timeline-row-main">
              <strong>{{ audit.actionCode }} · {{ audit.targetName }}</strong>
              <span>{{ audit.summary }}</span>
              <details v-if="evidenceMetadataEntries(audit.metadata).length" class="step-detail-fold">
                <summary>证据字段</summary>
                <code v-for="entry in evidenceMetadataEntries(audit.metadata)" :key="`${audit.id}:${entry[0]}`">
                  {{ entry[0] }}={{ entry[1] }}
                </code>
              </details>
            </div>
            <AiTag :tone="audit.result === 'Succeeded' ? 'success' : 'danger'">{{ audit.result }}</AiTag>
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
