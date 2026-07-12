import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { chatService } from '@/services/chatService'
import type { Session } from '@/types/protocols'

const CURRENT_SESSION_KEY = 'aicopilot.chat.currentSessionId'

export const useSessionStore = defineStore('chatSession', () => {
  const sessions = ref<Session[]>([])
  const currentSessionId = ref<string | null>(sessionStorage.getItem(CURRENT_SESSION_KEY))
  const activeSessionId = ref<string | null>(null)
  const isSessionActivating = ref(false)
  const isLoadingHistory = ref(false)

  const currentSession = computed(() => {
    if (!currentSessionId.value) {
      return null
    }

    return sessions.value.find((session) => session.id === currentSessionId.value) ?? null
  })

  const activeSession = computed(() => {
    if (!activeSessionId.value) {
      return null
    }

    return sessions.value.find((session) => session.id === activeSessionId.value) ?? null
  })

  function persistCurrentSession(sessionId: string | null) {
    currentSessionId.value = sessionId

    if (sessionId) {
      sessionStorage.setItem(CURRENT_SESSION_KEY, sessionId)
      return
    }

    sessionStorage.removeItem(CURRENT_SESSION_KEY)
  }

  function activateSession(sessionId: string | null) {
    activeSessionId.value = sessionId && sessions.value.some((session) => session.id === sessionId)
      ? sessionId
      : null
  }

  function beginSessionActivation(nextSessionId?: string | null) {
    isSessionActivating.value = true
    if (nextSessionId !== undefined && activeSessionId.value !== nextSessionId) {
      activeSessionId.value = null
    }
  }

  function completeSessionActivation(sessionId: string | null) {
    activateSession(sessionId)
    isSessionActivating.value = false
  }

  function failSessionActivation(fallbackSessionId: string | null = null) {
    activateSession(fallbackSessionId)
    isSessionActivating.value = false
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
    if (activeSessionId.value && !sessions.value.some((session) => session.id === activeSessionId.value)) {
      activeSessionId.value = null
    }
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
    if (activeSessionId.value === id) {
      activeSessionId.value = null
    }
    if (currentSessionId.value === id) {
      persistCurrentSession(sessions.value[0]?.id ?? null)
    }
  }

  function reset() {
    sessions.value = []
    persistCurrentSession(null)
    activeSessionId.value = null
    isSessionActivating.value = false
    isLoadingHistory.value = false
  }

  return {
    sessions,
    currentSessionId,
    currentSession,
    activeSessionId,
    activeSession,
    isSessionActivating,
    isLoadingHistory,
    persistCurrentSession,
    activateSession,
    beginSessionActivation,
    completeSessionActivation,
    failSessionActivation,
    upsertSession,
    loadSessions,
    createSession,
    deleteSession,
    reset
  }
})
