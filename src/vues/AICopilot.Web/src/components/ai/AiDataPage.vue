<script setup lang="ts">
withDefaults(
  defineProps<{
    eyebrow?: string
    title: string
    description?: string
  }>(),
  {
    eyebrow: '',
    description: ''
  }
)
</script>

<template>
  <div class="mx-auto flex w-full max-w-[1560px] flex-col gap-6">
    <header class="flex flex-wrap items-start justify-between gap-4">
      <div>
        <p v-if="eyebrow" class="text-xs font-extrabold uppercase tracking-[0.08em] text-[var(--ai-text-muted)]">{{ eyebrow }}</p>
        <h1 class="mt-1 text-[34px] font-black leading-tight text-[var(--ai-text)]">{{ title }}</h1>
        <p v-if="description" class="mt-2 max-w-3xl text-sm font-semibold leading-6 text-[var(--ai-text-muted)]">{{ description }}</p>
      </div>
      <slot name="actions" />
    </header>
    <slot />
  </div>
</template>
