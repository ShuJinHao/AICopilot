<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
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

const acceptanceSummary = {
  generatedAt: '2026-05-14',
  reportPath: '资料/acceptance-closure-latest.md',
  status: '全部通过'
}

const workspaceFolders = computed(() => store.workspaceSettings?.folders ?? [])
const allowedArtifactTypes = computed(() => store.workspaceSettings?.allowedArtifactTypes ?? [])
const activeRoutingModel = computed(() => store.routingModels.find((item) => item.isActive)?.name || '未激活')
const workspacePathPolicy = computed(() =>
  store.workspaceSettings?.allowsUserDefinedPath ? '允许用户指定路径' : '固定受控目录'
)

onMounted(() => {
  void store.refresh()
})
</script>

<template>
  <AppShell>
    <div class="page config-page">
      <header class="page-header">
        <div>
          <p class="page-kicker">运行时配置</p>
          <h1 class="page-title">运行配置</h1>
          <p class="page-description">集中管理模型、模板、MCP 工具、只读业务数据源、审批策略和 Agent 工作区边界。</p>
        </div>
        <el-button :icon="Refresh" :loading="store.isLoading" @click="store.refresh()">刷新</el-button>
      </header>

      <div class="metric-strip">
        <div class="metric">
          <span class="metric-label">语言模型</span>
          <strong class="metric-value">{{ store.languageModels.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">会话模板</span>
          <strong class="metric-value">{{ store.conversationTemplates.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">路由模型</span>
          <strong class="metric-value">{{ activeRoutingModel }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">只读数据源</span>
          <strong class="metric-value">{{ store.businessDatabases.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">MCP 服务</span>
          <strong class="metric-value">{{ store.mcpServers.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">服务商回退</span>
          <strong class="metric-value">{{ store.providerReliability?.fallbackEnabled ? '启用' : '关闭' }}</strong>
        </div>
      </div>

      <el-alert v-if="store.errorMessage" type="error" show-icon :closable="false" :title="store.errorMessage" />

      <el-tabs v-model="activeTab" class="config-tabs">
        <el-tab-pane label="模型" name="models">
          <LanguageModelConfig />
        </el-tab-pane>
        <el-tab-pane label="可靠性" name="reliability">
          <ProviderReliabilityConfig />
        </el-tab-pane>
        <el-tab-pane label="路由" name="routing">
          <RoutingModelConfig />
        </el-tab-pane>
        <el-tab-pane label="模板" name="templates">
          <ConversationTemplateConfig />
        </el-tab-pane>
        <el-tab-pane label="数据分析" name="data">
          <BusinessDatabaseConfig />
        </el-tab-pane>
        <el-tab-pane label="MCP" name="mcp">
          <McpServerConfig />
        </el-tab-pane>
        <el-tab-pane label="审批" name="approval">
          <ApprovalPolicyConfig />
        </el-tab-pane>
        <el-tab-pane label="Agent" name="agent">
          <div class="agent-config">
            <section class="agent-hero">
              <div>
                <p class="page-kicker">Agent / Workspace</p>
                <h2>受控产物运行边界</h2>
                <p>这里展示 Agent 工作区、运行历史、审批策略和交付验收状态。服务器路径仍由应用托管，不允许用户输入任意写入目录。</p>
              </div>
              <div class="acceptance-card">
                <span>最近验收</span>
                <strong>{{ acceptanceSummary.status }}</strong>
                <small>{{ acceptanceSummary.generatedAt }} · {{ acceptanceSummary.reportPath }}</small>
              </div>
            </section>

            <section class="agent-config-section">
              <div class="section-head">
                <div>
                  <h2>运行与记忆</h2>
                  <p>聊天、RAG 和 Agent 规划统一读取这些运行参数。</p>
                </div>
              </div>
              <div class="agent-config-grid">
                <div>
                  <span>路由历史</span>
                  <strong>{{ store.runtimeSettings?.routingHistoryCount ?? '-' }}</strong>
                </div>
                <div>
                  <span>回答历史</span>
                  <strong>{{ store.runtimeSettings?.answerHistoryCount ?? '-' }}</strong>
                </div>
                <div>
                  <span>RAG 改写历史</span>
                  <strong>{{ store.runtimeSettings?.ragRewriteHistoryCount ?? '-' }}</strong>
                </div>
                <div>
                  <span>Agent 规划历史</span>
                  <strong>{{ store.runtimeSettings?.agentPlanningHistoryCount ?? '-' }}</strong>
                </div>
                <div>
                  <span>摘要阈值</span>
                  <strong>{{ store.runtimeSettings?.summaryThresholdMessages ?? '-' }}</strong>
                </div>
                <div>
                  <span>上下文上限</span>
                  <strong>{{ store.runtimeSettings?.contextTokenLimit ?? '-' }}</strong>
                </div>
              </div>
            </section>

            <section class="agent-config-section">
              <div class="section-head">
                <div>
                  <h2>工作区</h2>
                  <p>产物只能写入应用管理的固定目录，正式输出必须经过 finalize。</p>
                </div>
                <el-tag :type="store.workspaceSettings?.allowsUserDefinedPath ? 'warning' : 'success'">
                  {{ workspacePathPolicy }}
                </el-tag>
              </div>
              <p class="workspace-root">{{ store.workspaceSettings?.rootPath || '未加载' }}</p>
              <div class="tag-list">
                <el-tag v-for="folder in workspaceFolders" :key="folder" type="info">
                  {{ folder }}/
                </el-tag>
              </div>
              <div class="tag-list">
                <el-tag v-for="type in allowedArtifactTypes" :key="type">
                  {{ type }}
                </el-tag>
              </div>
              <el-alert
                :title="store.workspaceSettings?.allowsUserDefinedPath ? '当前策略存在路径开放风险' : '任意服务器路径写入保持关闭'"
                :type="store.workspaceSettings?.allowsUserDefinedPath ? 'warning' : 'success'"
                show-icon
                :closable="false"
              />
            </section>

            <section class="agent-config-section">
              <div class="section-head">
                <div>
                  <h2>审批与交付门禁</h2>
                  <p>计划确认、风险工具、产物确认和正式输出仍统一落到审批与审计链路。</p>
                </div>
              </div>
              <div class="guardrail-grid">
                <div>
                  <span>审批策略入口</span>
                  <strong>审批 Tab</strong>
                </div>
                <div>
                  <span>产物下载</span>
                  <strong>所有权校验</strong>
                </div>
                <div>
                  <span>Cloud 写入</span>
                  <strong>禁止</strong>
                </div>
                <div>
                  <span>Shell / 任意路径</span>
                  <strong>未开放</strong>
                </div>
              </div>
            </section>
          </div>
        </el-tab-pane>
      </el-tabs>
    </div>
  </AppShell>
</template>

<style scoped>
.config-page {
  display: grid;
  align-content: start;
  gap: 14px;
  height: 100%;
  overflow: auto;
}

.config-tabs {
  min-width: 0;
}

:deep(.el-tabs__content) {
  overflow: visible;
}

.agent-config {
  display: grid;
  gap: 16px;
}

.agent-hero,
.agent-config-section {
  display: grid;
  gap: 14px;
  border: 1px solid var(--app-border);
  border-radius: 22px;
  padding: 20px;
  background: #ffffff;
  box-shadow: 0 1px 3px rgba(15, 23, 42, 0.04), 0 8px 24px rgba(15, 23, 42, 0.05);
}

.agent-hero {
  grid-template-columns: minmax(0, 1fr) 260px;
  align-items: stretch;
  background: #f8fafc;
}

.agent-hero h2,
.agent-config-section h2 {
  margin: 0;
  font-size: 18px;
}

.agent-hero p,
.section-head p {
  margin: 6px 0 0;
  color: var(--app-text-muted);
}

.acceptance-card {
  display: grid;
  gap: 6px;
  border: 1px solid var(--app-border);
  border-radius: 18px;
  padding: 16px;
  background: #ffffff;
}

.acceptance-card span,
.acceptance-card small,
.agent-config-grid span,
.guardrail-grid span,
.workspace-root {
  color: var(--app-text-muted);
}

.acceptance-card strong {
  font-size: 28px;
  font-variant-numeric: tabular-nums;
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
}

.agent-config-grid div,
.guardrail-grid div {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: 14px;
  padding: 12px;
  background: #f8fafc;
}

.agent-config-grid strong,
.guardrail-grid strong {
  font-size: 20px;
  font-variant-numeric: tabular-nums;
}

.workspace-root {
  margin: 0;
  overflow-wrap: anywhere;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

@media (max-width: 980px) {
  .agent-hero {
    grid-template-columns: 1fr;
  }
}
</style>
