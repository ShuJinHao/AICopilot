<script setup lang="ts">
import { computed } from 'vue'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/cn'

const buttonVariants = cva(
  'inline-flex min-h-10 items-center justify-center gap-2 whitespace-nowrap rounded-[14px] border px-4 text-sm font-bold transition disabled:pointer-events-none disabled:opacity-50',
  {
    variants: {
      variant: {
        primary: 'border-[var(--ai-graphite)] bg-[var(--ai-graphite)] text-white shadow-[0_10px_24px_rgba(63,111,115,0.2)] hover:bg-[var(--ai-graphite-soft)]',
        lime: 'border-[#c8ff3d] bg-[#c8ff3d] text-[var(--ai-graphite)] shadow-[0_10px_24px_rgba(200,255,61,0.18)] hover:bg-[#b7f531]',
        ghost: 'border-transparent bg-transparent text-[var(--ai-text-muted)] hover:bg-[var(--ai-surface-soft)] hover:text-[var(--ai-text)]',
        soft: 'border-[var(--ai-border)] bg-[var(--ai-surface)] text-[var(--ai-text)] shadow-[var(--ai-shadow-xs)] hover:border-[var(--ai-border-strong)]',
        danger: 'border-[#fecaca] bg-[#fef2f2] text-[#b42318] hover:bg-[#fee2e2]'
      },
      size: {
        sm: 'min-h-9 rounded-[12px] px-3 text-xs',
        md: 'min-h-11 px-4 text-sm',
        lg: 'min-h-[52px] rounded-[16px] px-5 text-base',
        icon: 'h-11 min-h-11 w-11 rounded-full p-0'
      }
    },
    defaultVariants: {
      variant: 'soft',
      size: 'md'
    }
  }
)

type ButtonVariants = VariantProps<typeof buttonVariants>

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariants['variant']
    size?: ButtonVariants['size']
    type?: 'button' | 'submit' | 'reset'
    disabled?: boolean
    class?: string
    ariaLabel?: string
  }>(),
  {
    variant: 'soft',
    size: 'md',
    type: 'button',
    disabled: false,
    class: '',
    ariaLabel: undefined
  }
)

const classes = computed(() => cn(buttonVariants({ variant: props.variant, size: props.size }), props.class))
</script>

<template>
  <button :type="type" :class="classes" :disabled="disabled" :aria-label="ariaLabel">
    <slot />
  </button>
</template>
