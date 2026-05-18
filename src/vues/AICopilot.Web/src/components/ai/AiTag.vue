<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const props = withDefaults(
  defineProps<{
    tone?: 'neutral' | 'lime' | 'teal' | 'blue' | 'warning' | 'danger' | 'success' | 'dark'
    class?: string
  }>(),
  {
    tone: 'neutral',
    class: ''
  }
)

const toneClass = computed(() => {
  const tones = {
    neutral: 'border-[var(--ai-border)] bg-[var(--ai-surface-soft)] text-[var(--ai-text-muted)]',
    lime: 'border-[#d8ff78] bg-[#efffbe] text-[#365314]',
    teal: 'border-[#99f6e4] bg-[#ecfeff] text-[#0f766e]',
    blue: 'border-[#bfdbfe] bg-[#eff6ff] text-[#1d4ed8]',
    warning: 'border-[#fed7aa] bg-[#fff7ed] text-[#b45309]',
    danger: 'border-[#fecaca] bg-[#fef2f2] text-[#b42318]',
    success: 'border-[#bbf7d0] bg-[#f0fdf4] text-[#15803d]',
    dark: 'border-[rgba(255,255,255,0.12)] bg-[var(--ai-graphite)] text-[#f8fafc]'
  }
  return tones[props.tone]
})
</script>

<template>
  <span
    :class="
      cn(
        'inline-flex min-h-7 items-center gap-1 rounded-full border px-3 text-xs font-bold leading-none',
        toneClass,
        props.class
      )
    "
  >
    <slot />
  </span>
</template>
