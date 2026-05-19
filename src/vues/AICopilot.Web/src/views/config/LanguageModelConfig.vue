<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { configService } from '@/services/configService'
import { useConfigStore } from '@/stores/configStore'
import type { LanguageModelSummary, LanguageModelTestResult, LanguageModelUsage } from '@/types/app'

const store = useConfigStore()
const usageLabels = {
  Chat: '对话',
  Routing: '路由'
} as const
const protocolOptions = [
  { label: 'OpenAI-compatible', value: 'OpenAICompatible' },
  { label: 'Claude / Anthropic Messages', value: 'AnthropicMessages' }
]

const protocolLabels: Record<string, string> = {
  OpenAICompatible: 'OpenAI-compatible',
  AnthropicMessages: 'Claude / Anthropic Messages'
}

const connectivityLabels: Record<string, string> = {
  Unknown: '未测试',
  Succeeded: '配置完成',
  Failed: '异常'
}

const testingModelIds = ref<Set<string>>(new Set())
const drawerTesting = ref(false)

function protocolLabel(protocolType: string) {
  return protocolLabels[protocolType] ?? protocolType
}

function connectivityLabel(status: string) {
  return connectivityLabels[status] ?? status
}

function connectivityType(status: string) {
  if (status === 'Succeeded') return 'success'
  if (status === 'Failed') return 'danger'
  return 'info'
}

function isTestingModel(id: string) {
  return testingModelIds.value.has(id)
}

function setTestingModel(id: string, value: boolean) {
  const next = new Set(testingModelIds.value)
  if (value) {
    next.add(id)
  } else {
    next.delete(id)
  }

  testingModelIds.value = next
}

function buildCurrentLanguageModelTestPayload() {
  const form = store.currentLanguageModel
  return {
    id: form.id,
    provider: form.provider.trim(),
    protocolType: form.protocolType.trim(),
    name: form.name.trim(),
    baseUrl: form.baseUrl.trim(),
    apiKey: form.apiKeyAction === 'replace' ? form.apiKey.trim() : '',
    clearApiKey: form.apiKeyAction === 'clear',
    maxTokens: form.contextWindowTokens,
    contextWindowTokens: form.contextWindowTokens,
    maxOutputTokens: form.maxOutputTokens,
    usages: form.usages.length > 0 ? form.usages : (['Chat'] as LanguageModelUsage[]),
    temperature: form.temperature,
    persistResult: false
  }
}

function showTestResult(result: LanguageModelTestResult) {
  if (result.success) {
    ElMessage.success(`配置完成，耗时 ${result.elapsedMilliseconds}ms`)
    return
  }

  ElMessageBox.alert(result.error || result.message || '连接测试失败', '连接测试失败', {
    type: 'error',
    confirmButtonText: '确认'
  })
}

async function testCurrentLanguageModel() {
  drawerTesting.value = true
  try {
    const result = await configService.testLanguageModel(buildCurrentLanguageModelTestPayload())
    showTestResult(result)
  } finally {
    drawerTesting.value = false
  }
}

async function testSavedLanguageModel(row: LanguageModelSummary) {
  setTestingModel(row.id, true)
  try {
    const result = await configService.testLanguageModel({ id: row.id, persistResult: true })
    await store.refreshLanguageModels()
    showTestResult(result)
  } finally {
    setTestingModel(row.id, false)
  }
}

