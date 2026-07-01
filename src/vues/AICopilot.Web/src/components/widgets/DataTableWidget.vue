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

    <div v-if="rows.length > 0" class="table-scroll">
      <table>
        <thead>
          <tr>
            <th v-for="column in columns" :key="column.key">{{ column.label || column.key }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, rowIndex) in rows" :key="rowIndex">
            <td v-for="column in columns" :key="column.key">
              {{ formatCell(row[column.key]) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-else class="empty-widget">暂无表格数据</div>
  </div>
</template>

<style scoped>
.table-widget {
  display: grid;
  gap: 10px;
  min-width: 0;
  padding: 14px;
}

.table-widget header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  max-width: calc(100% - 92px);
}

.table-widget h3 {
  margin: 0;
  color: var(--ai-text);
  font-size: 15px;
  font-weight: 900;
}

.table-widget p {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.table-scroll {
  max-height: 320px;
  overflow: auto;
  border: 1px solid var(--ai-border);
  border-radius: 8px;
}

table {
  min-width: 100%;
  border-collapse: separate;
  border-spacing: 0;
}

th,
td {
  border-bottom: 1px solid var(--ai-border);
  padding: 10px 12px;
  text-align: left;
  font-size: 13px;
  line-height: 1.45;
  vertical-align: top;
}

th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--ai-surface-soft);
  color: var(--ai-text-muted);
  font-weight: 900;
}

td {
  max-width: 320px;
  color: var(--ai-text);
  font-weight: 650;
  overflow-wrap: anywhere;
}

tbody tr:nth-child(even) td {
  background: rgba(248, 250, 252, 0.72);
}

tbody tr:hover td {
  background: #efffbe;
}

.empty-widget {
  display: grid;
  height: 140px;
  place-items: center;
  border: 1px dashed var(--ai-border);
  border-radius: 18px;
  color: var(--ai-text-muted);
  font-weight: 800;
}

@media (max-width: 640px) {
  .table-widget header {
    max-width: 100%;
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
