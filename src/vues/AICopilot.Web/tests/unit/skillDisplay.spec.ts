import { describe, expect, it } from 'vitest'
import { getSkillDisplayDescription } from '@/utils/skillDisplay'

describe('skill display descriptions', () => {
  it('uses product-facing Chinese summaries for built-in skills', () => {
    expect(getSkillDisplayDescription('general_report')).toBe('日常分析和报告生成')
    expect(getSkillDisplayDescription('data_analysis')).toBe('查询和分析产线数据')
    expect(getSkillDisplayDescription('knowledge_research')).toBe('从知识库检索相关文档')
    expect(getSkillDisplayDescription('artifact_report')).toBe('基于上传文件生成报告产物')
    expect(getSkillDisplayDescription('cloud_readonly')).toBe('查询和分析 Cloud 只读业务数据')
    expect(getSkillDisplayDescription('free_goal_chat')).toBe('普通对话，不调用工具')
  })

  it('does not expose raw backend descriptions for unknown skills', () => {
    expect(getSkillDisplayDescription('unknown_skill')).toBe('系统自动选择合适能力')
    expect(getSkillDisplayDescription(null)).toBe('系统自动选择合适能力')
  })
})

