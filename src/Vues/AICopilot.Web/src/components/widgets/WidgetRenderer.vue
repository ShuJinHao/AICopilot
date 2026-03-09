<script setup lang="ts">
import { defineAsyncComponent, computed } from 'vue';

const props = defineProps<{ data: any }>();

const ChartWidget = defineAsyncComponent(() => import('./ChartWidget.vue'));
const StatsWidget = defineAsyncComponent(() => import('./StatsWidget.vue'));
const DataTableWidget = defineAsyncComponent(() => import('./DataTableWidget.vue'));

const effectiveWidget = computed(() => {
  if (!props.data) return null;
  const decision = props.data.visual_decision || props.data.VisualDecision;
  if (decision) {
    return {
      ...decision,
      data: props.data.data || props.data.Data || decision.data || decision.Data || []
    };
  }
  return props.data;
});

const widgetType = computed(() => {
  const w = effectiveWidget.value;
  return w?.type || w?.Type || w?.widget_type || 'Unknown';
});
</script>

<template>
  <div v-if="effectiveWidget" class="widget-renderer">
    <ChartWidget v-if="widgetType === 'Chart'" :data="effectiveWidget" />
    <StatsWidget v-else-if="widgetType === 'StatsCard'" :widget="effectiveWidget" />
    <DataTableWidget v-else-if="widgetType === 'DataTable'" :widget="effectiveWidget" />
    <div v-else class="unknown-widget">
      <el-alert :title="`未知的组件类型: ${widgetType}`" type="info" :closable="false" />
    </div>
  </div>
</template>

<style scoped>
.widget-renderer { margin-top: 10px; width: 100%; }
.unknown-widget { margin-top: 8px; }
</style>