<script setup lang="ts">
import { computed } from 'vue'
import { Search } from '@element-plus/icons-vue'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()
const authStore = useAuthStore()
const canSearchKnowledge = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.search))
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">检索预览</h2>
        <p class="panel-subtitle">验证知识库召回片段和分数。</p>
      </div>
    </div>
    <div class="panel-body search-grid">
      <el-input v-model="store.searchQuery" placeholder="输入检索问题" clearable />
      <el-input-number v-model="store.searchTopK" :min="1" :max="20" />
      <el-input-number v-model="store.searchMinScore" :min="0" :max="1" :step="0.05" />
      <el-button
        type="primary"
        :icon="Search"
        :loading="store.loadingStates.search"
        :disabled="!canSearchKnowledge"
        @click="store.searchKnowledgeBase()"
      >
        检索
      </el-button>
    </div>
    <div class="search-results">
      <article v-for="result in store.searchResults" :key="`${result.documentId}-${result.score}`">
        <header>
          <strong>{{ result.documentName || `文档 #${result.documentId}` }}</strong>
          <el-tag type="info">{{ result.score.toFixed(3) }}</el-tag>
        </header>
        <p>{{ result.text }}</p>
      </article>
    </div>
  </section>
</template>

<style scoped>
.search-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 140px 140px 100px;
  gap: 10px;
}

.search-results {
  display: grid;
  gap: 10px;
  padding: 0 16px 16px;
}

.search-results article {
  border: 1px solid var(--app-border);
  border-radius: var(--radius-md);
  padding: 12px;
  background: var(--app-surface-muted);
  box-shadow: var(--shadow-sm);
}

.search-results header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.search-results p {
  margin: 8px 0 0;
  color: var(--app-text-muted);
}

@media (max-width: 1080px) {
  .search-grid {
    grid-template-columns: 1fr;
  }
}
</style>
