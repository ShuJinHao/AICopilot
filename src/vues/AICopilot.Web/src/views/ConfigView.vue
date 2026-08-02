<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { Bot, Cloud, RefreshCw, Settings2 } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiCard from '@/components/ai/AiCard.vue'
import AiCheckbox from '@/components/ai/AiCheckbox.vue'
import AiDataPage from '@/components/ai/AiDataPage.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiNumberInput from '@/components/ai/AiNumberInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import AppShell from '@/components/layout/AppShell.vue'
import { CONFIG_STORE_MESSAGES } from '@/constants/messages'
import { showAiToast } from '@/composables/useAiFeedback'
import { configService } from '@/services/configService'
import { useConfigStore } from '@/stores/configStore'
import { toStoreErrorMessage } from '@/stores/useDialogCrud'
import type { CloudReadonlyStatus, LanguageModelSummary } from '@/types/app'

const store = useConfigStore()
const cloudReadonlyStatus = ref<CloudReadonlyStatus | null>(null)
const isLoadingCloudReadonlyStatus = ref(false)
const testingModelIds = ref<Set<string>>(new Set())
const drawerTesting = ref(false)

const protocolOptions = [
  { label: 'OpenAI-compatible', value: 'OpenAICompatible' },
  { label: 'Claude / Anthropic Messages', value: 'AnthropicMessages' },
]

const modelOptions = computed(() =>
  store.languageModels
    .filter((model) => model.isEnabled)
    .map((model) => ({ label: `${model.name} / ${model.provider}`, value: model.id })),
)

function modelName(modelId: string) {
  const model = store.languageModels.find((item) => item.id === modelId)
  return model ? `${model.name} / ${model.provider}` : '未绑定可用模型'
}

function connectionTone(status?: string | null) {
  if (status === 'Succeeded') return 'success'
  if (status === 'Failed') return 'danger'
  return 'neutral'
}

function connectionLabel(status?: string | null) {
  if (status === 'Succeeded') return '连接正常'
  if (status === 'Failed') return '连接异常'
  return '未测试'
}

function cloudStatusTone(status?: string | null) {
  if (status === 'RealReady') return 'success'
  if (status === 'Disabled') return 'neutral'
  return 'warning'
}

function yesNo(value?: boolean) {
  return value ? '是' : '否'
}

async function refreshCloudReadonlyStatus() {
  isLoadingCloudReadonlyStatus.value = true
  try {
    cloudReadonlyStatus.value = await configService.getCloudReadonlyStatus()
  } catch (error) {
    console.error('Failed to load Cloud readonly status.', error)
    cloudReadonlyStatus.value = null
  } finally {
    isLoadingCloudReadonlyStatus.value = false
  }
}

async function refresh() {
  try {
    await Promise.all([store.refresh(), refreshCloudReadonlyStatus()])
  } catch (error) {
    store.errorMessage = toStoreErrorMessage(
      error,
      CONFIG_STORE_MESSAGES.pageLoadFailed,
      CONFIG_STORE_MESSAGES.pageLoadForbidden,
    )
  }
}

function markTesting(id: string, active: boolean) {
  const next = new Set(testingModelIds.value)
  if (active) next.add(id)
  else next.delete(id)
  testingModelIds.value = next
}

async function testModel(model: LanguageModelSummary) {
  markTesting(model.id, true)
  try {
    const result = await configService.testLanguageModel({ id: model.id, persistResult: true })
    showAiToast(
      result.success ? 'success' : 'error',
      result.error || result.message || (result.success ? '模型连接正常。' : '模型连接失败。'),
    )
    await store.refreshLanguageModels()
  } catch (error) {
    showAiToast('error', '模型连接失败，请稍后重试。')
    console.error('Failed to test language model.', error)
  } finally {
    markTesting(model.id, false)
  }
}

async function testCurrentLanguageModel() {
  drawerTesting.value = true
  try {
    const form = store.currentLanguageModel
    const result = await configService.testLanguageModel({
      id: form.id,
      provider: form.provider,
      protocolType: form.protocolType,
      name: form.name,
      baseUrl: form.baseUrl,
      apiKey: form.apiKeyAction === 'replace' ? form.apiKey : undefined,
      clearApiKey: form.apiKeyAction === 'clear',
      contextWindowTokens: form.contextWindowTokens,
      maxOutputTokens: form.maxOutputTokens,
      temperature: form.temperature,
      usages: ['Chat'],
      persistResult: false,
    })
    showAiToast(
      result.success ? 'success' : 'error',
      result.error || result.message || (result.success ? '模型连接正常。' : '模型连接失败。'),
    )
  } catch (error) {
    showAiToast('error', '模型连接失败，请检查模型配置。')
    console.error('Failed to test current language model.', error)
  } finally {
    drawerTesting.value = false
  }
}

function onProtocolChange(value: string) {
  store.currentLanguageModel.protocolType = value
}

onMounted(refresh)
</script>

