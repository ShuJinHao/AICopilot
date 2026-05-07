<script setup lang="ts">
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()

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
        <h2 class="panel-title">嵌入模型</h2>
        <p class="panel-subtitle">知识库向量化使用的模型配置。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateEmbeddingModelDialog()">新增模型</el-button>
    </div>
    <el-table :data="store.embeddingModels" stripe>
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column prop="provider" label="服务商" width="120" />
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
            <el-button
              link
              type="danger"
              @click="confirmDelete(`确认删除模型 ${row.name}？`, () => store.deleteEmbeddingModel(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.embeddingModel" size="560px" title="嵌入模型">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentEmbeddingModel.name" /></el-form-item>
      <el-form-item label="服务商"><el-input v-model="store.currentEmbeddingModel.provider" /></el-form-item>
      <el-form-item label="接口地址"><el-input v-model="store.currentEmbeddingModel.baseUrl" /></el-form-item>
      <el-form-item label="模型名称"><el-input v-model="store.currentEmbeddingModel.modelName" /></el-form-item>
      <el-form-item label="密钥">
        <el-input v-model="store.currentEmbeddingModel.apiKey" type="password" show-password />
      </el-form-item>
      <el-form-item label="维度"><el-input-number v-model="store.currentEmbeddingModel.dimensions" :min="1" /></el-form-item>
      <el-form-item label="最大令牌数">
        <el-input-number v-model="store.currentEmbeddingModel.maxTokens" :min="1" />
      </el-form-item>
      <el-form-item label="启用"><el-switch v-model="store.currentEmbeddingModel.isEnabled" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeEmbeddingModelDialog()">取消</el-button>
      <el-button type="primary" :loading="store.submittingStates.embeddingModel" @click="store.saveEmbeddingModel()">
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
