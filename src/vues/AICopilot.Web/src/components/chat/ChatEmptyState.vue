<script setup lang="ts">
import { computed } from 'vue'
import { ChevronRight, ListChecks, Search } from 'lucide-vue-next'
import { getChatModePresentation, type ChatAgentMode } from '@/protocol/chatPresentation'

const props = defineProps<{
  mode: ChatAgentMode
}>()

const emit = defineEmits<{
  useSuggestion: [text: string]
}>()

const presentation = computed(() => getChatModePresentation(props.mode))
const modeIcon = computed(() => (props.mode === 'plan' ? ListChecks : Search))
</script>

<template>
  <section class="empty-chat">
    <div class="empty-chat-copy">
      <span class="empty-mode-icon">
        <component :is="modeIcon" :size="21" />
      </span>
      <div>
        <span class="empty-mode-label">{{ presentation.shortLabel }}</span>
        <h2>{{ presentation.title }}</h2>
        <p>{{ presentation.description }} 模式只会在你明确切换后改变。</p>
      </div>
    </div>
    <div class="suggestions">
      <button
        v-for="item in presentation.suggestions"
        :key="item"
        type="button"
        @click="emit('useSuggestion', item)"
      >
        {{ item }}
        <ChevronRight :size="17" />
      </button>
    </div>
  </section>
</template>
