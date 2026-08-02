import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import {
  getChatModePresentation,
  requiresNewConversation,
  resolveConversationStatus,
} from '@/protocol/chatPresentation'

describe('chat presentation contract', () => {
  it('presents Plan and Execute as native MAF behavior modes', () => {
    const plan = getChatModePresentation(null)
    const execute = getChatModePresentation('execute')

    expect(plan.mode).toBe('plan')
    expect(plan.description).toContain('交互式澄清、调查并形成待办')
    expect(plan.description).toContain('受治理工具')
    expect(plan.description).not.toContain('不查询外部数据')
    expect(plan.suggestions).toContain('调查 DEV-001 设备日志异常，先确认范围并整理排查待办')

    expect(execute.description).toContain('自主连续完成待办')
    expect(execute.description).toContain('受治理工具')
    expect(execute.suggestions.join(' ')).toContain('查看 DEV-001')

    expect(getChatModePresentation('plan').mode).toBe('plan')
    expect(getChatModePresentation('execute').mode).toBe('execute')
  })

  it('maps authoritative lifecycle facts to one stable visible state', () => {
    const base = {
      isSessionActivating: false,
      agentSessionStatus: 'Ready',
      agentSessionResetRequired: false,
      isStreaming: false,
      hasPendingApproval: false,
      hasMessages: false,
      hasError: false,
    }

    expect(resolveConversationStatus({ ...base, isStreaming: true }).key).toBe('running')
    expect(resolveConversationStatus({ ...base, hasPendingApproval: true }).key).toBe(
      'waiting-approval',
    )
    expect(resolveConversationStatus({ ...base, hasMessages: true }).key).toBe('completed')
    expect(resolveConversationStatus({ ...base, hasError: true }).key).toBe('failed')
    expect(resolveConversationStatus({ ...base, agentSessionStatus: 'Interrupted' }).key).toBe(
      'interrupted',
    )
    expect(resolveConversationStatus({ ...base, agentSessionStatus: 'ResetRequired' }).key).toBe(
      'reset-required',
    )
  })

  it('offers a new conversation only for non-recoverable session states', () => {
    expect(requiresNewConversation('Interrupted')).toBe(true)
    expect(requiresNewConversation('ResetRequired')).toBe(true)
    expect(requiresNewConversation('Ready', true)).toBe(true)
    expect(requiresNewConversation('Running')).toBe(false)
    expect(requiresNewConversation('Ready')).toBe(false)

    const noticeSource = readFileSync(
      new URL('../../src/components/chat/AgentSessionRecoveryNotice.vue', import.meta.url),
      'utf8',
    )
    const storeSource = readFileSync(
      new URL('../../src/stores/chatStore.ts', import.meta.url),
      'utf8',
    )
    expect(noticeSource).toContain('新建会话')
    expect(storeSource).toContain('系统不会自动重放')
  })

  it('keeps complete safe backend error fields visible in the error banner', () => {
    const source = readFileSync(
      new URL('../../src/components/chat/ChatErrorBanner.vue', import.meta.url),
      'utf8',
    )

    expect(source).toContain('error.userFacingMessage')
    expect(source).toContain('error.code')
    expect(source).toContain('error.detail')
  })
})
