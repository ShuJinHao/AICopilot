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
  border-radius: 8px;
  font-size: 14px;
  line-height: 1.6;
  position: relative;
  word-break: break-word;
}

.bubble-user {
  background-color: #95ec69;
  color: #000;
}

.bubble-ai {
  background-color: #fff;
  border: 1px solid #e4e7ed;
  color: #333;
}

.cursor-blink {
  display: inline-block;
  width: 2px;
  height: 14px;
  background: #333;
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
