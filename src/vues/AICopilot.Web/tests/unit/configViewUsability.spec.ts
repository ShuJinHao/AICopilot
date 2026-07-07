import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const configViewSource = readFileSync(
  fileURLToPath(new URL('../../src/views/ConfigView.vue', import.meta.url)),
  'utf8'
)
const configServiceSource = readFileSync(
  fileURLToPath(new URL('../../src/services/configService.ts', import.meta.url)),
  'utf8'
)
const skillDisplaySource = readFileSync(
  fileURLToPath(new URL('../../src/utils/skillDisplay.ts', import.meta.url)),
  'utf8'
)

describe('ConfigView usability defaults', () => {
  it('shows a primary model card before advanced stage settings', () => {
    expect(configViewSource).toContain('data-testid="primary-model-config-card"')
    expect(configViewSource).toContain('slotsUseSingleModel')
    expect(configViewSource).toContain('advanced-config-fold')
    expect(configViewSource.indexOf('data-testid="primary-model-config-card"'))
      .toBeLessThan(configViewSource.indexOf('<div class="slot-grid">'))
  })

  it('keeps internal tool catalog loading behind advanced settings', () => {
    const start = configViewSource.indexOf('async function refreshAllAgentSettings()')
    const end = configViewSource.indexOf('function promptLength', start)
    const block = configViewSource.slice(start, end)

    expect(block).toContain('if (advancedConfigOpen.value)')
    expect(block).toContain('refreshToolCatalog()')
    expect(block).not.toContain('refreshToolCatalog(),')
    expect(configViewSource).toContain('watch(advancedConfigOpen')
    expect(configViewSource).toContain('Array.isArray(catalog.tools) ? catalog.tools : []')
    expect(configViewSource).toContain('(toolSummaries.value ?? []).find')
  })

  it('surfaces agent settings refresh failures through the config error banner', () => {
    const start = configViewSource.indexOf('async function refreshAllAgentSettings()')
    const end = configViewSource.indexOf('function promptLength', start)
    const block = configViewSource.slice(start, end)

    expect(configViewSource).toContain('function setPageLoadError(error: unknown)')
    expect(configViewSource).toContain('store.errorMessage = toStoreErrorMessage')
    expect(configViewSource).toContain('Failed to load skill definitions for config view.')
    expect(configViewSource).toContain('Failed to load Cloud readonly status for config view.')
    expect(configViewSource).toContain('Failed to load tool catalog for config view.')
    expect(block).toContain('try {')
    expect(block).toContain('Failed to refresh AI agent settings.')
    expect(block).toContain('setPageLoadError(error)')
    expect(configViewSource).toContain('<div v-if="store.errorMessage" class="error-banner">')
  })

  it('loads sanitized Cloud readonly status for operations', () => {
    expect(configViewSource).toContain('data-testid="cloud-readonly-status-card"')
    expect(configViewSource).toContain('cloudReadonlyStatusLabel')
    expect(configServiceSource).toContain("'/aigateway/cloud-readonly/status'")
    expect(configViewSource).not.toContain('ServiceAccountToken')
  })

  it('uses human labels for runtime parameters and Skill display', () => {
    expect(configViewSource).toContain('回答稳定性 / 创造性')
    expect(configViewSource).toContain('回答长度')
    expect(configViewSource).toContain('上下文容量')
    expect(configViewSource).toContain('value.toFixed(2)')
    expect(configViewSource).toContain('temperatureLabel(slot.model?.temperature)')
    expect(configViewSource).toContain('mappedListText(skill.allowedDataSourceModes, dataSourceModeLabels)')
    expect(configViewSource).toContain('mappedListText(skill.allowedKnowledgeScopes, knowledgeScopeLabels)')
    expect(configViewSource).toContain('approvalPolicyLabel(skill.approvalPolicy)')
    expect(configServiceSource).toContain("'/aigateway/tools/catalog'")
    expect(configViewSource).toContain('toolDisplayName(toolCode)')
    expect(configViewSource).toContain('refreshToolCatalog()')
    expect(configViewSource).toContain('AI 模拟业务库')
    expect(configViewSource).toContain('工具执行需确认')
    expect(skillDisplaySource).toContain('查询和分析 Cloud 只读业务数据')
    expect(skillDisplaySource).toContain('knowledge_research')
    expect(skillDisplaySource).not.toContain('knowledge_search')
    expect(configViewSource).toContain('getSkillDisplayDescription(skill.skillCode)')
    expect(configViewSource).not.toContain("return skill.description || '自动选择合适能力'")
  })
})
