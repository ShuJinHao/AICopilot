<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage, ElMessageBox, type UploadRequestOptions } from 'element-plus'
import { Edit, UploadFilled } from '@element-plus/icons-vue'
import { KNOWLEDGE_WRITE_PERMISSIONS } from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useRagStore } from '@/stores/ragStore'
import {
  classificationLabel,
  documentStatusLabel,
  documentStatusType,
  governanceType,
  sourceTypeLabel
} from '@/views/knowledgeLabels'

const store = useRagStore()
const authStore = useAuthStore()

const selectedBase = computed(() =>
  store.knowledgeBases.find((item) => item.id === store.selectedKnowledgeBaseId) ?? null
)
const canEditDocumentGovernance = computed(() =>
  authStore.hasPermission(KNOWLEDGE_WRITE_PERMISSIONS.document.governance)
)

async function uploadDocument(options: UploadRequestOptions) {
  if (options.file instanceof File) {
    await store.uploadDocument(options.file)
    ElMessage.success('文档已上传')
  }
}

async function confirmDelete(title: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(title, '确认操作', {
    type: 'warning',
    confirmButtonText: '确认',
    cancelButtonText: '取消'
  })
  await action()
  ElMessage.success('操作已完成')
}
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">知识库列表</h2>
        <p class="panel-subtitle">选择一个知识库查看文档和检索效果。</p>
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

  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">{{ selectedBase?.name || '未选择知识库' }}</h2>
        <p class="panel-subtitle">{{ selectedBase?.description || '选择知识库后查看文档。' }}</p>
      </div>
      <div class="toolbar">
        <el-button v-if="selectedBase" @click="store.openEditKnowledgeBaseDialog(selectedBase.id)">编辑</el-button>
        <el-button
          v-if="selectedBase"
          type="danger"
          plain
          @click="confirmDelete(`确认删除知识库 ${selectedBase.name}？`, () => store.deleteKnowledgeBase(selectedBase!.id))"
        >
          删除
        </el-button>
      </div>
    </div>
    <div class="panel-body document-zone">
      <div class="governance-form">
        <el-select v-model="store.uploadGovernanceForm.classification" placeholder="文档等级">
          <el-option label="内部" value="Internal" />
          <el-option label="公开" value="Public" />
          <el-option label="敏感" value="Sensitive" />
          <el-option label="禁用" value="Forbidden" />
        </el-select>
        <el-select v-model="store.uploadGovernanceForm.sourceType" placeholder="来源类型">
          <el-option label="用户上传" value="UserUploaded" />
          <el-option label="业务规则" value="BusinessRule" />
          <el-option label="Cloud 只读文档" value="CloudReadOnlyApiDoc" />
          <el-option label="运维手册" value="Runbook" />
          <el-option label="外部资料" value="External" />
        </el-select>
        <el-switch v-model="store.uploadGovernanceForm.isSanitized" active-text="已脱敏" />
        <el-switch v-model="store.uploadGovernanceForm.allowedForFinalPrompt" active-text="允许进入回答" />
      </div>
      <el-upload
        drag
        :http-request="uploadDocument"
        :show-file-list="false"
        :disabled="!store.selectedKnowledgeBaseId || store.loadingStates.document"
      >
        <el-icon><UploadFilled /></el-icon>
        <div>拖拽或点击上传知识文档</div>
        <template #tip>
          <span class="muted">上传后由后端解析、切分并写入向量索引。</span>
        </template>
      </el-upload>

      <el-table :data="store.documents" stripe>
        <el-table-column prop="name" label="文档" min-width="180" />
        <el-table-column prop="extension" label="类型" width="80" />
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="documentStatusType(row.status)">{{ documentStatusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="治理" min-width="170">
          <template #default="{ row }">
            <div class="governance-cell">
              <el-tag :type="governanceType(row)">{{ classificationLabel(row.classification) }}</el-tag>
              <span>{{ sourceTypeLabel(row.sourceType) }}</span>
              <small v-if="!row.allowedForFinalPrompt">不进入回答</small>
              <small v-else-if="row.isSanitized">已脱敏</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="chunkCount" label="片段数" width="90" />
        <el-table-column label="操作" width="150">
          <template #default="{ row }">
            <el-button
              v-if="canEditDocumentGovernance"
              link
              type="primary"
              :icon="Edit"
              @click="store.openEditDocumentGovernanceDialog(row)"
            >
              治理
            </el-button>
            <el-button
              link
              type="danger"
              @click="confirmDelete(`确认删除文档 ${row.name}？`, () => store.deleteDocument(row.id))"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
  </section>

  <el-drawer v-model="store.dialogStates.knowledgeBase" size="520px" title="知识库">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentKnowledgeBase.name" /></el-form-item>
      <el-form-item label="说明"><el-input v-model="store.currentKnowledgeBase.description" /></el-form-item>
      <el-form-item label="嵌入模型">
        <el-select v-model="store.currentKnowledgeBase.embeddingModelId" filterable>
          <el-option v-for="model in store.embeddingModels" :key="model.id" :label="model.name" :value="model.id" />
        </el-select>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeKnowledgeBaseDialog()">取消</el-button>
      <el-button type="primary" :loading="store.submittingStates.knowledgeBase" @click="store.saveKnowledgeBase()">
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.base-list,
.document-zone {
  display: grid;
  gap: 10px;
}

.governance-form {
  display: grid;
  grid-template-columns: minmax(0, 160px) minmax(0, 180px) auto auto;
  align-items: center;
  gap: 10px;
}

.governance-cell {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  min-height: 28px;
}

.governance-cell span,
.governance-cell small {
  color: var(--app-text-muted);
}

.base-list {
  padding: 12px;
}

.base-item {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: var(--radius-md);
  padding: 12px;
  background: var(--app-surface);
  cursor: pointer;
  text-align: left;
  transition: box-shadow 0.2s ease, border-color 0.2s ease;
}

.base-item.active,
.base-item:hover {
  border-color: var(--app-primary);
  box-shadow: var(--shadow-sm);
}

.base-item span,
.base-item em {
  color: var(--app-text-muted);
  font-size: 12px;
  font-style: normal;
}

.empty-box {
  border: 1px dashed var(--app-border);
  border-radius: var(--radius-md);
  padding: 16px;
  color: var(--app-text-muted);
  text-align: center;
}

:deep(.el-drawer__body) {
  overflow: auto;
}

@media (max-width: 1080px) {
  .governance-form {
    grid-template-columns: 1fr;
  }
}
</style>
