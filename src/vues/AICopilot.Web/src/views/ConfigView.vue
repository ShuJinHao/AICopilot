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
import ToolRegistryConfig from '@/views/config/ToolRegistryConfig.vue'

const store = useConfigStore()
const activeTab = ref('models')

const tabs = [
  { value: 'models', label: '模型' },
  { value: 'reliability', label: '模型池' },
  { value: 'routing', label: '路由' },
  { value: 'templates', label: '模板' },
  { value: 'data', label: '数据源' },
  { value: 'tools', label: '工具目录' },
  { value: 'mcp', label: 'MCP' },
  { value: 'approval', label: '审批' },
  { value: 'agent', label: 'Agent' }
]

const activeRoutingModel = computed(
  () => store.routingModels.find((item) => item.isActive)?.name || '未启用'
)
const mockToolCount = computed(
  () => store.toolRegistrations.filter((tool) => tool.providerType === 'MockMcp').length
)
const workspaceFolders = computed(() => store.workspaceSettings?.folders ?? [])
const runQueueItems = computed(() => store.runQueuePage?.items ?? [])
const workerRows = computed(() => store.workerStatus?.workers ?? [])

onMounted(() => {
  void store.refresh()
})
</script>

<template>
  <AppShell>
    <AiDataPage
      eyebrow="Runtime Control"
      title="运行配置"
      description="集中管理模型、数据源、工具目录、Mock MCP、审批策略和 Agent 工作区边界。"
    >
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
          <span>路由模型</span>
          <strong>{{ activeRoutingModel }}</strong>
        </AiCard>
        <AiCard class="metric" tone="teal">
          <span>业务数据源</span>
          <strong>{{ store.businessDatabases.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="lime">
          <span>工具登记</span>
          <strong>{{ store.toolRegistrations.length }}</strong>
        </AiCard>
        <AiCard class="metric" tone="soft">
          <span>Mock MCP</span>
          <strong>{{ mockToolCount }}</strong>
        </AiCard>
        <AiCard class="metric" tone="surface">
          <span>Provider Fallback</span>
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
        <ToolRegistryConfig v-else-if="activeTab === 'tools'" />
        <McpServerConfig v-else-if="activeTab === 'mcp'" />
        <ApprovalPolicyConfig v-else-if="activeTab === 'approval'" />

        <div v-else class="agent-config">
          <AiCard class="agent-section">
            <div class="section-head">
              <div>
                <h2>Agent 工作区</h2>
                <p>产物写入由后端工作区托管；计划、工具调用和 final 输出仍受审批链控制。</p>
              </div>
              <AiTag :tone="store.workspaceSettings?.allowsUserDefinedPath ? 'warning' : 'success'">
                {{ store.workspaceSettings?.allowsUserDefinedPath ? '开放路径' : '固定受控目录' }}
              </AiTag>
            </div>
            <p class="workspace-root">{{ store.workspaceSettings?.rootPath || '未加载' }}</p>
            <div class="tag-list">
              <AiTag v-for="folder in workspaceFolders" :key="folder" tone="neutral">{{ folder }}/</AiTag>
            </div>
          </AiCard>

          <AiCard class="agent-section">
            <div class="section-head">
              <div>
                <h2>运行队列</h2>
                <p>队列、租约、失败和 worker 状态均来自后端，不在前端推断。</p>
              </div>
              <AiTag :tone="store.runQueueSummary?.staleLeasedCount ? 'warning' : 'success'">
                {{ store.runQueueSummary?.staleLeasedCount ? '存在陈旧租约' : '队列正常' }}
              </AiTag>
            </div>
            <div class="agent-grid">
              <div><span>Queued</span><strong>{{ store.runQueueSummary?.queuedCount ?? '-' }}</strong></div>
              <div><span>Leased</span><strong>{{ store.runQueueSummary?.leasedCount ?? '-' }}</strong></div>
              <div><span>Failed</span><strong>{{ store.runQueueSummary?.failedCount ?? '-' }}</strong></div>
              <div><span>DeadLetter</span><strong>{{ store.runQueueSummary?.deadLetterCount ?? '-' }}</strong></div>
              <div><span>Stale Lease</span><strong>{{ store.runQueueSummary?.staleLeasedCount ?? '-' }}</strong></div>
              <div><span>Worker</span><strong>{{ store.runQueueSummary?.activeWorkerCount ?? '-' }}</strong></div>
            </div>
            <div v-if="runQueueItems.length" class="ops-table">
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

          <AiCard class="agent-section">
            <div class="section-head">
              <div>
                <h2>Worker 状态</h2>
                <p>用于确认 DataWorker 和 HttpApi 的工作区配置是否一致。</p>
              </div>
              <AiTag :tone="store.workerStatus?.workspaceConsistent ? 'success' : 'danger'">
                {{ store.workerStatus?.statusCode || '未加载' }}
              </AiTag>
            </div>
            <div class="agent-grid">
              <div><span>Active Workers</span><strong>{{ store.workerStatus?.activeWorkerCount ?? '-' }}</strong></div>
              <div><span>Queued</span><strong>{{ store.workerStatus?.queuedCount ?? '-' }}</strong></div>
              <div><span>Leased</span><strong>{{ store.workerStatus?.leasedCount ?? '-' }}</strong></div>
              <div><span>Workspace</span><strong>{{ store.workerStatus?.workspaceConsistent ? '一致' : '不一致' }}</strong></div>
            </div>
            <div v-if="workerRows.length" class="ops-table">
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
  min-height: 108px;
}

.metric span,
.agent-grid span,
.workspace-root,
.ops-row span,
.ops-row small,
.panel-empty {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.metric strong {
  display: block;
  margin-top: 12px;
  color: var(--ai-text);
  font-size: 26px;
  font-weight: 950;
  overflow-wrap: anywhere;
}

.error-banner {
  border: 1px solid #fecaca;
  border-radius: 14px;
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

.agent-section h2 {
  margin: 0;
  color: var(--ai-text);
  font-size: 20px;
  font-weight: 950;
}

.agent-section p {
  margin: 8px 0 0;
  color: var(--ai-text-muted);
  font-weight: 650;
  line-height: 1.7;
}

.section-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
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

.agent-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 10px;
  margin-top: 14px;
}

.agent-grid div {
  display: grid;
  gap: 5px;
  border: 1px solid var(--ai-border);
  border-radius: 8px;
  padding: 12px;
  background: var(--ai-surface-soft);
}

.agent-grid strong {
  color: var(--ai-text);
  font-size: 20px;
  font-weight: 950;
  overflow-wrap: anywhere;
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
  border-radius: 8px;
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

.panel-empty {
  margin-top: 12px;
}

@media (max-width: 1200px) {
  .metric-strip {
    grid-template-columns: repeat(3, minmax(160px, 1fr));
  }
}

@media (max-width: 760px) {
  .metric-strip {
    grid-template-columns: 1fr;
  }

  .section-head,
  .ops-row {
    grid-template-columns: 1fr;
  }
}
</style>
