<script setup lang="ts">
import { computed, ref } from 'vue'
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiNumberInput from '@/components/ai/AiNumberInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
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

const usageChat = computed({
  get: () => store.currentLanguageModel.usages.includes('Chat'),
  set: (value: boolean) => toggleUsage('Chat', value)
})
const usageRouting = computed({
  get: () => store.currentLanguageModel.usages.includes('Routing'),
  set: (value: boolean) => toggleUsage('Routing', value)
})

function toggleUsage(usage: LanguageModelUsage, enabled: boolean) {
  const next = new Set(store.currentLanguageModel.usages)
  if (enabled) {
    next.add(usage)
  } else {
    next.delete(usage)
  }
  store.currentLanguageModel.usages = [...next]
}

function protocolLabel(protocolType: string) {
  return protocolLabels[protocolType] ?? protocolType
}

function connectivityLabel(status: string) {
  return connectivityLabels[status] ?? status
}

function connectivityTone(status: string) {
  if (status === 'Succeeded') return 'success'
  if (status === 'Failed') return 'danger'
  return 'neutral'
}

function isTestingModel(id: string) {
  return testingModelIds.value.has(id)
}

function setTestingModel(id: string, value: boolean) {
  const next = new Set(testingModelIds.value)
  if (value) next.add(id)
  else next.delete(id)
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
    showAiToast('success', `配置完成，耗时 ${result.elapsedMilliseconds}ms`)
    return
  }
  showAiToast('error', result.error || result.message || '连接测试失败')
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
  if (protocolType !== 'AnthropicMessages') return

  store.currentLanguageModel.baseUrl = 'https://api.anthropic.com'
  if (!store.currentLanguageModel.provider || ['OpenAI', 'DeepSeek', '通义千问', '豆包'].includes(store.currentLanguageModel.provider)) {
    store.currentLanguageModel.provider = 'Anthropic'
  }
}

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, title))) return
  await action()
  showAiToast('success', '操作已完成')
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>语言模型</h2>
        <p>配置最终智能体与路由模型。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateLanguageModelDialog()">
        <Plus class="h-4 w-4" />
        新增模型
      </AiButton>
    </div>

    <AiTableCard :empty="store.languageModels.length === 0" empty-text="暂无语言模型">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>服务商</th>
            <th>协议</th>
            <th>接口地址</th>
            <th>上下文</th>
            <th>最大输出</th>
            <th>用途</th>
            <th>状态</th>
            <th>密钥</th>
            <th>连通性</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.languageModels" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ row.provider }}</td>
            <td><AiTag tone="neutral">{{ protocolLabel(row.protocolType) }}</AiTag></td>
            <td class="truncate-cell">{{ row.baseUrl }}</td>
            <td>{{ row.contextWindowTokens }}</td>
            <td>{{ row.maxOutputTokens }}</td>
            <td>
              <AiTag v-for="usage in row.usages" :key="usage" tone="blue" class="mr-1">
                {{ usageLabels[usage as keyof typeof usageLabels] ?? usage }}
              </AiTag>
            </td>
            <td><AiTag :tone="row.isEnabled ? 'success' : 'neutral'">{{ row.isEnabled ? '启用' : '停用' }}</AiTag></td>
            <td><AiTag :tone="row.hasApiKey ? 'success' : 'neutral'">{{ row.hasApiKey ? '已配置' : '未配置' }}</AiTag></td>
            <td>
              <AiTag :tone="connectivityTone(row.connectivityStatus)">
                {{ connectivityLabel(row.connectivityStatus) }}
              </AiTag>
            </td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" variant="lime" :disabled="isTestingModel(row.id)" @click="testSavedLanguageModel(row)">
                  {{ isTestingModel(row.id) ? '测试中' : '测试' }}
                </AiButton>
                <AiButton size="sm" @click="store.openEditLanguageModelDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除模型', `确认删除 ${row.name}？`, () => store.deleteLanguageModel(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.languageModel" title="语言模型" width="560px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentLanguageModel.name" /></label>
      <label><span>服务商</span><AiInput v-model="store.currentLanguageModel.provider" /></label>
      <label>
        <span>协议类型</span>
        <AiSelect
          v-model="store.currentLanguageModel.protocolType"
          :options="protocolOptions"
          @update:model-value="(value) => onProtocolChange(String(value ?? ''))"
        />
      </label>
      <label><span>接口地址</span><AiInput v-model="store.currentLanguageModel.baseUrl" /></label>
      <label>
        <span>密钥</span>
        <AiInput
          v-model="store.currentLanguageModel.apiKey"
          type="password"
          autocomplete="new-password"
          @update:model-value="store.currentLanguageModel.apiKeyAction = 'replace'"
        />
      </label>
      <AiCheckbox
        v-if="store.currentLanguageModel.hasApiKey"
        v-model="store.currentLanguageModel.clearApiKey"
        @update:model-value="store.currentLanguageModel.apiKeyAction = store.currentLanguageModel.clearApiKey ? 'clear' : 'keep'"
      >
        清除已有密钥
      </AiCheckbox>
      <div>
        <span class="field-label">用途</span>
        <div class="inline-options">
          <AiCheckbox v-model="usageChat">对话回答</AiCheckbox>
          <AiCheckbox v-model="usageRouting">路由识别</AiCheckbox>
        </div>
      </div>
      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentLanguageModel.isEnabled" /></div>
      <label><span>上下文窗口</span><AiNumberInput v-model="store.currentLanguageModel.contextWindowTokens" :min="1024" :step="1024" /></label>
      <label><span>最大输出</span><AiNumberInput v-model="store.currentLanguageModel.maxOutputTokens" :min="256" :step="256" /></label>
      <label><span>温度</span><AiNumberInput v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" /></label>
      <footer>
        <AiButton @click="store.closeLanguageModelDialog()">取消</AiButton>
        <AiButton :disabled="drawerTesting" @click="testCurrentLanguageModel()">{{ drawerTesting ? '测试中' : '测试连接' }}</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.languageModel" @click="store.saveLanguageModel()">
          {{ store.submittingStates.languageModel ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';

.truncate-cell {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.field-label {
  display: block;
  margin-bottom: 7px;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 850;
}
</style>
