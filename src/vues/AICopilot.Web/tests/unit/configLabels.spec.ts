import { describe, expect, it } from 'vitest'
import {
  DATA_SOURCE_EXTERNAL_SYSTEM,
  MCP_CAPABILITY_KIND,
  MCP_RISK_LEVEL,
  approvalTargetTypeLabel,
  databaseState,
  externalSystemLabel,
  fallbackScopeLabel,
  mcpRiskLabel,
  mcpRiskTagType,
  outputTokenBudgetLabel,
  providerLabel,
  runtimeNamePreview,
  safetyPreview
} from '@/views/configLabels'
import type { BusinessDatabaseSummary, McpAllowedTool } from '@/types/app'

function businessDatabase(overrides: Partial<BusinessDatabaseSummary>): BusinessDatabaseSummary {
  return {
    id: 'db-1',
    name: 'db',
    description: '',
    provider: 1,
    isEnabled: true,
    isReadOnly: true,
    externalSystemType: DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly,
    readOnlyCredentialVerified: true,
    createdAt: '2026-01-01T00:00:00Z',
    hasConnectionString: true,
    connectionStringMasked: null,
    ...overrides
  }
}

function allowedTool(overrides: Partial<McpAllowedTool> = {}): McpAllowedTool {
  return {
    toolName: 'queryDeviceStatus',
    externalSystemType: null,
    capabilityKind: null,
    riskLevel: null,
    readOnlyDeclared: true,
    mcpReadOnlyHint: true,
    mcpDestructiveHint: false,
    mcpIdempotentHint: null,
    ...overrides
  }
}

const cloudReadOnlyDefaults = {
  externalSystemType: DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly,
  capabilityKind: MCP_CAPABILITY_KIND.ReadOnlyQuery,
  riskLevel: MCP_RISK_LEVEL.Low
}

describe('configLabels', () => {
  it('maps business data source external system values explicitly', () => {
    expect(externalSystemLabel(DATA_SOURCE_EXTERNAL_SYSTEM.Unknown)).toBe('未知')
    expect(externalSystemLabel(DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly)).toBe('Cloud 只读')
    expect(externalSystemLabel(DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud)).toBe('非云系统')
  })

  it('maps MCP risk labels and tag types to backend enum values', () => {
    expect(mcpRiskLabel(MCP_RISK_LEVEL.Low)).toBe('低')
    expect(mcpRiskTagType(MCP_RISK_LEVEL.Low)).toBe('success')

    expect(mcpRiskLabel(MCP_RISK_LEVEL.RequiresApproval)).toBe('需审批')
    expect(mcpRiskTagType(MCP_RISK_LEVEL.RequiresApproval)).toBe('warning')

    expect(mcpRiskLabel(MCP_RISK_LEVEL.Blocked)).toBe('阻断')
    expect(mcpRiskTagType(MCP_RISK_LEVEL.Blocked)).toBe('danger')
  })

  it('maps provider and fallback scope labels', () => {
    expect(providerLabel(1)).toBe('PostgreSQL')
    expect(providerLabel(2)).toBe('SQL Server')
    expect(providerLabel(3)).toBe('MySQL')
    expect(providerLabel(9)).toBe('Provider 9')

    expect(fallbackScopeLabel('GeneralChat')).toBe('普通对话')
    expect(fallbackScopeLabel('McpToolCall')).toBe('MCP 工具调用')
    expect(fallbackScopeLabel('CustomScope')).toBe('CustomScope')
    expect(approvalTargetTypeLabel('Plugin')).toBe('插件')
    expect(approvalTargetTypeLabel('McpServer')).toBe('MCP 服务')
  })

  it('maps output token budget labels and database states', () => {
    expect(outputTokenBudgetLabel(2048)).toBe('2048')
    expect(outputTokenBudgetLabel(0)).toBe('未设置')
    expect(outputTokenBudgetLabel(null)).toBe('未设置')

    expect(databaseState(businessDatabase({ isEnabled: false }))).toEqual({
      label: '停用',
      type: 'info'
    })
    expect(databaseState(businessDatabase({ readOnlyCredentialVerified: false }))).toEqual({
      label: '需复核',
      type: 'warning'
    })
    expect(databaseState(businessDatabase({}))).toEqual({
      label: '只读已验证',
      type: 'success'
    })
  })

  it('normalizes MCP runtime names', () => {
    expect(runtimeNamePreview('Device Tools', 'Query Status')).toBe(
      'mcp__device_tools__query_status'
    )
    expect(runtimeNamePreview('', '')).toBe('mcp__unnamed__unnamed')
  })

  it('previews MCP safety decisions from explicit backend enum values', () => {
    expect(safetyPreview(allowedTool({ toolName: '' }), cloudReadOnlyDefaults)).toEqual({
      type: 'info',
      label: '待填写工具名'
    })
    expect(
      safetyPreview(allowedTool({ riskLevel: MCP_RISK_LEVEL.Blocked }), cloudReadOnlyDefaults)
    ).toEqual({
      type: 'danger',
      label: '风险级别已阻断'
    })
    expect(
      safetyPreview(allowedTool({ readOnlyDeclared: false }), cloudReadOnlyDefaults)
    ).toEqual({
      type: 'danger',
      label: 'Cloud 工具必须声明只读'
    })
    expect(
      safetyPreview(
        allowedTool({ capabilityKind: MCP_CAPABILITY_KIND.SideEffect }),
        cloudReadOnlyDefaults
      )
    ).toEqual({
      type: 'danger',
      label: 'Cloud 工具不能是副作用能力'
    })
    expect(safetyPreview(allowedTool({ mcpDestructiveHint: true }), cloudReadOnlyDefaults)).toEqual({
      type: 'danger',
      label: 'MCP 标注为破坏性能力'
    })
    expect(safetyPreview(allowedTool({ toolName: 'deleteDevice' }), cloudReadOnlyDefaults)).toEqual(
      {
        type: 'danger',
        label: '工具名包含写语义'
      }
    )
    expect(safetyPreview(allowedTool(), cloudReadOnlyDefaults)).toEqual({
      type: 'success',
      label: '可尝试暴露'
    })
  })
})
