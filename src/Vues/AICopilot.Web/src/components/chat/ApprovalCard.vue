<script setup lang="ts">
import { computed, ref } from 'vue'
import type { ApprovalChunk } from '@/types/models'
import ArgumentViewer from './ArgumentViewer.vue'

interface Props {
  chunk: ApprovalChunk
}

const props = defineProps<Props>()

const emit = defineEmits<{
  (
    e: 'approve',
    payload: {
      callId: string
      onsiteConfirmed: boolean
    }
  ): void
  (e: 'reject', payload: { callId: string }): void
}>()

const isProcessing = ref(false)
const onsiteConfirmed = ref(false)

const request = computed(() => props.chunk.request)
const status = computed(() => props.chunk.status)
const isPending = computed(() => status.value === 'pending')
const attestationExpiresText = computed(() => {
  if (!request.value.attestationExpiresAt) {
    return ''
  }

  return new Date(request.value.attestationExpiresAt).toLocaleString('zh-CN', {
    hour12: false
  })
})

function handleApprove() {
  if (isProcessing.value) {
    return
  }

  isProcessing.value = true
  emit('approve', {
    callId: request.value.callId,
    onsiteConfirmed: onsiteConfirmed.value
  })
}

function handleReject() {
  if (isProcessing.value) {
    return
  }

  isProcessing.value = true
  emit('reject', {
    callId: request.value.callId
  })
}
</script>

<template>
  <div class="approval-card" :class="status">
    <div class="card-header">
      <div class="header-icon">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
          />
        </svg>
      </div>
      <div class="header-content">
        <h3 class="title">敏感建议审批</h3>
        <p class="subtitle">AI 请求执行需要人工复核的外部工具能力。</p>
      </div>
      <div v-if="!isPending" class="status-badge" :class="status">
        {{ status === 'approved' ? '已批准' : '已拒绝' }}
      </div>
    </div>

    <div class="card-body">
      <div class="function-info">
        <span class="label">目标工具</span>
        <code class="function-name">{{ request.name }}</code>
      </div>

      <div class="arguments-section">
        <span class="label">参数详情</span>
        <ArgumentViewer :args="request.args" />
      </div>

      <div v-if="request.requiresOnsiteAttestation" class="onsite-panel">
        <div class="onsite-title">在岗复核</div>
        <div class="onsite-text">
          该建议必须在会话已有在岗声明的前提下，再次确认“现场有人在岗”后才能批准。
        </div>
        <div v-if="attestationExpiresText" class="onsite-expiry">
          当前会话在岗声明有效期至：{{ attestationExpiresText }}
        </div>
        <label v-if="isPending" class="onsite-check">
          <input v-model="onsiteConfirmed" type="checkbox" />
          <span>现场有人在岗，我已复核执行前条件。</span>
        </label>
      </div>
    </div>

    <div class="card-footer">
      <template v-if="isPending">
        <button class="btn btn-reject" @click="handleReject" :disabled="isProcessing">
          拒绝执行
        </button>
        <button
          class="btn btn-approve"
          @click="handleApprove"
          :disabled="isProcessing || (request.requiresOnsiteAttestation && !onsiteConfirmed)"
        >
          <span v-if="isProcessing">处理中...</span>
          <span v-else>批准执行</span>
        </button>
      </template>

      <div v-else class="result-message">
        <span v-if="status === 'approved'" class="text-success">审批已通过。</span>
        <span v-else class="text-danger">审批已拒绝。</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.approval-card {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: white;
  margin: 12px 0;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
  overflow: hidden;
  transition: all 0.3s ease;
  max-width: 640px;
}

.approval-card.rejected {
  opacity: 0.7;
  border-color: #cbd5e1;
  background: #f8fafc;
}

.approval-card.approved {
  border-color: #bbf7d0;
  background: #f0fdf4;
}

.card-header {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  background: #fff7ed;
  border-bottom: 1px solid #ffedd5;
}

.approval-card.approved .card-header {
  background: #dcfce7;
  border-bottom-color: #bbf7d0;
}

.approval-card.rejected .card-header {
  background: #f1f5f9;
  border-bottom-color: #e2e8f0;
}

.header-icon {
  width: 24px;
  height: 24px;
  margin-right: 12px;
  color: #ea580c;
}

.header-content {
  flex: 1;
}

.title {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: #1e293b;
}

.subtitle {
  margin: 0;
  font-size: 0.8rem;
  color: #64748b;
}

.card-body {
  padding: 16px;
  display: grid;
  gap: 12px;
}

.function-info {
  display: flex;
  align-items: center;
  gap: 8px;
}

.label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #64748b;
  min-width: 72px;
}

.function-name {
  background: #e0e7ff;
  color: #4338ca;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
  font-weight: bold;
}

.onsite-panel {
  padding: 10px 12px;
  border-radius: 8px;
  background: #f8fafc;
  border: 1px solid #dbeafe;
  display: grid;
  gap: 6px;
}

.onsite-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #0f172a;
}

.onsite-text,
.onsite-expiry {
  font-size: 0.82rem;
  color: #475569;
}

.onsite-check {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: #0f172a;
}

.card-footer {
  padding: 12px 16px;
  background: #f8fafc;
  border-top: 1px solid #e2e8f0;
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.2s;
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-reject {
  background: white;
  border-color: #cbd5e1;
  color: #475569;
}

.btn-reject:hover:not(:disabled) {
  background: #f1f5f9;
  color: #ef4444;
  border-color: #ef4444;
}

.btn-approve {
  background: #2563eb;
  color: white;
}

.btn-approve:hover:not(:disabled) {
  background: #1d4ed8;
}

.status-badge {
  font-size: 0.8rem;
  padding: 2px 8px;
  border-radius: 12px;
  font-weight: 600;
}

.status-badge.approved {
  background: #166534;
  color: white;
}

.status-badge.rejected {
  background: #64748b;
  color: white;
}

.result-message {
  font-size: 0.9rem;
  font-weight: 500;
}

.text-success {
  color: #166534;
}

.text-danger {
  color: #991b1b;
}
</style>
