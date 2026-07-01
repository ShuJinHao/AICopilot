<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { BarChart3, Grid3X3, TriangleAlert } from 'lucide-vue-next'
import type { ChartWidget as ChartWidgetModel, DataTableWidget as DataTableWidgetModel, StatsCardWidget } from '@/types/protocols'
import { normalizeWidgetPayload, type NormalizedWidget } from '@/protocol/widgetNormalizer'

const props = defineProps<{
  data: unknown
}>()

const ChartWidget = defineAsyncComponent(() => import('./ChartWidget.vue'))
const DataTableWidget = defineAsyncComponent(() => import('./DataTableWidget.vue'))
const StatsWidget = defineAsyncComponent(() => import('./StatsWidget.vue'))

const widget = computed<NormalizedWidget | null>(() => normalizeWidgetPayload(props.data))
const widgetType = computed(() => widget.value?.type ?? 'Unknown')
const chartWidget = computed(() => (widgetType.value === 'Chart' ? (widget.value as ChartWidgetModel) : null))
const tableWidget = computed(() => (widgetType.value === 'DataTable' ? (widget.value as DataTableWidgetModel) : null))
const statsWidget = computed(() => (widgetType.value === 'StatsCard' ? (widget.value as StatsCardWidget) : null))
</script>

<template>
  <section class="widget-frame">
    <ChartWidget v-if="chartWidget" :widget="chartWidget" />
    <DataTableWidget v-else-if="tableWidget" :widget="tableWidget" />
    <StatsWidget v-else-if="statsWidget" :widget="statsWidget" />
    <div v-else class="widget-empty">
      <TriangleAlert class="h-5 w-5" />
      <div>
        <strong>无法识别的组件</strong>
        <span>类型：{{ widgetType }}</span>
      </div>
    </div>
    <div class="widget-kind">
      <BarChart3 v-if="widgetType === 'Chart'" class="h-3.5 w-3.5" />
      <Grid3X3 v-else class="h-3.5 w-3.5" />
      <span>{{ widgetType }}</span>
    </div>
  </section>
</template>

<style scoped>
.widget-frame {
  position: relative;
  min-width: 0;
  overflow: hidden;
  border: 1px solid var(--ai-border);
  border-radius: 10px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.widget-kind {
  position: absolute;
  top: 10px;
  right: 10px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 4px 8px;
  background: rgba(255, 255, 255, 0.9);
  color: var(--ai-text-muted);
  font-size: 11px;
  font-weight: 800;
}

.widget-empty {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 18px;
  color: var(--ai-text-muted);
}

.widget-empty div {
  display: grid;
  gap: 2px;
}
</style>
