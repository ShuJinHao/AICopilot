import {
  ChunkType,
  MessageRole,
  type ChartWidget,
  type ChatChunk,
  type DataTableWidget,
  type StatsCardWidget
} from '@/types/protocols'
import type {
  AgentEventChunk,
  ChatMessage,
  FunctionCall,
  FunctionCallChunk,
  IntentChunk,
  WidgetChunk
} from '@/types/models'
import type { ChatRunStatus } from '@/stores/sessionScopedState'
import { formatAgentEventDetail } from './agentEventDisplay'
import { normalizeWidgetPayload } from './widgetNormalizer'

export interface RuntimeTag {
  key: string
  text: string
  tone: 'neutral' | 'blue' | 'warning' | 'danger' | 'success'
}

export interface RuntimeStatusDetail {
  phaseLabel: string
  summary: string
  elapsedText: string
  tone: RuntimeTag['tone']
  facts: RuntimeTag[]
}

export interface RuntimeEventDetail {
  key: string
  label: string
  detail: string
  tone: RuntimeTag['tone']
  statusText: string
}

export interface RuntimeIntentDetail {
  key: string
  name: string
  confidenceText: string
}

export interface RuntimeToolDetail {
  key: string
  name: string
  statusText: string
  tone: RuntimeTag['tone']
  argsSummary: string
  resultSummary: string
}

export interface RuntimeWidgetDetail {
  key: string
  typeLabel: string
  title: string
  summary: string
}

export interface RuntimeDetails {
  count: number
  status: RuntimeStatusDetail | null
  modelBadges: RuntimeTag[]
  events: RuntimeEventDetail[]
  intents: RuntimeIntentDetail[]
  tools: RuntimeToolDetail[]
  widgets: RuntimeWidgetDetail[]
}

const safeArgLabels: Record<string, string> = {
  deviceCode: '设备',
  deviceName: '设备',
  processCode: '工序',
  processName: '工序',
  stationCode: '工位',
  stationName: '工位',
  lineCode: '产线',
  lineName: '产线',
  level: '级别',
  logLevel: '级别',
  startTime: '开始',
  endTime: '结束',
  from: '开始',
  to: '结束',
  limit: '限制',
  top: '限制',
  topN: '限制',
  pageSize: '页大小'
}

const sensitiveKeyPattern =
  /(sql|query|statement|connection|password|secret|token|credential|authorization|header|source|table|view|schema|endpoint|url|uri|where|filter|raw)/i
const sensitiveValuePattern =
  /\b(select|insert|update|delete|merge|drop|from|where|join|connectionstring|password|authorization|bearer)\b/i
const sensitiveTextPattern =
  /\b(sql|select|insert|update|delete|merge|drop|from|where|join|connectionstring|password|secret|token|authorization|bearer|endpoint|sourceName|tableName|databaseName|schema|view)\b|https?:\/\/|\/internal\//i

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function parseJson(value: unknown): unknown {
  if (typeof value !== 'string') {
    return value
  }

  try {
    return JSON.parse(value)
  } catch (error) {
    console.error('Failed to parse runtime details JSON payload.', error)
    return value
  }
}

function getNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function compactText(value: unknown, maxLength = 72) {
  const text = String(value ?? '').replace(/\s+/g, ' ').trim()
  if (!text) {
    return ''
  }

  return text.length > maxLength ? `${text.slice(0, maxLength - 1)}...` : text
}

function summarizeRuntimeStatusText(value: unknown, phase: ChatRunStatus['phase']) {
  const text = compactText(value, 120)
  if (!text) {
    return '运行状态已记录'
  }

  if (sensitiveTextPattern.test(text)) {
    return phase === 'failed'
      ? '运行失败，原始错误未在详情中展开'
      : '运行状态已记录，原始详情未在详情中展开'
  }

  return text
}

function isSensitiveKey(key: string) {
  return sensitiveKeyPattern.test(key)
}

function isSafeValue(value: unknown) {
  if (value === null || value === undefined) {
    return false
  }

  if (typeof value === 'number' || typeof value === 'boolean') {
    return true
  }

  if (typeof value !== 'string') {
    return false
  }

  const trimmed = value.trim()
  return trimmed.length > 0 && !sensitiveValuePattern.test(trimmed)
}

function collectSafeArgs(value: unknown, depth = 0): RuntimeTag[] {
  if (!isRecord(value) || depth > 2) {
    return []
  }

  const tags: RuntimeTag[] = []
  for (const [key, entry] of Object.entries(value)) {
    if (tags.length >= 6) {
      break
    }

    if (isSensitiveKey(key)) {
      continue
    }

    const label = safeArgLabels[key]
    if (label && isSafeValue(entry)) {
      tags.push({
        key: `${key}-${tags.length}`,
        text: `${label}：${compactText(entry, 44)}`,
        tone: 'neutral'
      })
      continue
    }

    if (isRecord(entry) && !Array.isArray(entry)) {
      tags.push(...collectSafeArgs(entry, depth + 1).slice(0, 6 - tags.length))
    }
  }

  return tags
}

