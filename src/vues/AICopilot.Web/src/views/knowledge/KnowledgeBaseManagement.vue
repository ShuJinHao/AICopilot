<script setup lang="ts">
import { computed } from 'vue'
import { RefreshCcw, SlidersHorizontal, Trash2, UploadCloud } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'
import { documentStatusLabel, governanceType } from '@/views/knowledgeLabels'
import type { KnowledgeDocumentStatus, KnowledgeDocumentSummary } from '@/types/app'

const store = useRagStore()
const authStore = useAuthStore()

const selectedBase = computed(() => store.knowledgeBases.find((item) => item.id === store.selectedKnowledgeBaseId) ?? null)
const canEditDocumentGovernance = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.governance))
const canUploadDocuments = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.upload))
const canDeleteDocuments = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.delete))
const canUpdateKnowledgeBase = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.update))
const canDeleteKnowledgeBase = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.knowledgeBase.delete))
const canManageSelectedBase = computed(() => canUpdateKnowledgeBase.value || canDeleteKnowledgeBase.value)
const embeddingModelOptions = computed(() => store.embeddingModels.map((model) => ({ label: model.name, value: model.id })))

function mapTone(type: string | undefined) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger') return 'danger'
  return 'neutral'
}

function isFailedStatus(status: KnowledgeDocumentStatus) {
  return status === 'Failed' || status === 5
}

function formatDate(value?: string | null) {
  if (!value) return '-'
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

async function uploadDocument(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  await store.uploadDocument(file)
  showAiToast('success', '文档已上传')
  input.value = ''
}

async function retryDocument(row: KnowledgeDocumentSummary) {
  if (!(await confirmAiAction(`确认重新解析并索引文档 ${row.name}？`, '确认重试'))) return
  await store.retryDocument(row.id)
  showAiToast('success', '已提交重试')
}

async function confirmDelete(message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, '确认操作'))) return
  await action()
  showAiToast('success', '操作已完成')
}
</script>

