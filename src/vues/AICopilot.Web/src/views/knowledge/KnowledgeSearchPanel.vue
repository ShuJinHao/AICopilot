<script setup lang="ts">
import { computed } from 'vue'
import { Search } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiNumberInput from '@/components/ai/AiNumberInput.vue'
import AiTag from '@/components/ai/AiTag.vue'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()
const authStore = useAuthStore()
const canSearchKnowledge = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.search))
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>检索预览</h2>
        <p>验证知识库召回片段和分数。</p>
      </div>
    </div>
    <div class="search-grid">
      <AiInput v-model="store.searchQuery" placeholder="输入检索问题" />
      <AiNumberInput v-model="store.searchTopK" :min="1" :max="20" />
      <AiNumberInput v-model="store.searchMinScore" :min="0" :max="1" :step="0.05" />
      <AiButton variant="primary" :disabled="store.loadingStates.search || !canSearchKnowledge" @click="store.searchKnowledgeBase()">
        <Search class="h-4 w-4" />
        {{ store.loadingStates.search ? '检索中' : '检索' }}
      </AiButton>
    </div>
    <div class="search-results">
      <article v-for="result in store.searchResults" :key="`${result.documentId}-${result.score}`">
        <header>
          <strong>{{ result.documentName || `文档 #${result.documentId}` }}</strong>
          <AiTag tone="neutral">{{ result.score.toFixed(3) }}</AiTag>
        </header>
        <p>{{ result.text }}</p>
      </article>
    </div>
  </section>
</template>

<style scoped>
@import './shared-knowledge.css';

.search-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 140px 140px 110px;
  gap: 10px;
  border: 1px solid var(--ai-border);
  border-radius: 22px;
  background: var(--ai-surface);
  padding: 14px;
  box-shadow: var(--ai-shadow-card);
}

.search-results {
  display: grid;
  gap: 10px;
}

.search-results article {
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 14px;
  background: var(--ai-surface-soft);
}

.search-results header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.search-results p {
  margin: 8px 0 0;
  color: var(--ai-text-muted);
  font-weight: 650;
}

@media (max-width: 1080px) {
  .search-grid {
    grid-template-columns: 1fr;
  }
}
</style>
