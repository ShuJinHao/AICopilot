<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useConfigStore } from '@/stores/configStore'
import type { ApprovalPolicySummary, BusinessDatabaseSummary, McpServerSummary } from '@/types/app'

const store = useConfigStore()
const activeTab = ref('models')

const approvalToolNamesText = computed({
  get: () => store.currentApprovalPolicy.toolNames.join('\n'),
  set: (value: string) => {
    store.currentApprovalPolicy.toolNames = value
      .split(/\r?\n|,/)
      .map((item) => item.trim())
      .filter(Boolean)
  }
})

const mcpAllowedToolsText = computed({
  get: () => store.currentMcpServer.allowedTools.map((item) => item.toolName).join('\n'),
  set: (value: string) => {
    store.currentMcpServer.allowedTools = value
      .split(/\r?\n|,/)
      .map((toolName) => toolName.trim())
      .filter(Boolean)
      .map((toolName) => ({
        toolName,
        readOnlyDeclared: true
      }))
  }
})

onMounted(() => {
  void store.refresh()
})

function providerLabel(provider: number) {
  return provider === 1 ? 'PostgreSQL' : provider === 2 ? 'SQL Server' : provider === 3 ? 'MySQL' : `Provider ${provider}`
}

function externalSystemLabel(type: number) {
  return type === 1 ? 'Cloud ReadOnly' : 'Non Cloud'
}

function policyTarget(policy: ApprovalPolicySummary) {
  return `${policy.targetType} / ${policy.targetName}`
}

function mcpRisk(server: McpServerSummary) {
  return server.riskLevel >= 3 ? '高' : server.riskLevel === 2 ? '中' : '低'
}

