<script setup lang="ts">
import { TriangleAlert } from 'lucide-vue-next'
import type { ChatErrorPresentation } from '@/stores/chatErrorStore'

defineProps<{
  error: ChatErrorPresentation
}>()
</script>

<template>
  <section class="chat-error-banner" role="alert" aria-live="assertive">
    <span class="error-icon" aria-hidden="true">
      <TriangleAlert :size="19" />
    </span>
    <div class="error-copy">
      <div class="error-title-row">
        <strong>{{ error.userFacingMessage || error.message }}</strong>
        <code v-if="error.code">{{ error.code }}</code>
      </div>
      <dl v-if="error.detail">
        <dt>详情</dt>
        <dd>{{ error.detail }}</dd>
      </dl>
    </div>
  </section>
</template>

<style scoped>
.chat-error-banner {
  display: grid;
  grid-template-columns: 38px minmax(0, 1fr);
  gap: 11px;
  max-width: 980px;
  margin: 0 auto 14px;
  border: 1px solid color-mix(in srgb, var(--ai-coral) 42%, var(--ai-border));
  border-radius: 14px;
  padding: 12px 14px;
  background: color-mix(in srgb, var(--ai-coral) 9%, var(--ai-surface));
  color: var(--ai-text);
}

.error-icon {
  display: grid;
  width: 38px;
  height: 38px;
  place-items: center;
  border-radius: 11px;
  background: color-mix(in srgb, var(--ai-coral) 15%, var(--ai-surface));
  color: var(--app-danger);
}

.error-copy {
  display: grid;
  min-width: 0;
  gap: 8px;
}

.error-title-row {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}

.error-title-row strong {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 13px;
  font-weight: 850;
  line-height: 1.55;
}

.error-title-row code {
  flex: 0 0 auto;
  border: 1px solid color-mix(in srgb, var(--ai-coral) 28%, var(--ai-border));
  border-radius: 7px;
  padding: 3px 7px;
  background: var(--ai-surface);
  color: var(--app-danger);
  font-size: 11px;
  font-weight: 800;
}

dl {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 8px;
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  line-height: 1.55;
}

dt {
  font-weight: 850;
}

dd {
  min-width: 0;
  margin: 0;
  overflow-wrap: anywhere;
}

@media (max-width: 640px) {
  .error-title-row {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
