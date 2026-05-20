import type { ConfigDialogMode } from '@/types/app'

export type ConfigEditableDomain =
  | 'languageModel'
  | 'routingModel'
  | 'conversationTemplate'
  | 'approvalPolicy'
  | 'businessDatabase'
  | 'mcpServer'

export type ConfigLoadingDomain =
  | ConfigEditableDomain
  | 'semanticSource'
  | 'toolRegistry'
  | 'cloudReadonlyReadiness'
export type ReadOnlyConfigDomain = 'providerReliability'

export interface ConfigDomainStates {
  loadingStates: Record<ConfigLoadingDomain | ReadOnlyConfigDomain, boolean>
  dialogStates: Record<ConfigEditableDomain, boolean>
  dialogModes: Record<ConfigEditableDomain, ConfigDialogMode>
  submittingStates: Record<ConfigEditableDomain, boolean>
  actionErrors: Record<ConfigEditableDomain, string>
}
