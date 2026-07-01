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
    <div>
      <span class="stats-label">{{ widget.data?.label || widget.title || '指标' }}</span>
      <p v-if="widget.description">{{ widget.description }}</p>
    </div>
    <strong>{{ value }}<small v-if="unit">{{ unit }}</small></strong>
  </div>
</template>

<style scoped>
.stats-widget {
  display: flex;
  min-height: 86px;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 14px 16px;
  border: 1px solid rgba(200, 255, 61, 0.42);
  border-radius: 8px;
  background: linear-gradient(135deg, rgba(250, 255, 237, 0.96), rgba(255, 255, 255, 0.9));
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.stats-widget:hover {
  transform: translateY(-2px);
}

.stats-label {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 850;
}

.stats-widget strong {
  flex: 0 0 auto;
  color: var(--ai-text);
  font-size: 30px;
  font-weight: 900;
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
  max-width: 520px;
  margin: 4px 0 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 720;
  line-height: 1.45;
  overflow-wrap: anywhere;
}

@media (max-width: 640px) {
  .stats-widget {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
