import { ChunkType, type FunctionApprovalRequest } from '@/types/protocols'
import type { ApprovalChunk } from '@/types/models'

export function isApprovalChunk(chunk: { type: ChunkType }): chunk is ApprovalChunk {
  return chunk.type === ChunkType.ApprovalRequest
}

export function hasStrictApprovalIdentity(request: FunctionApprovalRequest) {
  return Boolean(request.targetType) && Boolean(request.targetName) && Boolean(request.toolName)
}

export function getApprovalFailureStatus(errorCode: string | null): ApprovalChunk['status'] {
  return errorCode === 'approval_already_processed' || errorCode === 'chat_context_expired'
    ? 'expired'
    : 'pending'
}
