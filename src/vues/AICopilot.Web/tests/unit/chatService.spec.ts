import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const serviceMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
  fetchEventSource: vi.fn(),
}))

vi.mock('@/appsetting', () => ({ baseUrl: '/api' }))
vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: serviceMocks.fetchEventSource,
}))
vi.mock('@/services/apiClient', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/services/apiClient')>()
  return {
    ...original,
    apiClient: {
      get: serviceMocks.get,
      post: serviceMocks.post,
      put: serviceMocks.put,
      delete: serviceMocks.delete,
    },
    getAccessToken: vi.fn(() => null),
  }
})

import { chatService } from '@/services/chatService'

const callbacks = {
  onChunkReceived: vi.fn(),
  onComplete: vi.fn(),
  onError: vi.fn(),
}

describe('chatService current Harness contract', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    serviceMocks.get.mockResolvedValue([])
    serviceMocks.post.mockResolvedValue({ id: 'session-1' })
  })

  afterEach(() => vi.useRealTimers())

  it('applies bounded read and mutation timeouts', async () => {
    await chatService.getSessions()
    await chatService.createSession()

    expect(serviceMocks.get).toHaveBeenCalledWith('/aigateway/session/list', undefined, {
      timeoutMs: 30_000,
    })
    expect(serviceMocks.post).toHaveBeenCalledWith('/aigateway/session', {}, { timeoutMs: 60_000 })
  })

  it('sends an optimistic version with the authenticated Agent mode mutation', async () => {
    serviceMocks.put.mockResolvedValue({ sessionId: 'session/1', mode: 'execute', version: 12 })

    await chatService.updateAgentMode('session/1', 'execute', 11)

    expect(serviceMocks.put).toHaveBeenCalledWith(
      '/aigateway/session/session%2F1/agent-mode',
      { mode: 'execute', expectedVersion: 11 },
      { timeoutMs: 60_000 },
    )
  })

  it('sends only sessionId and message for a chat turn', async () => {
    serviceMocks.fetchEventSource.mockImplementation(async (_url, options) => {
      const streamOptions = options as { body?: string; onclose?: () => void }
      expect(JSON.parse(streamOptions.body ?? '{}')).toEqual({
        sessionId: 'session-1',
        message: '继续解释',
      })
      streamOptions.onclose?.()
    })

    await chatService.sendMessageStream('session-1', '继续解释', callbacks)

    expect(callbacks.onComplete).toHaveBeenCalledOnce()
  })

  it('sends only the protected approval decision keys', async () => {
    serviceMocks.fetchEventSource.mockImplementation(async (_url, options) => {
      const streamOptions = options as { body?: string; onclose?: () => void }
      expect(JSON.parse(streamOptions.body ?? '{}')).toEqual({
        sessionId: 'session-1',
        callId: 'call-1',
        decision: 'approved',
      })
      streamOptions.onclose?.()
    })

    await chatService.sendApprovalDecisionStream(
      'session-1',
      'call-1',
      'approved',
      callbacks,
    )

    expect(callbacks.onComplete).toHaveBeenCalledOnce()
  })

  it('aborts a silent stream and reports a bounded timeout', async () => {
    vi.useFakeTimers()
    serviceMocks.fetchEventSource.mockImplementation(
      (_url, options) =>
        new Promise<void>((resolve) => {
          const signal = (options as { signal?: AbortSignal }).signal
          signal?.addEventListener('abort', () => resolve(), { once: true })
        }),
    )

    const streamPromise = chatService.sendMessageStream('session-1', '你好', callbacks)
    await vi.advanceTimersByTimeAsync(10 * 60_000)
    await streamPromise

    expect(serviceMocks.fetchEventSource).toHaveBeenCalledOnce()
    expect(callbacks.onError).toHaveBeenCalledWith(
      expect.objectContaining({
        status: 408,
        details: expect.objectContaining({ code: 'client_stream_timeout' }),
      }),
    )
  })

  it('never reconnects a mutating stream after partial output disconnects', async () => {
    serviceMocks.fetchEventSource.mockImplementation(async (_url, options) => {
      const streamOptions = options as {
        onmessage?: (event: { data: string }) => void
        onerror?: (error: unknown) => unknown
      }
      streamOptions.onmessage?.({
        data: JSON.stringify({ source: 'HarnessAgent', type: 'Text', content: '局部输出' }),
      })
      const disconnectError = new Error('connection lost after partial output')
      expect(() => streamOptions.onerror?.(disconnectError)).toThrow(disconnectError)
      throw disconnectError
    })

    await chatService.sendMessageStream('session-1', '你好', callbacks)

    expect(serviceMocks.fetchEventSource).toHaveBeenCalledOnce()
    expect(callbacks.onChunkReceived).toHaveBeenCalledOnce()
    expect(callbacks.onError).toHaveBeenCalledOnce()
  })
})
