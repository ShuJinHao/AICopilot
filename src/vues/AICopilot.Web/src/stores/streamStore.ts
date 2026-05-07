import { ref } from 'vue'
import { defineStore } from 'pinia'

export const useStreamStore = defineStore('chatStream', () => {
  const isStreaming = ref(false)

  function start() {
    isStreaming.value = true
  }

  function stop() {
    isStreaming.value = false
  }

  function reset() {
    isStreaming.value = false
  }

  return {
    isStreaming,
    start,
    stop,
    reset
  }
})
