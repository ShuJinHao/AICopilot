import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const serviceMocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  postForm: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
  download: vi.fn(),
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
      postForm: serviceMocks.postForm,
      put: serviceMocks.put,
      delete: serviceMocks.delete,
      download: serviceMocks.download,
    },
    getAccessToken: vi.fn(() => null),
  }
})

import { chatService } from '@/services/chatService'

describe('chatService bounded requests', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    serviceMocks.get.mockResolvedValue([])
    serviceMocks.post.mockResolvedValue({ id: 'session-1' })
    serviceMocks.download.mockResolvedValue(new Blob())
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('applies explicit read, mutation, and transfer timeouts', async () => {
    await chatService.getSessions()
    await chatService.createSession()
    await chatService.downloadArtifact('/api/aigateway/artifact/file/download')

    expect(serviceMocks.get).toHaveBeenCalledWith('/aigateway/session/list', undefined, {
      timeoutMs: 30_000,
    })
    expect(serviceMocks.post).toHaveBeenCalledWith('/aigateway/session', {}, { timeoutMs: 60_000 })
    expect(serviceMocks.download).toHaveBeenCalledWith(
      '/api/aigateway/artifact/file/download',
      undefined,
      { timeoutMs: 120_000 },
    )
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
    const callbacks = {
      onChunkReceived: vi.fn(),
      onComplete: vi.fn(),
      onError: vi.fn(),
    }

    const streamPromise = chatService.sendMessageStream('session-1', '你好', callbacks)
    await vi.advanceTimersByTimeAsync(10 * 60_000)
    await streamPromise

    expect(serviceMocks.fetchEventSource).toHaveBeenCalledTimes(1)
    expect(callbacks.onComplete).not.toHaveBeenCalled()
    expect(callbacks.onError).toHaveBeenCalledWith(
      expect.objectContaining({
        status: 408,
        details: expect.objectContaining({ code: 'client_stream_timeout' }),
      }),
    )
  })

  it('never retries a mutating stream after partial output disconnects', async () => {
    serviceMocks.fetchEventSource.mockImplementation(async (_url, options) => {
      const streamOptions = options as {
        onmessage?: (event: { data: string }) => void
        onerror?: (error: unknown) => unknown
      }
      streamOptions.onmessage?.({
        data: JSON.stringify({ source: 'Agent', type: 1, content: '局部输出' }),
      })
      const disconnectError = new Error('connection lost after partial output')
      expect(() => streamOptions.onerror?.(disconnectError)).toThrow(disconnectError)
      throw disconnectError
    })
    const callbacks = {
      onChunkReceived: vi.fn(),
      onComplete: vi.fn(),
      onError: vi.fn(),
    }

    await chatService.sendMessageStream('session-1', '你好', callbacks)

    expect(serviceMocks.fetchEventSource).toHaveBeenCalledTimes(1)
    expect(callbacks.onChunkReceived).toHaveBeenCalledTimes(1)
    expect(callbacks.onComplete).not.toHaveBeenCalled()
    expect(callbacks.onError).toHaveBeenCalledTimes(1)
    expect(callbacks.onError).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'connection lost after partial output' }),
    )
  })
})
