<script setup lang="ts">
import { onMounted } from 'vue'
import { Plus, RefreshCw } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
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
    <AiDataPage eyebrow="Knowledge Governance" title="知识库" description="管理向量模型、知识库、文档解析状态和检索预览。">
      <template #actions>
        <div class="page-actions">
          <AiButton :disabled="store.isLoading" @click="store.refresh()">
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isLoading }" />
            刷新
          </AiButton>
          <AiButton variant="primary" @click="store.openCreateKnowledgeBaseDialog()">
            <Plus class="h-4 w-4" />
            新增知识库
          </AiButton>
        </div>
      </template>

      <div class="metric-strip">
        <AiCard class="metric" tone="violet"><span>嵌入模型</span><strong>{{ store.embeddingModels.length }}</strong></AiCard>
        <AiCard class="metric" tone="blue"><span>知识库</span><strong>{{ store.knowledgeBases.length }}</strong></AiCard>
        <AiCard class="metric" tone="lime"><span>当前文档</span><strong>{{ store.documents.length }}</strong></AiCard>
        <AiCard class="metric" tone="teal"><span>检索结果</span><strong>{{ store.searchResults.length }}</strong></AiCard>
      </div>

      <div v-if="store.errorMessage" class="error-note">{{ store.errorMessage }}</div>

      <KnowledgeBaseManagement />
      <KnowledgeSearchPanel />
      <EmbeddingModelConfig />
      <DocumentGovernanceDrawer />
    </AiDataPage>
  </AppShell>
</template>

<style scoped>
@import './config/shared-config.css';

.page-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.metric-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(150px, 1fr));
  gap: 12px;
}

.metric span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.metric strong {
  display: block;
  margin-top: 12px;
  color: var(--ai-text);
  font-size: 30px;
  font-weight: 950;
}

@media (max-width: 980px) {
  .metric-strip {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
