<script setup lang="ts">
import { computed } from 'vue'
import { CheckCircle2, LoaderCircle, TriangleAlert } from 'lucide-vue-next'
import type { ChatRunStatus } from '@/stores/sessionScopedState'

const props = defineProps<{
  status: ChatRunStatus
}>()

const isRunning = computed(() =>
  props.status.phase === 'understanding' ||
  props.status.phase === 'querying' ||
  props.status.phase === 'answering'
)
const statusLabel = computed(() => {
  if (props.status.phase === 'failed') {
    return '失败'
  }

  if (props.status.phase === 'completed') {
    return '完成'
  }

  return '运行中'
})
const phaseSummary = computed(() => {
  if (isRunning.value && props.status.elapsedMs > 30_000) {
    return '查询仍在进行，数据源或模型响应较慢'
  }

  if (props.status.error?.message) {
    return props.status.error.message
  }

  return props.status.summary || '正在处理请求'
})
const elapsedText = computed(() => {
  const seconds = Math.max(0, Math.floor(props.status.elapsedMs / 1000))
  if (isRunning.value) {
    const minutes = Math.floor(seconds / 60)
    const remainder = seconds % 60
    return `${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`
  }

  if (seconds < 60) {
    return `${seconds}s`
  }

  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`
})
const facts = computed(() => {
  const items: string[] = []
  if ((props.status.queryCount ?? 0) > 0) {
    items.push(`查询 ${props.status.queryCount} 次`)
  }

  if (typeof props.status.returnedRows === 'number') {
    items.push(`返回 ${props.status.returnedRows} 行`)
  }

  return items
})
</script>

<template>
  <div class="run-status-strip" :class="`phase-${status.phase}`" aria-live="polite">
    <span class="run-icon" aria-hidden="true">
      <LoaderCircle v-if="isRunning" class="spin" :size="15" />
      <CheckCircle2 v-else-if="status.phase === 'completed'" :size="15" />
      <TriangleAlert v-else :size="15" />
    </span>
    <strong>{{ statusLabel }}</strong>
    <span class="run-dot" aria-hidden="true" />
    <span class="ai-number">{{ elapsedText }}</span>
    <span class="run-dot" aria-hidden="true" />
    <span class="run-summary">{{ phaseSummary }}</span>
    <template v-for="fact in facts" :key="fact">
      <span class="run-dot muted" aria-hidden="true" />
      <span class="run-fact">{{ fact }}</span>
    </template>
  </div>
</template>

<style scoped>
.run-status-strip {
  display: inline-flex;
  width: fit-content;
  max-width: 100%;
  min-height: 34px;
  align-items: center;
  gap: 8px;
  border: 1px solid rgba(63, 111, 115, 0.16);
  border-radius: 12px;
  padding: 6px 10px;
  background: rgba(255, 255, 255, 0.72);
  color: var(--ai-text-muted);
  box-shadow: var(--ai-shadow-xs);
  font-size: 12px;
  font-weight: 850;
  line-height: 1.35;
}

.run-status-strip strong {
  color: var(--ai-text);
  font-weight: 950;
}

.run-icon {
  display: inline-grid;
  width: 20px;
  height: 20px;
  place-items: center;
  flex: 0 0 auto;
  border-radius: 999px;
  background: rgba(63, 111, 115, 0.1);
  color: var(--ai-graphite);
}

.phase-querying .run-icon,
.phase-answering .run-icon,
.phase-understanding .run-icon {
  color: #b45309;
}

.phase-completed .run-icon {
  color: #15803d;
}

.phase-failed {
  border-color: rgba(180, 35, 24, 0.18);
  background: rgba(254, 242, 242, 0.88);
}

.phase-failed .run-icon {
  color: #b42318;
  background: rgba(180, 35, 24, 0.1);
}

.run-dot {
  width: 3px;
  height: 3px;
  flex: 0 0 auto;
  border-radius: 999px;
  background: var(--ai-border-strong);
}

.run-dot.muted {
  opacity: 0.64;
}

.run-summary,
.run-fact {
  min-width: 0;
  overflow-wrap: anywhere;
}

.run-fact {
  color: var(--ai-text);
}

.spin {
  animation: run-spin 1s linear infinite;
}

@keyframes run-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
