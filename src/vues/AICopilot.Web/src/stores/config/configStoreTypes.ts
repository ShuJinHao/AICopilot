import type { ConfigDialogMode } from '@/types/app'

export type ConfigEditableDomain =
  | 'languageModel'
  | 'routingModel'
  | 'conversationTemplate'

export type ConfigLoadingDomain =
  | ConfigEditableDomain

export interface ConfigDomainStates {
  loadingStates: Record<ConfigLoadingDomain, boolean>
  dialogStates: Record<ConfigEditableDomain, boolean>
  dialogModes: Record<ConfigEditableDomain, ConfigDialogMode>
  submittingStates: Record<ConfigEditableDomain, boolean>
  actionErrors: Record<ConfigEditableDomain, string>
}
