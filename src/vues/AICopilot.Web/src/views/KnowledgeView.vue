<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox, type UploadRequestOptions } from 'element-plus'
import { Plus, Refresh, Search, UploadFilled } from '@element-plus/icons-vue'
import AppShell from '@/components/layout/AppShell.vue'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()

const selectedBase = computed(() =>
  store.knowledgeBases.find((item) => item.id === store.selectedKnowledgeBaseId) ?? null
)

onMounted(() => {
  void store.refresh()
})

function statusType(status: string | number) {
  const value = String(status)
  if (value === 'Indexed') return 'success'
  if (value === 'Failed') return 'danger'
  if (value === 'Pending') return 'info'
  return 'warning'
}

async function uploadDocument(options: UploadRequestOptions) {
  if (options.file instanceof File) {
    await store.uploadDocument(options.file)
    ElMessage.success('文档已上传')
  }
}

async function confirmDelete(title: string, action: () => Promise<void>) {
  await ElMessageBox.confirm(title, '确认操作', { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' })
  await action()
  ElMessage.success('操作已完成')
}
</script>

<template>
  <AppShell>
    <div class="page knowledge-page">
      <header class="page-header">
        <div>
          <p class="page-kicker">Knowledge Governance</p>
          <h1 class="page-title">知识库</h1>
          <p class="page-description">管理向量模型、知识库、文档解析状态和检索预览。</p>
        </div>
        <div class="toolbar">
          <el-button :icon="Refresh" :loading="store.isLoading" @click="store.refresh()">刷新</el-button>
          <el-button type="primary" :icon="Plus" @click="store.openCreateKnowledgeBaseDialog()">新增知识库</el-button>
        </div>
      </header>

      <div class="metric-strip">
        <div class="metric">
          <span class="metric-label">Embedding 模型</span>
          <strong class="metric-value">{{ store.embeddingModels.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">知识库</span>
          <strong class="metric-value">{{ store.knowledgeBases.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">当前文档</span>
          <strong class="metric-value">{{ store.documents.length }}</strong>
        </div>
        <div class="metric">
          <span class="metric-label">检索结果</span>
          <strong class="metric-value">{{ store.searchResults.length }}</strong>
        </div>
      </div>

      <el-alert v-if="store.errorMessage" type="error" show-icon :closable="false" :title="store.errorMessage" />

      <div class="knowledge-grid">
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
              <el-button
                v-if="selectedBase"
                @click="store.openEditKnowledgeBaseDialog(selectedBase.id)"
              >
                编辑
              </el-button>
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
                  <el-tag :type="statusType(row.status)">{{ row.status }}</el-tag>
                </template>
              </el-table-column>
              <el-table-column prop="chunkCount" label="Chunks" width="90" />
              <el-table-column label="操作" width="90">
                <template #default="{ row }">
                  <el-button link type="danger" @click="confirmDelete(`确认删除文档 ${row.name}？`, () => store.deleteDocument(row.id))">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </section>
      </div>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">检索预览</h2>
            <p class="panel-subtitle">验证知识库召回片段和分数，不接真实模型。</p>
          </div>
        </div>
        <div class="panel-body search-grid">
          <el-input v-model="store.searchQuery" placeholder="输入检索问题" clearable />
          <el-input-number v-model="store.searchTopK" :min="1" :max="20" />
          <el-input-number v-model="store.searchMinScore" :min="0" :max="1" :step="0.05" />
          <el-button type="primary" :icon="Search" :loading="store.loadingStates.search" @click="store.searchKnowledgeBase()">检索</el-button>
        </div>
        <div class="search-results">
          <article v-for="result in store.searchResults" :key="`${result.documentId}-${result.score}`">
            <header>
              <strong>{{ result.documentName || `Document #${result.documentId}` }}</strong>
              <el-tag type="info">{{ result.score.toFixed(3) }}</el-tag>
            </header>
            <p>{{ result.text }}</p>
          </article>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div>
            <h2 class="panel-title">Embedding 模型</h2>
            <p class="panel-subtitle">知识库向量化使用的模型配置。</p>
          </div>
          <el-button type="primary" :icon="Plus" @click="store.openCreateEmbeddingModelDialog()">新增模型</el-button>
        </div>
        <el-table :data="store.embeddingModels" stripe>
          <el-table-column prop="name" label="名称" min-width="160" />
          <el-table-column prop="provider" label="Provider" width="120" />
          <el-table-column prop="modelName" label="模型" min-width="180" />
          <el-table-column prop="dimensions" label="维度" width="90" />
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="150" fixed="right">
            <template #default="{ row }">
              <div class="table-actions">
                <el-button link type="primary" @click="store.openEditEmbeddingModelDialog(row.id)">编辑</el-button>
                <el-button link type="danger" @click="confirmDelete(`确认删除模型 ${row.name}？`, () => store.deleteEmbeddingModel(row.id))">删除</el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </section>

      <el-drawer v-model="store.dialogStates.knowledgeBase" size="520px" title="知识库">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentKnowledgeBase.name" /></el-form-item>
          <el-form-item label="说明"><el-input v-model="store.currentKnowledgeBase.description" /></el-form-item>
          <el-form-item label="Embedding 模型">
            <el-select v-model="store.currentKnowledgeBase.embeddingModelId" filterable>
              <el-option v-for="model in store.embeddingModels" :key="model.id" :label="model.name" :value="model.id" />
            </el-select>
          </el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeKnowledgeBaseDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.knowledgeBase" @click="store.saveKnowledgeBase()">保存</el-button>
        </template>
      </el-drawer>

      <el-drawer v-model="store.dialogStates.embeddingModel" size="560px" title="Embedding 模型">
        <el-form label-position="top">
          <el-form-item label="名称"><el-input v-model="store.currentEmbeddingModel.name" /></el-form-item>
          <el-form-item label="Provider"><el-input v-model="store.currentEmbeddingModel.provider" /></el-form-item>
          <el-form-item label="Base URL"><el-input v-model="store.currentEmbeddingModel.baseUrl" /></el-form-item>
          <el-form-item label="Model Name"><el-input v-model="store.currentEmbeddingModel.modelName" /></el-form-item>
          <el-form-item label="API Key"><el-input v-model="store.currentEmbeddingModel.apiKey" type="password" show-password /></el-form-item>
          <el-form-item label="维度"><el-input-number v-model="store.currentEmbeddingModel.dimensions" :min="1" /></el-form-item>
          <el-form-item label="Max Tokens"><el-input-number v-model="store.currentEmbeddingModel.maxTokens" :min="1" /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="store.currentEmbeddingModel.isEnabled" /></el-form-item>
        </el-form>
        <template #footer>
          <el-button @click="store.closeEmbeddingModelDialog()">取消</el-button>
          <el-button type="primary" :loading="store.submittingStates.embeddingModel" @click="store.saveEmbeddingModel()">保存</el-button>
        </template>
      </el-drawer>
    </div>
  </AppShell>
</template>

<style scoped>
.knowledge-page {
  display: grid;
  align-content: start;
  gap: 14px;
  height: 100%;
  overflow: auto;
}

.knowledge-grid {
  display: grid;
  grid-template-columns: 330px minmax(0, 1fr);
  gap: 14px;
}

.base-list,
.document-zone,
.search-results {
  display: grid;
  gap: 10px;
}

.base-list {
  padding: 12px;
}

.base-item {
  display: grid;
  gap: 4px;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 12px;
  background: #ffffff;
  cursor: pointer;
  text-align: left;
}

.base-item.active,
.base-item:hover {
  border-color: var(--app-primary);
}

.base-item span,
.base-item em {
  color: var(--app-text-muted);
  font-size: 12px;
  font-style: normal;
}

.empty-box {
  border: 1px dashed var(--app-border);
  border-radius: 8px;
  padding: 16px;
  color: var(--app-text-muted);
  text-align: center;
}

.search-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 140px 140px 100px;
  gap: 10px;
}

.search-results {
  padding: 0 16px 16px;
}

.search-results article {
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 12px;
  background: var(--app-surface-muted);
}

.search-results header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.search-results p {
  margin: 8px 0 0;
  color: var(--app-text-muted);
}

@media (max-width: 1080px) {
  .knowledge-grid,
  .search-grid {
    grid-template-columns: 1fr;
  }
}
</style>
