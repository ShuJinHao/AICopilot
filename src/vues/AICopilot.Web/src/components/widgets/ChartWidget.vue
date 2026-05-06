<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts'
import type { ChartWidget } from '@/types/protocols'

const props = defineProps<{
  widget: ChartWidget
}>()

const chartRef = ref<HTMLElement | null>(null)
let chartInstance: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null

const rows = computed(() => props.widget.data?.dataset?.source ?? [])
const dimensions = computed(() => props.widget.data?.dataset?.dimensions ?? [])
const encoding = computed(() => props.widget.data?.encoding)
const category = computed(() => (props.widget.data?.category ?? 'Bar').toLowerCase())

function inferStringField() {
  const first = rows.value[0]
  if (!first) return dimensions.value[0] ?? ''
  return Object.keys(first).find((key) => typeof first[key] === 'string') ?? dimensions.value[0] ?? ''
}

function inferNumberFields() {
  const first = rows.value[0]
  if (!first) return dimensions.value.slice(1, 2)
  const fields = Object.keys(first).filter((key) => typeof first[key] === 'number')
  return fields.length > 0 ? fields : dimensions.value.slice(1, 2)
}

const option = computed<echarts.EChartsOption>(() => {
  const xField = encoding.value?.x || inferStringField()
  const yFields = encoding.value?.y?.length ? encoding.value.y : inferNumberFields()
  const chartType = category.value === 'line' ? 'line' : category.value === 'pie' ? 'pie' : 'bar'

  if (rows.value.length === 0 || !xField || yFields.length === 0) {
    return {}
  }

  if (chartType === 'pie') {
    const yField = yFields[0] ?? ''
    if (!yField) {
      return {}
    }

    return {
      tooltip: { trigger: 'item', confine: true },
      legend: { type: 'scroll', bottom: 0 },
      series: [
        {
          type: 'pie',
          radius: ['42%', '68%'],
          avoidLabelOverlap: true,
          data: rows.value.map((row) => ({
            name: String(row[xField] ?? '-'),
            value: Number(row[yField] ?? 0)
          }))
        }
      ]
    }
  }

  return {
    tooltip: { trigger: 'axis', confine: true },
    legend: { type: 'scroll', bottom: 0 },
    grid: { left: 24, right: 20, top: 24, bottom: 52, containLabel: true },
    xAxis: { type: 'category', data: rows.value.map((row) => String(row[xField] ?? '-')) },
    yAxis: { type: 'value' },
    series: yFields.map((field) => ({
      type: chartType,
      name: field,
      smooth: chartType === 'line',
      data: rows.value.map((row) => Number(row[field] ?? 0))
    }))
  }
})

function renderChart() {
  if (!chartRef.value || rows.value.length === 0) return
  chartInstance ??= echarts.init(chartRef.value)
  chartInstance.setOption(option.value, true)
}

onMounted(async () => {
  await nextTick()
  renderChart()
  if (chartRef.value) {
    resizeObserver = new ResizeObserver(() => chartInstance?.resize())
    resizeObserver.observe(chartRef.value)
  }
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  chartInstance?.dispose()
})

watch(option, () => renderChart(), { deep: true })
</script>

<template>
  <div class="chart-widget">
    <header>
      <h3>{{ widget.title || '图表结果' }}</h3>
      <p v-if="widget.description">{{ widget.description }}</p>
    </header>

    <div v-if="rows.length === 0" class="empty-widget">暂无可绘制的数据</div>
    <div v-else ref="chartRef" class="chart-surface" />
  </div>
</template>

<style scoped>
.chart-widget {
  display: grid;
  gap: 10px;
  padding: 16px;
}

.chart-widget header {
  display: grid;
  gap: 2px;
  padding-right: 92px;
}

.chart-widget h3 {
  margin: 0;
  color: var(--app-text);
  font-size: 15px;
  font-weight: 750;
}

.chart-widget p {
  margin: 0;
  color: var(--app-text-muted);
  font-size: 12px;
}

.chart-surface {
  width: 100%;
  height: 320px;
  min-height: 280px;
}

.empty-widget {
  display: grid;
  height: 160px;
  place-items: center;
  border: 1px dashed var(--app-border);
  border-radius: 8px;
  color: var(--app-text-muted);
}
</style>
