import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { ChunkType, type FunctionApprovalRequest } from '@/types/protocols'
import {
  getApprovalFailureStatus,
  getApprovalSafeArgsSummary,
  getCanonicalApprovalIdentity,
  hasStrictApprovalIdentity,
  isApprovalChunk,
} from '@/protocol/approvalProtocol'

function createApproval(overrides: Partial<FunctionApprovalRequest> = {}): FunctionApprovalRequest {
  return {
    callId: 'call-1',
    name: 'tool',
    targetType: 'McpServer',
    targetName: 'cloud-read',
    toolName: 'queryDeviceLogs',
    args: {},
    ...overrides,
  }
}

describe('approvalProtocol', () => {
  it('requires target type, target name, and tool name for strict identity', () => {
    expect(hasStrictApprovalIdentity(createApproval())).toBe(true)
    expect(hasStrictApprovalIdentity(createApproval({ targetType: null }))).toBe(false)
    expect(hasStrictApprovalIdentity(createApproval({ targetName: '' }))).toBe(false)
    expect(hasStrictApprovalIdentity(createApproval({ toolName: undefined }))).toBe(false)
  })

  it('maps already-processed or expired approval errors to expired status', () => {
    expect(getApprovalFailureStatus('approval_already_processed')).toBe('expired')
    expect(getApprovalFailureStatus('chat_context_expired')).toBe('expired')
    expect(getApprovalFailureStatus('approval_stream_failed')).toBe('pending')
    expect(getApprovalFailureStatus(null)).toBe('pending')
  })

  it('recognizes approval chunks by chunk type', () => {
    expect(isApprovalChunk({ type: ChunkType.ApprovalRequest })).toBe(true)
    expect(isApprovalChunk({ type: ChunkType.Text })).toBe(false)
  })

  it('shows only canonical identity and whitelisted argument facts', () => {
    const request = createApproval({
      args: {
        deviceCode: 'DEV-001',
        limit: 20,
        sql: 'select * from secret_table',
        token: 'secret-token',
        sourceName: 'internal-source',
      },
    })

    expect(getCanonicalApprovalIdentity(request)).toBe('McpServer / cloud-read / queryDeviceLogs')
    expect(getApprovalSafeArgsSummary(request)).toBe('设备：DEV-001 · 限制：20')
  })

  it('does not retain a raw argument viewer in the approval card', () => {
    const source = readFileSync(
      new URL('../../src/components/chat/ApprovalCard.vue', import.meta.url),
      'utf8',
    )

    expect(source).not.toContain('ArgumentViewer')
    expect(source).not.toContain('request.args')
    expect(source).not.toContain('确认调用只读工具')
    expect(source).toContain('确认调用受治理工具')
    expect(source).toContain('safeArgsSummary')
    expect(source).toContain('locallyLocked.value = true')
  })
})
