<script setup lang="ts">
import { computed } from 'vue'
import { Cpu, UserFilled } from '@element-plus/icons-vue'
import { renderMarkdown } from '@/utils/markdown'
import { ChunkType, MessageRole, type ChatChunk } from '@/types/protocols'
import { useChatStore } from '@/stores/chatStore'
import ApprovalCard from './ApprovalCard.vue'
import FunctionCallItem from './FunctionCallItem.vue'
import WidgetRenderer from '../widgets/WidgetRenderer.vue'
import type {
  ApprovalChunk,
  ChatMessage,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'

const userLabel = '\u4f60'
const unknownModelLabel = '\u672a\u77e5'
const responseModelLabel = '\u56de\u7b54\u6a21\u578b\uff1a'
const routingModelLabel = '\u8def\u7531\u6a21\u578b\uff1a'
const intentSeparator = '\u00b7'

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

  const badges: Array<{ key: string; type: 'success' | 'info'; text: string }> = [
    {
      key: 'final-model',
      type: 'success' as const,
      text: `${responseModelLabel}${props.message.finalModelName?.trim() || unknownModelLabel}`
    }
  ]

  if (props.message.routingModelName?.trim()) {
    badges.push({
      key: 'routing-model',
      type: 'info' as const,
      text: `${routingModelLabel}${props.message.routingModelName.trim()}`
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
      <el-icon>
        <UserFilled v-if="isUser" />
        <Cpu v-else />
      </el-icon>
    </div>

    <div class="message-body">
      <div class="message-meta">
        <strong>{{ isUser ? userLabel : 'AICopilot' }}</strong>
        <span>{{ new Date(message.timestamp).toLocaleTimeString('zh-CN', { hour12: false }) }}</span>
      </div>

      <section v-if="modelBadges.length > 0" class="model-strip">
        <el-tag
          v-for="badge in modelBadges"
          :key="badge.key"
          size="small"
          :type="badge.type"
          effect="light"
        >
          {{ badge.text }}
        </el-tag>
      </section>

      <section v-if="intentChunks.length > 0" class="intent-strip">
        <el-tag
          v-for="intent in intentChunks.flatMap((chunk) => chunk.intents)"
          :key="`${intent.intent}-${intent.confidence}`"
          size="small"
          type="info"
        >
          {{ intent.intent }} {{ intentSeparator }} {{ Math.round(intent.confidence * 100) }}%
        </el-tag>
      </section>

      <div class="chunk-list">
        <template v-for="(chunk, index) in chunks" :key="`${chunk.source}-${chunk.type}-${index}`">
          <div
            v-if="chunk.type === ChunkType.Text"
            class="text-block markdown-body"
            v-html="renderMarkdown(chunk.content)"
          />

          <FunctionCallItem
            v-else-if="chunk.type === ChunkType.FunctionCall"
            :call="asFunctionCall(chunk).functionCall"
          />

          <WidgetRenderer
            v-else-if="chunk.type === ChunkType.Widget"
            :data="asWidget(chunk).widget"
          />

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
  grid-template-columns: 34px minmax(0, 1fr);
  gap: 10px;
  align-items: start;
}

.message.user {
  grid-template-columns: minmax(0, 1fr) 34px;
}

.message.user .avatar {
  grid-column: 2;
  grid-row: 1;
  background: var(--app-surface-muted);
  color: var(--app-text);
  border: 1px solid var(--app-border);
}

.message.user .message-body {
  grid-column: 1;
  grid-row: 1;
  justify-self: end;
  background: rgba(15, 118, 110, 0.06);
  border-color: rgba(15, 118, 110, 0.15);
}

html.dark .message.user .message-body {
  background: rgba(20, 184, 166, 0.08);
  border-color: rgba(20, 184, 166, 0.2);
}

.avatar {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: var(--radius-md);
  background: var(--app-primary);
  color: #ffffff;
  box-shadow: var(--shadow-sm);
}

.message-body {
  display: grid;
  gap: 10px;
  min-width: 0;
  max-width: min(100%, 900px);
  border: 1px solid var(--app-border);
  border-radius: var(--radius-md);
  padding: 12px;
  background: var(--app-surface);
  box-shadow: var(--shadow-sm);
}

.message-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  color: var(--app-text-muted);
  font-size: 12px;
}

.message-meta strong {
  color: var(--app-text);
  font-size: 13px;
}

.model-strip,
.intent-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.chunk-list {
  display: grid;
  gap: 10px;
  min-width: 0;
}

.text-block {
  color: var(--app-text);
  overflow-wrap: anywhere;
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
  background: var(--app-primary);
  animation: blink 1s infinite;
}

@keyframes blink {
  50% {
    opacity: 0.2;
  }
}
</style>
