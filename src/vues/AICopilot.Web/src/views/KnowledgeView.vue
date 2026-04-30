<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, Edit, Refresh, Search, Upload } from '@element-plus/icons-vue'
import {
  KNOWLEDGE_READ_PERMISSIONS,
  KNOWLEDGE_WRITE_PERMISSIONS
} from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'
import type {
  EmbeddingModelFormModel,
  KnowledgeBaseFormModel,
  KnowledgeDocumentStatus
} from '@/types/app'

const authStore = useAuthStore()
const ragStore = useRagStore()

const embeddingModelFormRef = ref<FormInstance>()
const knowledgeBaseFormRef = ref<FormInstance>()
const fileInputRef = ref<HTMLInputElement>()

const canReadKnowledge = computed(() => authStore.hasAnyPermission(KNOWLEDGE_READ_PERMISSIONS))
const canReadEmbeddingModels = computed(() =>
  authStore.hasAnyPermission(['Rag.GetEmbeddingModel', 'Rag.GetListEmbeddingModels'])
)
const canReadKnowledgeBases = computed(() =>
  authStore.hasAnyPermission(['Rag.GetKnowledgeBase', 'Rag.GetListKnowledgeBases'])
)
const canReadDocuments = computed(() => authStore.hasPermission('Rag.GetListDocuments'))
const canSearchKnowledge = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.search))
const canCreateEmbeddingModels = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.embeddingModel.create)
)
const canUpdateEmbeddingModels = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.embeddingModel.update)
)
const canDeleteEmbeddingModels = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.embeddingModel.delete)
)
const canCreateKnowledgeBases = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.create)
)
const canUpdateKnowledgeBases = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.update)
)
const canDeleteKnowledgeBases = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.delete)
)
const canUploadDocuments = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.upload)
)
const canDeleteDocuments = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.delete)
)

const selectedKnowledgeBase = computed(
  () =>
    ragStore.knowledgeBases.find((item) => item.id === ragStore.selectedKnowledgeBaseId) ?? null
)
const hasEmbeddingModels = computed(() => ragStore.embeddingModels.length > 0)
const canCreateKnowledgeBaseNow = computed(
  () => canCreateKnowledgeBases.value && hasEmbeddingModels.value
)

const embeddingModelRules: FormRules<EmbeddingModelFormModel> = {
  name: [{ required: true, message: '请输入嵌入模型名称', trigger: 'blur' }],
  provider: [{ required: true, message: '请输入模型提供方', trigger: 'blur' }],
  baseUrl: [{ required: true, message: '请输入模型服务地址', trigger: 'blur' }],
  modelName: [{ required: true, message: '请输入模型标识', trigger: 'blur' }],
  dimensions: [{ required: true, type: 'number', min: 1, message: '向量维度必须大于 0', trigger: 'change' }],
  maxTokens: [{ required: true, type: 'number', min: 1, message: 'Token 上限必须大于 0', trigger: 'change' }]
}

const knowledgeBaseRules: FormRules<KnowledgeBaseFormModel> = {
  name: [{ required: true, message: '请输入知识库名称', trigger: 'blur' }],
  description: [{ required: true, message: '请输入知识库说明', trigger: 'blur' }],
  embeddingModelId: [{ required: true, message: '请选择嵌入模型', trigger: 'change' }]
}

const documentStatusNames: Record<number, string> = {
  0: 'Pending',
  1: 'Parsing',
  2: 'Splitting',
  3: 'Embedding',
  4: 'Indexed',
  5: 'Failed'
}

function normalizedDocumentStatus(status: KnowledgeDocumentStatus) {
  return typeof status === 'number' ? documentStatusNames[status] ?? String(status) : status
}

