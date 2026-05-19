<script setup lang="ts">
import { computed } from 'vue'
import { CheckCircle2, LoaderCircle, Wrench } from 'lucide-vue-next'
import AiTag from '@/components/ai/AiTag.vue'
import type { FunctionCall } from '@/types/models'

const props = defineProps<{
  call: FunctionCall
  mini?: boolean
}>()

const isRunning = computed(() => props.call.status === 'calling')
const argsText = computed(() => {
  try {
    return JSON.stringify(JSON.parse(props.call.args), null, 2)
  } catch {
    return props.call.args
  }
})
</script>

<template>
  <div class="function-call" :class="{ mini, running: isRunning }">
    <header>
      <LoaderCircle v-if="isRunning" class="h-4 w-4 animate-spin" />
      <CheckCircle2 v-else class="h-4 w-4" />
      <strong class="mono">{{ call.name }}</strong>
      <AiTag :tone="isRunning ? 'warning' : 'success'">
        {{ isRunning ? '执行中' : '已完成' }}
      </AiTag>
    </header>

    <details v-if="!mini">
      <summary>
        <Wrench class="h-4 w-4" />
        参数与结果
      </summary>
      <pre>{{ argsText }}</pre>
      <pre v-if="call.result">{{ call.result }}</pre>
    </details>
  </div>
</template>

<style scoped>
.function-call {
  overflow: hidden;
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 18px;
  background: var(--ai-graphite);
  color: #f8fafc;
  box-shadow: 0 14px 28px rgba(63, 111, 115, 0.2);
}

.function-call header {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 10px 12px;
  color: #f8fafc;
}

.function-call.running {
  border-color: rgba(255, 207, 61, 0.55);
}

.function-call.mini {
  display: inline-flex;
  max-width: 100%;
}

.function-call.mini header {
  padding: 6px 9px;
}

details {
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  padding: 12px;
}

summary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  color: var(--ai-graphite-muted);
  font-size: 12px;
  font-weight: 800;
}

pre {
  overflow: auto;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 14px;
  margin: 9px 0 0;
  padding: 10px;
  background: var(--ai-graphite-soft);
  color: #f8fafc;
  font-size: 12px;
}
</style>
