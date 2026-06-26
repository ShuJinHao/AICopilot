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
  workspaceFileCount,
  draftArtifactCount,
  finalArtifactCount,
  taskArtifacts,
  approvalGroups,
  agentStageCards,
  onsiteStatus
} = useAgentWorkbench()
const {
  latestPlan,
  latestPlanToolCatalogVersion,
  latestPlanVisibleToolCount,
  latestPlanRiskLine,
  latestPlanSource
} = useAgentPlanPreview()
const {
  timelineEventItems,
  latestTimelineSummary
} = useAgentTimelineDisplay()
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
            <strong>Cloud 只读边界</strong>
            <span>现场确认：{{ onsiteStatus.label }}</span>
          </div>
          <button type="button" @click="store.confirmOnsitePresence(30)">确认在岗</button>
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

      <ArtifactWorkspacePanel v-if="store.currentWorkspace || taskArtifacts.length" />
    </div>
  </details>
</template>
