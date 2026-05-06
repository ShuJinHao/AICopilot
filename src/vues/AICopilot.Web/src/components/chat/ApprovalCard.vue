<script setup lang="ts">
import { computed, ref } from 'vue'
import { WarningFilled } from '@element-plus/icons-vue'
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
      <el-icon><WarningFilled /></el-icon>
      <div>
        <h3>人工审批请求</h3>
        <p>模型请求调用受控工具，执行前需要人工复核。</p>
      </div>
      <el-tag :type="chunk.status === 'pending' ? 'warning' : chunk.status === 'approved' ? 'success' : 'info'">
        {{ statusText }}
      </el-tag>
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

      <el-alert
        v-if="!hasStrictIdentity"
        title="审批请求缺少完整工具身份，系统不会允许继续执行。"
        type="error"
        :closable="false"
        show-icon
      />

      <div class="args-block">
        <span>参数</span>
        <ArgumentViewer :args="request.args" />
      </div>

      <div v-if="request.requiresOnsiteAttestation" class="onsite-block">
        <strong>现场复核</strong>
        <p>此工具需要确认现场有人在岗，并再次确认执行前条件。</p>
        <p v-if="attestationExpiresText">会话在岗声明有效至：{{ attestationExpiresText }}</p>
        <el-checkbox v-if="isPending" v-model="onsiteConfirmed">
          现场有人在岗，且已复核执行前条件
        </el-checkbox>
      </div>
    </div>

    <footer>
      <template v-if="isPending">
        <el-button :disabled="isSubmitting || !hasStrictIdentity" @click="reject">拒绝</el-button>
        <el-button
          type="primary"
          :loading="isSubmitting"
          :disabled="!hasStrictIdentity || (request.requiresOnsiteAttestation && !onsiteConfirmed)"
          @click="approve"
        >
          批准
        </el-button>
      </template>
      <span v-else class="muted">审批状态：{{ statusText }}</span>
    </footer>
  </section>
</template>

<style scoped>
.approval-card {
  display: grid;
  gap: 0;
  border: 1px solid #f2c97d;
  border-radius: 8px;
  background: #fffaf0;
  overflow: hidden;
}

.approval-card.approved {
  border-color: #9bd3a8;
  background: #f3fbf4;
}

.approval-card.rejected,
.approval-card.expired {
  border-color: var(--app-border);
  background: var(--app-surface-muted);
}

.approval-card header,
.approval-card footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
}

.approval-card header {
  border-bottom: 1px solid rgba(180, 83, 9, 0.18);
}

.approval-card header > div {
  flex: 1;
  min-width: 0;
}

.approval-card h3 {
  margin: 0;
  font-size: 14px;
  font-weight: 750;
}

.approval-card p {
  margin: 2px 0 0;
  color: var(--app-text-muted);
  font-size: 12px;
}

.approval-body {
  display: grid;
  gap: 12px;
  padding: 14px;
}

dl {
  display: grid;
  gap: 8px;
  margin: 0;
}

dl div {
  display: grid;
  grid-template-columns: 72px minmax(0, 1fr);
  gap: 8px;
}

dt {
  color: var(--app-text-muted);
}

dd {
  margin: 0;
  overflow-wrap: anywhere;
}

.args-block,
.onsite-block {
  display: grid;
  gap: 8px;
}

.args-block > span {
  color: var(--app-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.onsite-block {
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 10px;
  background: #ffffff;
}

footer {
  border-top: 1px solid var(--app-border);
  background: rgba(255, 255, 255, 0.72);
}
</style>
