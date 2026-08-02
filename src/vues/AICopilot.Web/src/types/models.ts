import type { AgentEventPayload, ChatModelMetadataPayload } from '@/types/protocols'
import {
  type ChatChunk,
  type FunctionApprovalRequest,
  MessageRole,
  type Widget,
} from '@/types/protocols'

export interface FunctionCall {
  id: string
  name: string
  args: string
  result?: string
  status: 'calling' | 'completed'
}

export interface FunctionCallChunk extends ChatChunk {
  functionCall: FunctionCall
}

export interface WidgetChunk extends ChatChunk {
  widget: Widget
}

export interface ApprovalChunk extends ChatChunk {
  request: FunctionApprovalRequest
  status: 'pending' | 'approved' | 'rejected' | 'expired'
}

export interface AgentEventChunk extends ChatChunk {
  event: AgentEventPayload
}

export interface ChatMessage extends ChatModelMetadataPayload {
  messageId?: number
  sequence?: number
  sessionId: string
  role: MessageRole
  chunks: ChatChunk[]
  isStreaming: boolean
  timestamp: number
}
