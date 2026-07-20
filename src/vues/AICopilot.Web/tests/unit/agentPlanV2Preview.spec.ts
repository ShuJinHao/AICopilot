import { describe, expect, it } from 'vitest'
import { isAgentPlanConfirmable, parseAgentPlan } from '@/composables/useAgentPlanPreview'

const DIGEST = 'a'.repeat(64)

describe('Plan v2 preview', () => {
  it('rejects null, arrays and malformed preview payloads', () => {
    expect(parseAgentPlan('null')).toBeNull()
    expect(parseAgentPlan('[]')).toBeNull()
    expect(parseAgentPlan('{')).toBeNull()
  })

  it('recognizes a future gap-free canonical draft only when DTO integrity metadata is valid', () => {
    const plan = {
      schemaVersion: '2.0',
      planDigest: DIGEST,
      topologyProfile: 'LinearV1',
      planKind: 'PlanDraft',
      isExecutable: false,
      capabilityGaps: [],
      requestedCapabilityCodes: ['General.Chat'],
      steps: [{ title: 'Answer', description: 'Answer safely.', stepType: 'Analysis', toolCode: 'generate_chart_data' }],
      nodes: [],
    }
    const task = {
      planSchemaVersion: '2.0',
      planDigest: DIGEST,
      topologyProfile: 'LinearV1',
      planIntegrityStatus: 'ValidV2',
      isPlanExecutable: false,
    }

    expect(isAgentPlanConfirmable(task, plan, [])).toBe(true)
    expect(isAgentPlanConfirmable({ ...task, planIntegrityStatus: 'Invalid' }, plan, [])).toBe(false)
    expect(isAgentPlanConfirmable({ ...task, isPlanExecutable: true }, plan, [])).toBe(false)
    expect(isAgentPlanConfirmable({ ...task, planDigest: 'b'.repeat(64) }, plan, [])).toBe(false)
    expect(isAgentPlanConfirmable({ ...task, planSchemaVersion: '1.0' }, plan, [])).toBe(false)
    expect(isAgentPlanConfirmable({ ...task, topologyProfile: 'DagV1' }, plan, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, planDigest: 'b'.repeat(64) }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, schemaVersion: '1.0' }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, topologyProfile: 'DagV1' }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, requestedCapabilityCodes: [] }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, steps: [] }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, capabilityGaps: undefined }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, capabilityGaps: null as never }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, capabilityGaps: 'none' as never }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, capabilityGaps: [1 as never] }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, requestedCapabilityCodes: null as never }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, steps: null as never }, [])).toBe(false)
    expect(isAgentPlanConfirmable(task, { ...plan, steps: [{}] }, [])).toBe(false)
    expect(isAgentPlanConfirmable(
      task,
      { ...plan, capabilityGaps: ['plan_compiler_unavailable'] },
      ['plan_compiler_unavailable'],
    )).toBe(false)
    expect(isAgentPlanConfirmable(task, plan, ['tool unavailable'])).toBe(false)
  })
})
