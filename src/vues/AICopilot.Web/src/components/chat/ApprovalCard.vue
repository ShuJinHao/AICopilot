<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ShieldAlert } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiTag from '@/components/ai/AiTag.vue'
import {
  getApprovalSafeArgsSummary,
  getCanonicalApprovalIdentity,
  hasStrictApprovalIdentity,
} from '@/protocol/approvalProtocol'
import type { ApprovalChunk } from '@/types/models'

const props = defineProps<{
  chunk: ApprovalChunk
  isSubmitting?: boolean
}>()

const emit = defineEmits<{
  (event: 'approve', payload: { callId: string }): void
  (event: 'reject', payload: { callId: string }): void
}>()

const locallyLocked = ref(false)
const request = computed(() => props.chunk.request)
const isPending = computed(() => props.chunk.status === 'pending')
const hasStrictIdentity = computed(() => hasStrictApprovalIdentity(request.value))
const canonicalIdentity = computed(() => getCanonicalApprovalIdentity(request.value))
const safeArgsSummary = computed(() => getApprovalSafeArgsSummary(request.value))
const controlsLocked = computed(() => Boolean(props.isSubmitting || locallyLocked.value))
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
const approvalTitle = computed(() => '确认调用只读工具')

function approve() {
  if (isPending.value && hasStrictIdentity.value && !controlsLocked.value) {
    locallyLocked.value = true
    emit('approve', { callId: request.value.callId })
  }
}

function reject() {
  if (isPending.value && hasStrictIdentity.value && !controlsLocked.value) {
    locallyLocked.value = true
    emit('reject', { callId: request.value.callId })
  }
}

watch(
  () => props.isSubmitting,
  (isSubmitting, wasSubmitting) => {
    if (wasSubmitting && !isSubmitting && props.chunk.status === 'pending') {
      locallyLocked.value = false
    }
  },
)

watch(
  () => request.value.callId,
  () => {
    locallyLocked.value = false
  },
)
</script>

<template>
  <section class="approval-card" :class="chunk.status">
    <header>
      <span class="approval-icon"><ShieldAlert class="h-5 w-5" /></span>
      <div>
        <h3>{{ approvalTitle }}</h3>
        <p>仅对本次调用生效，提交后不能重复决定。</p>
      </div>
      <AiTag :tone="statusTone">{{ statusText }}</AiTag>
    </header>

    <div class="approval-body">
      <div v-if="!hasStrictIdentity" class="alert-danger">
        审批请求缺少完整执行身份，系统不会允许继续执行。
      </div>

      <div class="approval-facts">
        <div>
          <span>规范工具身份</span>
          <code>{{ canonicalIdentity || '身份不完整' }}</code>
        </div>
        <div>
          <span>安全参数摘要</span>
          <p>{{ safeArgsSummary }}</p>
        </div>
      </div>
    </div>

    <footer>
      <template v-if="isPending">
        <AiButton :disabled="controlsLocked || !hasStrictIdentity" @click="reject"> 拒绝 </AiButton>
        <AiButton
          variant="primary"
          :disabled="controlsLocked || !hasStrictIdentity"
          @click="approve"
        >
          {{ controlsLocked ? '提交中' : '批准' }}
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
  border: 1px solid color-mix(in srgb, var(--app-warning) 38%, var(--ai-border));
  border-radius: 16px;
  background: var(--ai-surface);
  box-shadow: var(--ai-shadow-xs);
}

.approval-card.approved {
  border-color: color-mix(in srgb, var(--app-success) 38%, var(--ai-border));
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
  border-radius: 11px;
  background: color-mix(in srgb, var(--app-warning) 12%, var(--ai-surface));
  color: var(--app-warning);
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

.approval-facts {
  display: grid;
  gap: 10px;
}

.approval-facts > div {
  display: grid;
  gap: 5px;
  border: 1px solid var(--ai-border);
  border-radius: 11px;
  padding: 10px 12px;
  background: var(--ai-surface-soft);
}

.approval-facts span {
  color: var(--ai-text-muted);
  font-size: 11px;
  font-weight: 800;
}

.approval-facts code,
.approval-facts p {
  margin: 0;
  overflow-wrap: anywhere;
  color: var(--ai-text);
  font-size: 12px;
  font-weight: 750;
  line-height: 1.5;
}

.approval-facts code {
  font-family: 'Cascadia Mono', 'SFMono-Regular', Consolas, monospace;
}

.alert-danger {
  border: 1px solid color-mix(in srgb, var(--app-danger) 36%, var(--ai-border));
  border-radius: 12px;
  background: color-mix(in srgb, var(--app-danger) 8%, var(--ai-surface));
  padding: 10px 12px;
  color: var(--app-danger);
  font-size: 13px;
  font-weight: 800;
}

footer {
  border-top: 1px solid var(--ai-border);
  background: color-mix(in srgb, var(--ai-surface) 72%, var(--ai-surface-soft));
}

.muted {
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 800;
}
</style>
