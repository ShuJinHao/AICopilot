<script setup lang="ts">
import { computed } from 'vue'
import { RefreshCw } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { useConfigStore } from '@/stores/configStore'

const store = useConfigStore()

const riskRows = computed(() =>
  Object.entries(store.toolCatalog?.riskSummary ?? {}).sort(([left], [right]) =>
    left.localeCompare(right)
  )
)

const readiness = computed(() => store.cloudReadonlyReadiness)
const readinessChecks = computed(() => readiness.value?.checks ?? [])
const readinessHistory = computed(() => store.cloudReadonlyReadinessHistory ?? [])
const sandboxStatus = computed(
  () => store.cloudReadonlySandboxStatus ?? readiness.value?.sandboxStatus ?? null
)
const sandboxChecks = computed(() => sandboxStatus.value?.checks ?? [])
const sandboxHistory = computed(() => store.cloudReadonlySandboxSmokeHistory ?? [])
const sandboxAgentTrialStatus = computed(() => store.cloudReadonlySandboxAgentTrialStatus)
const sandboxControlledTrialStatus = computed(() => store.cloudReadonlySandboxControlledTrialStatus)
const cloudReadonlyTool = computed(() =>
  store.toolRegistrations.find((tool) => tool.toolCode === 'query_cloud_data_readonly')
)
const cloudSandboxTool = computed(() =>
  store.toolRegistrations.find((tool) => tool.toolCode === 'query_cloud_sandbox_readonly')
)

function riskTone(risk: string) {
  if (risk === 'Critical' || risk === 'Blocked') return 'danger'
  if (risk === 'High' || risk === 'RequiresApproval') return 'warning'
  if (risk === 'Medium') return 'teal'
  return 'success'
}

function boundaryTone(boundary: string) {
  if (boundary === 'SimulationBusinessOnly') return 'success'
  if (boundary === 'RagContextOnly') return 'teal'
  if (boundary === 'ArtifactDraftOnly') return 'blue'
  if (boundary === 'CloudReadonlySandboxOnly') return 'warning'
  return 'neutral'
}

function readinessTone(status?: string | null) {
  if (
    !status ||
    status === 'NotConfigured' ||
    status === 'RealSandboxPending' ||
    status === 'SandboxSmokeRequired' ||
    status === 'Disabled'
  )
    return 'warning'
  if (status === 'Blocked' || status === 'Failed') return 'danger'
  return 'success'
}

