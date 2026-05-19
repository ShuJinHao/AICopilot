<script setup lang="ts">
import { TabsList, TabsRoot, TabsTrigger } from 'reka-ui'
import { cn } from '@/lib/cn'

defineProps<{
  items: Array<{ value: string; label: string; count?: number }>
  modelValue: string
  class?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()
</script>

<template>
  <TabsRoot :model-value="modelValue" :class="cn('grid gap-3', $props.class)" @update:model-value="(value) => emit('update:modelValue', String(value))">
    <TabsList class="flex flex-wrap gap-2 rounded-[18px] bg-[var(--ai-surface-soft)] p-1">
      <TabsTrigger
        v-for="item in items"
        :key="item.value"
        :value="item.value"
        class="inline-flex min-h-9 items-center justify-center gap-1 rounded-[14px] px-3 text-xs font-bold text-[var(--ai-text-muted)] outline-none transition data-[state=active]:bg-[var(--ai-surface)] data-[state=active]:text-[var(--ai-text)] data-[state=active]:shadow-[var(--ai-shadow-xs)]"
      >
        {{ item.label }}
        <span v-if="item.count !== undefined" class="rounded-full bg-[#c8ff3d] px-1.5 py-0.5 text-[10px] text-[var(--ai-graphite)]">
          {{ item.count }}
        </span>
      </TabsTrigger>
    </TabsList>
    <slot />
  </TabsRoot>
</template>
