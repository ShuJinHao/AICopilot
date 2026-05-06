<script setup lang="ts">
import { computed } from 'vue'
import type { StatsCardWidget } from '@/types/protocols'

const props = defineProps<{
  widget: StatsCardWidget
}>()

const value = computed(() => props.widget.data?.value ?? '-')
const unit = computed(() => props.widget.data?.unit ?? '')
</script>

<template>
  <div class="stats-widget">
    <span class="stats-label">{{ widget.data?.label || widget.title || '指标' }}</span>
    <strong>{{ value }}<small v-if="unit">{{ unit }}</small></strong>
    <p v-if="widget.description">{{ widget.description }}</p>
  </div>
</template>

<style scoped>
.stats-widget {
  display: grid;
  gap: 6px;
  min-width: 180px;
  padding: 16px;
}

.stats-label {
  color: var(--app-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.stats-widget strong {
  color: var(--app-text);
  font-size: 30px;
  font-weight: 800;
  line-height: 1.1;
}

.stats-widget small {
  margin-left: 4px;
  color: var(--app-text-muted);
  font-size: 13px;
  font-weight: 650;
}

.stats-widget p {
  margin: 0;
  color: var(--app-text-muted);
  font-size: 12px;
}
</style>