<template>
  <AppShell>
    <AiDataPage
      eyebrow="Harness Configuration"
      title="对话配置"
      description="主聊天的实际模型由启用的 ChatAnswer 会话模板决定；Text-to-SQL 模板仅供内部轻量 Agent 使用。"
    >
      <template #actions>
        <AiButton :disabled="store.isLoading || isLoadingCloudReadonlyStatus" @click="refresh">
          <RefreshCw :size="16" />
          {{ store.isLoading ? '刷新中' : '刷新' }}
        </AiButton>
      </template>

      <div v-if="store.errorMessage" class="error-banner">{{ store.errorMessage }}</div>

      <div class="summary-grid">
        <AiCard tone="teal" class="summary-card">
          <Bot :size="22" />
          <div>
            <strong>{{ store.languageModels.length }}</strong>
            <span>对话模型</span>
          </div>
        </AiCard>
        <AiCard tone="blue" class="summary-card">
          <Settings2 :size="22" />
          <div>
            <strong>{{ store.conversationTemplates.length }}</strong>
            <span>活动模板</span>
          </div>
        </AiCard>
        <AiCard tone="surface" class="summary-card">
          <Cloud :size="22" />
          <div>
            <strong>{{ cloudReadonlyStatus?.status || '-' }}</strong>
            <span>Cloud 只读</span>
          </div>
        </AiCard>
      </div>

      <div class="config-grid">
        <AiCard class="config-card">
          <header class="card-head">
            <div>
              <h2>语言模型</h2>
              <p>仅保留 Chat 用途，模型是否用于主聊天由模板绑定决定。</p>
            </div>
            <AiButton size="sm" variant="lime" @click="store.openCreateLanguageModelDialog()">新增模型</AiButton>
          </header>

          <div class="record-list">
            <article v-for="model in store.languageModels" :key="model.id" class="record-row">
              <div class="record-main">
                <div class="record-title">
                  <strong>{{ model.name }}</strong>
                  <AiTag :tone="model.isEnabled ? 'success' : 'neutral'">
                    {{ model.isEnabled ? '已启用' : '已停用' }}
                  </AiTag>
                  <AiTag :tone="connectionTone(model.connectivityStatus)">
                    {{ connectionLabel(model.connectivityStatus) }}
                  </AiTag>
                </div>
                <span>{{ model.provider }} · {{ model.protocolType }}</span>
                <small>上下文 {{ model.contextWindowTokens }} · 最大输出 {{ model.maxOutputTokens }}</small>
              </div>
              <div class="row-actions">
                <AiButton
                  size="sm"
                  :disabled="testingModelIds.has(model.id)"
                  @click="testModel(model)"
                >
                  {{ testingModelIds.has(model.id) ? '测试中' : '测试连接' }}
                </AiButton>
                <AiButton size="sm" @click="store.openEditLanguageModelDialog(model.id)">编辑</AiButton>
              </div>
            </article>
            <p v-if="store.languageModels.length === 0" class="empty-copy">尚未配置对话模型。</p>
          </div>
        </AiCard>

        <AiCard class="config-card">
          <header class="card-head">
            <div>
              <h2>会话模板</h2>
              <p>内建仅保留 chat_answer 与 business_readonly_text_to_sql。</p>
            </div>
          </header>

          <div class="record-list">
            <article
              v-for="template in store.conversationTemplates"
              :key="template.id"
              class="record-row"
            >
              <div class="record-main">
                <div class="record-title">
                  <strong>{{ template.name }}</strong>
                  <AiTag :tone="template.isEnabled ? 'success' : 'neutral'">
                    {{ template.isEnabled ? '已启用' : '已停用' }}
                  </AiTag>
                </div>
                <span class="mono">{{ template.code || '-' }}</span>
                <small>{{ modelName(template.modelId) }}</small>
              </div>
              <AiButton size="sm" @click="store.openEditConversationTemplateDialog(template.id)">
                编辑模板
              </AiButton>
            </article>
            <p v-if="store.conversationTemplates.length === 0" class="empty-copy">尚未播种活动模板。</p>
          </div>
        </AiCard>
      </div>

      <AiCard class="cloud-card" tone="blue">
        <header class="card-head">
          <div>
            <h2>Cloud 只读数据</h2>
            <p>{{ cloudReadonlyStatus?.message || '当前无法读取 Cloud 只读状态。' }}</p>
          </div>
          <AiTag :tone="cloudStatusTone(cloudReadonlyStatus?.status)">
            {{ cloudReadonlyStatus?.status || '未知' }}
          </AiTag>
        </header>
        <div class="cloud-facts">
          <span>模式 <strong>{{ cloudReadonlyStatus?.mode || '-' }}</strong></span>
          <span>BaseUrl <strong>{{ yesNo(cloudReadonlyStatus?.baseUrlConfigured) }}</strong></span>
          <span>凭据 <strong>{{ yesNo(cloudReadonlyStatus?.tokenConfigured) }}</strong></span>
          <span>正式只读 <strong>{{ yesNo(cloudReadonlyStatus?.productionReadAllowed) }}</strong></span>
        </div>
      </AiCard>
    </AiDataPage>

    <AiDrawer v-model="store.dialogStates.languageModel" title="对话模型" width="620px">
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
        <div class="form-row"><span>用途</span><AiTag tone="blue">Chat</AiTag></div>
        <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentLanguageModel.isEnabled" /></div>
        <label><span>上下文窗口</span><AiNumberInput v-model="store.currentLanguageModel.contextWindowTokens" :min="1024" :step="1024" /></label>
        <label><span>最大输出</span><AiNumberInput v-model="store.currentLanguageModel.maxOutputTokens" :min="256" :step="256" /></label>
        <label><span>温度</span><AiNumberInput v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" /></label>
        <div v-if="store.actionErrors.languageModel" class="error-banner">{{ store.actionErrors.languageModel }}</div>
        <footer>
          <AiButton @click="store.closeLanguageModelDialog()">取消</AiButton>
          <AiButton :disabled="drawerTesting" @click="testCurrentLanguageModel">
            {{ drawerTesting ? '测试中' : '测试连接' }}
          </AiButton>
          <AiButton variant="primary" :disabled="store.submittingStates.languageModel" @click="store.saveLanguageModel()">
            {{ store.submittingStates.languageModel ? '保存中' : '保存' }}
          </AiButton>
        </footer>
      </div>
    </AiDrawer>

    <AiDrawer v-model="store.dialogStates.conversationTemplate" title="会话模板" width="760px">
      <div class="ai-form">
        <label><span>名称</span><AiInput v-model="store.currentConversationTemplate.name" /></label>
        <label><span>说明</span><AiInput v-model="store.currentConversationTemplate.description" /></label>
        <label>
          <span>绑定模型</span>
          <AiSelect v-model="store.currentConversationTemplate.modelId" :options="modelOptions" placeholder="选择模型" />
        </label>
        <div class="form-grid">
          <label><span>最大输出</span><AiNumberInput v-model="store.currentConversationTemplate.maxTokens" :min="256" :step="256" /></label>
          <label><span>温度</span><AiNumberInput v-model="store.currentConversationTemplate.temperature" :min="0" :max="2" :step="0.1" /></label>
        </div>
        <label><span>系统提示词</span><AiTextarea v-model="store.currentConversationTemplate.systemPrompt" :rows="16" /></label>
        <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentConversationTemplate.isEnabled" /></div>
        <div v-if="store.actionErrors.conversationTemplate" class="error-banner">{{ store.actionErrors.conversationTemplate }}</div>
        <footer>
          <AiButton @click="store.closeConversationTemplateDialog()">取消</AiButton>
          <AiButton variant="primary" :disabled="store.submittingStates.conversationTemplate" @click="store.saveConversationTemplate()">
            {{ store.submittingStates.conversationTemplate ? '保存中' : '保存' }}
          </AiButton>
        </footer>
      </div>
    </AiDrawer>
  </AppShell>
