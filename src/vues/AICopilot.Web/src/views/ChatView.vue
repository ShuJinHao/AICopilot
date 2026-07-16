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
  } catch (error) {
    // initialize() already restored the prior session boundary and published the user-facing error.
    console.error('Failed to initialize the chat view.', error)
  }
})
</script>

<template>
  <AppShell>
    <ChatWindow />
  </AppShell>
</template>
