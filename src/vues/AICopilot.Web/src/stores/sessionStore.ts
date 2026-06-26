import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type { Session } from '@/types/protocols'

const CURRENT_SESSION_KEY = 'aicopilot.chat.currentSessionId'

export const useSessionStore = defineStore('chatSession', () => {
  const sessions = ref<Session[]>([])
  const currentSessionId = ref<string | null>(sessionStorage.getItem(CURRENT_SESSION_KEY))
  const isLoadingHistory = ref(false)

  const currentSession = computed(() => {
    if (!currentSessionId.value) {
      return null
    }

    return sessions.value.find((session) => session.id === currentSessionId.value) ?? null
  })

  function persistCurrentSession(sessionId: string | null) {
    currentSessionId.value = sessionId

    if (sessionId) {
      sessionStorage.setItem(CURRENT_SESSION_KEY, sessionId)
      return
    }

    sessionStorage.removeItem(CURRENT_SESSION_KEY)
  }

  function upsertSession(session: Session) {
    const index = sessions.value.findIndex((item) => item.id === session.id)
    if (index >= 0) {
      sessions.value[index] = session
      return
    }

    sessions.value = [session, ...sessions.value]
  }

  async function loadSessions() {
    sessions.value = await chatService.getSessions()
  }

  async function createSession() {
    const newSession = await chatService.createSession()
    upsertSession(newSession)
    persistCurrentSession(newSession.id)
    return newSession
  }

  async function deleteSession(id: string) {
    await chatService.deleteSession(id)
    sessions.value = sessions.value.filter((session) => session.id !== id)
    if (currentSessionId.value === id) {
      persistCurrentSession(sessions.value[0]?.id ?? null)
    }
  }

  function reset() {
    sessions.value = []
    persistCurrentSession(null)
    isLoadingHistory.value = false
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    isLoadingHistory,
    persistCurrentSession,
    upsertSession,
    loadSessions,
    createSession,
    deleteSession,
    reset
  }
})
