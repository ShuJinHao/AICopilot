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
  border: 1px solid rgba(216, 255, 120, 0.62);
  border-radius: 20px;
  background: linear-gradient(135deg, rgba(239, 255, 190, 0.92), rgba(255, 255, 255, 0.88));
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.stats-widget:hover {
  transform: translateY(-2px);
}

.stats-label {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.stats-widget strong {
  color: var(--ai-text);
  font-size: 32px;
  font-weight: 800;
  line-height: 1.1;
  letter-spacing: 0;
}

.stats-widget small {
  margin-left: 4px;
  color: var(--ai-text-muted);
  font-size: 14px;
  font-weight: 650;
}

.stats-widget p {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 12px;
}
</style>