function onProtocolChange(protocolType: string) {
  if (protocolType !== 'AnthropicMessages') {
    return
  }

  store.currentLanguageModel.baseUrl = 'https://api.anthropic.com'
  if (
    !store.currentLanguageModel.provider ||
    ['OpenAI', 'DeepSeek', '通义千问', '豆包'].includes(store.currentLanguageModel.provider)
  ) {
    store.currentLanguageModel.provider = 'Anthropic'
  }
}

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(message, title, { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' })
  await action()
  ElMessage.success('操作已完成')
}
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">语言模型</h2>
        <p class="panel-subtitle">配置最终智能体与路由模型。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateLanguageModelDialog()">新增模型</el-button>
    </div>
    <el-table :data="store.languageModels" stripe>
      <el-table-column prop="name" label="名称" min-width="180" />
      <el-table-column prop="provider" label="服务商" width="120" />
      <el-table-column prop="protocolType" label="协议" width="150">
        <template #default="{ row }">
          <el-tag type="info">{{ protocolLabel(row.protocolType) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="baseUrl" label="接口地址" min-width="220" show-overflow-tooltip />
      <el-table-column prop="contextWindowTokens" label="上下文窗口" width="130" />
      <el-table-column prop="maxOutputTokens" label="最大输出" width="110" />
      <el-table-column label="用途" width="150">
        <template #default="{ row }">
          <el-tag v-for="usage in row.usages" :key="usage" size="small" class="usage-tag">
            {{ usageLabels[usage as keyof typeof usageLabels] ?? usage }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="密钥" width="110">
        <template #default="{ row }">
          <el-tag :type="row.hasApiKey ? 'success' : 'info'">{{ row.hasApiKey ? '已配置' : '未配置' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="连通性" width="130">
        <template #default="{ row }">
          <el-tooltip
            v-if="row.connectivityStatus === 'Failed' && row.connectivityError"
            :content="row.connectivityError"
            placement="top"
          >
            <el-tag :type="connectivityType(row.connectivityStatus)">
              {{ connectivityLabel(row.connectivityStatus) }}
            </el-tag>
          </el-tooltip>
          <el-tag v-else :type="connectivityType(row.connectivityStatus)">
            {{ connectivityLabel(row.connectivityStatus) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="210" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button
              link
              type="success"
              :loading="isTestingModel(row.id)"
              @click="testSavedLanguageModel(row)"
            >
              测试
            </el-button>
            <el-button link type="primary" @click="store.openEditLanguageModelDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除模型', `确认删除 ${row.name}？`, () => store.deleteLanguageModel(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.languageModel" size="520px" title="语言模型">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentLanguageModel.name" /></el-form-item>
      <el-form-item label="服务商"><el-input v-model="store.currentLanguageModel.provider" /></el-form-item>
      <el-form-item label="协议类型">
        <el-select v-model="store.currentLanguageModel.protocolType" @change="onProtocolChange">
          <el-option v-for="item in protocolOptions" :key="item.value" :label="item.label" :value="item.value" />
        </el-select>
      </el-form-item>
      <el-form-item label="接口地址"><el-input v-model="store.currentLanguageModel.baseUrl" /></el-form-item>
      <el-form-item label="密钥">
        <el-input
          v-model="store.currentLanguageModel.apiKey"
          type="password"
          show-password
          @input="store.currentLanguageModel.apiKeyAction = 'replace'"
        />
        <el-checkbox
          v-if="store.currentLanguageModel.hasApiKey"
          v-model="store.currentLanguageModel.clearApiKey"
          @change="store.currentLanguageModel.apiKeyAction = store.currentLanguageModel.clearApiKey ? 'clear' : 'keep'"
        >
          清除已有密钥
        </el-checkbox>
      </el-form-item>
      <el-form-item label="用途">
        <el-checkbox-group v-model="store.currentLanguageModel.usages">
          <el-checkbox label="Chat">对话回答</el-checkbox>
          <el-checkbox label="Routing">路由识别</el-checkbox>
        </el-checkbox-group>
      </el-form-item>
      <el-form-item label="启用">
        <el-switch v-model="store.currentLanguageModel.isEnabled" />
      </el-form-item>
      <el-form-item label="上下文窗口">
        <el-input-number v-model="store.currentLanguageModel.contextWindowTokens" :min="1024" :step="1024" />
      </el-form-item>
      <el-form-item label="最大输出">
        <el-input-number v-model="store.currentLanguageModel.maxOutputTokens" :min="256" :step="256" />
      </el-form-item>
      <el-form-item label="温度">
        <el-input-number v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeLanguageModelDialog()">取消</el-button>
      <el-button :loading="drawerTesting" @click="testCurrentLanguageModel()">测试连接</el-button>
      <el-button
        type="primary"
        :loading="store.submittingStates.languageModel"
        @click="store.saveLanguageModel()"
      >
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
:deep(.el-drawer__body) {
  overflow: auto;
}

.usage-tag + .usage-tag {
  margin-left: 4px;
}
</style>
