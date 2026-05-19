<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/cn'

const props = withDefaults(
  defineProps<{
    tone?: 'surface' | 'soft' | 'dark' | 'lime' | 'teal' | 'blue' | 'violet'
    class?: string
  }>(),
  {
    tone: 'surface',
    class: ''
  }
)

const toneClass = computed(() => {
  const tones = {
    surface: 'border-[var(--ai-border)] bg-[var(--ai-surface)] text-[var(--ai-text)] shadow-[var(--ai-shadow-card)]',
    soft: 'border-[var(--ai-border)] bg-[var(--ai-surface-soft)] text-[var(--ai-text)]',
    dark: 'border-[rgba(255,255,255,0.14)] bg-[var(--ai-graphite)] text-[#f8fafc] shadow-[0_18px_36px_rgba(63,111,115,0.2)]',
    lime: 'border-[#ddff88] bg-[#efffbe] text-[var(--ai-graphite)]',
    teal: 'border-[#99f6e4] bg-[#ccfbf1] text-[#0f2f2c]',
    blue: 'border-[#bfdbfe] bg-[#dbeafe] text-[#172554]',
    violet: 'border-[#ddd6fe] bg-[#ede9fe] text-[#2e1065]'
  }
  return tones[props.tone]
})
</script>

<template>
  <section :class="cn('rounded-[24px] border p-5', toneClass, props.class)">
    <slot />
  </section>
</template>
