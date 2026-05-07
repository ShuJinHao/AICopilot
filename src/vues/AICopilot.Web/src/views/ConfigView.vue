<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useConfigStore } from '@/stores/configStore'
import ApprovalPolicyConfig from '@/views/config/ApprovalPolicyConfig.vue'
import BusinessDatabaseConfig from '@/views/config/BusinessDatabaseConfig.vue'
import ConversationTemplateConfig from '@/views/config/ConversationTemplateConfig.vue'
import LanguageModelConfig from '@/views/config/LanguageModelConfig.vue'
import McpServerConfig from '@/views/config/McpServerConfig.vue'
import ProviderReliabilityConfig from '@/views/config/ProviderReliabilityConfig.vue'

const store = useConfigStore()
const activeTab = ref('models')

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
          <p class="page-description">集中管理模型、模板、MCP 工具、只读业务数据源和审批策略。</p>
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
</style>
