<script setup lang="ts">
import { Activity } from 'lucide-vue-next'
import { renderMarkdown } from '@/utils/markdown'
import FunctionCallItem from './FunctionCallItem.vue'
import { type ChatChunk, ChunkType } from '@/types/protocols'
import type { FunctionCallChunk, WidgetChunk } from '@/types/models'
import WidgetRenderer from '../widgets/WidgetRenderer.vue'

defineProps<{
  chunks: ChatChunk[]
  isStreaming: boolean
}>()

const getFunctionCall = (chunk: ChatChunk): FunctionCallChunk => chunk as FunctionCallChunk
const getWidget = (chunk: ChatChunk): WidgetChunk => chunk as WidgetChunk
</script>

<template>
  <section class="block-analysis">
    <header>
      <Activity class="h-4 w-4" />
      <span>数据分析与决策</span>
      <span v-if="isStreaming" class="typing-dot">...</span>
    </header>
    <div class="analysis-content">
      <template v-for="(chunk, index) in chunks" :key="`${chunk.type}-${index}`">
        <div v-if="chunk.type === ChunkType.Text" class="markdown-body text-analysis" v-html="renderMarkdown(chunk.content)" />
        <FunctionCallItem v-else-if="chunk.type === ChunkType.FunctionCall" :call="getFunctionCall(chunk).functionCall" />
        <WidgetRenderer v-else-if="chunk.type === ChunkType.Widget" :data="getWidget(chunk).widget" />
      </template>
    </div>
  </section>
</template>

<style scoped>
.block-analysis {
  width: 100%;
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 20px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

header {
  display: flex;
  align-items: center;
  gap: 8px;
  border-bottom: 1px solid var(--ai-border);
  background: var(--ai-surface-soft);
  padding: 10px 12px;
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 900;
}

.analysis-content {
  display: grid;
  gap: 12px;
  padding: 12px;
}

.text-analysis {
  color: var(--ai-text);
  font-size: 14px;
  line-height: 1.7;
}

.typing-dot {
  animation: blink 1.5s infinite;
}

@keyframes blink {
  50% {
    opacity: 0;
  }
}
</style>
