export type TagType = 'success' | 'warning' | 'danger' | 'info'

interface LabelState {
  label: string
  type: TagType
}

const documentStatusLabels: Record<string, LabelState> = {
  Pending: { label: '待处理', type: 'info' },
  '0': { label: '待处理', type: 'info' },
  Parsing: { label: '解析中', type: 'warning' },
  '1': { label: '解析中', type: 'warning' },
  Splitting: { label: '切分中', type: 'warning' },
  '2': { label: '切分中', type: 'warning' },
  Embedding: { label: '向量化中', type: 'warning' },
  '3': { label: '向量化中', type: 'warning' },
  Indexed: { label: '已入库', type: 'success' },
  '4': { label: '已入库', type: 'success' },
  Failed: { label: '失败', type: 'danger' },
  '5': { label: '失败', type: 'danger' }
}

const classificationLabels: Record<string, string> = {
  Public: '公开',
  '0': '公开',
  Internal: '内部',
  '1': '内部',
  Sensitive: '敏感',
  '2': '敏感',
  Forbidden: '禁用',
  '3': '禁用'
}

const sourceTypeLabels: Record<string, string> = {
  UserUploaded: '用户上传',
  '0': '用户上传',
  BusinessRule: '业务规则',
  '1': '业务规则',
  CloudReadOnlyApiDoc: 'Cloud 只读文档',
  '2': 'Cloud 只读文档',
  Runbook: '运维手册',
  '3': '运维手册',
  External: '外部资料',
  '4': '外部资料'
}

function documentStatusMeta(status: string | number): LabelState {
  return documentStatusLabels[String(status)] ?? { label: String(status), type: 'warning' }
}

export function documentStatusLabel(status: string | number) {
  return documentStatusMeta(status).label
}

export function documentStatusType(status: string | number): TagType {
  return documentStatusMeta(status).type
}

export function governanceType(row: {
  classification: string | number
  allowedForFinalPrompt: boolean
}): TagType {
  const classification = String(row.classification)
  if (!row.allowedForFinalPrompt || classification === 'Forbidden' || classification === '3') {
    return 'danger'
  }

  if (classification === 'Sensitive' || classification === '2') {
    return 'warning'
  }

  return 'success'
}

export function classificationLabel(value: string | number) {
  return classificationLabels[String(value)] ?? String(value)
}

export function sourceTypeLabel(value: string | number) {
  return sourceTypeLabels[String(value)] ?? String(value)
}
