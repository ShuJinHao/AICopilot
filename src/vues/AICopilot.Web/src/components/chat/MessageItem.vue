<script setup lang="ts">
import { computed } from 'vue'
import { Bot, UserRound } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import { renderMarkdown } from '@/utils/markdown'
import { ChunkType, MessageRole, type ChatChunk } from '@/types/protocols'
import { useChatStore } from '@/stores/chatStore'
import ApprovalCard from './ApprovalCard.vue'
import FunctionCallItem from './FunctionCallItem.vue'
import WidgetRenderer from '../widgets/WidgetRenderer.vue'
import type { ApprovalChunk, ChatMessage, FunctionCallChunk, IntentChunk, WidgetChunk } from '@/types/models'

const props = defineProps<{
  message: ChatMessage
}>()

const store = useChatStore()
const isUser = computed(() => props.message.role === MessageRole.User)
const chunks = computed(() => props.message.chunks)
const intentChunks = computed(() => chunks.value.filter((chunk) => chunk.type === ChunkType.Intent) as IntentChunk[])
const modelBadges = computed(() => {
  if (isUser.value) {
    return []
  }

  const badges: Array<{ key: string; tone: 'success' | 'blue'; text: string }> = [
    {
      key: 'final-model',
      tone: 'success',
      text: `回答模型：${props.message.finalModelName?.trim() || '未知'}`
    }
  ]

  if (props.message.routingModelName?.trim()) {
    badges.push({
      key: 'routing-model',
      tone: 'blue',
      text: `路由模型：${props.message.routingModelName.trim()}`
    })
  }

  return badges
})

function asFunctionCall(chunk: ChatChunk) {
  return chunk as FunctionCallChunk
}

function asWidget(chunk: ChatChunk) {
  return chunk as WidgetChunk
}

function asApproval(chunk: ChatChunk) {
  return chunk as ApprovalChunk
}

async function approve(payload: { callId: string; onsiteConfirmed: boolean }, chunk: ApprovalChunk) {
  await store.submitApproval(payload.callId, 'approved', payload.onsiteConfirmed, chunk)
}

async function reject(payload: { callId: string }, chunk: ApprovalChunk) {
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

      <section v-if="modelBadges.length > 0" class="model-strip">
        <AiTag v-for="badge in modelBadges" :key="badge.key" :tone="badge.tone">
          {{ badge.text }}
        </AiTag>
      </section>

      <section v-if="intentChunks.length > 0" class="intent-strip">
        <AiTag v-for="intent in intentChunks.flatMap((chunk) => chunk.intents)" :key="`${intent.intent}-${intent.confidence}`" tone="neutral">
          {{ intent.intent }} · {{ Math.round(intent.confidence * 100) }}%
        </AiTag>
      </section>

      <div class="chunk-list">
        <template v-for="(chunk, index) in chunks" :key="`${chunk.source}-${chunk.type}-${index}`">
          <div v-if="chunk.type === ChunkType.Text" class="text-block markdown-body" v-html="renderMarkdown(chunk.content)" />

          <FunctionCallItem v-else-if="chunk.type === ChunkType.FunctionCall" :call="asFunctionCall(chunk).functionCall" />

          <WidgetRenderer v-else-if="chunk.type === ChunkType.Widget" :data="asWidget(chunk).widget" />

          <ApprovalCard
            v-else-if="chunk.type === ChunkType.ApprovalRequest"
            :chunk="asApproval(chunk)"
            :is-submitting="store.isStreaming"
            @approve="(payload) => approve(payload, asApproval(chunk))"
            @reject="(payload) => reject(payload, asApproval(chunk))"
          />
        </template>

        <span v-if="message.isStreaming" class="streaming-caret" />
      </div>
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
  background: rgba(239, 255, 190, 0.92);
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
  border: 1px solid var(--ai-border);
  border-radius: 22px;
  padding: 14px;
  background: rgba(255, 255, 255, 0.94);
  box-shadow: var(--ai-shadow-xs);
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

.model-strip,
.intent-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.chunk-list {
  display: grid;
  gap: 12px;
  min-width: 0;
}

.text-block {
  color: var(--ai-text);
  overflow-wrap: anywhere;
  font-size: 14px;
  line-height: 1.7;
}

:deep(.markdown-body p) {
  margin: 0 0 8px;
}

:deep(.markdown-body p:last-child) {
  margin-bottom: 0;
}

:deep(.markdown-body ul),
:deep(.markdown-body ol) {
  margin: 8px 0;
  padding-left: 20px;
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