function countTopLevelFields(value: unknown) {
  return isRecord(value) ? Object.keys(value).length : 0
}

function extractRowCount(value: unknown): number | undefined {
  const parsed = parseJson(value)

  if (Array.isArray(parsed)) {
    return parsed.length
  }

  if (!isRecord(parsed)) {
    return undefined
  }

  const directCount =
    getNumber(parsed.returnedRowCount) ??
    getNumber(parsed.returnedRows) ??
    getNumber(parsed.rowCount) ??
    getNumber(parsed.totalRows) ??
    getNumber(parsed.totalCount) ??
    getNumber(parsed.count)

  if (directCount !== undefined) {
    return directCount
  }

  for (const key of ['rows', 'items', 'data', 'records', 'result']) {
    const entry = parsed[key]
    const count = Array.isArray(entry) ? entry.length : extractRowCount(entry)
    if (count !== undefined) {
      return count
    }
  }

  return undefined
}

function hasTruncatedFlag(value: unknown): boolean {
  const parsed = parseJson(value)
  if (!isRecord(parsed)) {
    return false
  }

  if (parsed.isTruncated === true || parsed.truncated === true) {
    return true
  }

  return Object.values(parsed).some((entry) =>
    (isRecord(entry) || typeof entry === 'string') && hasTruncatedFlag(entry)
  )
}

export function summarizeFunctionArgs(args: string) {
  const parsed = parseJson(args)
  const tags = collectSafeArgs(parsed)
  if (tags.length > 0) {
    return tags.map((tag) => tag.text).join(' · ')
  }

  if (isRecord(parsed) && countTopLevelFields(parsed) === 0) {
    return '无参数'
  }

  if (isRecord(parsed)) {
    return `参数已记录（${countTopLevelFields(parsed)} 个字段），详情不展开原文`
  }

  return '参数已记录，因无法结构化解析未展开'
}

export function summarizeFunctionResult(result: string | undefined, status: FunctionCall['status']) {
  if (status === 'calling') {
    return '等待工具结果'
  }

  if (!result) {
    return '工具已完成，未返回结构化摘要'
  }

  const rowCount = extractRowCount(result)
  if (rowCount !== undefined) {
    return hasTruncatedFlag(result)
      ? `返回 ${rowCount} 条记录，结果已截断`
      : `返回 ${rowCount} 条记录`
  }

  const parsed = parseJson(result)
  if (isRecord(parsed)) {
    const success = parsed.success ?? parsed.succeeded
    if (success === false) {
      return '工具返回失败状态，原始错误未在详情中展开'
    }
  }

  return '工具结果已记录，未在详情中展开原文'
}

function phaseLabel(status: ChatRunStatus) {
  switch (status.phase) {
    case 'understanding':
      return '理解中'
    case 'querying':
      return '查询中'
    case 'answering':
      return '生成中'
    case 'completed':
      return '已完成'
    case 'failed':
      return '失败'
  }
}

function phaseTone(status: ChatRunStatus): RuntimeTag['tone'] {
  if (status.phase === 'failed') {
    return 'danger'
  }

  if (status.phase === 'completed') {
    return 'success'
  }

  return 'warning'
}

function elapsedText(elapsedMs: number) {
  const seconds = Math.max(0, Math.floor(elapsedMs / 1000))
  if (seconds < 60) {
    return `${seconds}s`
  }

  return `${Math.floor(seconds / 60)}m ${seconds % 60}s`
}

function buildStatus(status: ChatRunStatus | null | undefined): RuntimeStatusDetail | null {
  if (!status) {
    return null
  }

  const facts: RuntimeTag[] = []
  if ((status.queryCount ?? 0) > 0) {
    facts.push({ key: 'query-count', text: `查询 ${status.queryCount} 次`, tone: 'blue' })
  }

  if (typeof status.returnedRows === 'number') {
    facts.push({ key: 'returned-rows', text: `返回 ${status.returnedRows} 行`, tone: 'blue' })
  }

  if (status.error?.code) {
    facts.push({ key: 'error-code', text: status.error.code, tone: 'danger' })
  }

  return {
    phaseLabel: phaseLabel(status),
    summary: summarizeRuntimeStatusText(status.error?.message || status.summary, status.phase),
    elapsedText: elapsedText(status.elapsedMs),
    tone: phaseTone(status),
    facts
  }
}

function buildModelBadges(message: ChatMessage): RuntimeTag[] {
  const badges: RuntimeTag[] = []
  const finalModelName = message.finalModelName?.trim() || '未知'
  const routingModelName = message.routingModelName?.trim()

  badges.push({
    key: 'final-model',
    tone: 'success',
    text: `回答模型：${finalModelName}`
  })

  if (routingModelName) {
    badges.push({
      key: 'routing-model',
      tone: 'blue',
      text: `路由模型：${routingModelName}`
    })
  }

  if (typeof message.contextWindowTokens === 'number') {
    badges.push({
      key: 'context-window',
      tone: 'neutral',
      text: `上下文：${message.contextWindowTokens.toLocaleString('zh-CN')} tokens`
    })
  }

  if (typeof message.maxOutputTokens === 'number') {
    badges.push({
      key: 'max-output',
      tone: 'neutral',
      text: `输出上限：${message.maxOutputTokens.toLocaleString('zh-CN')} tokens`
    })
  }

  return badges
}

