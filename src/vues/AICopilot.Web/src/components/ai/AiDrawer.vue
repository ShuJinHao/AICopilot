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
    <div v-if="model" class="fixed inset-0 z-[80] bg-[rgba(48,83,86,0.32)] backdrop-blur-sm" @click.self="model = false">
      <aside
        class="ml-auto flex h-full w-full flex-col overflow-hidden border-l border-[var(--ai-border)] bg-[var(--ai-surface)] shadow-[var(--ai-shadow-shell)]"
        :style="{ maxWidth: width }"
      >
        <header class="flex items-start justify-between gap-4 border-b border-[var(--ai-border)] px-6 py-5">
          <div>
            <h2 class="text-lg font-extrabold text-[var(--ai-text)]">{{ title }}</h2>
            <p v-if="description" class="mt-1 text-sm font-medium text-[var(--ai-text-muted)]">{{ description }}</p>
          </div>
          <AiButton variant="ghost" size="icon" aria-label="关闭" @click="model = false">
            <X class="h-4 w-4" />
          </AiButton>
        </header>
        <div class="flex-1 overflow-auto p-6">
          <slot />
        </div>
      </aside>
    </div>
  </Teleport>
</template>
