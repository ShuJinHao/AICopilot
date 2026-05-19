<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const model = defineModel<string | number | null>()

const props = withDefaults(
  defineProps<{
    type?: 'text' | 'password' | 'search' | 'email' | 'url'
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    class?: string
    autocomplete?: string
  }>(),
  {
    type: 'text',
    placeholder: '',
    disabled: false,
    readonly: false,
    class: '',
    autocomplete: undefined
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
    v-model="model"
    :type="type"
    :placeholder="placeholder"
    :disabled="disabled"
    :readonly="readonly"
    :autocomplete="autocomplete"
    :class="classes"
  />
</template>