function checkTone(status: string) {
  if (status === 'Passed' || status === 'Ready') return 'success'
  if (status === 'BlockedByPolicy' || status === 'Skipped') return 'warning'
  return 'danger'
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>Tool Registry</h2>
        <p>工具目录、风险分级、Mock MCP、Planner 可见性和 CloudReadonly 预检状态由后端统一裁剪。</p>
      </div>
      <div class="header-actions">
        <AiTag :tone="store.toolCatalog?.mockMcpOnly ? 'success' : 'warning'">
          {{ store.toolCatalog?.mockMcpOnly ? 'Mock MCP only' : 'External MCP visible' }}
        </AiTag>
        <AiTag :tone="readinessTone(readiness?.status)">
          {{ readiness?.boundary || 'ReadinessOnly' }} · {{ readiness?.status || 'NotConfigured' }}
        </AiTag>
        <AiTag :tone="readinessTone(sandboxStatus?.status)">
          {{ sandboxStatus?.boundary || 'SandboxSmokeOnly' }} · {{ sandboxStatus?.status || 'NotConfigured' }}
        </AiTag>
        <AiTag :tone="readinessTone(sandboxAgentTrialStatus?.status)">
          {{ sandboxAgentTrialStatus?.boundary || 'SandboxAgentTrial' }} / {{ sandboxAgentTrialStatus?.status || 'Disabled' }}
        </AiTag>
        <AiTag :tone="readinessTone(sandboxControlledTrialStatus?.status)">
          {{ sandboxControlledTrialStatus?.boundary || 'SandboxControlledTrial' }} / {{ sandboxControlledTrialStatus?.status || 'Disabled' }}
        </AiTag>
      </div>
    </div>

    <AiCard class="readiness-card">
      <div class="section-head">
        <div>
          <h3>Real CloudReadonly Readiness</h3>
          <p>只做预检和 sandbox smoke，不开放真实生产读取，不把 CloudReadonly 工具放入 Planner。</p>
        </div>
        <div class="readiness-actions">
          <AiButton
            :disabled="store.loadingStates.cloudReadonlyReadiness"
            @click="store.runCloudReadonlyReadinessCheck()"
          >
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.loadingStates.cloudReadonlyReadiness }" />
            运行 Fake 预检
          </AiButton>
          <AiButton
            :disabled="store.loadingStates.cloudReadonlyReadiness"
            @click="store.runCloudReadonlySandboxSmoke()"
          >
            <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.loadingStates.cloudReadonlyReadiness }" />
            运行 Sandbox Smoke
          </AiButton>
        </div>
      </div>

      <div class="readiness-grid">
        <div>
          <span>CloudAiRead</span>
          <strong>{{ readiness?.cloudAiReadEnabled ? 'Enabled' : 'Disabled' }}</strong>
        </div>
        <div>
          <span>Real</span>
          <strong>{{ readiness?.realEnabled ? 'Enabled' : 'Disabled' }}</strong>
        </div>
        <div>
          <span>Production Read</span>
          <strong>{{ readiness?.allowProductionRead ? 'Open' : 'Closed' }}</strong>
        </div>
        <div>
          <span>Token</span>
          <strong>{{ readiness?.tokenConfigured ? 'Configured' : 'Not configured' }}</strong>
        </div>
        <div>
          <span>Base URL</span>
          <strong>{{ readiness?.baseUrlConfigured ? 'Configured' : 'Not configured' }}</strong>
        </div>
        <div>
          <span>Last Check</span>
          <strong>{{ readiness?.lastCheckedAt || '-' }}</strong>
        </div>
      </div>

      <div class="sandbox-panel">
        <div class="section-subhead">
          <h4>CloudReadonly Sandbox Smoke</h4>
          <p>使用独立 CloudReadonlySandbox 配置，只标记为 SandboxSmokeOnly，不进入 Agent Runtime。</p>
        </div>

        <div class="readiness-grid compact">
          <div>
            <span>Sandbox</span>
            <strong>{{ sandboxStatus?.sandboxEnabled ? 'Enabled' : 'Disabled' }}</strong>
          </div>
          <div>
            <span>Sandbox Base URL</span>
            <strong>{{ sandboxStatus?.baseUrlConfigured ? 'Configured' : 'Not configured' }}</strong>
          </div>
          <div>
            <span>Sandbox Token</span>
            <strong>{{ sandboxStatus?.tokenConfigured ? 'Configured' : 'Not configured' }}</strong>
          </div>
          <div>
            <span>Last Smoke</span>
            <strong>{{ sandboxStatus?.lastSmokeAt || '-' }}</strong>
          </div>
        </div>

        <div class="message-strip" v-if="sandboxStatus?.errors?.length || sandboxStatus?.warnings?.length">
          <AiTag v-for="error in sandboxStatus?.errors ?? []" :key="`sandbox-error-${error}`" tone="danger">
            {{ error }}
          </AiTag>
          <AiTag v-for="warning in sandboxStatus?.warnings ?? []" :key="`sandbox-warning-${warning}`" tone="warning">
            {{ warning }}
          </AiTag>
        </div>

        <AiTableCard :empty="sandboxChecks.length === 0" empty-text="尚未运行 sandbox smoke">
          <table class="ai-table">
            <thead>
              <tr>
                <th>Endpoint</th>
                <th>Policy</th>
                <th>Status</th>
                <th>Rows</th>
                <th>Hash / Error</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="check in sandboxChecks" :key="`sandbox-${check.endpointCode}-${check.path}`">
                <td>
                  <strong>{{ check.endpointCode }}</strong>
                  <small>{{ check.method }} {{ check.path }}</small>
                </td>
                <td><AiTag :tone="check.policyStatus === 'Allowed' ? 'success' : 'warning'">{{ check.policyStatus }}</AiTag></td>
                <td>
                  <AiTag :tone="checkTone(check.status)">{{ check.status }}</AiTag>
                  <small>{{ check.httpStatus ?? '-' }} · {{ check.durationMs }}ms</small>
                </td>
                <td>
                  <span class="mono">{{ check.rowCount }}</span>
                  <small>{{ check.isTruncated ? 'truncated' : 'full' }}</small>
                </td>
                <td>
                  <span class="mono">{{ check.resultHash || '-' }}</span>
                  <small>{{ check.errorCode || 'no error' }}</small>
                </td>
              </tr>
            </tbody>
          </table>
        </AiTableCard>
      </div>

      <div class="sandbox-panel">
        <div class="section-subhead">
          <h4>Cloud Sandbox Agent Trial</h4>
          <p>仅固定模板进入 SandboxAgentTrial，生产 CloudReadonly 工具仍保持 disabled / hidden / non-executable。</p>
        </div>

        <div class="readiness-grid compact">
          <div>
            <span>Trial Gate</span>
            <strong>{{ sandboxAgentTrialStatus?.status || 'Disabled' }}</strong>
          </div>
          <div>
            <span>Smoke</span>
            <strong>{{ sandboxAgentTrialStatus?.sandboxSmokeStatus || 'NotConfigured' }}</strong>
          </div>
          <div>
            <span>Tool Visible</span>
            <strong>{{ sandboxAgentTrialStatus?.toolVisible ? 'Visible' : 'Hidden' }}</strong>
          </div>
          <div>
            <span>Tool Executable</span>
            <strong>{{ sandboxAgentTrialStatus?.toolExecutable ? 'Executable' : 'Blocked' }}</strong>
          </div>
        </div>

        <div class="readiness-footer">
          <AiTag :tone="cloudSandboxTool?.dataBoundary === 'CloudReadonlySandboxOnly' ? 'warning' : 'neutral'">
            {{ cloudSandboxTool?.dataBoundary || 'CloudReadonlySandboxOnly' }}
          </AiTag>
          <AiTag :tone="cloudSandboxTool?.approvalPolicy === 'SandboxAgentTrial' ? 'warning' : 'neutral'">
            {{ cloudSandboxTool?.approvalPolicy || 'SandboxAgentTrial' }}
          </AiTag>
          <AiTag tone="neutral">
            Fixed templates {{ sandboxAgentTrialStatus?.availableScenarioIds?.length ?? 0 }}
          </AiTag>
          <AiTag tone="neutral">
            Last trial {{ sandboxAgentTrialStatus?.lastTrialAt || '-' }}
          </AiTag>
        </div>

        <div
          class="message-strip"
          v-if="sandboxAgentTrialStatus?.errors?.length || sandboxAgentTrialStatus?.warnings?.length"
        >
          <AiTag
            v-for="error in sandboxAgentTrialStatus?.errors ?? []"
            :key="`sandbox-trial-error-${error}`"
            tone="danger"
          >
            {{ error }}
          </AiTag>
          <AiTag
            v-for="warning in sandboxAgentTrialStatus?.warnings ?? []"
            :key="`sandbox-trial-warning-${warning}`"
            tone="warning"
          >
            {{ warning }}
          </AiTag>
        </div>
      </div>

      <div class="sandbox-panel">
        <div class="section-subhead">
          <h4>Cloud Sandbox Controlled Trial</h4>
          <p>允许受控自由目标先生成 intent，再映射到 sandbox allowlist endpoint；生产 CloudReadonly 工具仍不开放。</p>
        </div>

        <div class="readiness-grid compact">
          <div>
            <span>Controlled Gate</span>
            <strong>{{ sandboxControlledTrialStatus?.status || 'Disabled' }}</strong>
          </div>
          <div>
            <span>Smoke</span>
            <strong>{{ sandboxControlledTrialStatus?.sandboxSmokeStatus || 'NotConfigured' }}</strong>
          </div>
          <div>
            <span>Fixed Trial</span>
            <strong>{{ sandboxControlledTrialStatus?.fixedTrialStatus || 'Disabled' }}</strong>
          </div>
          <div>
            <span>Free Goal</span>
            <strong>{{ sandboxControlledTrialStatus?.freeGoalEnabled ? 'Enabled' : 'Disabled' }}</strong>
          </div>
          <div>
            <span>Tool Visible</span>
            <strong>{{ sandboxControlledTrialStatus?.toolVisible ? 'Visible' : 'Hidden' }}</strong>
          </div>
          <div>
            <span>Tool Executable</span>
            <strong>{{ sandboxControlledTrialStatus?.toolExecutable ? 'Executable' : 'Blocked' }}</strong>
          </div>
        </div>

        <div class="readiness-footer">
          <AiTag tone="warning">SandboxControlledTrial</AiTag>
          <AiTag tone="warning">ControlledGoal</AiTag>
          <AiTag tone="neutral">
            Last trial {{ sandboxControlledTrialStatus?.lastTrialAt || '-' }}
          </AiTag>
        </div>

        <div
          class="message-strip"
          v-if="sandboxControlledTrialStatus?.errors?.length || sandboxControlledTrialStatus?.warnings?.length"
        >
          <AiTag
            v-for="error in sandboxControlledTrialStatus?.errors ?? []"
            :key="`sandbox-controlled-error-${error}`"
            tone="danger"
          >
            {{ error }}
          </AiTag>
          <AiTag
            v-for="warning in sandboxControlledTrialStatus?.warnings ?? []"
            :key="`sandbox-controlled-warning-${warning}`"
            tone="warning"
          >
            {{ warning }}
          </AiTag>
        </div>
      </div>

      <div class="message-strip" v-if="readiness?.errors?.length || readiness?.warnings?.length">
        <AiTag v-for="error in readiness?.errors ?? []" :key="`error-${error}`" tone="danger">
          {{ error }}
        </AiTag>
        <AiTag v-for="warning in readiness?.warnings ?? []" :key="`warning-${warning}`" tone="warning">
          {{ warning }}
        </AiTag>
      </div>

      <AiTableCard :empty="readinessChecks.length === 0" empty-text="尚未运行 readiness endpoint 预检">
        <table class="ai-table">
          <thead>
            <tr>
              <th>Endpoint</th>
              <th>Policy</th>
              <th>Status</th>
              <th>Rows</th>
              <th>Hash / Error</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="check in readinessChecks" :key="`${check.endpointCode}-${check.path}`">
              <td>
                <strong>{{ check.endpointCode }}</strong>
                <small>{{ check.method }} {{ check.path }}</small>
              </td>
              <td><AiTag :tone="check.policyStatus === 'Allowed' ? 'success' : 'warning'">{{ check.policyStatus }}</AiTag></td>
              <td>
                <AiTag :tone="checkTone(check.status)">{{ check.status }}</AiTag>
                <small>{{ check.httpStatus ?? '-' }} · {{ check.durationMs }}ms</small>
              </td>
              <td>
                <span class="mono">{{ check.rowCount }}</span>
                <small>{{ check.isTruncated ? 'truncated' : 'full' }}</small>
              </td>
              <td>
                <span class="mono">{{ check.resultHash || '-' }}</span>
                <small>{{ check.errorCode || 'no error' }}</small>
              </td>
            </tr>
          </tbody>
        </table>
      </AiTableCard>

      <div class="readiness-footer">
        <AiTag :tone="cloudReadonlyTool?.isEnabled ? 'danger' : 'success'">
          CloudReadonly tool {{ cloudReadonlyTool?.isEnabled ? 'enabled' : 'disabled' }}
        </AiTag>
        <AiTag :tone="cloudReadonlyTool?.isVisibleToPlanner ? 'danger' : 'success'">
          Planner {{ cloudReadonlyTool?.isVisibleToPlanner ? 'visible' : 'hidden' }}
        </AiTag>
        <AiTag :tone="cloudReadonlyTool?.isExecutableByAgent ? 'danger' : 'success'">
          Agent {{ cloudReadonlyTool?.isExecutableByAgent ? 'executable' : 'blocked' }}
        </AiTag>
        <AiTag tone="neutral">History {{ readinessHistory.length }}</AiTag>
        <AiTag tone="neutral">Sandbox history {{ sandboxHistory.length }}</AiTag>
      </div>
    </AiCard>

    <div class="registry-metrics">
      <AiCard class="metric-card">
        <span>Catalog Version</span>
        <strong>v{{ store.toolCatalog?.version ?? '-' }}</strong>
      </AiCard>
      <AiCard class="metric-card">
        <span>Planner Visible</span>
        <strong>{{ store.toolCatalog?.availableToolCount ?? 0 }}</strong>
      </AiCard>
      <AiCard class="metric-card">
        <span>Mock Tools</span>
        <strong>{{ store.toolRegistrations.filter((tool) => tool.providerType === 'MockMcp').length }}</strong>
      </AiCard>
      <AiCard class="metric-card">
        <span>Approval Tools</span>
        <strong>{{ store.toolRegistrations.filter((tool) => tool.requiresApproval).length }}</strong>
      </AiCard>
    </div>

    <div class="risk-strip">
      <AiTag v-for="[risk, count] in riskRows" :key="risk" :tone="riskTone(risk)">
        {{ risk }} {{ count }}
      </AiTag>
      <AiTag v-if="riskRows.length === 0" tone="neutral">No visible risk summary</AiTag>
    </div>

    <AiTableCard :empty="store.toolRegistrations.length === 0" empty-text="暂无工具登记">
      <table class="ai-table">
        <thead>
          <tr>
            <th>工具</th>
            <th>Provider</th>
            <th>边界</th>
            <th>风险</th>
            <th>审批</th>
            <th>Planner / Agent</th>
            <th>版本</th>
            <th>运行时</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="tool in store.toolRegistrations" :key="tool.id">
            <td>
              <strong>{{ tool.toolCode }}</strong>
              <small>{{ tool.displayName }}</small>
            </td>
            <td>
              <AiTag :tone="tool.providerType === 'MockMcp' ? 'success' : 'neutral'">
                {{ tool.providerType }}
              </AiTag>
              <small>{{ tool.category }}</small>
            </td>
            <td>
              <AiTag :tone="boundaryTone(tool.dataBoundary)">{{ tool.dataBoundary }}</AiTag>
              <small>{{ tool.businessDomains.join(', ') || 'All domains' }}</small>
            </td>
            <td><AiTag :tone="riskTone(tool.riskLevel)">{{ tool.riskLevel }}</AiTag></td>
            <td>
              <AiTag :tone="tool.requiresApproval ? 'warning' : 'success'">
                {{ tool.requiresApproval ? tool.approvalPolicy : 'None' }}
              </AiTag>
            </td>
            <td>
              <div class="flag-stack">
                <AiTag :tone="tool.isVisibleToPlanner ? 'success' : 'neutral'">
                  Planner {{ tool.isVisibleToPlanner ? 'visible' : 'hidden' }}
                </AiTag>
                <AiTag :tone="tool.isExecutableByAgent ? 'success' : 'neutral'">
                  Agent {{ tool.isExecutableByAgent ? 'exec' : 'blocked' }}
                </AiTag>
              </div>
            </td>
            <td>
              <span class="mono">schema {{ tool.schemaVersion }}</span>
              <span class="mono">catalog {{ tool.catalogVersion }}</span>
            </td>
            <td>
              <AiTag :tone="tool.runtimeAvailable ? 'success' : 'warning'">
                {{ tool.runtimeAvailable ? 'available' : 'unavailable' }}
              </AiTag>
              <small v-if="tool.sourceServerName">{{ tool.sourceServerName }}</small>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>
