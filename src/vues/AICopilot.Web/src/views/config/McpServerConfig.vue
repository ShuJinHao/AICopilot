<script setup lang="ts">
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useConfigStore } from '@/stores/configStore'
import { DATA_SOURCE_EXTERNAL_SYSTEM, mcpRiskLabel, runtimeNamePreview, safetyPreview } from '@/views/configLabels'
import type { McpAllowedTool } from '@/types/app'

const store = useConfigStore()

const externalSystemOptions = [
  { value: null, label: '继承' },
  { value: DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly, label: 'Cloud 只读' },
  { value: DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud, label: '非云系统' }
]
const capabilityOptions = [
  { value: null, label: '继承' },
  { value: 0, label: '只读查询' },
  { value: 1, label: '诊断' },
  { value: 2, label: '本地建议' },
  { value: 3, label: '副作用' }
]
const riskOptions = [
  { value: null, label: '继承' },
  { value: 0, label: '低' },
  { value: 1, label: '需审批' },
  { value: 2, label: '阻断' }
]

function mapTone(type: string | undefined) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger') return 'danger'
  return 'neutral'
}

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, title))) return
  await action()
  showAiToast('success', '操作已完成')
}

function addMcpAllowedTool() {
  store.currentMcpServer.allowedTools.push({
    toolName: '',
    externalSystemType: store.currentMcpServer.externalSystemType,
    capabilityKind: store.currentMcpServer.capabilityKind,
    riskLevel: store.currentMcpServer.riskLevel,
    readOnlyDeclared: true,
    mcpReadOnlyHint: true,
    mcpDestructiveHint: false,
    mcpIdempotentHint: null
  })
}

function removeMcpAllowedTool(index: number) {
  store.currentMcpServer.allowedTools.splice(index, 1)
}

function policySummary(tool: McpAllowedTool) {
  return store.currentMcpServer.allowedTools.length === 0
    ? null
    : (store.mcpServers
        .find((server) => server.id === store.currentMcpServer.id)
        ?.toolPolicySummaries.find((policy) => policy.toolName.toLowerCase() === tool.toolName.toLowerCase()) ?? null)
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>MCP 服务</h2>
        <p>工具暴露需要声明只读、能力类型和风险等级。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateMcpServerDialog()">
        <Plus class="h-4 w-4" />
        新增 MCP
      </AiButton>
    </div>
    <div class="warning-note">
      MCP 配置保存后会在运行时刷新周期内收敛；如果工具未暴露，请检查 allowlist、安全策略、连接状态和 MCP 运行时刷新。
    </div>
    <AiTableCard :empty="store.mcpServers.length === 0" empty-text="暂无 MCP 服务">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>说明</th>
            <th>风险</th>
            <th>工具</th>
            <th>审批摘要</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.mcpServers" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ row.description }}</td>
            <td><AiTag tone="neutral">{{ mcpRiskLabel(row.riskLevel) }}</AiTag></td>
            <td>
              <AiTag v-for="tool in row.allowedTools.slice(0, 3)" :key="tool.toolName" tone="teal" class="mr-1">
                {{ tool.toolName }}
              </AiTag>
            </td>
            <td>
              <AiTag v-for="policy in row.toolPolicySummaries" :key="policy.toolName" :tone="policy.requiresApproval ? 'warning' : 'neutral'" class="mr-1">
                {{ policy.toolName }}
              </AiTag>
            </td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" @click="store.openEditMcpServerDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除 MCP', `确认删除 ${row.name}？`, () => store.deleteMcpServer(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.mcpServer" title="MCP 服务" width="92vw">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentMcpServer.name" /></label>
      <label><span>说明</span><AiInput v-model="store.currentMcpServer.description" /></label>
      <label><span>命令</span><AiInput v-model="store.currentMcpServer.command" /></label>
      <label><span>参数</span><AiTextarea v-model="store.currentMcpServer.arguments" :rows="3" /></label>
      <p class="form-hint">留空表示保留已保存参数。</p>

      <section class="tool-matrix">
        <div class="matrix-head">
          <div>
            <h3>允许工具矩阵</h3>
            <p>每个工具声明外部系统、能力、风险和 MCP 标注。</p>
          </div>
          <AiButton variant="lime" @click="addMcpAllowedTool">
            <Plus class="h-4 w-4" />
            新增工具
          </AiButton>
        </div>
        <div class="matrix-scroll">
          <table class="ai-table compact">
            <thead>
              <tr>
                <th>工具名</th>
                <th>运行时名称</th>
                <th>外部系统</th>
                <th>能力</th>
                <th>风险</th>
                <th>只读</th>
                <th>MCP 标注</th>
                <th>审批</th>
                <th>暴露预览</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(row, index) in store.currentMcpServer.allowedTools" :key="index">
                <td><AiInput v-model="row.toolName" placeholder="queryDeviceStatus" /></td>
                <td><span class="mono">{{ runtimeNamePreview(store.currentMcpServer.name, row.toolName) }}</span></td>
                <td><AiSelect v-model="row.externalSystemType" :options="externalSystemOptions" /></td>
                <td><AiSelect v-model="row.capabilityKind" :options="capabilityOptions" /></td>
                <td><AiSelect v-model="row.riskLevel" :options="riskOptions" /></td>
                <td><AiSwitch v-model="row.readOnlyDeclared" /></td>
                <td>
                  <div class="hint-switches">
                    <AiCheckbox :model-value="Boolean(row.mcpReadOnlyHint)" @update:model-value="(value) => (row.mcpReadOnlyHint = value)">
                      只读
                    </AiCheckbox>
                    <AiCheckbox :model-value="Boolean(row.mcpDestructiveHint)" @update:model-value="(value) => (row.mcpDestructiveHint = value)">
                      破坏性
                    </AiCheckbox>
                  </div>
                </td>
                <td>
                  <AiTag :tone="policySummary(row)?.requiresApproval ? 'warning' : 'neutral'">
                    {{ policySummary(row)?.requiresApproval ? '策略要求' : '未绑定' }}
                  </AiTag>
                </td>
                <td>
                  <AiTag :tone="mapTone(safetyPreview(row, store.currentMcpServer).type)">
                    {{ safetyPreview(row, store.currentMcpServer).label }}
                  </AiTag>
                </td>
                <td><AiButton size="sm" variant="danger" @click="removeMcpAllowedTool(index)">删除</AiButton></td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentMcpServer.isEnabled" /></div>
      <footer>
        <AiButton @click="store.closeMcpServerDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.mcpServer" @click="store.saveMcpServer()">
          {{ store.submittingStates.mcpServer ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';

.form-hint {
  margin: -8px 0 0;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 700;
}

.tool-matrix {
  display: grid;
  gap: 12px;
}

.matrix-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.matrix-head h3,
.matrix-head p {
  margin: 0;
}

.matrix-head p {
  margin-top: 4px;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 650;
}

.matrix-scroll {
  overflow: auto;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
}

.compact th,
.compact td {
  padding: 10px;
}

.compact :deep(input),
.compact :deep(select) {
  min-width: 150px;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  word-break: break-all;
}

.hint-switches {
  display: grid;
  gap: 4px;
}
</style>
