<script setup lang="ts">
import { computed } from 'vue'
import DeviceLogAnswerCard from './DeviceLogAnswerCard.vue'
import { parseDeviceLogAnswerSections } from '@/protocol/deviceLogAnswerSections'
import { renderMarkdown } from '@/utils/markdown'

const props = defineProps<{
  content: string
}>()

const deviceLogAnswer = computed(() => parseDeviceLogAnswerSections(props.content))
const renderedMarkdown = computed(() => renderMarkdown(props.content))
</script>

<template>
  <DeviceLogAnswerCard v-if="deviceLogAnswer" :answer="deviceLogAnswer" />
  <div v-else class="text-block markdown-body" v-html="renderedMarkdown" />
</template>

<style scoped>
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
</style>
