import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import type { ChatHistoryMessage } from '@/types/app'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage } from '@/types/models'
import { useSessionStore } from './sessionStore'

function isApprovalChunk(chunk: { type: ChunkType }): chunk is ApprovalChunk {
  return chunk.type === ChunkType.ApprovalRequest
}

export const useMessageStore = defineStore('chatMessage', () => {
  const sessionStore = useSessionStore()
  const messagesMap = ref<Record<string, ChatMessage[]>>({})

  const currentMessages = computed(() => {
    if (!sessionStore.currentSessionId) {
      return []
    }

    return messagesMap.value[sessionStore.currentSessionId] || []
  })

  function setHistory(sessionId: string, history: ChatHistoryMessage[]) {
    messagesMap.value[sessionId] = history.map((message) => ({
      sessionId: message.sessionId,
      role: message.role === MessageRole.User ? MessageRole.User : MessageRole.Assistant,
      chunks: [
        {
          source: message.role === MessageRole.User ? 'User' : 'FinalAgentRunExecutor',
          type: ChunkType.Text,
          content: message.content
        }
      ],
      isStreaming: false,
      timestamp: new Date(message.createdAt).getTime()
    }))
  }

  function ensureSession(sessionId: string) {
    if (!messagesMap.value[sessionId]) {
      messagesMap.value[sessionId] = []
    }
  }

  function addMessage(sessionId: string, message: ChatMessage) {
    ensureSession(sessionId)
    const list = messagesMap.value[sessionId]!
    list.push(message)
    return list[list.length - 1]!
  }

  function removeMessages(sessionId: string, ...messages: ChatMessage[]) {
    const list = messagesMap.value[sessionId]
    if (!list?.length) {
      return
    }

    const targets = new Set(messages)
    messagesMap.value[sessionId] = list.filter((message) => !targets.has(message))
  }

  function getLastAssistantMessage(sessionId: string) {
    const list = messagesMap.value[sessionId]
    if (!list?.length) {
      return null
    }

    const lastMessage = list[list.length - 1]!
    return lastMessage.role === MessageRole.Assistant ? lastMessage : null
  }

  function getApprovalChunks(sessionId: string) {
    return (messagesMap.value[sessionId] ?? [])
      .flatMap((message) => message.chunks)
      .filter(isApprovalChunk)
  }

  function reset() {
    messagesMap.value = {}
  }

  return {
    messagesMap,
    currentMessages,
    setHistory,
    ensureSession,
    addMessage,
    removeMessages,
    getLastAssistantMessage,
    getApprovalChunks,
    reset
  }
})
