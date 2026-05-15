<script setup lang="ts">
import { useChatStore } from '@/stores/chatStore'
import { renderMarkdown } from '@/utils/markdown'
import { ChunkType, type ChatChunk } from '@/types/protocols'
import type { ApprovalChunk, FunctionCallChunk } from '@/types/models'
import ApprovalCard from './ApprovalCard.vue'
import FunctionCallItem from './FunctionCallItem.vue'

const store = useChatStore()

const props = defineProps<{
  chunks: ChatChunk[]
  isUser: boolean
  isStreaming: boolean
}>()

const getFunctionCall = (chunk: ChatChunk) => chunk as FunctionCallChunk

async function onApprove(
  payload: {
    callId: string
    onsiteConfirmed: boolean
  },
  chunk: ApprovalChunk
) {
  await store.submitApproval(payload.callId, 'approved', payload.onsiteConfirmed, chunk)
}

async function onReject(payload: { callId: string }, chunk: ApprovalChunk) {
  await store.submitApproval(payload.callId, 'rejected', false, chunk)
}
</script>

<template>
  <div class="block-final message-bubble" :class="isUser ? 'bubble-user' : 'bubble-ai'">
    <template v-for="chunk in chunks" :key="`${chunk.source}-${chunk.type}-${chunk.content}`">
      <div
        v-if="chunk.type === ChunkType.Text"
        class="markdown-body inline-block-container"
        v-html="renderMarkdown(chunk.content)"
      />

      <div v-else-if="chunk.type === ChunkType.FunctionCall" class="my-1 inline-block">
        <FunctionCallItem :call="getFunctionCall(chunk).functionCall" :mini="true" />
      </div>

      <ApprovalCard
        v-else-if="chunk.type === ChunkType.ApprovalRequest"
        :chunk="chunk as ApprovalChunk"
        :is-submitting="store.isStreaming"
        @approve="(payload) => onApprove(payload, chunk as ApprovalChunk)"
        @reject="(payload) => onReject(payload, chunk as ApprovalChunk)"
      />
    </template>

    <span v-if="isStreaming" class="cursor-blink">|</span>
  </div>
</template>

<style scoped>
.message-bubble {
  padding: 10px 14px;
  border-radius: var(--radius-md);
  font-size: 14px;
  line-height: 1.6;
  position: relative;
  word-break: break-word;
}

.bubble-user {
  background-color: rgba(15, 118, 110, 0.08);
  border: 1px solid rgba(15, 118, 110, 0.2);
  color: var(--app-text);
}

html.dark .bubble-user {
  background-color: rgba(20, 184, 166, 0.1);
  border-color: rgba(20, 184, 166, 0.25);
}

.bubble-ai {
  background-color: var(--app-surface);
  border: 1px solid var(--app-border);
  color: var(--app-text);
  box-shadow: var(--shadow-sm);
}

.cursor-blink {
  display: inline-block;
  width: 2px;
  height: 14px;
  background: var(--app-text);
  animation: blink 1s infinite;
  vertical-align: middle;
  margin-left: 2px;
}

.inline-block {
  display: inline-block;
}

.inline-block-container {
  display: inline-block;
  width: 100%;
}

.my-1 {
  margin: 4px 0;
}

:deep(.markdown-body p:last-child) {
  margin-bottom: 0;
}

:deep(.markdown-body p:first-child) {
  margin-top: 0;
}

@keyframes blink {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0;
  }
}
</style>
