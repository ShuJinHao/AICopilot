import { describe, expect, it } from 'vitest'
import { parseAgentPlan } from '@/composables/useAgentPlanPreview'

describe('Plan v2 preview contract', () => {
  it('parses version, digest, topology, selection modes, and nodes without mutating payload', () => {
    const digest = 'a'.repeat(64)
    const json = JSON.stringify({
      schemaVersion: '2.0',
      planDigest: digest,
      topologyProfile: 'LinearV1',
      planKind: 'PlanDraft',
      isExecutable: false,
      pluginSelectionMode: 'BuiltInOnly',
      selectedPluginIds: [],
      capabilitySelectionMode: 'InferredFromGoal',
      requestedCapabilityCodes: ['General.Chat'],
      nodes: [{ nodeId: 'node-001', nodeKind: 'DeterministicTransformNode', dependsOn: [] }],
      steps: [],
    })

    const plan = parseAgentPlan(json)

    expect(plan).toMatchObject({
      schemaVersion: '2.0',
      planDigest: digest,
      topologyProfile: 'LinearV1',
      pluginSelectionMode: 'BuiltInOnly',
      capabilitySelectionMode: 'InferredFromGoal',
    })
    expect(plan?.nodes).toHaveLength(1)
  })
})
