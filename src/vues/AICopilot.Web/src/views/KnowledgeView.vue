<script setup lang="ts">
import { onMounted } from 'vue'
import { Plus, Refresh } from '@element-plus/icons-vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useRagStore } from '@/stores/ragStore'
import DocumentGovernanceDrawer from '@/views/knowledge/DocumentGovernanceDrawer.vue'
import EmbeddingModelConfig from '@/views/knowledge/EmbeddingModelConfig.vue'
import KnowledgeBaseManagement from '@/views/knowledge/KnowledgeBaseManagement.vue'
import KnowledgeSearchPanel from '@/views/knowledge/KnowledgeSearchPanel.vue'

const store = useRagStore()

onMounted(() => {
  void store.refresh()
})
</script>

<template>
  <AppShell>
    <div class="page knowledge-page">
      <header class="page-header">
        <div>
          <p class="page-kicker">知识治理</p>
          <h1 class="page-title">知识库</h1>
          <p class="page-description">管理向量模型、知识库、文档解析状态和检索预览。</p>
        </div>
        <div class="toolbar">
          <el-button :icon="Refresh" :loading="store.isLoading" @click="store.refresh()">刷新</el-button>
          <el-button type="primary" :icon="Plus" @click="store.openCreateKnowledgeBaseDialog()">新增知识库</el-button>
        </div>
      </header>

      <div class="metric-strip">
        <div class="metric">
          <span class="metric-label">嵌入模型</span>
          <strong class="metric-value">{{ store.embeddingModels.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">知识库</span>
          <strong class="metric-value">{{ store.knowledgeBases.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">当前文档</span>
          <strong class="metric-value">{{ store.documents.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">检索结果</span>
          <strong class="metric-value">{{ store.searchResults.length }}</strong>
        </div>
      </div>

      <el-alert v-if="store.errorMessage" type="error" show-icon :closable="false" :title="store.errorMessage" />

      <div class="knowledge-grid">
        <KnowledgeBaseManagement />
      </div>

      <KnowledgeSearchPanel />
      <EmbeddingModelConfig />
      <DocumentGovernanceDrawer />
    </div>
  </AppShell>
</template>

<style scoped>
.knowledge-page {
  display: grid;
  align-content: start;
  gap: 14px;
  height: 100%;
  overflow: auto;
}

.knowledge-grid {
  display: grid;
  grid-template-columns: 330px minmax(0, 1fr);
  gap: 14px;
}

@media (max-width: 1080px) {
  .knowledge-grid {
    grid-template-columns: 1fr;
  }
}
</style>