function documentStatusLabel(status: KnowledgeDocumentStatus) {
  switch (normalizedDocumentStatus(status)) {
    case 'Pending':
      return '等待处理'
    case 'Parsing':
      return '解析中'
    case 'Splitting':
      return '切片中'
    case 'Embedding':
      return '向量化中'
    case 'Indexed':
      return '已索引'
    case 'Failed':
      return '失败'
    default:
      return '未知'
  }
}

function documentStatusType(status: KnowledgeDocumentStatus) {
  switch (normalizedDocumentStatus(status)) {
    case 'Indexed':
      return 'success'
    case 'Failed':
      return 'danger'
    case 'Parsing':
    case 'Splitting':
    case 'Embedding':
      return 'warning'
    default:
      return 'info'
  }
}

function embeddingModelName(id: string) {
  return ragStore.embeddingModels.find((item) => item.id === id)?.name ?? '未找到模型'
}

function formatDate(value?: string | null) {
  if (!value) {
    return '-'
  }

  return new Date(value).toLocaleString()
}

async function refreshAll() {
  if (!canReadKnowledge.value) {
    return
  }

  await ragStore.refresh()
}

function openCreateEmbeddingModelDialog() {
  if (!canCreateEmbeddingModels.value) {
    return
  }

  ragStore.openCreateEmbeddingModelDialog()
}

async function openEditEmbeddingModelDialog(id: string) {
  if (!canUpdateEmbeddingModels.value) {
    return
  }

  await ragStore.openEditEmbeddingModelDialog(id)
}

async function saveEmbeddingModel() {
  if (!embeddingModelFormRef.value) {
    return
  }

  await embeddingModelFormRef.value.validate()
  await ragStore.saveEmbeddingModel()
  ElMessage.success('嵌入模型已保存')
}

async function confirmDeleteEmbeddingModel(id: string) {
  if (!canDeleteEmbeddingModels.value) {
    return
  }

  await ElMessageBox.confirm('确认删除该嵌入模型？已绑定知识库时后端会拒绝删除。', '删除嵌入模型', {
    type: 'warning'
  })
  await ragStore.deleteEmbeddingModel(id)
  ElMessage.success('嵌入模型已删除')
}

function openCreateKnowledgeBaseDialog() {
  if (!canCreateKnowledgeBases.value) {
    return
  }

  if (!hasEmbeddingModels.value) {
    ElMessage.warning('请先创建至少一个嵌入模型。')
    return
  }

  ragStore.openCreateKnowledgeBaseDialog()
}

async function openEditKnowledgeBaseDialog(id: string) {
  if (!canUpdateKnowledgeBases.value) {
    return
  }

  await ragStore.openEditKnowledgeBaseDialog(id)
}

async function saveKnowledgeBase() {
  if (!knowledgeBaseFormRef.value) {
    return
  }

  await knowledgeBaseFormRef.value.validate()
  await ragStore.saveKnowledgeBase()
  ElMessage.success('知识库已保存')
}

async function confirmDeleteKnowledgeBase(id: string) {
  if (!canDeleteKnowledgeBases.value) {
    return
  }

  await ElMessageBox.confirm('确认删除该知识库？其中的文档和索引也会一并删除。', '删除知识库', {
    type: 'warning'
  })
  await ragStore.deleteKnowledgeBase(id)
  ElMessage.success('知识库已删除')
}

function openUploadFilePicker() {
  if (!canUploadDocuments.value || !selectedKnowledgeBase.value) {
    return
  }

  fileInputRef.value?.click()
}

async function handleDocumentFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''

  if (!file) {
    return
  }

  await ragStore.uploadDocument(file)
  ElMessage.success('文档已上传，后台将继续索引。')
}

async function confirmDeleteDocument(id: number) {
  if (!canDeleteDocuments.value) {
    return
  }

  await ElMessageBox.confirm('确认删除该文档？对应索引将不再用于检索。', '删除文档', {
    type: 'warning'
  })
  await ragStore.deleteDocument(id)
  ElMessage.success('文档已删除')
}

