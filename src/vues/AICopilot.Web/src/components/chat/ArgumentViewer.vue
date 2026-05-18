<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  args: string | Record<string, unknown>
}>()

function toRecord(value: string | Record<string, unknown>) {
  if (typeof value !== 'string') return value

  try {
    const parsed = JSON.parse(value) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : { Raw: value }
  } catch {
    return { Raw: value }
  }
}

const entries = computed(() => Object.entries(toRecord(props.args)))

function formatValue(value: unknown) {
  if (value === null || value === undefined) return '-'
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (typeof value === 'object') return JSON.stringify(value)
  return String(value)
}
</script>

<template>
  <div class="arg-viewer">
    <span v-if="entries.length === 0" class="empty">无参数</span>
    <div v-for="[key, value] in entries" v-else :key="key" class="arg-row">
      <span>{{ key }}</span>
      <code>{{ formatValue(value) }}</code>
    </div>
  </div>
</template>

<style scoped>
.arg-viewer {
  display: grid;
  gap: 7px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 16px;
  padding: 10px;
  background: var(--ai-graphite);
  box-shadow: 0 14px 28px rgba(63, 111, 115, 0.18);
}

.arg-row {
  display: grid;
  grid-template-columns: minmax(80px, 150px) minmax(0, 1fr);
  gap: 10px;
  align-items: start;
}

.arg-row span {
  color: var(--ai-graphite-muted);
  font-size: 12px;
  font-weight: 800;
}

code {
  overflow-wrap: anywhere;
  border-radius: 12px;
  background: var(--ai-graphite-soft);
  padding: 4px 7px;
  color: #f8fafc;
  font-family: 'Cascadia Mono', Consolas, monospace;
  font-size: 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
}

.empty {
  color: var(--ai-graphite-muted);
  font-weight: 700;
}
</style>
