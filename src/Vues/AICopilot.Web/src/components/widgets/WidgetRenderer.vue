<script setup lang="ts">
import { defineAsyncComponent, computed } from 'vue';

const props = defineProps<{
  data: any // 接收由外层传来的完整组件 JSON
}>();

// 异步按需加载对应的组件
const ChartWidget = defineAsyncComponent(() => import('./ChartWidget.vue'));
const StatsWidget = defineAsyncComponent(() => import('./StatsWidget.vue'));
const DataTableWidget = defineAsyncComponent(() => import('./DataTableWidget.vue'));

// 准确提取组件类型，兼容大小写
const widgetType = computed(() => {
  if (!props.data) return 'Unknown';
  return props.data.widget_type || props.data.type || props.data.Type;
});
</script>

<template>
  <div class="widget-renderer">
    
    <ChartWidget 
      v-if="widgetType === 'Chart'" 
      :data="data" 
    />
    
    <StatsWidget 
      v-else-if="widgetType === 'StatsCard'" 
      :widget="data" 
    />
    
    <DataTableWidget 
      v-else-if="widgetType === 'DataTable'" 
      :widget="data" 
    />

    <div v-else class="unknown-widget">
      <el-alert
        :title="`暂不支持的组件类型: ${widgetType}`"
        type="warning"
        show-icon
        :closable="false"
      />
    </div>
    
  </div>
</template>

<style scoped>
.widget-renderer { margin-top: 10px; width: 100%; }
.unknown-widget { margin-top: 8px; }
</style>