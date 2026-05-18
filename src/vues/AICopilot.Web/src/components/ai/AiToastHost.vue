<script setup lang="ts">
import { CheckCircle2, Info, TriangleAlert, XCircle } from 'lucide-vue-next'
import { useAiToasts } from '@/composables/useAiFeedback'

const { toasts, removeToast } = useAiToasts()

const icons = {
  success: CheckCircle2,
  error: XCircle,
  warning: TriangleAlert,
  info: Info
}
</script>

<template>
  <Teleport to="body">
    <div class="fixed right-5 top-5 z-[100] flex w-[360px] max-w-[calc(100vw-40px)] flex-col gap-3">
      <button
        v-for="toast in toasts"
        :key="toast.id"
        type="button"
        class="flex items-start gap-3 rounded-[18px] border border-[var(--ai-border)] bg-[var(--ai-surface)] p-4 text-left shadow-[var(--ai-shadow-shell)]"
        @click="removeToast(toast.id)"
      >
        <component :is="icons[toast.type]" class="mt-0.5 h-5 w-5 text-[var(--ai-text)]" />
        <span class="text-sm font-bold leading-5 text-[var(--ai-text)]">{{ toast.message }}</span>
      </button>
    </div>
  </Teleport>
</template>
