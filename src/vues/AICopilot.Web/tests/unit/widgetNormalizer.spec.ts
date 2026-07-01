import { describe, expect, it } from 'vitest'
import { normalizeWidgetPayload } from '@/protocol/widgetNormalizer'

describe('widgetNormalizer', () => {
  it('normalizes backend chart widgets with widget_type and PascalCase fields', () => {
    const widget = normalizeWidgetPayload({
      widget_type: 'Chart',
      Title: '日志级别分布',
      Description: '按日志级别统计。',
      Data: {
        Category: 'Pie',
        Dataset: {
          Dimensions: ['level', 'count'],
          Source: [{ level: 'ERROR', count: 2 }]
        },
        Encoding: {
          X: 'level',
          Y: ['count']
        }
      }
    })

    expect(widget).toMatchObject({
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
  })

  it('normalizes backend data table widgets with PascalCase columns and rows', () => {
    const widget = normalizeWidgetPayload({
      Type: 'DataTable',
      Title: '设备日志证据表',
      Data: {
        Columns: [
          { Key: 'occurredAt', Label: '时间', DataType: 'date' },
          { Key: 'message', Label: '日志内容', DataType: 'string' }
        ],
        Rows: [
          { occurredAt: '2026-04-20 11:00:00 UTC', message: 'Motor overload' }
        ]
      }
    })

    expect(widget).toMatchObject({
      type: 'DataTable',
      title: '设备日志证据表',
      data: {
        columns: [
          { key: 'occurredAt', label: '时间', dataType: 'date' },
          { key: 'message', label: '日志内容', dataType: 'string' }
        ],
        rows: [
          { occurredAt: '2026-04-20 11:00:00 UTC', message: 'Motor overload' }
        ]
      }
    })
  })

  it('normalizes legacy visual decision table payloads with raw array data', () => {
    const widget = normalizeWidgetPayload({
      visual_decision: 'DataTable',
      data: [{ deviceCode: 'DEV-001', level: 'ERROR' }]
    })

    expect(widget).toMatchObject({
      type: 'DataTable',
      data: {
        columns: [
          { key: 'deviceCode', label: 'deviceCode', dataType: 'string' },
          { key: 'level', label: 'level', dataType: 'string' }
        ],
        rows: [{ deviceCode: 'DEV-001', level: 'ERROR' }]
      }
    })
  })
})
