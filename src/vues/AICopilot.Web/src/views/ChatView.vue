<script setup lang="ts">
import { onMounted } from 'vue'
import AppShell from '@/components/layout/AppShell.vue'
import ChatWindow from '@/components/chat/ChatWindow.vue'
import { useChatStore } from '@/stores/chatStore'

const chatStore = useChatStore()

onMounted(async () => {
  await chatStore.initialize()
})
</script>

<template>
  <AppShell>
    <ChatWindow />
  </AppShell>
</template>
