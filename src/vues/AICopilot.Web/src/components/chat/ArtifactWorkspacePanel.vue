<script setup lang="ts">
import { Download, Eye } from 'lucide-vue-next'
import { useAgentWorkbench } from '@/composables/useAgentWorkbench'
import { sourceModeLabel } from '@/composables/useAgentPlanPreview'
import { useChatStore } from '@/stores/chatStore'

const store = useChatStore()
const {
  latestTask,
  taskArtifacts,
  draftArtifacts,
  finalArtifacts,
  currentArtifactPreview,
  workspaceFileCount,
  artifactGroups,
  chartBars,
  canSubmitFinalReview
} = useAgentWorkbench()

async function submitFinalReview() {
  const code = store.currentWorkspace?.workspaceCode || latestTask.value?.workspaceCode
  if (!code || !canSubmitFinalReview.value) return
  await store.submitFinalReview(code)
}

async function downloadArtifact(artifactId: string) {
  const artifact = taskArtifacts.value.find((item) => item.id === artifactId)
  if (!artifact) return
  await store.downloadArtifact(artifact)
}

async function previewArtifact(artifactId: string) {
  await store.loadArtifactPreview(artifactId)
}
</script>

<template>
  <section v-if="store.currentWorkspace || taskArtifacts.length" class="runtime-section-block" data-testid="inline-artifact-card">
    <div class="runtime-section-title">
      <strong>产物详情</strong>
      <span>{{ store.currentWorkspace ? `${taskArtifacts.length} 个产物 · ${workspaceFileCount} 个文件` : '等待产物生成' }}</span>
    </div>

    <details class="artifact-detail-fold" data-testid="artifact-detail-fold">
      <summary>产物明细</summary>
      <div v-if="chartBars.length" class="chart-preview">
        <div class="chart-preview-head">
          <span>图表预览</span>
          <small>{{ sourceModeLabel(store.chartPreview?.sourceLabel || store.chartPreview?.sourceMode || store.chartPreview?.source || 'workspace') }}</small>
        </div>
        <div v-for="bar in chartBars" :key="bar.label" class="chart-bar-row">
          <span>{{ bar.label }}</span>
          <div><i :style="{ width: bar.width }" /></div>
          <strong>{{ bar.value }}</strong>
        </div>
      </div>

      <div class="artifact-summary">
        <div>
          <span>工作区</span>
          <strong>{{ store.currentWorkspace?.workspaceCode || '-' }}</strong>
        </div>
        <div>
          <span>文件</span>
          <strong>{{ workspaceFileCount }}</strong>
        </div>
        <div>
          <span>草稿</span>
          <strong>{{ draftArtifacts.length }}</strong>
        </div>
        <div>
          <span>正式</span>
          <strong>{{ finalArtifacts.length }}</strong>
        </div>
      </div>
      <template v-if="artifactGroups.length">
        <div v-for="group in artifactGroups" :key="group.label" class="artifact-group">
          <div class="group-title">{{ group.label }}</div>
          <div v-for="artifact in group.artifacts" :key="artifact.id" class="artifact-row">
            <div>
              <strong>{{ artifact.name }}</strong>
              <span>v{{ artifact.artifactVersion || artifact.version }} · {{ artifact.artifactStatus || artifact.status }} · {{ artifact.approvalStatus || '-' }}</span>
              <span class="artifact-source-line">
                {{ artifact.sourceLabel || sourceModeLabel(artifact.sourceMode || 'UnknownSource') }}
                <template v-if="artifact.boundary"> · {{ artifact.boundary }}</template>
              </span>
            </div>
            <div class="artifact-actions">
              <button type="button" aria-label="预览产物" @click="previewArtifact(artifact.id)">
                <Eye :size="16" />
              </button>
              <button type="button" aria-label="下载产物" @click="downloadArtifact(artifact.id)">
                <Download :size="16" />
              </button>
            </div>
          </div>
        </div>
      </template>
      <div v-else class="panel-empty">暂无产物</div>
      <div v-if="currentArtifactPreview" class="artifact-preview-panel">
        <div class="section-title">
          <strong>{{ currentArtifactPreview.name }}</strong>
          <span>{{ currentArtifactPreview.previewKind }} · v{{ currentArtifactPreview.artifactVersion }}</span>
        </div>
        <pre v-if="currentArtifactPreview.content" class="artifact-preview-content">{{ currentArtifactPreview.content.slice(0, 1600) }}</pre>
        <div v-else-if="currentArtifactPreview.rows?.length" class="artifact-preview-table">
          <div class="artifact-preview-table-head">
            <span v-for="column in currentArtifactPreview.columns" :key="column">{{ column }}</span>
          </div>
          <div v-for="(row, index) in currentArtifactPreview.rows.slice(0, 5)" :key="index" class="artifact-preview-table-row">
            <span v-for="column in currentArtifactPreview.columns" :key="column">{{ row[column] ?? '-' }}</span>
          </div>
        </div>
      </div>
      <button class="inline-secondary-action" type="button" :disabled="!canSubmitFinalReview || store.isAgentBusy" @click="submitFinalReview">
        提交最终审批
      </button>
    </details>
  </section>
</template>
