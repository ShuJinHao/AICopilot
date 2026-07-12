<script setup lang="ts">
import { computed } from 'vue'
import { Bot, UserRound } from 'lucide-vue-next'
import { ChunkType, MessageRole, type ChatChunk } from '@/types/protocols'
import { useChatStore } from '@/stores/chatStore'
import ApprovalCard from './ApprovalCard.vue'
import ChatRunStatusStrip from './ChatRunStatusStrip.vue'
import MessageTextBlock from './MessageTextBlock.vue'
import MessageRuntimeDetailsPanel from './MessageRuntimeDetailsPanel.vue'
import WidgetRenderer from '../widgets/WidgetRenderer.vue'
import type { ApprovalChunk, ChatMessage, WidgetChunk } from '@/types/models'

const props = defineProps<{
  message: ChatMessage
}>()

const store = useChatStore()
const isUser = computed(() => props.message.role === MessageRole.User)
const runStatus = computed(() => isUser.value ? null : store.getRunStatusForMessage(props.message))
const chunks = computed(() => props.message.chunks)
const visibleChunks = computed(() =>
  chunks.value.filter((chunk) =>
    chunk.type === ChunkType.Text ||
    chunk.type === ChunkType.Widget ||
    chunk.type === ChunkType.ApprovalRequest
  )
)

function asWidget(chunk: ChatChunk) {
  return chunk as WidgetChunk
}

function asApproval(chunk: ChatChunk) {
  return chunk as ApprovalChunk
}

async function approve(payload: { callId: string; onsiteConfirmed: boolean }, chunk: ApprovalChunk) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) return
  await store.submitApproval(payload.callId, 'approved', payload.onsiteConfirmed, chunk)
}

async function reject(payload: { callId: string }, chunk: ApprovalChunk) {
  if (!store.resolvedSessionId || store.isSessionTransitionBlocked) return
  await store.submitApproval(payload.callId, 'rejected', false, chunk)
}
</script>

<template>
  <article class="message" :class="{ user: isUser }">
    <div class="avatar">
      <UserRound v-if="isUser" class="h-4 w-4" />
      <Bot v-else class="h-4 w-4" />
    </div>

    <div class="message-body">
      <div class="message-meta">
        <strong>{{ isUser ? '你' : 'AICopilot' }}</strong>
        <span>{{ new Date(message.timestamp).toLocaleTimeString('zh-CN', { hour12: false }) }}</span>
      </div>

      <ChatRunStatusStrip v-if="runStatus" :status="runStatus" />

      <div class="chunk-list">
        <template v-for="(chunk, index) in visibleChunks" :key="`${chunk.source}-${chunk.type}-${index}`">
          <MessageTextBlock v-if="chunk.type === ChunkType.Text" :content="chunk.content" />

          <WidgetRenderer v-else-if="chunk.type === ChunkType.Widget" :data="asWidget(chunk).widget" />

          <ApprovalCard
            v-else-if="chunk.type === ChunkType.ApprovalRequest"
            :chunk="asApproval(chunk)"
            :is-submitting="store.isSessionTransitionBlocked || !store.resolvedSessionId"
            @approve="(payload) => approve(payload, asApproval(chunk))"
            @reject="(payload) => reject(payload, asApproval(chunk))"
          />
        </template>

        <span v-if="message.isStreaming" class="streaming-caret" />
      </div>

      <MessageRuntimeDetailsPanel v-if="!isUser" :message="message" :status="runStatus" />
    </div>
  </article>
</template>

<style scoped>
.message {
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr);
  gap: 12px;
  align-items: start;
}

.message.user {
  grid-template-columns: minmax(0, 1fr) 38px;
}

.message.user .avatar {
  grid-column: 2;
  grid-row: 1;
  border-color: rgba(200, 255, 61, 0.38);
  background: #efffbe;
  color: var(--ai-graphite);
}

.message.user .message-body {
  grid-column: 1;
  grid-row: 1;
  justify-self: end;
  border-color: rgba(200, 255, 61, 0.32);
  border-style: solid;
  border-width: 1px;
  border-radius: 18px;
  padding: 12px 14px;
  background: rgba(239, 255, 190, 0.92);
  box-shadow: var(--ai-shadow-xs);
}

.avatar {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 15px;
  background: var(--ai-graphite);
  color: #f8fafc;
  box-shadow: 0 10px 24px rgba(63, 111, 115, 0.18);
}

.message-body {
  display: grid;
  gap: 12px;
  min-width: 0;
  max-width: min(100%, 940px);
  border: 0;
  border-radius: 0;
  padding: 2px 0;
  background: transparent;
  box-shadow: none;
}

.message-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.message-meta strong {
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 900;
}

.chunk-list {
  display: grid;
  gap: 12px;
  min-width: 0;
}

.streaming-caret {
  width: 8px;
  height: 18px;
  border-radius: 2px;
  background: #c8ff3d;
  animation: blink 1s infinite;
}

@keyframes blink {
  50% {
    opacity: 0.2;
  }
}
</style>
