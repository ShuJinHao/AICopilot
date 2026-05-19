<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RefreshCw } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
import AiTabs from '@/components/ai/AiTabs.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useConfigStore } from '@/stores/configStore'
import ApprovalPolicyConfig from '@/views/config/ApprovalPolicyConfig.vue'
import BusinessDatabaseConfig from '@/views/config/BusinessDatabaseConfig.vue'
import ConversationTemplateConfig from '@/views/config/ConversationTemplateConfig.vue'
import LanguageModelConfig from '@/views/config/LanguageModelConfig.vue'
import McpServerConfig from '@/views/config/McpServerConfig.vue'
import ProviderReliabilityConfig from '@/views/config/ProviderReliabilityConfig.vue'
import RoutingModelConfig from '@/views/config/RoutingModelConfig.vue'

const store = useConfigStore()
const activeTab = ref('models')

const tabs = [
  { value: 'models', label: '模型' },
  { value: 'reliability', label: '可靠性' },
  { value: 'routing', label: '路由' },
  { value: 'templates', label: '模板' },
  { value: 'data', label: '数据分析' },
  { value: 'mcp', label: 'MCP' },
  { value: 'approval', label: '审批' },
  { value: 'agent', label: 'Agent' }
]

const acceptanceSummary = {
  generatedAt: '2026-05-14',
  reportPath: '资料/acceptance-closure-latest.md',
  status: '全部通过'
}

const workspaceFolders = computed(() => store.workspaceSettings?.folders ?? [])
const allowedArtifactTypes = computed(() => store.workspaceSettings?.allowedArtifactTypes ?? [])
const activeRoutingModel = computed(() => store.routingModels.find((item) => item.isActive)?.name || '未激活')
const workspacePathPolicy = computed(() => (store.workspaceSettings?.allowsUserDefinedPath ? '允许用户指定路径' : '固定受控目录'))
const runQueueItems = computed(() => store.runQueuePage?.items ?? [])
const workerRows = computed(() => store.workerStatus?.workers ?? [])

onMounted(() => {
  void store.refresh()
})
</script>