</template>

<style scoped>
.summary-grid,
.config-grid {
  display: grid;
  gap: 16px;
}

.summary-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.summary-card {
  display: flex;
  align-items: center;
  gap: 14px;
}

.summary-card div {
  display: grid;
}

.summary-card strong {
  font-size: 22px;
  font-weight: 950;
}

.summary-card span,
.record-main span,
.record-main small,
.card-head p {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 750;
}

.config-grid {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.config-card,
.cloud-card {
  display: grid;
  align-content: start;
  gap: 16px;
}

.card-head,
.record-row,
.record-title,
.row-actions,
.form-row,
.ai-form footer,
.cloud-facts {
  display: flex;
  align-items: center;
}

.card-head,
.record-row,
.form-row {
  justify-content: space-between;
  gap: 12px;
}

.card-head h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 950;
}

.card-head p {
  margin: 5px 0 0;
  line-height: 1.6;
}

.record-list,
.record-main,
.ai-form {
  display: grid;
  gap: 12px;
}

.record-row {
  min-width: 0;
  border: 1px solid var(--ai-border);
  border-radius: 16px;
  padding: 13px;
  background: var(--ai-surface-soft);
}

.record-main {
  min-width: 0;
  gap: 5px;
}

.record-title,
.row-actions,
.cloud-facts {
  flex-wrap: wrap;
  gap: 8px;
}

.record-title strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cloud-facts span {
  border: 1px solid rgba(148, 163, 184, 0.25);
  border-radius: 12px;
  padding: 9px 11px;
  background: rgba(255, 255, 255, 0.64);
  font-size: 12px;
}

.empty-copy {
  margin: 0;
  border: 1px dashed var(--ai-border);
  border-radius: 16px;
  padding: 16px;
  color: var(--ai-text-muted);
  text-align: center;
}

.ai-form label {
  display: grid;
  gap: 7px;
  color: var(--ai-text);
  font-size: 13px;
  font-weight: 850;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.ai-form footer {
  justify-content: flex-end;
  gap: 8px;
  margin-top: 8px;
}

.error-banner {
  border: 1px solid #fecaca;
  border-radius: 14px;
  padding: 12px 14px;
  background: #fef2f2;
  color: #b42318;
  font-size: 13px;
  font-weight: 800;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}

@media (max-width: 980px) {
  .summary-grid,
  .config-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 640px) {
  .record-row,
  .card-head {
    align-items: flex-start;
    flex-direction: column;
  }

  .form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
