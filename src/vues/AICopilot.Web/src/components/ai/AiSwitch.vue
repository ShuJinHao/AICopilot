<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const model = defineModel<boolean>({ default: false })

const props = withDefaults(
  defineProps<{
    disabled?: boolean
    class?: string
    ariaLabel?: string
  }>(),
  {
    disabled: false,
    class: '',
    ariaLabel: undefined
  }
)

const classes = computed(() =>
  cn(
    'relative inline-flex h-7 w-12 items-center rounded-full border transition',
    model.value ? 'border-[#c8ff3d] bg-[#c8ff3d]' : 'border-[var(--ai-border)] bg-[var(--ai-surface-soft)]',
    props.disabled && 'cursor-not-allowed opacity-50',
    props.class
  )
)
</script>

<template>
  <button
    type="button"
    role="switch"
    :aria-checked="model"
    :aria-label="ariaLabel"
    :disabled="disabled"
    :class="classes"
    @click="model = !model"
  >
    <span
      :class="
        cn(
          'h-5 w-5 rounded-full bg-white shadow-[0_2px_8px_rgba(63,111,115,0.18)] transition',
          model ? 'translate-x-[22px]' : 'translate-x-1'
        )
      "
    />
  </button>
</template>
