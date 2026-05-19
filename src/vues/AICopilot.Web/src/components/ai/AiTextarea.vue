<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const model = defineModel<string | null>()

const props = withDefaults(
  defineProps<{
    placeholder?: string
    rows?: number
    disabled?: boolean
    class?: string
  }>(),
  {
    placeholder: '',
    rows: 4,
    disabled: false,
    class: ''
  }
)

const classes = computed(() =>
  cn(
    'min-h-24 w-full resize-y rounded-[14px] border border-[var(--ai-border)] bg-[var(--ai-surface)] px-4 py-3 text-sm font-semibold leading-6 text-[var(--ai-text)] outline-none transition placeholder:text-[var(--ai-text-subtle)] focus:border-[var(--ai-border-strong)] focus:shadow-[0_0_0_4px_rgba(63,111,115,0.10)] disabled:cursor-not-allowed disabled:bg-[var(--ai-surface-soft)] disabled:text-[var(--ai-text-subtle)]',
    props.class
  )
)
</script>

<template>
  <textarea v-model="model" :rows="rows" :placeholder="placeholder" :disabled="disabled" :class="classes" />
</template>