<template>
  <AppShell>
    <AiDataPage eyebrow="Runtime Control" title="运行配置" description="集中管理模型、模板、MCP 工具、只读业务数据源、审批策略和 Agent 工作区边界。">
      <template #actions>
        <AiButton :disabled="store.isLoading" @click="store.refresh()">
          <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isLoading }" />
          刷新
        </AiButton>
      </template>

      <div class="metric-strip">
        <AiCard class="metric" tone="violet">
          <span>语言模型</span>
          <strong>{{ store.languageModels.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="blue">
          <span>会话模板</span>
          <strong>{{ store.conversationTemplates.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="lime">
          <span>路由模型</span>
          <strong>{{ activeRoutingModel }}</strong>
        </AiCard>
        <AiCard class="metric" tone="teal">
          <span>只读数据源</span>
          <strong>{{ store.businessDatabases.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="soft">
          <span>MCP 服务</span>
          <strong>{{ store.mcpServers.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="surface">
          <span>服务商回退</span>
          <strong>{{ store.providerReliability?.fallbackEnabled ? '启用' : '关闭' }}</strong>
        </AiCard>
      </div>

      <div v-if="store.errorMessage" class="error-banner">{{ store.errorMessage }}</div>

      <AiTabs v-model="activeTab" :items="tabs">
        <LanguageModelConfig v-if="activeTab === 'models'" />
        <ProviderReliabilityConfig v-else-if="activeTab === 'reliability'" />
        <RoutingModelConfig v-else-if="activeTab === 'routing'" />
        <ConversationTemplateConfig v-else-if="activeTab === 'templates'" />
        <BusinessDatabaseConfig v-else-if="activeTab === 'data'" />
        <McpServerConfig v-else-if="activeTab === 'mcp'" />
        <ApprovalPolicyConfig v-else-if="activeTab === 'approval'" />

        <div v-else class="agent-config">
          <AiCard class="agent-hero" tone="soft">
            <div>
              <p class="page-kicker">Agent / Workspace</p>
              <h2>受控产物运行边界</h2>
              <p>这里展示 Agent 工作区、运行历史、审批策略和交付验收状态。服务端路径仍由应用托管，不允许用户输入任意写入目录。</p>
            </div>
            <div class="acceptance-card">
              <span>最近验收</span>
              <strong>{{ acceptanceSummary.status }}</strong>
              <small>{{ acceptanceSummary.generatedAt }} · {{ acceptanceSummary.reportPath }}</small>
            </div>
          </AiCard>

          <AiCard class="agent-config-section">
            <h2>运行与记忆</h2>
            <p>聊天、RAG 和 Agent 规划统一读取这些运行参数。</p>
            <div class="agent-config-grid">
              <div><span>路由历史</span><strong>{{ store.runtimeSettings?.routingHistoryCount ?? '-' }}</strong></div>
              <div><span>回答历史</span><strong>{{ store.runtimeSettings?.answerHistoryCount ?? '-' }}</strong></div>
              <div><span>RAG 改写历史</span><strong>{{ store.runtimeSettings?.ragRewriteHistoryCount ?? '-' }}</strong></div>
              <div><span>Agent 规划历史</span><strong>{{ store.runtimeSettings?.agentPlanningHistoryCount ?? '-' }}</strong></div>
              <div><span>摘要阈值</span><strong>{{ store.runtimeSettings?.summaryThresholdMessages ?? '-' }}</strong></div>
              <div><span>上下文上限</span><strong>{{ store.runtimeSettings?.contextTokenLimit ?? '-' }}</strong></div>
            </div>
          </AiCard>

          <AiCard class="agent-config-section">
            <div class="section-head">
              <div>
                <h2>工作区</h2>
                <p>产物只能写入应用管理的固定目录，正式输出必须经过 finalize。</p>
              </div>
              <AiTag :tone="store.workspaceSettings?.allowsUserDefinedPath ? 'warning' : 'success'">
                {{ workspacePathPolicy }}
              </AiTag>
            </div>
            <p class="workspace-root">{{ store.workspaceSettings?.rootPath || '未加载' }}</p>
            <div class="tag-list">
              <AiTag v-for="folder in workspaceFolders" :key="folder" tone="neutral">{{ folder }}/</AiTag>
            </div>
            <div class="tag-list">
              <AiTag v-for="type in allowedArtifactTypes" :key="type" tone="teal">{{ type }}</AiTag>
            </div>
            <div class="notice" :class="{ warning: store.workspaceSettings?.allowsUserDefinedPath }">
              {{ store.workspaceSettings?.allowsUserDefinedPath ? '当前策略存在路径开放风险。' : '任意服务端路径写入保持关闭。' }}
            </div>
          </AiCard>

          <AiCard class="agent-config-section">
            <h2>审批与交付门禁</h2>
            <p>计划确认、风险工具、产物确认和正式输出仍统一落到审批与审计链路。</p>
            <div class="guardrail-grid">
              <div><span>审批策略入口</span><strong>审批 Tab</strong></div>
              <div><span>产物下载</span><strong>所有权校验</strong></div>
              <div><span>Cloud 写入</span><strong>禁止</strong></div>
              <div><span>Shell / 任意路径</span><strong>未开放</strong></div>
            </div>
          </AiCard>

          <AiCard class="agent-config-section">
            <div class="section-head">
              <div>
                <h2>Run Queue</h2>
                <p>队列统计和状态全部来自后端运维接口。</p>
              </div>
              <AiTag :tone="store.runQueueSummary?.staleLeasedCount ? 'warning' : 'success'">
                {{ store.runQueueSummary?.staleLeasedCount ? '存在陈旧租约' : '队列正常' }}
              </AiTag>
            </div>
            <div class="agent-config-grid">
              <div><span>Queued</span><strong>{{ store.runQueueSummary?.queuedCount ?? '-' }}</strong></div>
              <div><span>Leased</span><strong>{{ store.runQueueSummary?.leasedCount ?? '-' }}</strong></div>
              <div><span>Failed</span><strong>{{ store.runQueueSummary?.failedCount ?? '-' }}</strong></div>
              <div><span>DeadLetter</span><strong>{{ store.runQueueSummary?.deadLetterCount ?? '-' }}</strong></div>
              <div><span>Stale Lease</span><strong>{{ store.runQueueSummary?.staleLeasedCount ?? '-' }}</strong></div>
              <div><span>Worker</span><strong>{{ store.runQueueSummary?.activeWorkerCount ?? '-' }}</strong></div>
            </div>
            <div class="ops-table" v-if="runQueueItems.length">
              <div v-for="item in runQueueItems" :key="item.id" class="ops-row">
                <div>
                  <strong>{{ item.status }}</strong>
                  <span>{{ item.triggerType }} · {{ item.failureCode || item.safeMessage || '无错误' }}</span>
                </div>
                <small>{{ item.updatedAt }}</small>
              </div>
            </div>
            <div v-else class="panel-empty">暂无队列记录</div>
          </AiCard>

          <AiCard class="agent-config-section">
            <div class="section-head">
              <div>
                <h2>Worker Status</h2>
                <p>工作区一致性、心跳和活跃 Worker 由后端统一判定。</p>
              </div>
              <AiTag :tone="store.workerStatus?.workspaceConsistent ? 'success' : 'danger'">
                {{ store.workerStatus?.statusCode || '未加载' }}
              </AiTag>
            </div>
            <div class="agent-config-grid">
              <div><span>Active Workers</span><strong>{{ store.workerStatus?.activeWorkerCount ?? '-' }}</strong></div>
              <div><span>Queued</span><strong>{{ store.workerStatus?.queuedCount ?? '-' }}</strong></div>
              <div><span>Leased</span><strong>{{ store.workerStatus?.leasedCount ?? '-' }}</strong></div>
              <div><span>Workspace</span><strong>{{ store.workerStatus?.workspaceConsistent ? '一致' : '不一致' }}</strong></div>
            </div>
            <div class="ops-table" v-if="workerRows.length">
              <div v-for="worker in workerRows" :key="worker.workerId" class="ops-row">
                <div>
                  <strong>{{ worker.workerName }}</strong>
                  <span>{{ worker.workspaceMatchesHttpApi ? '工作区匹配' : '工作区不匹配' }} · {{ worker.version }}</span>
                </div>
                <small>{{ worker.lastSeenAt || '-' }}</small>
              </div>
            </div>
            <div v-else class="panel-empty">暂无 Worker 心跳</div>
          </AiCard>
        </div>
      </AiTabs>
    </AiDataPage>
  </AppShell>
</template>

<style scoped>
.metric-strip {
  display: grid;
  grid-template-columns: repeat(6, minmax(140px, 1fr));
  gap: 12px;
}

.metric {
  min-height: 112px;
}

.metric span,
.agent-config-grid span,
.guardrail-grid span,
.acceptance-card span,
.acceptance-card small,
.workspace-root {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.metric strong {
  display: block;
  margin-top: 14px;
  color: var(--ai-text);
  font-size: 28px;
  font-weight: 950;
  font-variant-numeric: tabular-nums;
}

.error-banner,
.notice {
  border: 1px solid #fecaca;
  border-radius: 18px;
  background: #fef2f2;
  padding: 12px 14px;
  color: #b42318;
  font-size: 13px;
  font-weight: 800;
}

.agent-config {
  display: grid;
  gap: 16px;
}

.agent-hero {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 280px;
  align-items: stretch;
  gap: 18px;
}

.page-kicker {
  margin: 0 0 8px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 900;
  text-transform: uppercase;
}

.agent-hero h2,
.agent-config-section h2 {
  margin: 0;
  color: var(--ai-text);
  font-size: 20px;
  font-weight: 950;
}

.agent-hero p,
.agent-config-section p {
  margin: 8px 0 0;
  color: var(--ai-text-muted);
  font-weight: 650;
  line-height: 1.7;
}

.acceptance-card {
  display: grid;
  gap: 6px;
  border: 1px solid var(--ai-border);
  border-radius: 20px;
  padding: 16px;
  background: var(--ai-surface);
}

.acceptance-card strong {
  font-size: 30px;
  font-weight: 950;
}

.section-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.agent-config-grid,
.guardrail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 10px;
  margin-top: 14px;
}

.agent-config-grid div,
.guardrail-grid div {
  display: grid;
  gap: 5px;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 12px;
  background: var(--ai-surface-soft);
}

.agent-config-grid strong,
.guardrail-grid strong {
  color: var(--ai-text);
  font-size: 20px;
  font-weight: 950;
  font-variant-numeric: tabular-nums;
}

.workspace-root {
  margin: 12px 0 0;
  overflow-wrap: anywhere;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.notice {
  margin-top: 12px;
  border-color: #bbf7d0;
  background: #f0fdf4;
  color: #15803d;
}

.notice.warning {
  border-color: #fed7aa;
  background: #fff7ed;
  color: #b45309;
}

.ops-table {
  display: grid;
  gap: 8px;
  margin-top: 14px;
}

.ops-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 12px;
  align-items: center;
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 11px 12px;
  background: var(--ai-surface-soft);
}

.ops-row div {
  display: grid;
  min-width: 0;
  gap: 3px;
}

.ops-row strong,
.ops-row span,
.ops-row small {
  min-width: 0;
  overflow-wrap: anywhere;
}

.ops-row strong {
  color: var(--ai-text);
  font-weight: 900;
}

.ops-row span,
.ops-row small,
.panel-empty {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 750;
}

.panel-empty {
  margin-top: 12px;
}

@media (max-width: 1200px) {
  .metric-strip {
    grid-template-columns: repeat(3, minmax(160px, 1fr));
  }
}

@media (max-width: 980px) {
  .metric-strip,
  .agent-hero {
    grid-template-columns: 1fr;
  }
}
</style>