<template>
  <div class="knowledge-management">
    <section class="config-panel base-panel">
      <div class="panel-header">
        <div>
          <h2>知识库列表</h2>
          <p>选择一个知识库查看文档状态。</p>
        </div>
      </div>
      <div class="base-list">
        <button
          v-for="base in store.knowledgeBases"
          :key="base.id"
          type="button"
          class="base-item"
          :class="{ active: store.selectedKnowledgeBaseId === base.id }"
          @click="store.selectKnowledgeBase(base.id)"
        >
          <strong>{{ base.name }}</strong>
          <span>{{ base.description || '无说明' }}</span>
          <em>{{ base.documentCount }} 个文档</em>
        </button>
        <div v-if="store.knowledgeBases.length === 0" class="empty-box">暂无知识库</div>
      </div>
    </section>

    <section class="config-panel document-panel">
      <div class="panel-header">
        <div>
          <h2>{{ selectedBase?.name || '未选择知识库' }}</h2>
          <p>{{ selectedBase?.description || '选择知识库后查看文档。' }}</p>
        </div>
        <AiActionGroup v-if="selectedBase && canManageSelectedBase">
          <AiButton v-if="canUpdateKnowledgeBase" @click="store.openEditKnowledgeBaseDialog(selectedBase.id)">编辑</AiButton>
          <AiButton v-if="canDeleteKnowledgeBase" variant="danger" @click="confirmDelete(`确认删除知识库 ${selectedBase.name}？`, () => store.deleteKnowledgeBase(selectedBase!.id))">
            删除
          </AiButton>
        </AiActionGroup>
      </div>

      <label class="upload-box" :class="{ disabled: !store.selectedKnowledgeBaseId || store.loadingStates.document || !canUploadDocuments }">
        <input type="file" :disabled="!store.selectedKnowledgeBaseId || store.loadingStates.document || !canUploadDocuments" @change="uploadDocument" />
        <UploadCloud class="h-8 w-8" />
        <strong>拖拽或点击上传知识文档</strong>
        <span>上传后自动解析，完成后可在聊天中检索引用。</span>
      </label>
      <div v-if="store.actionErrors.document" class="error-note">{{ store.actionErrors.document }}</div>

      <AiTableCard :empty="store.documents.length === 0" empty-text="当前知识库暂无文档">
        <table class="ai-table">
          <thead>
            <tr>
              <th>文档</th>
              <th>状态</th>
              <th>更新时间</th>
              <th class="right">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in store.documents" :key="row.id">
              <td>
                <div class="document-cell">
                  <strong>{{ row.name }}</strong>
                  <span>{{ row.extension || '未知类型' }}</span>
                  <details class="document-detail-fold">
                    <summary>文档详情</summary>
                    <div class="document-detail-grid">
                      <span>片段数</span>
                      <strong>{{ row.chunkCount }}</strong>
                    </div>
                    <AiButton v-if="canEditDocumentGovernance" size="sm" @click="store.openEditDocumentGovernanceDialog(row)">
                      <SlidersHorizontal class="h-3.5 w-3.5" />
                      高级治理
                    </AiButton>
                  </details>
                </div>
              </td>
              <td>
                <div class="status-cell">
                  <AiTag :tone="mapTone(governanceType(row))">{{ documentStatusLabel(row.status) }}</AiTag>
                  <small v-if="isFailedStatus(row.status) && row.errorMessage">{{ row.errorMessage }}</small>
                </div>
              </td>
              <td>{{ formatDate(row.processedAt || row.createdAt) }}</td>
              <td>
                <AiActionGroup>
                  <AiButton v-if="isFailedStatus(row.status) && canUploadDocuments" size="sm" @click="retryDocument(row)">
                    <RefreshCcw class="h-3.5 w-3.5" />
                    重试
                  </AiButton>
                  <AiButton v-if="canDeleteDocuments" size="sm" variant="danger" @click="confirmDelete(`确认删除文档 ${row.name}？`, () => store.deleteDocument(row.id))">
                    <Trash2 class="h-3.5 w-3.5" />
                    删除
                  </AiButton>
                </AiActionGroup>
              </td>
            </tr>
          </tbody>
        </table>
      </AiTableCard>
    </section>
  </div>

  <AiDrawer v-model="store.dialogStates.knowledgeBase" title="知识库" width="540px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentKnowledgeBase.name" /></label>
      <label><span>说明</span><AiInput v-model="store.currentKnowledgeBase.description" /></label>
      <details class="knowledge-base-advanced">
        <summary>
          <SlidersHorizontal class="h-3.5 w-3.5" />
          高级设置
        </summary>
        <label><span>嵌入模型</span><AiSelect v-model="store.currentKnowledgeBase.embeddingModelId" :options="embeddingModelOptions" /></label>
      </details>
      <footer>
        <AiButton @click="store.closeKnowledgeBaseDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.knowledgeBase" @click="store.saveKnowledgeBase()">
          {{ store.submittingStates.knowledgeBase ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-knowledge.css';

.knowledge-management {
  display: grid;
  grid-template-columns: 330px minmax(0, 1fr);
  gap: 14px;
}

.base-panel,
.document-panel {
  border: 1px solid var(--ai-border);
  border-radius: 24px;
  background: var(--ai-surface);
  padding: 16px;
  box-shadow: var(--ai-shadow-card);
}

.base-list,
.document-panel {
  display: grid;
  gap: 10px;
}

.base-item {
  display: grid;
  gap: 5px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  padding: 13px;
  background: var(--ai-surface);
  cursor: pointer;
  text-align: left;
  transition:
    box-shadow 0.2s ease,
    border-color 0.2s ease,
    background-color 0.2s ease;
}

.base-item.active,
.base-item:hover {
  border-color: #d8ff78;
  background: #efffbe;
  box-shadow: var(--ai-shadow-xs);
}

.base-item strong {
  color: var(--ai-text);
}

.base-item span,
.base-item em,
.empty-box,
.governance-cell span,
.governance-cell small {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-style: normal;
  font-weight: 750;
}

.empty-box {
  border: 1px dashed var(--ai-border);
  border-radius: 18px;
  padding: 16px;
  text-align: center;
}

.upload-box {
  display: grid;
  cursor: pointer;
  place-items: center;
  gap: 8px;
  border: 1px dashed var(--ai-border-strong);
  border-radius: 22px;
  background: var(--ai-surface-soft);
  padding: 26px;
  color: var(--ai-text);
  text-align: center;
}

.upload-box input {
  display: none;
}

.upload-box span {
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 700;
}

.upload-box.disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.document-cell,
.status-cell {
  display: grid;
  gap: 4px;
  min-width: 0;
}

.document-detail-fold {
  min-width: 0;
}

.document-detail-fold summary,
.knowledge-base-advanced summary {
  display: inline-flex;
  min-height: 28px;
  cursor: pointer;
  align-items: center;
  gap: 6px;
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.knowledge-base-advanced {
  border: 1px solid var(--ai-border);
  border-radius: 14px;
  padding: 8px 10px;
  background: var(--ai-surface-soft);
}

.knowledge-base-advanced label {
  margin-top: 10px;
}

.document-detail-grid {
  display: grid;
  grid-template-columns: minmax(72px, max-content) minmax(0, 1fr);
  gap: 8px;
  max-width: 240px;
  margin: 2px 0 8px;
  border: 1px solid var(--ai-border);
  border-radius: 12px;
  padding: 8px 10px;
  background: var(--ai-surface-soft);
}

.document-detail-grid span {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.document-detail-grid strong {
  color: var(--ai-text);
  font-size: 12px;
  font-weight: 900;
}

.document-cell strong {
  min-width: 0;
  overflow: hidden;
  color: var(--ai-text);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.document-cell span,
.status-cell small {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 750;
}

@media (max-width: 1080px) {
  .knowledge-management {
    grid-template-columns: 1fr;
  }
}
</style>
