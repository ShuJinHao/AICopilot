<script setup lang="ts">
import { computed } from 'vue'
import { MessageSquare, Plus } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import { useChatStore } from '@/stores/chatStore'

const store = useChatStore()
const sessions = computed(() => store.sessions)

async function selectSession(id: string) {
  await store.selectSession(id)
}

async function createSession() {
  await store.createNewSession()
}
</script>

<template>
  <aside class="session-panel">
    <header>
      <div>
        <h2>会话</h2>
        <span>{{ sessions.length }} 个上下文</span>
      </div>
      <AiButton variant="lime" size="icon" aria-label="新建会话" @click="createSession">
        <Plus class="h-4 w-4" />
      </AiButton>
    </header>

    <div class="session-list">
      <button
        v-for="session in sessions"
        :key="session.id"
        class="session-item"
        :class="{ active: store.currentSessionId === session.id }"
        type="button"
        @click="selectSession(session.id)"
      >
        <MessageSquare class="h-4 w-4" />
        <span>{{ session.title || '未命名会话' }}</span>
      </button>

      <div v-if="sessions.length === 0" class="empty-state">暂无历史会话</div>
    </div>
  </aside>
</template>

<style scoped>
.session-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  border-right: 1px solid var(--ai-border);
  background: rgba(255, 255, 255, 0.68);
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 16px;
  border-bottom: 1px solid var(--ai-border);
}

h2 {
  margin: 0;
  color: var(--ai-text);
  font-size: 15px;
  font-weight: 900;
}

header span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.session-list {
  display: grid;
  align-content: start;
  gap: 8px;
  min-height: 0;
  overflow-y: auto;
  padding: 12px;
}

.session-item {
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr);
  gap: 10px;
  align-items: center;
  border: 1px solid transparent;
  border-radius: 16px;
  padding: 11px 12px;
  background: transparent;
  color: var(--ai-text-muted);
  cursor: pointer;
  text-align: left;
  transition:
    background-color 0.2s ease,
    border-color 0.2s ease,
    box-shadow 0.2s ease,
    color 0.2s ease;
}

.session-item:hover,
.session-item.active {
  border-color: var(--ai-border);
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
  color: var(--ai-text);
}

.session-item.active {
  border-color: #d8ff78;
  background: #efffbe;
  font-weight: 850;
}

.session-item span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-state {
  border: 1px dashed var(--ai-border);
  border-radius: 18px;
  padding: 16px;
  color: var(--ai-text-muted);
  text-align: center;
  font-size: 13px;
  font-weight: 700;
}
</style>
