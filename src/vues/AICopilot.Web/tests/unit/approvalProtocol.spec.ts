import { describe, expect, it } from 'vitest'
import { ChunkType, type FunctionApprovalRequest } from '@/types/protocols'
import { getApprovalFailureStatus, hasStrictApprovalIdentity, isApprovalChunk } from '@/protocol/approvalProtocol'

function createApproval(overrides: Partial<FunctionApprovalRequest> = {}): FunctionApprovalRequest {
  return {
    callId: 'call-1',
    name: 'tool',
    targetType: 'McpServer',
    targetName: 'cloud-read',
    toolName: 'queryDeviceLogs',
    args: {},
    requiresOnsiteAttestation: false,
    ...overrides
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
})
