import type { BusinessDatabaseSummary, McpAllowedTool } from '@/types/app'

export const DATA_SOURCE_EXTERNAL_SYSTEM = {
  Unknown: 0,
  CloudReadOnly: 1,
  NonCloud: 2
} as const

export const MCP_RISK_LEVEL = {
  Low: 0,
  RequiresApproval: 1,
  Blocked: 2
} as const

export const MCP_CAPABILITY_KIND = {
  ReadOnlyQuery: 0,
  Diagnostics: 1,
  LocalSuggestion: 2,
  SideEffect: 3
} as const

export type TagType = 'success' | 'warning' | 'danger' | 'info'

interface LabelState {
  label: string
  type: TagType
}

interface McpSafetyDefaults {
  externalSystemType: number
  capabilityKind: number
  riskLevel: number
}

const externalSystemLabels: Record<number, string> = {
  [DATA_SOURCE_EXTERNAL_SYSTEM.Unknown]: '未知',
  [DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly]: 'Cloud 只读',
  [DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud]: '非云系统'
}

const mcpRiskLabels: Record<number, string> = {
  [MCP_RISK_LEVEL.Low]: '低',
  [MCP_RISK_LEVEL.RequiresApproval]: '需审批',
  [MCP_RISK_LEVEL.Blocked]: '阻断'
}

const mcpRiskTagTypes: Record<number, TagType> = {
  [MCP_RISK_LEVEL.Low]: 'success',
  [MCP_RISK_LEVEL.RequiresApproval]: 'warning',
  [MCP_RISK_LEVEL.Blocked]: 'danger'
}

const providerLabels: Record<number, string> = {
  1: 'PostgreSQL',
  2: 'SQL Server',
  3: 'MySQL'
}

const fallbackScopeLabels: Record<string, string> = {
  GeneralChat: '普通对话',
  RagSummary: 'RAG 汇总',
  DataAnalysisFinalSummary: '数据分析最终总结',
  McpToolCall: 'MCP 工具调用',
  ApprovalResume: '审批恢复',
  SideEffectingTool: '副作用工具',
  DataAnalysisSqlToolChain: '数据分析 SQL 工具链'
}

const approvalTargetTypeLabels: Record<string, string> = {
  Plugin: '插件',
  McpServer: 'MCP 服务'
}

const writeSemanticWords = [
  'create',
  'update',
  'delete',
  'reset',
  'restart',
  'write',
  '新增',
  '修改',
  '删除',
  '下发',
  '写入',
  '重启',
  '同步'
] as const

export function externalSystemLabel(type: number | null | undefined) {
  return type == null ? '未知' : externalSystemLabels[type] ?? `未知(${type})`
}

export function mcpRiskLabel(riskLevel: number | null | undefined) {
  return riskLevel == null ? '未知' : mcpRiskLabels[riskLevel] ?? `未知(${riskLevel})`
}

export function mcpRiskTagType(riskLevel: number | null | undefined): TagType {
  return riskLevel == null ? 'info' : mcpRiskTagTypes[riskLevel] ?? 'info'
}

export function providerLabel(provider: number) {
  return providerLabels[provider] ?? `Provider ${provider}`
}

export function fallbackScopeLabel(scope: string) {
  return fallbackScopeLabels[scope] ?? scope
}

export function approvalTargetTypeLabel(targetType: string) {
  return approvalTargetTypeLabels[targetType] ?? targetType
}

export function outputTokenBudgetLabel(value?: number | null) {
  return value && value > 0 ? `${value}` : '未设置'
}

export function databaseState(row: BusinessDatabaseSummary): LabelState {
  if (!row.isEnabled) {
    return { label: '停用', type: 'info' }
  }

  if (!row.isReadOnly || !row.readOnlyCredentialVerified) {
    return { label: '需复核', type: 'warning' }
  }

  return { label: '只读已验证', type: 'success' }
}

export function normalizeRuntimeSegment(value: string) {
  const normalized = value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '')

  return normalized || 'unnamed'
}

export function runtimeNamePreview(serverName: string, toolName: string) {
  return `mcp__${normalizeRuntimeSegment(serverName)}__${normalizeRuntimeSegment(toolName)}`
}

export function safetyPreview(tool: McpAllowedTool, serverDefaults: McpSafetyDefaults): LabelState {
  const externalSystemType = tool.externalSystemType ?? serverDefaults.externalSystemType
  const capabilityKind = tool.capabilityKind ?? serverDefaults.capabilityKind
  const riskLevel = tool.riskLevel ?? serverDefaults.riskLevel
  const lowerName = tool.toolName.toLowerCase()

  if (!tool.toolName.trim()) {
    return { type: 'info', label: '待填写工具名' }
  }

  if (riskLevel === MCP_RISK_LEVEL.Blocked) {
    return { type: 'danger', label: '风险级别已阻断' }
  }

  if (externalSystemType === DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly) {
    if (!tool.readOnlyDeclared) {
      return { type: 'danger', label: 'Cloud 工具必须声明只读' }
    }

    if (capabilityKind === MCP_CAPABILITY_KIND.SideEffect) {
      return { type: 'danger', label: 'Cloud 工具不能是副作用能力' }
    }

    if (tool.mcpDestructiveHint === true) {
      return { type: 'danger', label: 'MCP 标注为破坏性能力' }
    }

    if (writeSemanticWords.some((word) => lowerName.includes(word) || tool.toolName.includes(word))) {
      return { type: 'danger', label: '工具名包含写语义' }
    }
  }

  return { type: 'success', label: '可尝试暴露' }
}