async function searchKnowledgeBase() {
  if (!canSearchKnowledge.value) {
    return
  }

  await ragStore.searchKnowledgeBase()
}

onMounted(async () => {
  await refreshAll()
})
</script>

<template>
  <div class="knowledge-page">
    <section class="page-header">
      <div>
        <h1>知识库</h1>
        <p>管理嵌入模型、知识库文档和检索验证。</p>
      </div>
      <el-button :icon="Refresh" :loading="ragStore.isLoading" @click="refreshAll">刷新</el-button>
    </section>

    <el-alert
      v-if="ragStore.errorMessage"
      :title="ragStore.errorMessage"
      type="error"
      show-icon
      class="page-alert"
    />

    <el-alert
      v-if="!canReadKnowledge"
      title="当前账号没有知识库管理权限。"
      type="warning"
      show-icon
      class="page-alert"
    />

    <div v-else class="knowledge-layout">
      <aside class="kb-sidebar">
        <div class="panel-header">
          <div>
            <h2>知识库列表</h2>
            <span>{{ ragStore.knowledgeBases.length }} 个知识库</span>
          </div>
          <el-button
            type="primary"
            size="small"
            :disabled="!canCreateKnowledgeBaseNow"
            @click="openCreateKnowledgeBaseDialog"
          >
            新增
          </el-button>
        </div>

        <el-empty v-if="!ragStore.knowledgeBases.length" description="暂无知识库" />
        <div v-else class="kb-list">
          <button
            v-for="item in ragStore.knowledgeBases"
            :key="item.id"
            class="kb-item"
            :class="{ active: item.id === ragStore.selectedKnowledgeBaseId }"
            @click="ragStore.selectKnowledgeBase(item.id)"
          >
            <span class="kb-name">{{ item.name }}</span>
            <span class="kb-meta">{{ item.documentCount }} 个文档</span>
          </button>
        </div>
      </aside>

      <main class="knowledge-main">
        <section v-if="canReadEmbeddingModels" class="panel">
          <div class="panel-header">
            <div>
              <h2>嵌入模型</h2>
              <span>{{ ragStore.embeddingModels.length }} 个配置</span>
            </div>
            <el-button
              type="primary"
              :disabled="!canCreateEmbeddingModels"
              @click="openCreateEmbeddingModelDialog"
            >
              新增模型
            </el-button>
          </div>

          <el-table
            :data="ragStore.embeddingModels"
            :loading="ragStore.loadingStates.embeddingModel"
            border
            empty-text="暂无嵌入模型"
          >
            <el-table-column prop="name" label="名称" min-width="160" />
            <el-table-column prop="provider" label="提供方" width="120" />
            <el-table-column prop="modelName" label="模型标识" min-width="180" />
            <el-table-column prop="dimensions" label="维度" width="90" />
            <el-table-column prop="maxTokens" label="Token" width="90" />
            <el-table-column label="密钥" width="90">
              <template #default="{ row }">
                <el-tag :type="row.hasApiKey ? 'success' : 'info'" size="small">
                  {{ row.hasApiKey ? '已配置' : '未配置' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag :type="row.isEnabled ? 'success' : 'info'" size="small">
                  {{ row.isEnabled ? '启用' : '停用' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="150" fixed="right">
              <template #default="{ row }">
                <el-button
                  text
                  :icon="Edit"
                  :disabled="!canUpdateEmbeddingModels"
                  @click="openEditEmbeddingModelDialog(row.id)"
                />
                <el-button
                  text
                  type="danger"
                  :icon="Delete"
                  :disabled="!canDeleteEmbeddingModels"
                  @click="confirmDeleteEmbeddingModel(row.id)"
                />
              </template>
            </el-table-column>
          </el-table>
        </section>

        <section v-if="selectedKnowledgeBase && canReadKnowledgeBases" class="panel">
          <div class="panel-header">
            <div>
              <h2>{{ selectedKnowledgeBase.name }}</h2>
              <span>{{ embeddingModelName(selectedKnowledgeBase.embeddingModelId) }}</span>
            </div>
            <div class="panel-actions">
              <el-button
                :icon="Edit"
                :disabled="!canUpdateKnowledgeBases"
                @click="openEditKnowledgeBaseDialog(selectedKnowledgeBase.id)"
              >
                编辑
              </el-button>
              <el-button
                type="danger"
                :icon="Delete"
                :disabled="!canDeleteKnowledgeBases"
                @click="confirmDeleteKnowledgeBase(selectedKnowledgeBase.id)"
              >
                删除
              </el-button>
            </div>
          </div>
          <p class="description-text">{{ selectedKnowledgeBase.description }}</p>
        </section>

        <section v-if="selectedKnowledgeBase && canReadDocuments" class="panel">
          <div class="panel-header">
            <div>
              <h2>文档</h2>
              <span>{{ ragStore.documents.length }} 个文档</span>
            </div>
            <div class="panel-actions">
              <el-button :icon="Refresh" @click="ragStore.refreshDocuments()">刷新状态</el-button>
              <el-button
                type="primary"
                :icon="Upload"
                :disabled="!canUploadDocuments"
                :loading="ragStore.loadingStates.document"
                @click="openUploadFilePicker"
              >
                上传
              </el-button>
              <input
                ref="fileInputRef"
                class="file-input"
                type="file"
                @change="handleDocumentFileChange"
              >
            </div>
          </div>

          <el-alert
            v-if="ragStore.actionErrors.document"
            :title="ragStore.actionErrors.document"
            type="error"
            show-icon
            class="section-alert"
          />

          <el-table
            :data="ragStore.documents"
            :loading="ragStore.loadingStates.document"
            border
            empty-text="暂无文档"
          >
            <el-table-column prop="name" label="文件名" min-width="180" />
            <el-table-column label="状态" width="110">
              <template #default="{ row }">
                <el-tag :type="documentStatusType(row.status)" size="small">
                  {{ documentStatusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="chunkCount" label="切片" width="80" />
            <el-table-column label="创建时间" min-width="160">
              <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
            </el-table-column>
            <el-table-column label="处理时间" min-width="160">
              <template #default="{ row }">{{ formatDate(row.processedAt) }}</template>
            </el-table-column>
            <el-table-column label="错误" min-width="180">
              <template #default="{ row }">{{ row.errorMessage || '-' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="90" fixed="right">
              <template #default="{ row }">
                <el-button
                  text
                  type="danger"
                  :icon="Delete"
                  :disabled="!canDeleteDocuments"
                  @click="confirmDeleteDocument(row.id)"
                />
              </template>
            </el-table-column>
          </el-table>
        </section>

        <section v-if="selectedKnowledgeBase" class="panel">
          <div class="panel-header">
            <div>
              <h2>检索验证</h2>
              <span>{{ ragStore.searchResults.length }} 条结果</span>
            </div>
          </div>

          <el-alert
            v-if="ragStore.actionErrors.search"
            :title="ragStore.actionErrors.search"
            type="error"
            show-icon
            class="section-alert"
          />

          <div class="search-row">
            <el-input
              v-model="ragStore.searchQuery"
              placeholder="输入检索文本"
              clearable
              @keyup.enter="searchKnowledgeBase"
            />
            <el-input-number v-model="ragStore.searchTopK" :min="1" :max="20" />
            <el-input-number v-model="ragStore.searchMinScore" :min="0" :max="1" :step="0.05" />
            <el-button
              type="primary"
              :icon="Search"
              :disabled="!canSearchKnowledge || !ragStore.searchQuery.trim()"
              :loading="ragStore.loadingStates.search"
              @click="searchKnowledgeBase"
            >
              检索
            </el-button>
          </div>

          <el-empty v-if="!ragStore.searchResults.length" description="暂无检索结果" />
          <div v-else class="search-results">
            <article v-for="item in ragStore.searchResults" :key="`${item.documentId}-${item.score}`">
              <div class="result-meta">
                <span>{{ item.documentName || `文档 ${item.documentId}` }}</span>
                <el-tag size="small">{{ item.score.toFixed(3) }}</el-tag>
              </div>
              <p>{{ item.text }}</p>
            </article>
          </div>
        </section>

        <section v-if="!selectedKnowledgeBase" class="panel empty-panel">
          <el-empty description="请选择或创建一个知识库" />
        </section>
      </main>
    </div>

    <el-dialog
      v-model="ragStore.dialogStates.embeddingModel"
      :title="ragStore.dialogModes.embeddingModel === 'create' ? '新增嵌入模型' : '编辑嵌入模型'"
      width="720px"
      destroy-on-close
      @closed="ragStore.closeEmbeddingModelDialog()"
    >
      <el-alert
        v-if="ragStore.actionErrors.embeddingModel"
        :title="ragStore.actionErrors.embeddingModel"
        type="error"
        show-icon
        class="section-alert"
      />

      <el-form
        ref="embeddingModelFormRef"
        :model="ragStore.currentEmbeddingModel"
        :rules="embeddingModelRules"
        label-position="top"
      >
        <div class="inline-fields">
          <el-form-item label="名称" prop="name">
            <el-input v-model="ragStore.currentEmbeddingModel.name" placeholder="例如 OpenAI Embedding" />
          </el-form-item>
          <el-form-item label="提供方" prop="provider">
            <el-input v-model="ragStore.currentEmbeddingModel.provider" placeholder="例如 OpenAI" />
          </el-form-item>
        </div>
        <el-form-item label="服务地址" prop="baseUrl">
          <el-input v-model="ragStore.currentEmbeddingModel.baseUrl" placeholder="https://api.openai.com/v1" />
        </el-form-item>
        <div class="inline-fields">
          <el-form-item label="模型标识" prop="modelName">
            <el-input v-model="ragStore.currentEmbeddingModel.modelName" placeholder="text-embedding-3-small" />
          </el-form-item>
          <el-form-item label="启用状态">
            <el-switch v-model="ragStore.currentEmbeddingModel.isEnabled" />
          </el-form-item>
        </div>
        <div class="inline-fields">
          <el-form-item label="向量维度" prop="dimensions">
            <el-input-number v-model="ragStore.currentEmbeddingModel.dimensions" :min="1" style="width: 100%" />
          </el-form-item>
          <el-form-item label="Token 上限" prop="maxTokens">
            <el-input-number v-model="ragStore.currentEmbeddingModel.maxTokens" :min="1" style="width: 100%" />
          </el-form-item>
        </div>
        <el-form-item v-if="ragStore.dialogModes.embeddingModel === 'edit'" label="密钥处理">
          <el-radio-group v-model="ragStore.currentEmbeddingModel.apiKeyAction">
            <el-radio-button label="keep">保留</el-radio-button>
            <el-radio-button label="replace">替换</el-radio-button>
            <el-radio-button label="clear">清空</el-radio-button>
          </el-radio-group>
          <div class="field-tip">
            当前密钥：{{ ragStore.currentEmbeddingModel.hasApiKey ? '已配置' : '未配置' }}
          </div>
        </el-form-item>
        <el-form-item
          v-if="
            ragStore.dialogModes.embeddingModel === 'create' ||
            ragStore.currentEmbeddingModel.apiKeyAction === 'replace'
          "
          label="API Key"
        >
          <el-input
            v-model="ragStore.currentEmbeddingModel.apiKey"
            type="password"
            show-password
            placeholder="请输入 API Key，可留空"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="ragStore.closeEmbeddingModelDialog()">取消</el-button>
          <el-button
            type="primary"
            :loading="ragStore.submittingStates.embeddingModel"
            @click="saveEmbeddingModel"
          >
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>

    <el-dialog
      v-model="ragStore.dialogStates.knowledgeBase"
      :title="ragStore.dialogModes.knowledgeBase === 'create' ? '新增知识库' : '编辑知识库'"
      width="640px"
      destroy-on-close
      @closed="ragStore.closeKnowledgeBaseDialog()"
    >
      <el-alert
        v-if="ragStore.actionErrors.knowledgeBase"
        :title="ragStore.actionErrors.knowledgeBase"
        type="error"
        show-icon
        class="section-alert"
      />

      <el-form
        ref="knowledgeBaseFormRef"
        :model="ragStore.currentKnowledgeBase"
        :rules="knowledgeBaseRules"
        label-position="top"
      >
        <el-form-item label="名称" prop="name">
          <el-input v-model="ragStore.currentKnowledgeBase.name" placeholder="请输入知识库名称" />
        </el-form-item>
        <el-form-item label="说明" prop="description">
          <el-input
            v-model="ragStore.currentKnowledgeBase.description"
            type="textarea"
            :rows="3"
            placeholder="请输入知识库说明"
          />
        </el-form-item>
        <el-form-item label="嵌入模型" prop="embeddingModelId">
          <el-select v-model="ragStore.currentKnowledgeBase.embeddingModelId" style="width: 100%">
            <el-option
              v-for="item in ragStore.embeddingModels"
              :key="item.id"
              :label="`${item.name} / ${item.modelName}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
      </el-form>

      <template #footer>
        <div class="dialog-actions">
          <el-button @click="ragStore.closeKnowledgeBaseDialog()">取消</el-button>
          <el-button
            type="primary"
            :loading="ragStore.submittingStates.knowledgeBase"
            @click="saveKnowledgeBase"
          >
            保存
          </el-button>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.knowledge-page {
  display: grid;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.page-header h1,
.panel-header h2 {
  margin: 0;
  color: #0f172a;
}

.page-header h1 {
  font-size: 24px;
}

.page-header p,
.panel-header span,
.description-text,
.field-tip {
  margin: 4px 0 0;
  color: #64748b;
  font-size: 13px;
}

.page-alert,
.section-alert {
  margin-bottom: 4px;
}

.knowledge-layout {
  display: grid;
  grid-template-columns: minmax(240px, 320px) minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.kb-sidebar,
.panel {
  border: 1px solid #d9e2ec;
  border-radius: 8px;
  background: #fff;
  padding: 16px;
}

.knowledge-main {
  display: grid;
  gap: 16px;
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}

.panel-actions,
.dialog-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.kb-list {
  display: grid;
  gap: 8px;
}

.kb-item {
  width: 100%;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px;
  background: #fff;
  text-align: left;
  cursor: pointer;
  display: grid;
  gap: 4px;
}

.kb-item.active {
  border-color: #2563eb;
  background: #eff6ff;
}

.kb-name {
  color: #0f172a;
  font-weight: 600;
}

.kb-meta {
  color: #64748b;
  font-size: 12px;
}

.description-text {
  line-height: 1.6;
}

.file-input {
  display: none;
}

.search-row {
  display: grid;
  grid-template-columns: minmax(220px, 1fr) 120px 140px auto;
  gap: 8px;
  align-items: center;
}

.search-results {
  display: grid;
  gap: 10px;
  margin-top: 14px;
}

.search-results article {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px;
  background: #f8fafc;
}

.search-results p {
  margin: 8px 0 0;
  color: #334155;
  line-height: 1.6;
  white-space: pre-wrap;
}

.result-meta {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  color: #0f172a;
  font-weight: 600;
}

.inline-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.empty-panel {
  min-height: 220px;
  display: grid;
  place-items: center;
}

@media (max-width: 960px) {
  .knowledge-layout,
  .inline-fields,
  .search-row {
    grid-template-columns: 1fr;
  }

  .page-header,
  .panel-header {
    display: grid;
  }
}
</style>
