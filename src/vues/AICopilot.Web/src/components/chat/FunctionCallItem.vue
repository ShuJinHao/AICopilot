<script setup lang="ts">
import { computed } from 'vue'
import { CircleCheck, Loading, Tools } from '@element-plus/icons-vue'
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
      <el-icon>
        <Loading v-if="isRunning" />
        <CircleCheck v-else />
      </el-icon>
      <strong class="mono">{{ call.name }}</strong>
      <el-tag size="small" :type="isRunning ? 'warning' : 'success'">
        {{ isRunning ? '执行中' : '已完成' }}
      </el-tag>
    </header>

    <template v-if="!mini">
      <details>
        <summary>
          <el-icon><Tools /></el-icon>
          参数与结果
        </summary>
        <pre>{{ argsText }}</pre>
        <pre v-if="call.result">{{ call.result }}</pre>
      </details>
    </template>
  </div>
</template>

<style scoped>
.function-call {
  border: 1px solid var(--app-border);
  border-radius: 8px;
  background: var(--app-surface-muted);
  overflow: hidden;
}

.function-call header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
}

.function-call.running {
  border-color: #f2c97d;
}

.function-call.mini {
  display: inline-flex;
  max-width: 100%;
}

.function-call.mini header {
  padding: 5px 8px;
}

details {
  border-top: 1px solid var(--app-border);
  padding: 10px;
}

summary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  color: var(--app-text-muted);
  font-size: 12px;
}

pre {
  overflow: auto;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  margin: 8px 0 0;
  padding: 10px;
  background: #ffffff;
  font-size: 12px;
}
</style>
