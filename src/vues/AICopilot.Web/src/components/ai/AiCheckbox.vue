<script setup lang="ts">
const model = defineModel<boolean>({ default: false })

withDefaults(
  defineProps<{
    disabled?: boolean
  }>(),
  {
    disabled: false
  }
)
</script>

<template>
  <label class="inline-flex cursor-pointer items-center gap-2 text-sm font-semibold text-[var(--ai-text)]">
    <input
      v-model="model"
      type="checkbox"
      :disabled="disabled"
      class="h-4 w-4 rounded border-[var(--ai-border)] accent-[var(--ai-graphite)] disabled:cursor-not-allowed disabled:opacity-50"
    />
    <slot />
  </label>
</template>
