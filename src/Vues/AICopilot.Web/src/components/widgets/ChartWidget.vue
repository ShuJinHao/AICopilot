<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue';
import * as echarts from 'echarts';

const props = defineProps<{
  data: any // 接收由 WidgetRenderer 传来的完整 JSON 对象
}>();

const chartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;
let resizeObserver: ResizeObserver | null = null;

const getChartOption = () => {
  // 严格按照你图 6 截图中的大写结构解析
  const widgetData = props.data.Data; 
  if (!widgetData) return {};

  const chartType = widgetData.Category || 'Bar'; // "Pie", "Bar", "Line"
  const dataset = widgetData.Dataset; // 包含 Dimensions 和 Source
  const title = props.data.Title || props.data.title;

  const typeLower = chartType.toLowerCase();

  // ECharts 最简标准配置（直接使用 dataset）
  const option: any = {
    title: { text: title, left: 'center', textStyle: { fontSize: 14, color: '#333' } },
    tooltip: { trigger: chartType === 'Pie' ? 'item' : 'axis', confine: true },
    legend: { bottom: 0, type: 'scroll' },
    grid: { left: '3%', right: '4%', bottom: '15%', containLabel: true },
    // 注入你截图 6 里的 Dataset
    dataset: {
      dimensions: dataset.Dimensions,
      source: dataset.Source
    }
  };

  // 根据类型进行极其简单的渲染
  if (chartType === 'Pie') {
    option.series = [ { type: 'pie', radius: '50%' } ];
  } else {
    option.xAxis = { type: 'category' };
    option.yAxis = { type: 'value' };
    // 如果是柱状图或折线图，生成一条线/柱子
    option.series = [ { type: typeLower } ];
  }

  return option;
};

const initChart = () => {
  if (!chartRef.value) return;
  chartInstance = echarts.init(chartRef.value);
  chartInstance.setOption(getChartOption());
};

onMounted(async () => {
  await nextTick();
  initChart();
  resizeObserver = new ResizeObserver(() => chartInstance?.resize());
  if (chartRef.value) resizeObserver.observe(chartRef.value);
});

onUnmounted(() => {
  resizeObserver?.disconnect();
  chartInstance?.dispose();
});

watch(() => props.data, () => {
  chartInstance?.setOption(getChartOption(), true);
}, { deep: true });
</script>

<template>
  <div class="chart-container">
    <div ref="chartRef" class="echarts-dom"></div>
  </div>
</template>

<style scoped>
.chart-container {
  width: 100%;
  max-width: 600px;
  background: #fff;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  padding: 16px;
  margin-top: 8px;
}
.echarts-dom { width: 100%; height: 350px; }
</style>