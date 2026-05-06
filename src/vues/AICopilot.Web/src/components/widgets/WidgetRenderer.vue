<script setup lang="ts">
import { computed } from 'vue'
import { Grid, Histogram, Warning } from '@element-plus/icons-vue'
import ChartWidget from './ChartWidget.vue'
import DataTableWidget from './DataTableWidget.vue'
import StatsWidget from './StatsWidget.vue'
import type {
  ChartWidget as ChartWidgetModel,
  DataTableWidget as DataTableWidgetModel,
  StatsCardWidget,
  Widget
} from '@/types/protocols'

const props = defineProps<{
  data: unknown
}>()

type NormalizedWidget = Widget | ChartWidgetModel | DataTableWidgetModel | StatsCardWidget

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function normalizeWidget(value: unknown): NormalizedWidget | null {
  if (!isRecord(value)) {
    return null
  }

  const directType = value.type ?? value.Type
  if (typeof directType === 'string') {
    return value as unknown as NormalizedWidget
  }

  const decision = value.visual_decision ?? value.VisualDecision
  if (isRecord(decision)) {
    return {
      ...decision,
      type: String(decision.type ?? decision.Type ?? 'Unknown'),
      data: value.data ?? value.Data ?? decision.data ?? decision.Data ?? null
    } as NormalizedWidget
  }

  return null
}

const widget = computed(() => normalizeWidget(props.data))
const widgetType = computed(() => widget.value?.type ?? 'Unknown')
const chartWidget = computed(() =>
  widgetType.value === 'Chart' ? (widget.value as ChartWidgetModel) : null
)
const tableWidget = computed(() =>
  widgetType.value === 'DataTable' ? (widget.value as DataTableWidgetModel) : null
)
const statsWidget = computed(() =>
  widgetType.value === 'StatsCard' ? (widget.value as StatsCardWidget) : null
)
</script>

<template>
  <section class="widget-frame">
    <ChartWidget v-if="chartWidget" :widget="chartWidget" />
    <DataTableWidget v-else-if="tableWidget" :widget="tableWidget" />
    <StatsWidget v-else-if="statsWidget" :widget="statsWidget" />
    <div v-else class="widget-empty">
      <el-icon><Warning /></el-icon>
      <div>
        <strong>无法识别的组件</strong>
        <span>类型：{{ widgetType }}</span>
      </div>
    </div>
    <div class="widget-kind">
      <el-icon v-if="widgetType === 'Chart'"><Histogram /></el-icon>
      <el-icon v-else><Grid /></el-icon>
      <span>{{ widgetType }}</span>
    </div>
  </section>
</template>

<style scoped>
.widget-frame {
  position: relative;
  min-width: 0;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  background: var(--app-surface);
  overflow: hidden;
}

.widget-kind {
  position: absolute;
  top: 10px;
  right: 10px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: 6px;
  padding: 3px 7px;
  background: rgba(255, 255, 255, 0.88);
  color: var(--app-text-muted);
  font-size: 11px;
}

.widget-empty {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 16px;
  color: var(--app-text-muted);
}

.widget-empty div {
  display: grid;
  gap: 2px;
}
</style>
