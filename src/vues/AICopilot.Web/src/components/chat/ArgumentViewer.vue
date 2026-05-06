<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  args: string | Record<string, unknown>
}>()

function toRecord(value: string | Record<string, unknown>) {
  if (typeof value !== 'string') return value

  try {
    const parsed = JSON.parse(value) as unknown
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : { Raw: value }
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
  gap: 6px;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 8px;
  background: var(--app-surface-muted);
}

.arg-row {
  display: grid;
  grid-template-columns: minmax(80px, 140px) minmax(0, 1fr);
  gap: 8px;
  align-items: start;
}

.arg-row span {
  color: var(--app-text-muted);
  font-size: 12px;
}

code {
  overflow-wrap: anywhere;
  border-radius: 6px;
  background: #ffffff;
  padding: 3px 6px;
  color: var(--app-text);
  font-family: "Cascadia Mono", Consolas, monospace;
  font-size: 12px;
}

.empty {
  color: var(--app-text-muted);
}
</style>
