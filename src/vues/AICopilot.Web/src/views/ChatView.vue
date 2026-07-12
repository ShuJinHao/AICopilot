<script setup lang="ts">
import { onMounted } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import ChatWindow from '@/components/chat/ChatWindow.vue'
import { useChatStore } from '@/stores/chatStore'

const chatStore = useChatStore()
chatStore.prepareInitialization()

onBeforeRouteLeave(() => !chatStore.isSessionTransitionBlocked)

onMounted(async () => {
  try {
    await chatStore.initialize()
  } catch (_error) {
    // initialize() already restored the prior session boundary and published the user-facing error.
  }
})
</script>

<template>
  <AppShell>
    <ChatWindow />
  </AppShell>
</template>
