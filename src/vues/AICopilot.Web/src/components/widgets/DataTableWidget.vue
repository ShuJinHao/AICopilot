<script setup lang="ts">
import { computed } from 'vue'
import type { DataTableWidget } from '@/types/protocols'

const props = defineProps<{
  widget: DataTableWidget
}>()

const columns = computed(() => props.widget.data?.columns ?? [])
const rows = computed(() => props.widget.data?.rows ?? [])

function formatCell(value: unknown) {
  if (value === null || value === undefined || value === '') return '-'
  if (typeof value === 'boolean') return value ? '是' : '否'
  return String(value)
}
</script>

<template>
  <div class="table-widget">
    <header>
      <h3>{{ widget.title || '数据表' }}</h3>
      <p>{{ rows.length }} 条记录</p>
    </header>

    <el-table v-if="rows.length > 0" :data="rows" stripe border height="360" class="data-table">
      <el-table-column
        v-for="column in columns"
        :key="column.key"
        :prop="column.key"
        :label="column.label || column.key"
        min-width="150"
        show-overflow-tooltip
      >
        <template #default="{ row }">
          <span>{{ formatCell(row[column.key]) }}</span>
        </template>
      </el-table-column>
    </el-table>

    <div v-else class="empty-widget">暂无表格数据</div>
  </div>
</template>

<style scoped>
.table-widget {
  display: grid;
  gap: 12px;
  min-width: 0;
  padding: 16px;
}

.table-widget header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding-right: 92px;
}

.table-widget h3 {
  margin: 0;
  font-size: 15px;
  font-weight: 750;
}

.table-widget p {
  margin: 0;
  color: var(--app-text-muted);
  font-size: 12px;
}

.data-table {
  width: 100%;
}

.empty-widget {
  display: grid;
  height: 140px;
  place-items: center;
  border: 1px dashed var(--app-border);
  border-radius: 8px;
  color: var(--app-text-muted);
}
</style>
