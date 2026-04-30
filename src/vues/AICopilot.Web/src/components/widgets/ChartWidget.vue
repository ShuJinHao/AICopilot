<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick } from 'vue';
import * as echarts from 'echarts';

const props = defineProps<{ data: any }>();
const chartRef = ref<HTMLElement | null>(null);
let chartInstance: echarts.ECharts | null = null;
let resizeObserver: ResizeObserver | null = null;

const getChartOption = () => {
  const w = props.data;
  if (!w) return {};

  const config = w.chart_config || w.ChartConfig || {};
  const type = (w.category || w.Category || config.category || config.Category || 'Bar').toLowerCase();
  
  // 数据源
  const source = Array.isArray(w.data) ? w.data : (w.data?.dataset?.source || []);

  const option: any = {
    title: { text: w.title || w.Title, left: 'center', textStyle: { fontSize: 14, fontWeight: 'bold' } },
    tooltip: { trigger: type === 'pie' ? 'item' : 'axis', confine: true },
    legend: { bottom: 0, type: 'scroll' },
    dataset: { source: source },
    series: []
  };

  // 【智能字段匹配】
  // 1. 获取后端建议的 Key
  let xKey = config.x || config.X;
  let yKey = config.y || config.Y;

  // 2. 如果数据里没有这个 Key，尝试自动寻找替代品
  if (source.length > 0) {
    const keys = Object.keys(source[0]);
    
    // 检查 yKey (数值轴) 是否存在
    if (!keys.includes(yKey)) {
        // 如果不存在，找第一个数值类型的字段代替
        const numKey = keys.find(k => typeof source[0][k] === 'number');
        if (numKey) yKey = numKey;
    }
    
    // 检查 xKey (类目轴) 是否存在
    if (!keys.includes(xKey)) {
        // 如果不存在，找第一个字符串类型的字段代替
        const strKey = keys.find(k => typeof source[0][k] === 'string');
        if (strKey) xKey = strKey;
    }
  }

  const seriesItem: any = {
    type: type,
    encode: {
      itemName: xKey,
      value: yKey,
      x: xKey,
      y: yKey
    }
  };

  if (type === 'pie') {
    seriesItem.radius = ['40%', '70%'];
    seriesItem.avoidLabelOverlap = true;
    option.xAxis = undefined;
    option.yAxis = undefined;
  } else {
    option.xAxis = { type: 'category' };
    option.yAxis = { type: 'value' };
    option.grid = { left: '3%', right: '4%', bottom: '15%', containLabel: true };
  }

  option.series = [seriesItem];
  return option;
};

const initChart = () => {
  if (!chartRef.value) return;
  if (chartInstance) chartInstance.dispose();
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
  if (chartInstance) chartInstance.setOption(getChartOption(), true);
}, { deep: true });
</script>

<template>
  <div class="chart-container">
    <div ref="chartRef" class="echarts-dom"></div>
    <div v-if="(!data.data || data.data.length === 0)" style="color:#999;font-size:12px;text-align:center;padding:10px;">
      (暂未检测到图表数据)
    </div>
  </div>
</template>

<style scoped>
.chart-container {
  width: 100%; max-width: 600px; background: #fff;
  border-radius: 8px; border: 1px solid #e4e7ed; padding: 16px; margin-top: 8px;
}
.echarts-dom { width: 100%; height: 350px; }
</style>