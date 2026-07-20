import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/services/apiClient'
import {
  resolveChatErrorMessage,
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

  it('keeps known error codes visible without exposing raw detail', () => {
    expect(
      resolveChatErrorMessage({
        code: 'agent_plan_tool_denied',
        detail: 'toolCode query_device_logs is not allowed by skill general_report.',
      }),
    ).toBe('计划包含当前 Skill 不允许的工具，请调整 Skill 或重新生成计划。')

    expect(
      resolveChatErrorMessage({
        code: 'agent_worker_unavailable',
      }),
    ).toBe('当前没有可用 DataWorker，请检查 Worker 状态。')

    expect(
      resolveChatErrorMessage({
        code: 'agent_skill_selection_required',
      }),
    ).toBe('无法自动识别合适的 Skill，请补充任务目标或手动选择 Skill 后重试。')

    expect(
      resolveChatErrorMessage({
        code: 'plan_payload_too_large',
        detail: 'raw plan must never be rendered',
      }),
    ).toBe('计划内容超过固定大小上限，请缩小目标范围或减少产物后重新生成。')

    expect(
      resolveChatErrorMessage({
        code: 'evidence_payload_too_large',
      }),
    ).toBe('证据载荷超过固定大小上限，请缩小查询或分批生成。')

    expect(
      resolveChatErrorMessage({
        code: 'tool_output_schema_invalid',
        detail: 'provider raw output must never be rendered',
      }),
    ).toBe('工具输出与注册契约不一致，本次执行未记为成功，结果不可用于后续审批或完成，请联系管理员检查工具配置。')

  })

  it('scopes active errors to the current session', () => {
    const store = useChatErrorStore()

    store.bindCurrentSession('session-1')
    store.setSessionError('session-2', 'other session error')

    expect(store.errorMessage).toBe('')

    store.setSessionError('session-1', 'current session error')
    expect(store.errorMessage).toBe('current session error')

    store.clearSessionError('session-1')
    expect(store.errorMessage).toBe('')
  })

  it('does not expose backend detail for unknown error codes', () => {
    expect(
      resolveChatErrorMessage({
        code: 'unknown_backend_code',
        detail: '后端返回的真实失败原因',
      }),
    ).toBe('请求失败，请稍后重试。')
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
            Goal: ['The Goal field is required.'],
            SkillCode: ['Skill is invalid.'],
          },
        }),
      ),
    ).toBe('Goal: The Goal field is required.；SkillCode: Skill is invalid.')

    expect(
      toFriendlyMessage(
        new ApiError('API Error: 400', 400, {
          detail: 'Planner model is not configured.',
        }),
      ),
    ).toBe('Planner model is not configured.')

    expect(
      toFriendlyMessage(
        new ApiError('API Error: 500', 500, {
          title: 'Model provider unavailable',
        }),
      ),
    ).toBe('Model provider unavailable')
  })

  it('does not expose raw ApiError messages', () => {
    expect(
      toFriendlyMessage(new ApiError('Provider endpoint /internal/model failed', 500, null)),
    ).toBe('请求失败，请稍后重试。')
  })
})
