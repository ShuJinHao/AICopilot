<script setup lang="ts">
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'
import {
  DATA_SOURCE_EXTERNAL_SYSTEM,
  mcpRiskLabel,
  mcpRiskTagType,
  runtimeNamePreview,
  safetyPreview
} from '@/views/configLabels'
import type { McpAllowedTool } from '@/types/app'

const store = useConfigStore()

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(message, title, { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' })
  await action()
  ElMessage.success('操作已完成')
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
    : store.mcpServers
        .find((server) => server.id === store.currentMcpServer.id)
        ?.toolPolicySummaries.find((policy) => policy.toolName.toLowerCase() === tool.toolName.toLowerCase()) ?? null
}
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">MCP 服务</h2>
        <p class="panel-subtitle">工具暴露需要声明只读、能力类型和风险等级。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateMcpServerDialog()">新增 MCP</el-button>
    </div>
    <el-alert
      class="safety-alert"
      type="warning"
      :closable="false"
      show-icon
      title="MCP 配置由启动期 bootstrap 读取；保存配置后需要重启服务，工具才会重新发现并应用安全策略。"
    />
    <el-table :data="store.mcpServers" stripe>
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
      <el-table-column label="风险" width="90">
        <template #default="{ row }">
          <el-tag :type="mcpRiskTagType(row.riskLevel)">
            {{ mcpRiskLabel(row.riskLevel) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="工具" min-width="180">
        <template #default="{ row }">
          <el-tag v-for="tool in row.allowedTools.slice(0, 3)" :key="tool.toolName" class="tool-tag">
            {{ tool.toolName }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="审批摘要" min-width="180">
        <template #default="{ row }">
          <el-tag
            v-for="policy in row.toolPolicySummaries"
            :key="policy.toolName"
            class="tool-tag"
            :type="policy.requiresApproval ? 'warning' : 'info'"
          >
            {{ policy.toolName }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button link type="primary" @click="store.openEditMcpServerDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除 MCP', `确认删除 ${row.name}？`, () => store.deleteMcpServer(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.mcpServer" size="92vw" title="MCP 服务">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentMcpServer.name" /></el-form-item>
      <el-form-item label="说明"><el-input v-model="store.currentMcpServer.description" /></el-form-item>
      <el-form-item label="命令"><el-input v-model="store.currentMcpServer.command" /></el-form-item>
      <el-form-item label="参数">
        <el-input v-model="store.currentMcpServer.arguments" type="textarea" :rows="3" />
      </el-form-item>
      <p class="form-hint">留空表示保留已保存参数。</p>
      <el-form-item label="允许工具矩阵">
        <div class="tool-matrix">
          <el-table :data="store.currentMcpServer.allowedTools" size="small" class="tool-matrix-table">
            <el-table-column label="工具名" min-width="150">
              <template #default="{ row }">
                <el-input v-model="row.toolName" placeholder="queryDeviceStatus" />
              </template>
            </el-table-column>
            <el-table-column label="运行时名称" min-width="210">
              <template #default="{ row }">
                <span class="mono">{{ runtimeNamePreview(store.currentMcpServer.name, row.toolName) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="外部系统" width="145">
              <template #default="{ row }">
                <el-select v-model="row.externalSystemType" placeholder="继承">
                  <el-option :value="null" label="继承" />
                  <el-option :value="DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly" label="Cloud 只读" />
                  <el-option :value="DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud" label="非云系统" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="能力" width="140">
              <template #default="{ row }">
                <el-select v-model="row.capabilityKind" placeholder="继承">
                  <el-option :value="null" label="继承" />
                  <el-option :value="0" label="只读查询" />
                  <el-option :value="1" label="诊断" />
                  <el-option :value="2" label="本地建议" />
                  <el-option :value="3" label="副作用" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="风险" width="130">
              <template #default="{ row }">
                <el-select v-model="row.riskLevel" placeholder="继承">
                  <el-option :value="null" label="继承" />
                  <el-option :value="0" label="低" />
                  <el-option :value="1" label="需审批" />
                  <el-option :value="2" label="阻断" />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="只读" width="78">
              <template #default="{ row }">
                <el-switch v-model="row.readOnlyDeclared" />
              </template>
            </el-table-column>
            <el-table-column label="MCP 标注" width="170">
              <template #default="{ row }">
                <div class="hint-switches">
                  <el-checkbox v-model="row.mcpReadOnlyHint">只读</el-checkbox>
                  <el-checkbox v-model="row.mcpDestructiveHint">破坏性</el-checkbox>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="审批" width="110">
              <template #default="{ row }">
                <el-tag :type="policySummary(row)?.requiresApproval ? 'warning' : 'info'">
                  {{ policySummary(row)?.requiresApproval ? '策略要求' : '未绑定' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="暴露预览" width="120">
              <template #default="{ row }">
                <el-tag :type="safetyPreview(row, store.currentMcpServer).type">
                  {{ safetyPreview(row, store.currentMcpServer).label }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column width="72">
              <template #default="{ $index }">
                <el-button text type="danger" @click="removeMcpAllowedTool($index)">删除</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-button class="add-tool-button" :icon="Plus" @click="addMcpAllowedTool">新增工具</el-button>
        </div>
      </el-form-item>
      <el-form-item label="启用"><el-switch v-model="store.currentMcpServer.isEnabled" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeMcpServerDialog()">取消</el-button>
      <el-button type="primary" :loading="store.submittingStates.mcpServer" @click="store.saveMcpServer()">
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.safety-alert {
  margin-bottom: 12px;
}

.form-hint {
  margin: -8px 0 12px;
  color: var(--app-text-muted);
  font-size: 12px;
}

.tool-tag {
  margin-right: 4px;
}

.tool-matrix {
  display: grid;
  gap: 10px;
  width: 100%;
}

.tool-matrix-table {
  width: 100%;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  word-break: break-all;
}

.hint-switches {
  display: grid;
  gap: 2px;
}

.add-tool-button {
  justify-self: start;
}

:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
