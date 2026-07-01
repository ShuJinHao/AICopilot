import type {
  ChartWidget,
  DataTableWidget,
  StatsCardWidget,
  Widget
} from '@/types/protocols'

export type NormalizedWidget = Widget | ChartWidget | DataTableWidget | StatsCardWidget

const widgetTypes = new Set(['Chart', 'DataTable', 'StatsCard'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function getField(record: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(record, key)) {
      return record[key]
    }
  }

  return undefined
}

function asString(value: unknown, fallback = '') {
  return typeof value === 'string' ? value : fallback
}

function normalizeWidgetType(value: unknown) {
  if (typeof value !== 'string' || !value.trim()) {
    return null
  }

  const trimmed = value.trim().replace(/Widget$/, '')
  return widgetTypes.has(trimmed) ? trimmed : trimmed
}

function normalizeBaseWidget(record: Record<string, unknown>, type: string, data: unknown): Widget {
  const title = asString(getField(record, 'title', 'Title'), '')
  return {
    id: asString(getField(record, 'id', 'Id'), `${type}-${title || 'widget'}`),
    type,
    title,
    description: asString(getField(record, 'description', 'Description'), ''),
    data
  }
}

function normalizeChartData(data: unknown): ChartWidget['data'] {
  const dataRecord = isRecord(data) ? data : {}
  const datasetRecord = getField(dataRecord, 'dataset', 'Dataset')
  const dataset = isRecord(datasetRecord) ? datasetRecord : {}
  const encodingRecord = getField(dataRecord, 'encoding', 'Encoding')
  const encoding = isRecord(encodingRecord) ? encodingRecord : {}
  const source = getField(dataset, 'source', 'Source')
  const dimensions = getField(dataset, 'dimensions', 'Dimensions')
  const y = getField(encoding, 'y', 'Y')
  const seriesName = getField(encoding, 'seriesName', 'SeriesName')

  return {
    category: asString(getField(dataRecord, 'category', 'Category'), 'Bar') as ChartWidget['data']['category'],
    dataset: {
      dimensions: Array.isArray(dimensions) ? dimensions.map((item) => String(item)) : [],
      source: Array.isArray(source) ? (source as Array<Record<string, unknown>>) : []
    },
    encoding: {
      x: asString(getField(encoding, 'x', 'X'), ''),
      y: Array.isArray(y) ? y.map((item) => String(item)) : typeof y === 'string' ? [y] : [],
      seriesName: typeof seriesName === 'string' ? seriesName : undefined
    }
  }
}

function normalizeStatsData(data: unknown): StatsCardWidget['data'] {
  const dataRecord = isRecord(data) ? data : {}
  const value = getField(dataRecord, 'value', 'Value')
  const unit = getField(dataRecord, 'unit', 'Unit')

  return {
    label: asString(getField(dataRecord, 'label', 'Label'), ''),
    value: typeof value === 'number' || typeof value === 'string' ? value : '-',
    unit: typeof unit === 'string' ? unit : undefined
  }
}

function inferColumns(rows: Array<Record<string, unknown>>) {
  const firstRow = rows[0]
  if (!firstRow) {
    return []
  }

  return Object.keys(firstRow).map((key) => ({
    key,
    label: key,
    dataType: 'string' as const
  }))
}

function normalizeTableData(data: unknown): DataTableWidget['data'] {
  if (Array.isArray(data)) {
    const rows = data.filter(isRecord)
    return {
      columns: inferColumns(rows),
      rows
    }
  }

  const dataRecord = isRecord(data) ? data : {}
  const columns = getField(dataRecord, 'columns', 'Columns')
  const rows = getField(dataRecord, 'rows', 'Rows')

  const normalizedRows = Array.isArray(rows) ? rows.filter(isRecord) : []
  return {
    columns: Array.isArray(columns)
      ? columns.filter(isRecord).map((column) => ({
          key: asString(getField(column, 'key', 'Key'), ''),
          label: asString(getField(column, 'label', 'Label'), ''),
          dataType: asString(getField(column, 'dataType', 'DataType'), 'string') as DataTableWidget['data']['columns'][number]['dataType']
        }))
      : inferColumns(normalizedRows),
    rows: normalizedRows
  }
}

export function normalizeWidgetPayload(value: unknown): NormalizedWidget | null {
  if (!isRecord(value)) {
    return null
  }

  const directType = normalizeWidgetType(getField(value, 'type', 'Type', 'widget_type'))
  if (directType) {
    const rawData = getField(value, 'data', 'Data')
    const base = normalizeBaseWidget(value, directType, rawData)

    if (directType === 'Chart') {
      return { ...base, type: 'Chart', data: normalizeChartData(rawData) }
    }

    if (directType === 'StatsCard') {
      return { ...base, type: 'StatsCard', data: normalizeStatsData(rawData) }
    }

    if (directType === 'DataTable') {
      return { ...base, type: 'DataTable', data: normalizeTableData(rawData) }
    }

    return base
  }

  const decision = getField(value, 'visual_decision', 'VisualDecision')
  if (isRecord(decision)) {
    return normalizeWidgetPayload({
      ...decision,
      data: getField(value, 'data', 'Data') ?? getField(decision, 'data', 'Data')
    })
  }

  const decisionType = normalizeWidgetType(decision)
  if (decisionType) {
    return normalizeWidgetPayload({
      ...value,
      type: decisionType,
      data: getField(value, 'data', 'Data')
    })
  }

  return null
}
