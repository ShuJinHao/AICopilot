<script setup lang="ts">
import { BrainCircuit } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import type { IntentResult } from '@/types/protocols'

defineProps<{
  intents: IntentResult[]
}>()

function getIntentTone(confidence: number) {
  if (confidence > 0.8) return 'success'
  if (confidence > 0.5) return 'warning'
  return 'danger'
}
</script>

<template>
  <details class="block-intent">
    <summary>
      <BrainCircuit class="h-4 w-4" />
      <span class="label">意图识别</span>
      <div v-if="intents.length > 0" class="intent-tags">
        <AiTag v-for="item in intents" :key="`${item.intent}-${item.confidence}`" :tone="getIntentTone(item.confidence)">
          {{ item.intent }} {{ (item.confidence * 100).toFixed(0) }}%
        </AiTag>
      </div>
    </summary>

    <div v-if="intents.length > 0" class="intent-body">
      <div v-for="(item, idx) in intents" :key="idx" class="intent-item">
        <div class="info-row">
          <span>意图</span>
          <strong>{{ item.intent }}</strong>
        </div>
        <div v-if="item.query" class="info-row">
          <span>关键词</span>
          <em>{{ item.query }}</em>
        </div>
        <div v-if="item.reasoning" class="info-row">
          <span>推理</span>
          <em>{{ item.reasoning }}</em>
        </div>
      </div>
    </div>
  </details>
</template>

<style scoped>
.block-intent {
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  background: var(--ai-surface);
}

summary {
  display: flex;
  min-height: 44px;
  cursor: pointer;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 900;
}

.intent-tags {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  gap: 6px;
}

.intent-body {
  display: grid;
  gap: 10px;
  border-top: 1px solid var(--ai-border);
  background: var(--ai-surface-soft);
  padding: 12px;
  font-size: 13px;
}

.intent-item {
  display: grid;
  gap: 5px;
}

.intent-item + .intent-item {
  border-top: 1px dashed var(--ai-border);
  padding-top: 10px;
}

.info-row {
  display: grid;
  grid-template-columns: 60px minmax(0, 1fr);
  gap: 8px;
}

.info-row span {
  color: var(--ai-text-muted);
  font-weight: 800;
}

.info-row strong,
.info-row em {
  min-width: 0;
  color: var(--ai-text);
  font-style: normal;
}
</style>
