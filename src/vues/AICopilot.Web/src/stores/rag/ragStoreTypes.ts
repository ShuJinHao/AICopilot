import type { ConfigDialogMode } from '@/types/app'

export type RagEditableDomain = 'embeddingModel' | 'knowledgeBase' | 'documentGovernance'
export type RagCrudDomain = 'embeddingModel' | 'knowledgeBase'
export type RagLoadingDomain = 'embeddingModel' | 'knowledgeBase' | 'document' | 'search'

export interface RagDomainStates {
  loadingStates: Record<RagLoadingDomain, boolean>
  dialogStates: Record<RagEditableDomain, boolean>
  dialogModes: Record<RagEditableDomain, ConfigDialogMode>
  submittingStates: Record<RagEditableDomain, boolean>
  actionErrors: Record<RagEditableDomain | 'document' | 'search', string>
}

export interface RagCrudStates {
  loadingStates: Record<RagCrudDomain, boolean>
  dialogStates: Record<RagCrudDomain, boolean>
  dialogModes: Record<RagCrudDomain, ConfigDialogMode>
  submittingStates: Record<RagCrudDomain, boolean>
  actionErrors: Record<RagCrudDomain, string>
}

export function toRagCrudStates(states: RagDomainStates): RagCrudStates {
  return states
}
