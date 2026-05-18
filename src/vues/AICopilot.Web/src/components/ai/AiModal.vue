<script setup lang="ts">
import { X } from 'lucide-vue-next'
import AiButton from './AiButton.vue'

const model = defineModel<boolean>({ default: false })

withDefaults(
  defineProps<{
    title: string
    description?: string
    width?: string
  }>(),
  {
    description: '',
    width: '520px'
  }
)
</script>

<template>
  <Teleport to="body">
    <div v-if="model" class="fixed inset-0 z-[80] flex items-center justify-center bg-[rgba(48,83,86,0.42)] p-5 backdrop-blur-sm">
      <section class="max-h-[88vh] w-full overflow-hidden rounded-[24px] border border-[var(--ai-border)] bg-[var(--ai-surface)] shadow-[var(--ai-shadow-shell)]" :style="{ maxWidth: width }">
        <header class="flex items-start justify-between gap-4 border-b border-[var(--ai-border)] px-6 py-5">
          <div>
            <h2 class="text-lg font-extrabold text-[var(--ai-text)]">{{ title }}</h2>
            <p v-if="description" class="mt-1 text-sm font-medium text-[var(--ai-text-muted)]">{{ description }}</p>
          </div>
          <AiButton variant="ghost" size="icon" aria-label="关闭" @click="model = false">
            <X class="h-4 w-4" />
          </AiButton>
        </header>
        <div class="max-h-[calc(88vh-92px)] overflow-auto p-6">
          <slot />
        </div>
      </section>
    </div>
  </Teleport>
</template>
