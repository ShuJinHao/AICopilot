<script setup lang="ts">
import { computed } from 'vue'
import { ChatLineRound, Plus } from '@element-plus/icons-vue'
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
      <el-button type="primary" :icon="Plus" @click="createSession" />
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
        <el-icon><ChatLineRound /></el-icon>
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
  border-right: 1px solid var(--app-border);
  background: var(--app-surface-muted);
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 14px;
  border-bottom: 1px solid var(--app-border);
}

h2 {
  margin: 0;
  font-size: 15px;
  font-weight: 800;
}

header span {
  color: var(--app-text-muted);
  font-size: 12px;
}

.session-list {
  display: grid;
  align-content: start;
  gap: 6px;
  min-height: 0;
  overflow-y: auto;
  padding: 10px;
}

.session-item {
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  border: 1px solid transparent;
  border-radius: 8px;
  padding: 9px 10px;
  background: transparent;
  color: var(--app-text);
  cursor: pointer;
  text-align: left;
}

.session-item:hover,
.session-item.active {
  border-color: var(--app-border);
  background: #ffffff;
}

.session-item.active {
  color: var(--app-primary-strong);
  font-weight: 750;
}

.session-item span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.empty-state {
  border: 1px dashed var(--app-border);
  border-radius: 8px;
  padding: 14px;
  color: var(--app-text-muted);
  text-align: center;
}
</style>
