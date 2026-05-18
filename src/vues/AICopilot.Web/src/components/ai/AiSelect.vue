<script setup lang="ts">
import { computed } from 'vue'
import { ChevronDown } from 'lucide-vue-next'
import { cn } from '@/lib/cn'

type SelectValue = string | number | boolean | null

const model = defineModel<SelectValue>()

const props = withDefaults(
  defineProps<{
    options: Array<{ label: string; value: SelectValue; disabled?: boolean }>
    placeholder?: string
    disabled?: boolean
    class?: string
  }>(),
  {
    placeholder: '请选择',
    disabled: false,
    class: ''
  }
)

const selectedIndex = computed(() => props.options.findIndex((option) => Object.is(option.value, model.value)))

function updateValue(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  if (value === '') {
    model.value = null
    return
  }
  model.value = props.options[Number(value)]?.value ?? null
}

const classes = computed(() =>
  cn(
    'h-11 w-full appearance-none rounded-[14px] border border-[var(--ai-border)] bg-[var(--ai-surface)] px-4 pr-10 text-sm font-semibold text-[var(--ai-text)] outline-none transition focus:border-[var(--ai-border-strong)] focus:shadow-[0_0_0_4px_rgba(63,111,115,0.10)] disabled:cursor-not-allowed disabled:bg-[var(--ai-surface-soft)] disabled:text-[var(--ai-text-subtle)]',
    props.class
  )
)
</script>

<template>
  <label class="relative block">
    <select :value="selectedIndex >= 0 ? String(selectedIndex) : ''" :disabled="disabled" :class="classes" @change="updateValue">
      <option value="" disabled>{{ placeholder }}</option>
      <option v-for="(option, index) in options" :key="`${index}-${option.label}`" :value="String(index)" :disabled="option.disabled">
        {{ option.label }}
      </option>
    </select>
    <ChevronDown class="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--ai-text-muted)]" />
  </label>
</template>
