<script setup lang="ts">
import { computed } from 'vue'
import { Edit3, UploadCloud } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'
import { classificationLabel, documentStatusLabel, governanceType, sourceTypeLabel } from '@/views/knowledgeLabels'

const store = useRagStore()
const authStore = useAuthStore()

const selectedBase = computed(() => store.knowledgeBases.find((item) => item.id === store.selectedKnowledgeBaseId) ?? null)
const canEditDocumentGovernance = computed(() => authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.governance))
const embeddingModelOptions = computed(() => store.embeddingModels.map((model) => ({ label: model.name, value: model.id })))

const classificationOptions = [
  { label: '内部', value: 'Internal' },
  { label: '公开', value: 'Public' },
  { label: '敏感', value: 'Sensitive' },
  { label: '禁用', value: 'Forbidden' }
]
const sourceTypeOptions = [
  { label: '用户上传', value: 'UserUploaded' },
  { label: '业务规则', value: 'BusinessRule' },
  { label: 'Cloud 只读文档', value: 'CloudReadOnlyApiDoc' },
  { label: '运维手册', value: 'Runbook' },
  { label: '外部资料', value: 'External' }
]

function mapTone(type: string | undefined) {
  if (type === 'success') return 'success'
  if (type === 'warning') return 'warning'
  if (type === 'danger') return 'danger'
  return 'neutral'
}

async function uploadDocument(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  await store.uploadDocument(file)
  showAiToast('success', '文档已上传')
  input.value = ''
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
          <p>选择一个知识库查看文档和检索效果。</p>
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
        <AiActionGroup v-if="selectedBase">
          <AiButton @click="store.openEditKnowledgeBaseDialog(selectedBase.id)">编辑</AiButton>
          <AiButton variant="danger" @click="confirmDelete(`确认删除知识库 ${selectedBase.name}？`, () => store.deleteKnowledgeBase(selectedBase!.id))">
            删除
          </AiButton>
        </AiActionGroup>
      </div>

      <div class="governance-form">
        <AiSelect v-model="store.uploadGovernanceForm.classification" :options="classificationOptions" placeholder="文档等级" />
        <AiSelect v-model="store.uploadGovernanceForm.sourceType" :options="sourceTypeOptions" placeholder="来源类型" />
        <div class="form-row compact-row"><span>已脱敏</span><AiSwitch v-model="store.uploadGovernanceForm.isSanitized" /></div>
        <div class="form-row compact-row"><span>进入回答</span><AiSwitch v-model="store.uploadGovernanceForm.allowedForFinalPrompt" /></div>
      </div>

      <label class="upload-box" :class="{ disabled: !store.selectedKnowledgeBaseId || store.loadingStates.document }">
        <input type="file" :disabled="!store.selectedKnowledgeBaseId || store.loadingStates.document" @change="uploadDocument" />
        <UploadCloud class="h-8 w-8" />
        <strong>拖拽或点击上传知识文档</strong>
        <span>上传后由后端解析、切分并写入向量索引。</span>
      </label>

      <AiTableCard :empty="store.documents.length === 0" empty-text="当前知识库暂无文档">
        <table class="ai-table">
          <thead>
            <tr>
              <th>文档</th>
              <th>类型</th>
              <th>状态</th>
              <th>治理</th>
              <th>片段数</th>
              <th class="right">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in store.documents" :key="row.id">
              <td>{{ row.name }}</td>
              <td>{{ row.extension }}</td>
              <td><AiTag :tone="mapTone(governanceType(row))">{{ documentStatusLabel(row.status) }}</AiTag></td>
              <td>
                <div class="governance-cell">
                  <AiTag :tone="mapTone(governanceType(row))">{{ classificationLabel(row.classification) }}</AiTag>
                  <span>{{ sourceTypeLabel(row.sourceType) }}</span>
                  <small v-if="!row.allowedForFinalPrompt">不进入回答</small>
                  <small v-else-if="row.isSanitized">已脱敏</small>
                </div>
              </td>
              <td>{{ row.chunkCount }}</td>
              <td>
                <AiActionGroup>
                  <AiButton v-if="canEditDocumentGovernance" size="sm" @click="store.openEditDocumentGovernanceDialog(row)">
                    <Edit3 class="h-3.5 w-3.5" />
                    治理
                  </AiButton>
                  <AiButton size="sm" variant="danger" @click="confirmDelete(`确认删除文档 ${row.name}？`, () => store.deleteDocument(row.id))">
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
      <label><span>嵌入模型</span><AiSelect v-model="store.currentKnowledgeBase.embeddingModelId" :options="embeddingModelOptions" /></label>
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

.governance-form {
  display: grid;
  grid-template-columns: minmax(0, 160px) minmax(0, 190px) auto auto;
  align-items: center;
  gap: 10px;
}

.compact-row {
  justify-content: flex-start;
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

.governance-cell {
  display: flex;
  min-height: 28px;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

@media (max-width: 1080px) {
  .knowledge-management,
  .governance-form {
    grid-template-columns: 1fr;
  }
}
</style>