function agentEventLabel(stage: string) {
  switch (stage) {
    case 'plan_draft_started':
      return '计划草案开始'
    case 'intent_understanding':
      return '理解目标'
    case 'capability_discovery':
      return '发现能力'
    case 'plan_draft_ready':
      return '草案就绪'
    case 'plan_draft_failed':
      return '草案失败'
    default:
      return stage
  }
}

function buildEvents(chunks: ChatChunk[]): RuntimeEventDetail[] {
  return (chunks.filter((chunk) => chunk.type === ChunkType.AgentEvent) as AgentEventChunk[])
    .map((chunk, index) => ({
      key: `${chunk.event.stage}-${index}`,
      label: agentEventLabel(chunk.event.stage),
      detail: formatAgentEventDetail(chunk.event),
      tone: chunk.event.recoverable ? 'blue' as const : 'warning' as const,
      statusText: chunk.event.recoverable ? '可继续' : '需关注'
    }))
}

function buildIntents(chunks: ChatChunk[]): RuntimeIntentDetail[] {
  return (chunks.filter((chunk) => chunk.type === ChunkType.Intent) as IntentChunk[])
    .flatMap((chunk) => chunk.intents)
    .map((intent, index) => ({
      key: `${intent.intent}-${index}`,
      name: intent.intent,
      confidenceText: `${Math.round(intent.confidence * 100)}%`
    }))
}

function buildTools(chunks: ChatChunk[]): RuntimeToolDetail[] {
  return (chunks.filter((chunk) => chunk.type === ChunkType.FunctionCall) as FunctionCallChunk[])
    .map((chunk, index) => {
      const call = chunk.functionCall
      const isRunning = call.status === 'calling'
      return {
        key: `${call.id || call.name}-${index}`,
        name: call.name || '未命名工具',
        statusText: isRunning ? '执行中' : '已完成',
        tone: isRunning ? 'warning' as const : 'success' as const,
        argsSummary: summarizeFunctionArgs(call.args),
        resultSummary: summarizeFunctionResult(call.result, call.status)
      }
    })
}

function widgetTypeLabel(type: string) {
  switch (type) {
    case 'StatsCard':
      return '指标卡'
    case 'Chart':
      return '图表'
    case 'DataTable':
      return '数据表'
    default:
      return type || '组件'
  }
}

function summarizeWidget(chunk: WidgetChunk) {
  const normalized = normalizeWidgetPayload(chunk.widget)
  if (!normalized) {
    return {
      typeLabel: '组件',
      title: '结构化展示',
      summary: '组件载荷已记录'
    }
  }

  if (normalized.type === 'DataTable') {
    const data = normalized.data as DataTableWidget['data']
    return {
      typeLabel: widgetTypeLabel(normalized.type),
      title: normalized.title || '数据表',
      summary: `展示 ${data.rows.length} 行证据`
    }
  }

  if (normalized.type === 'Chart') {
    const data = normalized.data as ChartWidget['data']
    return {
      typeLabel: widgetTypeLabel(normalized.type),
      title: normalized.title || '图表',
      summary: `展示 ${data.dataset.source.length} 个数据点`
    }
  }

  if (normalized.type === 'StatsCard') {
    const data = normalized.data as StatsCardWidget['data']
    return {
      typeLabel: widgetTypeLabel(normalized.type),
      title: normalized.title || data.label || '指标卡',
      summary: `${data.label || '指标'}：${compactText(data.value, 32)}${data.unit ?? ''}`
    }
  }

  return {
    typeLabel: widgetTypeLabel(normalized.type),
    title: normalized.title || '结构化展示',
    summary: normalized.description || '组件载荷已记录'
  }
}

function buildWidgets(chunks: ChatChunk[]): RuntimeWidgetDetail[] {
  return (chunks.filter((chunk) => chunk.type === ChunkType.Widget) as WidgetChunk[])
    .map((chunk, index) => {
      const widget = summarizeWidget(chunk)
      return {
        key: `${widget.title}-${index}`,
        ...widget
      }
    })
}

export function buildRuntimeDetails(
  message: ChatMessage,
  status?: ChatRunStatus | null
): RuntimeDetails {
  if (message.role === MessageRole.User) {
    return {
      count: 0,
      status: null,
      modelBadges: [],
      events: [],
      intents: [],
      tools: [],
      widgets: []
    }
  }

  const statusDetail = buildStatus(status)
  const modelBadges = buildModelBadges(message)
  const events = buildEvents(message.chunks)
  const intents = buildIntents(message.chunks)
  const tools = buildTools(message.chunks)
  const widgets = buildWidgets(message.chunks)

  return {
    status: statusDetail,
    modelBadges,
    events,
    intents,
    tools,
    widgets,
    count:
      (statusDetail ? 1 : 0) +
      modelBadges.length +
      events.length +
      intents.length +
      tools.length +
      widgets.length
  }
}
