<script setup lang="ts">
import { computed, ref } from 'vue'
import { ShieldAlert } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiTag from '@/components/ai/AiTag.vue'
import type { ApprovalChunk } from '@/types/models'
import ArgumentViewer from './ArgumentViewer.vue'

const props = defineProps<{
  chunk: ApprovalChunk
  isSubmitting?: boolean
}>()

const emit = defineEmits<{
  (event: 'approve', payload: { callId: string; onsiteConfirmed: boolean }): void
  (event: 'reject', payload: { callId: string }): void
}>()

const onsiteConfirmed = ref(false)
const request = computed(() => props.chunk.request)
const isPending = computed(() => props.chunk.status === 'pending')
const hasStrictIdentity = computed(
  () => Boolean(request.value.targetType) && Boolean(request.value.targetName) && Boolean(request.value.toolName)
)
const targetText = computed(() =>
  [request.value.targetType, request.value.targetName, request.value.toolName].filter(Boolean).join(' / ')
)
const statusText = computed(() => {
  switch (props.chunk.status) {
    case 'approved':
      return '已批准'
    case 'rejected':
      return '已拒绝'
    case 'expired':
      return '已失效'
    default:
      return '待审批'
  }
})
const statusTone = computed(() => {
  if (props.chunk.status === 'approved') return 'success'
  if (props.chunk.status === 'pending') return 'warning'
  return 'neutral'
})
const attestationExpiresText = computed(() =>
  request.value.attestationExpiresAt
    ? new Date(request.value.attestationExpiresAt).toLocaleString('zh-CN', { hour12: false })
    : ''
)

function approve() {
  if (isPending.value && hasStrictIdentity.value) {
    emit('approve', { callId: request.value.callId, onsiteConfirmed: onsiteConfirmed.value })
  }
}

function reject() {
  if (isPending.value && hasStrictIdentity.value) {
    emit('reject', { callId: request.value.callId })
  }
}
</script>

<template>
  <section class="approval-card" :class="chunk.status">
    <header>
      <span class="approval-icon"><ShieldAlert class="h-5 w-5" /></span>
      <div>
        <h3>人工审批请求</h3>
        <p>模型请求调用受控工具，执行前需要人工复核。</p>
      </div>
      <AiTag :tone="statusTone">{{ statusText }}</AiTag>
    </header>

    <div class="approval-body">
      <dl>
        <div>
          <dt>工具</dt>
          <dd class="mono">{{ request.toolName || request.name }}</dd>
        </div>
        <div v-if="targetText">
          <dt>身份</dt>
          <dd class="mono">{{ targetText }}</dd>
        </div>
        <div v-if="request.runtimeName">
          <dt>运行标识</dt>
          <dd class="mono">{{ request.runtimeName }}</dd>
        </div>
      </dl>

      <div v-if="!hasStrictIdentity" class="alert-danger">
        审批请求缺少完整工具身份，系统不会允许继续执行。
      </div>

      <div class="args-block">
        <span>参数</span>
        <ArgumentViewer :args="request.args" />
      </div>

      <div v-if="request.requiresOnsiteAttestation" class="onsite-block">
        <strong>现场复核</strong>
        <p>此工具需要确认现场有人在岗，并再次确认执行前条件。</p>
        <p v-if="attestationExpiresText">在岗声明有效至：{{ attestationExpiresText }}</p>
        <AiCheckbox v-if="isPending" v-model="onsiteConfirmed">
          现场有人在岗，且已复核执行前条件
        </AiCheckbox>
      </div>
    </div>

    <footer>
      <template v-if="isPending">
        <AiButton :disabled="isSubmitting || !hasStrictIdentity" @click="reject">拒绝</AiButton>
        <AiButton
          variant="primary"
          :disabled="Boolean(isSubmitting || !hasStrictIdentity || (request.requiresOnsiteAttestation && !onsiteConfirmed))"
          @click="approve"
        >
          {{ isSubmitting ? '提交中' : '批准' }}
        </AiButton>
      </template>
      <span v-else class="muted">审批状态：{{ statusText }}</span>
    </footer>
  </section>
</template>

<style scoped>
.approval-card {
  display: grid;
  overflow: hidden;
  border: 1px solid #fed7aa;
  border-radius: 22px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.approval-card.approved {
  border-color: #bbf7d0;
}

.approval-card.rejected,
.approval-card.expired {
  border-color: var(--ai-border);
  background: var(--ai-surface-soft);
  box-shadow: none;
}

.approval-card header,
.approval-card footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 13px 15px;
}

.approval-card header {
  border-bottom: 1px solid var(--ai-border);
}

.approval-card header > div {
  flex: 1;
  min-width: 0;
}

.approval-icon {
  display: grid;
  height: 36px;
  width: 36px;
  place-items: center;
  border-radius: 14px;
  background: #fff7ed;
  color: #b45309;
}

.approval-card h3 {
  margin: 0;
  font-size: 14px;
  font-weight: 900;
  color: var(--ai-text);
}

.approval-card p {
  margin: 2px 0 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 650;
}

.approval-body {
  display: grid;
  gap: 12px;
  padding: 15px;
}

dl {
  display: grid;
  gap: 8px;
  margin: 0;
}

dl div {
  display: grid;
  grid-template-columns: 76px minmax(0, 1fr);
  gap: 8px;
}

dt {
  color: var(--ai-text-muted);
  font-weight: 800;
}

dd {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--ai-text);
}

.alert-danger {
  border: 1px solid #fecaca;
  border-radius: 16px;
  background: #fef2f2;
  padding: 10px 12px;
  color: #b42318;
  font-size: 13px;
  font-weight: 800;
}

.args-block,
.onsite-block {
  display: grid;
  gap: 8px;
}

.args-block > span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.onsite-block {
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 12px;
  background: var(--ai-surface-soft);
}

footer {
  border-top: 1px solid var(--ai-border);
  background: rgba(248, 250, 252, 0.72);
}

.muted {
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 800;
}
</style>
