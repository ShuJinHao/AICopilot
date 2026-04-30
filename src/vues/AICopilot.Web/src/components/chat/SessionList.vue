<script setup lang="ts">
import { computed } from 'vue'
import { ChatDotRound, Plus } from '@element-plus/icons-vue'
import { useChatStore } from '@/stores/chatStore'

const store = useChatStore()

const sessions = computed(() => store.sessions)
const currentSessionId = computed(() => store.currentSessionId)

async function handleSelect(id: string) {
  await store.selectSession(id)
}

async function handleNewChat() {
  await store.createNewSession()
}
</script>

<template>
  <div class="session-sidebar">
    <div class="sidebar-header">
      <el-button
        type="primary"
        class="new-chat-btn"
        :icon="Plus"
        @click="handleNewChat"
      >
        新建会话
      </el-button>
    </div>

    <div class="session-list">
      <div
        v-for="session in sessions"
        :key="session.id"
        class="session-item"
        :class="{ active: currentSessionId === session.id }"
        @click="handleSelect(session.id)"
      >
        <el-icon class="icon"><ChatDotRound /></el-icon>
        <span class="title">{{ session.title }}</span>
      </div>

      <div v-if="sessions.length === 0" class="empty-tip">
        暂无历史会话
      </div>
    </div>
  </div>
</template>

<style scoped>
.session-sidebar {
  display: flex;
  flex-direction: column;
  height: 100%;
  background-color: var(--bg-color-secondary);
  border-right: 1px solid var(--border-color);
}

.sidebar-header {
  padding: 20px;
  flex-shrink: 0;
}

.new-chat-btn {
  width: 100%;
  border-radius: 8px;
}

.session-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 0 12px 12px;
}

.session-item {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  margin-bottom: 8px;
  border-radius: 8px;
  cursor: pointer;
  color: var(--text-primary);
  transition: all 0.2s ease;
  font-size: 14px;
}

.session-item:hover {
  background-color: rgba(0, 0, 0, 0.05);
}

.session-item.active {
  background-color: #e6f0ff;
  color: var(--brand-color);
  font-weight: 500;
}

.icon {
  margin-right: 10px;
  font-size: 16px;
}

.title {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.empty-tip {
  text-align: center;
  color: var(--text-secondary);
  font-size: 12px;
  margin-top: 20px;
}
</style>
