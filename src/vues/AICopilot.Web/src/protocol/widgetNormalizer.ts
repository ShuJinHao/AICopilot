import type {
  ChartWidget,
  DataTableWidget,
  StatsCardWidget,
  Widget
} from '@/types/protocols'

export type NormalizedWidget = ChartWidget | DataTableWidget | StatsCardWidget

const chartCategories = new Set(['Bar', 'Line', 'Pie'])
const tableDataTypes = new Set(['string', 'number', 'date', 'boolean'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function readBase(value: Record<string, unknown>): Widget | null {
  if (
    typeof value.id !== 'string' ||
    typeof value.type !== 'string' ||
    typeof value.title !== 'string' ||
    typeof value.description !== 'string'
  ) {
    return null
  }

  return {
    id: value.id,
    type: value.type,
    title: value.title,
    description: value.description,
    data: value.data
  }
}

function readChart(base: Widget): ChartWidget | null {
  if (!isRecord(base.data)) {
    return null
  }

  const { category, dataset, encoding } = base.data
  if (
    typeof category !== 'string' ||
    !chartCategories.has(category) ||
    !isRecord(dataset) ||
    !isRecord(encoding) ||
    !isStringArray(dataset.dimensions) ||
    !Array.isArray(dataset.source) ||
    !dataset.source.every(isRecord) ||
    typeof encoding.x !== 'string' ||
    !isStringArray(encoding.y) ||
    (encoding.seriesName !== undefined && typeof encoding.seriesName !== 'string')
  ) {
    return null
  }

  return {
    ...base,
    type: 'Chart',
    data: {
      category: category as ChartWidget['data']['category'],
      dataset: {
        dimensions: dataset.dimensions,
        source: dataset.source
      },
      encoding: {
        x: encoding.x,
        y: encoding.y,
        ...(encoding.seriesName === undefined ? {} : { seriesName: encoding.seriesName })
      }
    }
  }
}

function readStatsCard(base: Widget): StatsCardWidget | null {
  if (!isRecord(base.data)) {
    return null
  }

  const { label, value, unit } = base.data
  if (
    typeof label !== 'string' ||
    (typeof value !== 'string' && typeof value !== 'number') ||
    (unit !== undefined && typeof unit !== 'string')
  ) {
    return null
  }

  return {
    ...base,
    type: 'StatsCard',
    data: {
      label,
      value,
      ...(unit === undefined ? {} : { unit })
    }
  }
}

function readDataTable(base: Widget): DataTableWidget | null {
  if (!isRecord(base.data)) {
    return null
  }

  const { columns, rows } = base.data
  if (
    !Array.isArray(columns) ||
    !columns.every((column) =>
      isRecord(column) &&
      typeof column.key === 'string' &&
      typeof column.label === 'string' &&
      typeof column.dataType === 'string' &&
      tableDataTypes.has(column.dataType)
    ) ||
    !Array.isArray(rows) ||
    !rows.every(isRecord)
  ) {
    return null
  }

  return {
    ...base,
    type: 'DataTable',
    data: {
      columns: columns as DataTableWidget['data']['columns'],
      rows
    }
  }
}

export function normalizeWidgetPayload(value: unknown): NormalizedWidget | null {
  if (!isRecord(value)) {
    return null
  }

  const base = readBase(value)
  if (!base) {
    return null
  }

  switch (base.type) {
    case 'Chart':
      return readChart(base)
    case 'StatsCard':
      return readStatsCard(base)
    case 'DataTable':
      return readDataTable(base)
    default:
      return null
  }
}
