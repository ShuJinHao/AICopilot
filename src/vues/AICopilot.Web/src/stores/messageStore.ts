import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import type { ChatHistoryMessage } from '@/types/app'
import { ChunkType, MessageRole } from '@/types/protocols'
import type { ApprovalChunk, ChatMessage } from '@/types/models'
import { processChunk } from '@/protocol/chunkReducer'
import { useSessionStore } from './sessionStore'

const unknownModelLabel = '\u672a\u77e5'

// History render payload is only a stable display snapshot. Runtime state such as
// approvals, intent, and tool calls must come from live stores or timeline events.
const stableHistoryChunkTypes = new Set<ChunkType>([
  ChunkType.Text,
  ChunkType.Widget,
  ChunkType.Error
])

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
    messagesMap.value[sessionId] = history.map(toChatMessage)
  }

  function prependHistory(sessionId: string, history: ChatHistoryMessage[]) {
    if (!history.length) {
      return
    }

    ensureSession(sessionId)
    const list = messagesMap.value[sessionId]!
    const existingKeys = new Set(list.map(getMessageKey))
    const restored = history
      .map(toChatMessage)
      .filter((message) => !existingKeys.has(getMessageKey(message)))

    if (!restored.length) {
      return
    }

    messagesMap.value[sessionId] = [...restored, ...list]
  }

  function toChatMessage(message: ChatHistoryMessage): ChatMessage {
    const role = message.role === MessageRole.User ? MessageRole.User : MessageRole.Assistant
    const restored: ChatMessage = {
      messageId: message.messageId,
      sequence: message.sequence,
      sessionId: message.sessionId,
      role,
      finalModelId: role === MessageRole.Assistant ? (message.finalModelId ?? null) : null,
      finalModelName: role === MessageRole.Assistant ? (message.finalModelName ?? unknownModelLabel) : null,
      routingModelId: role === MessageRole.Assistant ? (message.routingModelId ?? null) : null,
      routingModelName: role === MessageRole.Assistant ? (message.routingModelName ?? null) : null,
      contextWindowTokens: role === MessageRole.Assistant ? (message.contextWindowTokens ?? null) : null,
      maxOutputTokens: role === MessageRole.Assistant ? (message.maxOutputTokens ?? null) : null,
      chunks: [],
      isStreaming: false,
      timestamp: new Date(message.createdAt).getTime()
    }

    const stableRenderChunks = message.renderChunks?.filter((chunk) => stableHistoryChunkTypes.has(chunk.type))
    const renderChunks = stableRenderChunks?.length
      ? stableRenderChunks
      : [
          {
            source: role === MessageRole.User ? 'User' : 'FinalAgentRunExecutor',
            type: ChunkType.Text,
            content: message.content
          }
        ]

    for (const chunk of renderChunks) {
      processChunk(restored, chunk, {
        setSessionError: () => {},
        onApprovalChunk: () => {}
      })
    }

    if (restored.chunks.length === 0 && message.content.trim()) {
      restored.chunks.push({
        source: role === MessageRole.User ? 'User' : 'FinalAgentRunExecutor',
        type: ChunkType.Text,
        content: message.content
      })
    }

    return restored
  }

  function ensureSession(sessionId: string) {
    if (!messagesMap.value[sessionId]) {
      messagesMap.value[sessionId] = []
    }
  }

  function getMessageKey(message: Pick<ChatMessage, 'messageId' | 'sequence' | 'role' | 'timestamp'>) {
    if (typeof message.messageId === 'number' && message.messageId > 0) {
      return `id:${message.messageId}`
    }

    if (typeof message.sequence === 'number' && message.sequence > 0) {
      return `seq:${message.sequence}`
    }

    return `local:${message.role}:${message.timestamp}`
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
    prependHistory,
    ensureSession,
    addMessage,
    removeMessages,
    getLastAssistantMessage,
    getApprovalChunks,
    reset
  }
})
