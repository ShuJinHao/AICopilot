<script setup lang="ts">
import AiToastHost from '@/components/ai/AiToastHost.vue'
import { useTheme } from '@/composables/useTheme'

useTheme()
</script>

<template>
  <RouterView />
  <AiToastHost />
</template>
