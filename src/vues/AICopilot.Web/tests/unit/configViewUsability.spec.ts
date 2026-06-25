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

describe('ConfigView usability defaults', () => {
  it('shows a primary model card before advanced stage settings', () => {
    expect(configViewSource).toContain('data-testid="primary-model-config-card"')
    expect(configViewSource).toContain('slotsUseSingleModel')
    expect(configViewSource).toContain('advanced-config-fold')
    expect(configViewSource.indexOf('data-testid="primary-model-config-card"'))
      .toBeLessThan(configViewSource.indexOf('<div class="slot-grid">'))
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
    expect(configViewSource).toContain('查询和分析 Cloud 只读业务数据')
  })
})
