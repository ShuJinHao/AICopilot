import { describe, expect, it } from 'vitest'
import { parseDeviceLogAnswerSections } from '@/protocol/deviceLogAnswerSections'

describe('deviceLogAnswerSections', () => {
  it('parses DeviceLog final answer sections into the fixed product order', () => {
    const answer = parseDeviceLogAnswerSections([
      '结论：基于 DataAnalysis 只读查询，最近 1 天模切设备存在 ERROR/WARN 日志。',
      '关键指标：',
      '1. 日志总数：20 条',
      '2. ERROR：8 条',
      '关键记录：',
      '1. 2026-07-01 08:00 DEV-001 Motor overload',
      '可能原因：',
      '1. AI 推断分析：重复过载可能与驱动或负载波动相关。',
      '建议动作：',
      '1. 由现场人员核对报警时间点和维护记录。',
      '不能直接执行的动作：',
      '1. AICopilot 不能直接重启设备、修改参数或写入 Cloud。',
      '查询范围：最近 1 天；工序包含模切；级别 ERROR,WARN。'
    ].join('\n'))

    expect(answer?.sections.map((section) => section.key)).toEqual([
      'conclusion',
      'metrics',
      'records',
      'rootCause',
      'actions',
      'blockedActions',
      'scope'
    ])
    expect(answer?.sections.find((section) => section.key === 'metrics')?.items).toEqual([
      '日志总数：20 条',
      'ERROR：8 条'
    ])
  })

  it('supports markdown heading variants and root cause aliases', () => {
    const answer = parseDeviceLogAnswerSections([
      '### 结论：只读查询返回 3 条记录。',
      '**关键指标：**',
      '- 返回记录：3 条',
      '**关键记录**',
      '- DEV-001 WARN',
      '根因分析：AI 推断分析：可能是传感器状态波动。',
      '建议动作：现场复核传感器和维护记录。',
      '不能直接执行的动作：不能替用户写入 Cloud 或重启设备。',
      '查询范围说明：最近 24 小时。'
    ].join('\n'))

    expect(answer?.sections.find((section) => section.key === 'rootCause')).toMatchObject({
      title: '可能原因',
      content: 'AI 推断分析：可能是传感器状态波动。'
    })
    expect(answer?.sections.find((section) => section.key === 'scope')?.content).toBe('最近 24 小时。')
  })

  it('does not restructure ordinary answers with only a conclusion line', () => {
    expect(parseDeviceLogAnswerSections('结论：这个权限需要管理员确认。\n\n请按流程申请。')).toBeNull()
  })
})
