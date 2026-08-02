<script setup lang="ts">
import { CircleOff, Plus } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'

const props = defineProps<{
  status: 'Interrupted' | 'ResetRequired'
  message: string
  busy?: boolean
}>()

const emit = defineEmits<{
  createSession: []
}>()
</script>

<template>
  <section class="session-recovery-notice" role="alert">
    <span class="notice-icon" aria-hidden="true">
      <CircleOff :size="20" />
    </span>
    <div class="notice-copy">
      <span>{{ props.status === 'Interrupted' ? '运行已中断' : '状态不可恢复' }}</span>
      <strong>
        {{ props.status === 'Interrupted' ? '请在新会话中继续' : '需要新建会话' }}
      </strong>
      <p>{{ message }}</p>
    </div>
    <AiButton
      variant="primary"
      :disabled="busy"
      aria-label="新建会话"
      @click="emit('createSession')"
    >
      <Plus :size="17" />
      新建会话
    </AiButton>
  </section>
</template>

<style scoped>
.session-recovery-notice {
  display: grid;
  grid-template-columns: 42px minmax(0, 1fr) auto;
  gap: 12px;
  max-width: 980px;
  margin: 0 auto 14px;
  border: 1px solid color-mix(in srgb, var(--app-danger) 34%, var(--ai-border));
  border-radius: 14px;
  padding: 14px;
  background: color-mix(in srgb, var(--app-danger) 7%, var(--ai-surface));
}

.notice-icon {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 12px;
  background: color-mix(in srgb, var(--app-danger) 12%, var(--ai-surface));
  color: var(--app-danger);
}

.notice-copy {
  display: grid;
  min-width: 0;
  gap: 2px;
}

.notice-copy > span {
  color: var(--app-danger);
  font-size: 11px;
  font-weight: 850;
}

.notice-copy strong {
  color: var(--ai-text);
  font-size: 14px;
  font-weight: 900;
}

.notice-copy p {
  margin: 2px 0 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 650;
  line-height: 1.55;
}

@media (max-width: 680px) {
  .session-recovery-notice {
    grid-template-columns: 42px minmax(0, 1fr);
  }

  .session-recovery-notice :deep(button) {
    grid-column: 1 / -1;
    width: 100%;
  }
}
</style>
