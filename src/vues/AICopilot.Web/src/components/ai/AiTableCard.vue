<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string
    description?: string
    empty?: boolean
    emptyText?: string
  }>(),
  {
    title: '',
    description: '',
    empty: false,
    emptyText: '暂无数据'
  }
)
</script>

<template>
  <section class="overflow-hidden rounded-[24px] border border-[var(--ai-border)] bg-[var(--ai-surface)] shadow-[var(--ai-shadow-card)]">
    <header v-if="title || description || $slots.actions" class="flex items-start justify-between gap-4 border-b border-[var(--ai-border)] px-5 py-4">
      <div>
        <h2 v-if="title" class="text-base font-extrabold text-[var(--ai-text)]">{{ title }}</h2>
        <p v-if="description" class="mt-1 text-sm font-medium text-[var(--ai-text-muted)]">{{ description }}</p>
      </div>
      <slot name="actions" />
    </header>
    <div v-if="empty" class="flex min-h-[180px] items-center justify-center p-8 text-sm font-semibold text-[var(--ai-text-muted)]">
      {{ emptyText }}
    </div>
    <div v-else class="overflow-auto">
      <slot />
    </div>
  </section>
</template>
