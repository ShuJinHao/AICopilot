<template>
  <section class="flex flex-wrap items-center justify-between gap-3 rounded-[22px] border border-[var(--ai-border)] bg-[var(--ai-surface)] p-4 shadow-[var(--ai-shadow-card)]">
    <div class="flex min-w-0 flex-1 flex-wrap items-center gap-3">
      <slot />
    </div>
    <div v-if="$slots.actions" class="flex items-center gap-2">
      <slot name="actions" />
    </div>
  </section>
</template>
