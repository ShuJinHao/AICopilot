<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const model = defineModel<number | null>()

const props = withDefaults(
  defineProps<{
    min?: number
    max?: number
    step?: number
    placeholder?: string
    disabled?: boolean
    class?: string
  }>(),
  {
    min: undefined,
    max: undefined,
    step: 1,
    placeholder: '',
    disabled: false,
    class: ''
  }
)

const classes = computed(() =>
  cn(
    'h-11 w-full rounded-[14px] border border-[var(--ai-border)] bg-[var(--ai-surface)] px-4 text-sm font-semibold text-[var(--ai-text)] outline-none transition placeholder:text-[var(--ai-text-subtle)] focus:border-[var(--ai-border-strong)] focus:shadow-[0_0_0_4px_rgba(63,111,115,0.10)] disabled:cursor-not-allowed disabled:bg-[var(--ai-surface-soft)] disabled:text-[var(--ai-text-subtle)]',
    props.class
  )
)
</script>

<template>
  <input
    v-model.number="model"
    type="number"
    :min="min"
    :max="max"
    :step="step"
    :placeholder="placeholder"
    :disabled="disabled"
    :class="classes"
  />
</template>
