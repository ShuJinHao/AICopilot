<script setup lang="ts">
import { TooltipArrow, TooltipContent, TooltipPortal, TooltipProvider, TooltipRoot, TooltipTrigger } from 'reka-ui'

defineProps<{
  content: string
}>()
</script>

<template>
  <TooltipProvider :delay-duration="120">
    <TooltipRoot>
      <TooltipTrigger as-child>
        <slot />
      </TooltipTrigger>
      <TooltipPortal>
        <TooltipContent
          side="right"
          :side-offset="10"
          class="z-50 rounded-[10px] bg-[var(--ai-graphite)] px-3 py-2 text-xs font-bold text-white shadow-[0_14px_28px_rgba(63,111,115,0.22)]"
        >
          {{ content }}
          <TooltipArrow class="fill-[var(--ai-graphite)]" />
        </TooltipContent>
      </TooltipPortal>
    </TooltipRoot>
  </TooltipProvider>
</template>