function databaseState(row: BusinessDatabaseSummary) {
  if (!row.isEnabled) return { label: '停用', type: 'info' as const }
  if (!row.isReadOnly || !row.readOnlyCredentialVerified) return { label: '需复核', type: 'warning' as const }
  return { label: '只读已验证', type: 'success' as const }
}

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(message, title, { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' })
  await action()
  ElMessage.success('操作已完成')
}
</script>

<template>
  <AppShell>
    <div class="page config-page">
      <header class="page-header">
        <div>
          <p class="page-kicker">Runtime Configuration</p>
          <h1 class="page-title">运行配置</h1>
          <p class="page-description">集中管理模型、模板、MCP 工具、只读业务数据源和审批策略。</p>
        </div>
        <el-button :icon="Refresh" :loading="store.isLoading" @click="store.refresh()">刷新</el-button>
      </header>

      <div class="metric-strip">
        <div class="metric">
          <span class="metric-label">语言模型</span>
          <strong class="metric-value">{{ store.languageModels.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">会话模板</span>
          <strong class="metric-value">{{ store.conversationTemplates.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">只读数据源</span>
          <strong class="metric-value">{{ store.businessDatabases.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">MCP 服务</span>
          <strong class="metric-value">{{ store.mcpServers.length }}</strong>
        </div>
      </div>

      <el-alert v-if="store.errorMessage" type="error" show-icon :closable="false" :title="store.errorMessage" />

      <el-tabs v-model="activeTab" class="config-tabs">
        <el-tab-pane label="模型" name="models">
          <section class="panel">
            <div class="panel-header">
              <div>
                <h2 class="panel-title">语言模型</h2>
                <p class="panel-subtitle">配置 final agent 与路由模型。</p>
              </div>
              <el-button type="primary" :icon="Plus" @click="store.openCreateLanguageModelDialog()">新增模型</el-button>
            </div>
            <el-table :data="store.languageModels" stripe>
              <el-table-column prop="name" label="名称" min-width="180" />
              <el-table-column prop="provider" label="Provider" width="120" />
              <el-table-column prop="baseUrl" label="Base URL" min-width="220" show-overflow-tooltip />
              <el-table-column prop="maxTokens" label="Max Tokens" width="120" />
              <el-table-column label="API Key" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.hasApiKey ? 'success' : 'info'">{{ row.hasApiKey ? '已配置' : '未配置' }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="150" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button link type="primary" @click="store.openEditLanguageModelDialog(row.id)">编辑</el-button>
                    <el-button link type="danger" @click="confirmAction('删除模型', `确认删除 ${row.name}？`, () => store.deleteLanguageModel(row.id))">删除</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </section>
        </el-tab-pane>

        <el-tab-pane label="模板" name="templates">
          <section class="panel">
            <div class="panel-header">
              <div>
                <h2 class="panel-title">会话模板</h2>
                <p class="panel-subtitle">系统提示词和模型参数入口。</p>
              </div>
              <el-button type="primary" :icon="Plus" @click="store.openCreateConversationTemplateDialog()">新增模板</el-button>
            </div>
            <el-table :data="store.conversationTemplates" stripe>
              <el-table-column prop="name" label="名称" min-width="180" />
              <el-table-column prop="description" label="说明" min-width="240" show-overflow-tooltip />
              <el-table-column label="状态" width="100">
                <template #default="{ row }">
                  <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="150" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button link type="primary" @click="store.openEditConversationTemplateDialog(row.id)">编辑</el-button>
                    <el-button link type="danger" @click="confirmAction('删除模板', `确认删除 ${row.name}？`, () => store.deleteConversationTemplate(row.id))">删除</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </section>
        </el-tab-pane>

        <el-tab-pane label="DataAnalysis" name="data">
          <section class="panel">
            <div class="panel-header">
              <div>
                <h2 class="panel-title">只读业务数据源</h2>
                <p class="panel-subtitle">DataAnalysis 只能连接已验证的只读数据源。</p>
              </div>
              <el-button type="primary" :icon="Plus" @click="store.openCreateBusinessDatabaseDialog()">新增数据源</el-button>
            </div>
            <el-alert
              class="safety-alert"
              type="info"
              :closable="false"
              show-icon
              title="配置管理台保存时始终强制只读；运行期仍会执行 SQL 安全拒绝、结果截断和连接只读校验。"
            />
            <el-table :data="store.businessDatabases" stripe>
              <el-table-column prop="name" label="名称" min-width="160" />
              <el-table-column label="Provider" width="120">
                <template #default="{ row }">{{ providerLabel(row.provider) }}</template>
              </el-table-column>
              <el-table-column label="外部系统" width="140">
                <template #default="{ row }">{{ externalSystemLabel(row.externalSystemType) }}</template>
              </el-table-column>
              <el-table-column label="状态" width="130">
                <template #default="{ row }">
                  <el-tag :type="databaseState(row).type">{{ databaseState(row).label }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
              <el-table-column label="操作" width="150" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button link type="primary" @click="store.openEditBusinessDatabaseDialog(row.id)">编辑</el-button>
                    <el-button link type="danger" @click="confirmAction('删除数据源', `确认删除 ${row.name}？`, () => store.deleteBusinessDatabase(row.id))">删除</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </section>

          <section class="panel semantic-panel">
            <div class="panel-header">
              <div>
                <h2 class="panel-title">语义映射健康度</h2>
                <p class="panel-subtitle">确认语义视图是否存在、字段是否齐备。</p>
              </div>
            </div>
            <el-table :data="store.semanticSourceStatuses" stripe>
              <el-table-column prop="target" label="Target" width="150" />
              <el-table-column prop="databaseName" label="业务库" min-width="160" />
              <el-table-column prop="effectiveSourceName" label="视图/来源" min-width="180" />
              <el-table-column prop="status" label="状态" min-width="140" />
            </el-table>
          </section>
        </el-tab-pane>

        <el-tab-pane label="MCP" name="mcp">
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
                  <el-tag :type="row.riskLevel >= 3 ? 'danger' : row.riskLevel === 2 ? 'warning' : 'success'">
                    {{ mcpRisk(row) }}
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
                    <el-button link type="danger" @click="confirmAction('删除 MCP', `确认删除 ${row.name}？`, () => store.deleteMcpServer(row.id))">删除</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </section>
        </el-tab-pane>

        <el-tab-pane label="审批" name="approval">
          <section class="panel">
            <div class="panel-header">
              <div>
                <h2 class="panel-title">审批策略</h2>
                <p class="panel-subtitle">定义工具调用的 HITL 和现场复核要求。</p>
              </div>
              <el-button type="primary" :icon="Plus" @click="store.openCreateApprovalPolicyDialog()">新增策略</el-button>
            </div>
            <el-table :data="store.approvalPolicies" stripe>
              <el-table-column prop="name" label="名称" min-width="160" />
              <el-table-column label="目标" min-width="180">
                <template #default="{ row }">{{ policyTarget(row) }}</template>
              </el-table-column>
              <el-table-column label="现场复核" width="110">
                <template #default="{ row }">
                  <el-tag :type="row.requiresOnsiteAttestation ? 'warning' : 'info'">
                    {{ row.requiresOnsiteAttestation ? '需要' : '不需要' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column label="状态" width="90">
                <template #default="{ row }">
                  <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column label="操作" width="150" fixed="right">
                <template #default="{ row }">
                  <div class="table-actions">
                    <el-button link type="primary" @click="store.openEditApprovalPolicyDialog(row.id)">编辑</el-button>
                    <el-button link type="danger" @click="confirmAction('删除策略', `确认删除 ${row.name}？`, () => store.deleteApprovalPolicy(row.id))">删除</el-button>
                  </div>
                </template>
              </el-table-column>
            </el-table>
          </section>
        </el-tab-pane>
      </el-tabs>

      <el-drawer v-model="store.dialogStates.languageModel" size="520px" title="语言模型">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentLanguageModel.name" /></el-form-item>
          <el-form-item label="Provider"><el-input v-model="store.currentLanguageModel.provider" /></el-form-item>
          <el-form-item label="Base URL"><el-input v-model="store.currentLanguageModel.baseUrl" /></el-form-item>
          <el-form-item label="API Key"><el-input v-model="store.currentLanguageModel.apiKey" type="password" show-password /></el-form-item>
          <el-form-item label="Max Tokens"><el-input-number v-model="store.currentLanguageModel.maxTokens" :min="1" /></el-form-item>
          <el-form-item label="Temperature"><el-input-number v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeLanguageModelDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.languageModel" @click="store.saveLanguageModel()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.dialogStates.conversationTemplate" size="640px" title="会话模板">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentConversationTemplate.name" /></el-form-item>
          <el-form-item label="说明"><el-input v-model="store.currentConversationTemplate.description" /></el-form-item>
          <el-form-item label="系统提示词"><el-input v-model="store.currentConversationTemplate.systemPrompt" type="textarea" :rows="10" /></el-form-item>
          <el-form-item label="模型"><el-select v-model="store.currentConversationTemplate.modelId" filterable><el-option v-for="model in store.languageModels" :key="model.id" :label="model.name" :value="model.id" /></el-select></el-form-item>
          <el-form-item label="启用"><el-switch v-model="store.currentConversationTemplate.isEnabled" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeConversationTemplateDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.conversationTemplate" @click="store.saveConversationTemplate()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.dialogStates.businessDatabase" size="560px" title="只读业务数据源">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentBusinessDatabase.name" /></el-form-item>
          <el-form-item label="说明"><el-input v-model="store.currentBusinessDatabase.description" /></el-form-item>
          <el-form-item label="连接串"><el-input v-model="store.currentBusinessDatabase.connectionString" type="textarea" :rows="4" /></el-form-item>
          <el-form-item label="Provider"><el-select v-model="store.currentBusinessDatabase.provider"><el-option :value="1" label="PostgreSQL" /><el-option :value="2" label="SQL Server" /><el-option :value="3" label="MySQL" /></el-select></el-form-item>
          <el-form-item label="外部系统"><el-select v-model="store.currentBusinessDatabase.externalSystemType"><el-option :value="0" label="Non Cloud" /><el-option :value="1" label="Cloud ReadOnly" /></el-select></el-form-item>
          <el-form-item label="只读凭据已验证"><el-switch v-model="store.currentBusinessDatabase.readOnlyCredentialVerified" /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="store.currentBusinessDatabase.isEnabled" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeBusinessDatabaseDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.businessDatabase" @click="store.saveBusinessDatabase()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.dialogStates.mcpServer" size="580px" title="MCP 服务">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentMcpServer.name" /></el-form-item>
          <el-form-item label="说明"><el-input v-model="store.currentMcpServer.description" /></el-form-item>
          <el-form-item label="Command"><el-input v-model="store.currentMcpServer.command" /></el-form-item>
          <el-form-item label="Arguments"><el-input v-model="store.currentMcpServer.arguments" type="textarea" :rows="3" /></el-form-item>
          <p class="form-hint">留空表示保留已保存参数。</p>
          <el-form-item label="允许工具"><el-input v-model="mcpAllowedToolsText" type="textarea" :rows="5" placeholder="每行一个工具名，默认声明只读" /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="store.currentMcpServer.isEnabled" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeMcpServerDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.mcpServer" @click="store.saveMcpServer()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.dialogStates.approvalPolicy" size="560px" title="审批策略">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentApprovalPolicy.name" /></el-form-item>
          <el-form-item label="说明"><el-input v-model="store.currentApprovalPolicy.description" /></el-form-item>
          <el-form-item label="目标类型"><el-select v-model="store.currentApprovalPolicy.targetType"><el-option label="Plugin" value="Plugin" /><el-option label="McpServer" value="McpServer" /></el-select></el-form-item>
          <el-form-item label="目标名称"><el-input v-model="store.currentApprovalPolicy.targetName" /></el-form-item>
          <el-form-item label="工具名"><el-input v-model="approvalToolNamesText" type="textarea" :rows="5" placeholder="每行一个工具名" /></el-form-item>
          <el-form-item label="需要现场复核"><el-switch v-model="store.currentApprovalPolicy.requiresOnsiteAttestation" /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="store.currentApprovalPolicy.isEnabled" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeApprovalPolicyDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.approvalPolicy" @click="store.saveApprovalPolicy()">保存</el-button>
        </template>
      </el-drawer>
    </div>
  </AppShell>
</template>

<style scoped>
.config-page {
  display: grid;
  align-content: start;
  gap: 14px;
  height: 100%;
  overflow: auto;
}

.config-tabs {
  min-width: 0;
}

.semantic-panel {
  margin-top: 14px;
}

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

:deep(.el-tabs__content) {
  overflow: visible;
}

:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
