import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import {
  createChatErrorPresentation,
  resolveChatErrorMessage,
  toChatErrorPresentation,
  toFriendlyMessage,
  useChatErrorStore,
} from '@/stores/chatErrorStore'

describe('chatErrorStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('prefers backend user-facing error text', () => {
    expect(
      resolveChatErrorMessage({
        code: 'approval_pending',
        userFacingMessage: 'custom approval message',
      }),
    ).toBe('custom approval message')
  })

  it('keeps stable fallback messages for known error codes', () => {
    expect(
      resolveChatErrorMessage({
        code: 'tool_blocked',
        detail: 'toolCode query_device_logs is outside the requested capability range.',
      }),
    ).toBe('该工具被安全策略阻断。')

    expect(
      resolveChatErrorMessage({
        code: 'agent_session_interrupted',
      }),
    ).toBe('上一次执行已中断；系统不会自动重放，请新建会话后继续。')

    expect(
      resolveChatErrorMessage({
        code: 'agent_session_version_conflict',
        detail: 'raw state must never be rendered',
      }),
    ).toBe('会话状态已变化，请刷新后重试。')

    expect(
      resolveChatErrorMessage({
        code: 'approval_already_processed',
      }),
    ).toBe('审批上下文已失效，请重新发起请求。')

    expect(
      resolveChatErrorMessage({
        code: 'tool_output_schema_invalid',
        detail: 'provider raw output must never be rendered',
      }),
    ).toBe(
      '工具输出与注册契约不一致，本次执行未记为成功，结果不可用于后续审批或完成，请联系管理员检查工具配置。',
    )
  })

  it('scopes active errors to the current session', () => {
    const store = useChatErrorStore()

    store.bindCurrentSession('session-1')
    store.setSessionError('session-2', {
      code: 'other_session_error',
      detail: 'other session detail',
      userFacingMessage: 'other session error',
    })

    expect(store.errorMessage).toBe('')
    expect(store.errorPresentation).toBeNull()

    store.setSessionError('session-1', {
      code: 'current_session_error',
      detail: 'current session detail',
      userFacingMessage: 'current session error',
    })
    expect(store.errorMessage).toBe('current session error')
    expect(store.errorPresentation).toEqual({
      message: 'current session error',
      code: 'current_session_error',
      detail: 'current session detail',
      userFacingMessage: 'current session error',
    })

    store.bindCurrentSession('session-2')
    expect(store.errorPresentation).toBeNull()

    store.clearSessionError('session-1')
    expect(store.errorMessage).toBe('')
  })

  it('keeps the fixed fallback for an unknown code while retaining its safe detail', () => {
    expect(
      resolveChatErrorMessage({
        code: 'unknown_backend_code',
        detail: '后端返回的真实失败原因',
      }),
    ).toBe('请求失败，请稍后重试。')

    expect(
      createChatErrorPresentation({
        code: 'unknown_backend_code',
        detail: '后端返回的安全失败原因',
        userFacingMessage: '本次查询未完成。',
      }),
    ).toEqual({
      message: '本次查询未完成。',
      code: 'unknown_backend_code',
      detail: '后端返回的安全失败原因',
      userFacingMessage: '本次查询未完成。',
    })
  })

  it('has explicit messages for model provider failures', () => {
    expect(resolveChatErrorMessage({ code: 'model_provider_unavailable' })).toBe(
      '模型服务暂时不可用，请稍后重试或联系管理员检查模型网络。',
    )
    expect(resolveChatErrorMessage({ code: 'model_request_timeout' })).toBe(
      '模型响应超时，请稍后重试或缩小问题范围。',
    )
    expect(resolveChatErrorMessage({ code: 'client_stream_timeout' })).toBe(
      '对话连接长时间无响应，请重试。',
    )
  })

  it('extracts ProblemDetails and ASP.NET validation errors from ApiError details', () => {
    expect(
      toFriendlyMessage(
        new ApiError('API Error: 400', 400, {
          title: 'Validation failed',
          errors: {
            Message: ['The Message field is required.'],
            SessionId: ['The SessionId field is invalid.'],
          },
        }),
      ),
    ).toBe('Message: The Message field is required.；SessionId: The SessionId field is invalid.')

    expect(
      toFriendlyMessage(
        new ApiError('API Error: 400', 400, {
          detail: 'Chat model is not configured.',
        }),
      ),
    ).toBe('Chat model is not configured.')

    expect(
      toFriendlyMessage(
        new ApiError('API Error: 500', 500, {
          title: 'Model provider unavailable',
        }),
      ),
    ).toBe('Model provider unavailable')
  })

  it('preserves safe code, detail, and user-facing text from ApiError', () => {
    expect(
      toChatErrorPresentation(
        new ApiError('API Error: 409', 409, {
          code: 'agent_session_version_conflict',
          detail: '会话版本已从 3 更新为 4。',
          userFacingMessage: '会话状态已变化，请刷新后重试。',
        }),
      ),
    ).toEqual({
      message: '会话状态已变化，请刷新后重试。',
      code: 'agent_session_version_conflict',
      detail: '会话版本已从 3 更新为 4。',
      userFacingMessage: '会话状态已变化，请刷新后重试。',
    })

    expect(
      toChatErrorPresentation(
        new ApiError('API Error: 400', 400, {
          code: 'request_validation_failed',
          detail: '请求字段校验未通过。',
          userFacingMessage: '请检查输入后重试。',
          errors: { Message: ['Message 不能为空。'] },
        }),
      ).detail,
    ).toBe('请求字段校验未通过。；Message: Message 不能为空。')
  })

  it('does not expose raw ApiError messages', () => {
    expect(
      toFriendlyMessage(new ApiError('Provider endpoint /internal/model failed', 500, null)),
    ).toBe('请求失败，请稍后重试。')
  })
})
