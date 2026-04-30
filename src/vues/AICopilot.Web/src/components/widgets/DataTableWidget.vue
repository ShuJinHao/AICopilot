<script setup lang="ts">
import { computed } from 'vue';

const props = defineProps<{
  widget: any // 接收由 WidgetRenderer 传来的完整组件 JSON
}>();

// 1. 兼容读取核心数据
const tableDataRaw = computed(() => {
  return props.widget.Data || props.widget.data || {};
});

const title = computed(() => {
  return props.widget.Title || props.widget.title || '';
});

// 2. 核心修复：智能提取列名 (兼容后端传 字符串 或 对象)
const columns = computed(() => {
  const cols = tableDataRaw.value.Columns || tableDataRaw.value.columns || [];
  return cols.map((c: any, index: number) => {
    // 如果后端直接传了字符串数组 ["order_id"]
    if (typeof c === 'string') {
      return { label: c, prop: c }; 
    }
    // 如果后端传了对象数组 { Key: 'order_id', Label: '订单ID' }
    const key = c.Key || c.key || `col_${index}`;
    const label = c.Label || c.label || key;
    return { label: label, prop: key };
  });
});

// 3. 智能提取行数据 (完美兼容数组行和对象行)
const tableData = computed(() => {
  const rows = tableDataRaw.value.Rows || tableDataRaw.value.rows || [];
  
  return rows.map((row: any) => {
    // 情况 A: 后端传来的是二维数组 [ ["1001", "Paid"] ]
    if (Array.isArray(row)) {
      const rowData: any = {};
      // 这里已经加上了明确的类型声明 (col: any, index: number)，彻底消灭 ts 报错
      columns.value.forEach((col: any, index: number) => {
        rowData[col.prop] = row[index];
      });
      return rowData;
    } 
    // 情况 B: 后端传来的是对象 { order_id: "1001", status: "Paid" }
    else if (typeof row === 'object' && row !== null) {
      // 因为对象的 key 和列的 prop 已经对齐了，直接解构返回即可
      return { ...row };
    }
    return {};
  });
});
</script>

<template>
  <div class="table-widget">
    <h4 v-if="title" class="widget-title">{{ title }}</h4>
    <el-table 
      :data="tableData" 
      border 
      stripe 
      style="width: 100%" 
      max-height="400">
      
      <el-table-column 
        v-for="(col, index) in columns" 
        :key="index" 
        :prop="col.prop" 
        :label="col.label" 
        min-width="120"
      />
      
    </el-table>
  </div>
</template>

<style scoped>
.table-widget { 
  background: #fff; 
  padding: 15px; 
  border-radius: 8px; 
  border: 1px solid #ebeef5; 
  margin-top: 10px;
}
.widget-title { 
  margin-top: 0; 
  margin-bottom: 15px; 
  color: #303133; 
  font-size: 15px;
}
</style>