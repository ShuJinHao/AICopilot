const skillDisplayDescriptions: Record<string, string> = {
  general_report: '日常分析和报告生成',
  data_analysis: '查询和分析产线数据',
  knowledge_research: '从知识库检索相关文档',
  artifact_report: '基于上传文件生成报告产物',
  cloud_readonly: '查询和分析 Cloud 只读业务数据',
  free_goal_chat: '普通对话，不调用工具'
}

export function getSkillDisplayDescription(skillCode?: string | null) {
  const normalizedCode = skillCode?.trim().toLowerCase() ?? ''
  return skillDisplayDescriptions[normalizedCode] ?? '系统自动选择合适能力'
}