</template>

<style scoped>
@import './shared-config.css';

.header-actions,
.risk-strip,
.flag-stack,
.message-strip,
.readiness-footer,
.readiness-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.readiness-card {
  margin-bottom: 16px;
}

.section-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 14px;
}

.section-head h3,
.section-subhead h4 {
  margin: 0;
  color: var(--ai-text);
  font-size: 18px;
  font-weight: 950;
}

.section-subhead h4 {
  font-size: 15px;
}

.section-head p,
.section-subhead p {
  margin: 5px 0 0;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 700;
}

.sandbox-panel {
  border-top: 1px solid var(--ai-border);
  margin: 6px 0 14px;
  padding-top: 14px;
}

.section-subhead {
  margin-bottom: 12px;
}

.readiness-grid,
.registry-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(140px, 1fr));
  gap: 12px;
  margin-bottom: 12px;
}

.readiness-grid {
  grid-template-columns: repeat(6, minmax(120px, 1fr));
}

.readiness-grid.compact {
  grid-template-columns: repeat(4, minmax(140px, 1fr));
}

.readiness-grid > div,
.metric-card {
  min-height: 88px;
}

.readiness-grid > div {
  border: 1px solid var(--ai-border);
  border-radius: 8px;
  padding: 13px;
  background: var(--ai-surface-soft);
}

.readiness-grid span,
.metric-card span,
.ai-table small,
.mono {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 750;
}

.readiness-grid strong,
.metric-card strong {
  display: block;
  margin-top: 9px;
  color: var(--ai-text);
  font-size: 22px;
  font-weight: 950;
  overflow-wrap: anywhere;
}

.metric-card strong {
  font-size: 26px;
}

.message-strip,
.risk-strip,
.readiness-footer {
  margin-bottom: 12px;
}

.readiness-footer {
  margin-top: 12px;
  margin-bottom: 0;
}

.ai-table td {
  vertical-align: top;
}

.ai-table td > strong,
.ai-table td > small,
.ai-table td > .mono {
  display: block;
  max-width: 300px;
  overflow-wrap: anywhere;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
}

@media (max-width: 1180px) {
  .readiness-grid,
  .readiness-grid.compact,
  .registry-metrics {
    grid-template-columns: repeat(2, minmax(140px, 1fr));
  }
}

@media (max-width: 760px) {
  .section-head {
    flex-direction: column;
  }

  .readiness-grid,
  .readiness-grid.compact,
  .registry-metrics {
    grid-template-columns: 1fr;
  }
}
</style>
