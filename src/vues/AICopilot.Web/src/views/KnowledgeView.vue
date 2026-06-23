<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Plus, RefreshCw, Search, Settings2 } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
import AppShell from '@/components/layout/AppShell.vue'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'
import DocumentGovernanceDrawer from '@/views/knowledge/DocumentGovernanceDrawer.vue'
import EmbeddingModelConfig from '@/views/knowledge/EmbeddingModelConfig.vue'
import KnowledgeBaseManagement from '@/views/knowledge/KnowledgeBaseManagement.vue'
import KnowledgeSearchPanel from '@/views/knowledge/KnowledgeSearchPanel.vue'

const store = useRagStore()
const authStore = useAuthStore()
const showSearch = ref(false)
const showAdvanced = ref(false)
const canCreateKnowledgeBase = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.create)
)

onMounted(() => {
  void store.refresh()
})
</script>

<template>
  <AppShell>
    <AiDataPage eyebrow="知识管理" title="知识库" description="上传知识文档，查看解析状态，并按需测试检索。">
      <template #actions>
        <div class="page-actions">
          <AiButton :disabled="store.isLoading" @click="store.refresh()">
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.isLoading }" />
            刷新
          </AiButton>
          <AiButton :class="showSearch ? 'active' : ''" @click="showSearch = !showSearch">
            <Search class="h-4 w-4" />
            测试检索
          </AiButton>
          <AiButton :class="showAdvanced ? 'active' : ''" @click="showAdvanced = !showAdvanced">
            <Settings2 class="h-4 w-4" />
            高级设置
          </AiButton>
          <AiButton v-if="canCreateKnowledgeBase" variant="primary" @click="store.openCreateKnowledgeBaseDialog()">
            <Plus class="h-4 w-4" />
            新增知识库
          </AiButton>
        </div>
      </template>

      <div v-if="store.errorMessage" class="error-note">{{ store.errorMessage }}</div>

      <div class="knowledge-stack">
        <KnowledgeBaseManagement />
        <KnowledgeSearchPanel v-if="showSearch" />
        <EmbeddingModelConfig v-if="showAdvanced" />
      </div>
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

.page-actions :deep(.active) {
  border-color: #d8ff78;
  background: #efffbe;
}

.knowledge-stack {
  display: grid;
  gap: 14px;
}
</style>
