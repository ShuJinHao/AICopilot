import { describe, expect, it } from 'vitest'
import {
  classificationLabel,
  documentStatusLabel,
  documentStatusType,
  governanceType,
  sourceTypeLabel
} from '@/views/knowledgeLabels'

describe('knowledgeLabels', () => {
  it('maps string document statuses to Chinese labels and tag types', () => {
    expect(documentStatusLabel('Pending')).toBe('待处理')
    expect(documentStatusType('Pending')).toBe('info')
    expect(documentStatusLabel('Parsing')).toBe('解析中')
    expect(documentStatusType('Parsing')).toBe('warning')
    expect(documentStatusLabel('Splitting')).toBe('切分中')
    expect(documentStatusType('Splitting')).toBe('warning')
    expect(documentStatusLabel('Embedding')).toBe('向量化中')
    expect(documentStatusType('Embedding')).toBe('warning')
    expect(documentStatusLabel('Indexed')).toBe('已入库')
    expect(documentStatusType('Indexed')).toBe('success')
    expect(documentStatusLabel('Failed')).toBe('失败')
    expect(documentStatusType('Failed')).toBe('danger')
  })

  it('maps numeric document statuses from default enum serialization', () => {
    expect(documentStatusLabel(0)).toBe('待处理')
    expect(documentStatusType(0)).toBe('info')
    expect(documentStatusLabel(1)).toBe('解析中')
    expect(documentStatusType(1)).toBe('warning')
    expect(documentStatusLabel(2)).toBe('切分中')
    expect(documentStatusType(2)).toBe('warning')
    expect(documentStatusLabel(3)).toBe('向量化中')
    expect(documentStatusType(3)).toBe('warning')
    expect(documentStatusLabel(4)).toBe('已入库')
    expect(documentStatusType(4)).toBe('success')
    expect(documentStatusLabel(5)).toBe('失败')
    expect(documentStatusType(5)).toBe('danger')
  })

  it('maps document governance classification labels and tag types', () => {
    expect(classificationLabel('Public')).toBe('公开')
    expect(classificationLabel(0)).toBe('公开')
    expect(classificationLabel('Internal')).toBe('内部')
    expect(classificationLabel(1)).toBe('内部')
    expect(classificationLabel('Sensitive')).toBe('敏感')
    expect(classificationLabel(2)).toBe('敏感')
    expect(classificationLabel('Forbidden')).toBe('禁用')
    expect(classificationLabel(3)).toBe('禁用')

    expect(governanceType({ classification: 'Internal', allowedForFinalPrompt: true })).toBe(
      'success'
    )
    expect(governanceType({ classification: 'Sensitive', allowedForFinalPrompt: true })).toBe(
      'warning'
    )
    expect(governanceType({ classification: 2, allowedForFinalPrompt: true })).toBe('warning')
    expect(governanceType({ classification: 'Forbidden', allowedForFinalPrompt: true })).toBe(
      'danger'
    )
    expect(governanceType({ classification: 3, allowedForFinalPrompt: true })).toBe('danger')
    expect(governanceType({ classification: 'Internal', allowedForFinalPrompt: false })).toBe(
      'danger'
    )
  })

  it('maps document source type strings and numbers', () => {
    expect(sourceTypeLabel('UserUploaded')).toBe('用户上传')
    expect(sourceTypeLabel(0)).toBe('用户上传')
    expect(sourceTypeLabel('BusinessRule')).toBe('业务规则')
    expect(sourceTypeLabel(1)).toBe('业务规则')
    expect(sourceTypeLabel('CloudReadOnlyApiDoc')).toBe('Cloud 只读文档')
    expect(sourceTypeLabel(2)).toBe('Cloud 只读文档')
    expect(sourceTypeLabel('Runbook')).toBe('运维手册')
    expect(sourceTypeLabel(3)).toBe('运维手册')
    expect(sourceTypeLabel('External')).toBe('外部资料')
    expect(sourceTypeLabel(4)).toBe('外部资料')
  })
})
