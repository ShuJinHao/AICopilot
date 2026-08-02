import { describe, expect, it } from 'vitest'
import { normalizeWidgetPayload } from '@/protocol/widgetNormalizer'

describe('widgetNormalizer', () => {
  it('accepts the canonical trusted chart payload', () => {
    const widget = normalizeWidgetPayload({
      id: 'chart-log-level',
      type: 'Chart',
      title: '日志级别分布',
      description: '按日志级别统计。',
      data: {
        category: 'Pie',
        dataset: {
          dimensions: ['level', 'count'],
          source: [{ level: 'ERROR', count: 2 }]
        },
        encoding: {
          x: 'level',
          y: ['count']
        }
      }
    })

    expect(widget).toMatchObject({
      id: 'chart-log-level',
      type: 'Chart',
      data: {
        category: 'Pie',
        dataset: { dimensions: ['level', 'count'] },
        encoding: { x: 'level', y: ['count'] }
      }
    })
  })

  it('accepts the canonical trusted data-table payload', () => {
    const widget = normalizeWidgetPayload({
      id: 'table-device-log',
      type: 'DataTable',
      title: '设备日志证据表',
      description: '本轮查询的可信记录。',
      data: {
        columns: [
          { key: 'occurredAt', label: '时间', dataType: 'date' },
          { key: 'message', label: '日志内容', dataType: 'string' }
        ],
        rows: [{ occurredAt: '2026-04-20T11:00:00Z', message: 'Motor overload' }]
      }
    })

    expect(widget).toMatchObject({
      type: 'DataTable',
      data: {
        columns: [
          { key: 'occurredAt', label: '时间', dataType: 'date' },
          { key: 'message', label: '日志内容', dataType: 'string' }
        ]
      }
    })
  })

  it('rejects non-canonical casing and retired visual-decision payloads', () => {
    expect(normalizeWidgetPayload({ Type: 'DataTable', Data: { Columns: [], Rows: [] } }))
      .toBeNull()
    expect(normalizeWidgetPayload({
      visual_decision: 'DataTable',
      data: [{ deviceCode: 'DEV-001' }]
    })).toBeNull()
  })
})
