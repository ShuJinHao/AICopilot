<script setup lang="ts">
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'

const store = useConfigStore()

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
      <el-table-column prop="baseUrl" label="接口地址" min-width="220" show-overflow-tooltip />
      <el-table-column prop="maxTokens" label="最大令牌数" width="120" />
      <el-table-column label="密钥" width="110">
        <template #default="{ row }">
          <el-tag :type="row.hasApiKey ? 'success' : 'info'">{{ row.hasApiKey ? '已配置' : '未配置' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
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
      <el-form-item label="接口地址"><el-input v-model="store.currentLanguageModel.baseUrl" /></el-form-item>
      <el-form-item label="密钥">
        <el-input v-model="store.currentLanguageModel.apiKey" type="password" show-password />
      </el-form-item>
      <el-form-item label="最大令牌数">
        <el-input-number v-model="store.currentLanguageModel.maxTokens" :min="1" />
      </el-form-item>
      <el-form-item label="温度">
        <el-input-number v-model="store.currentLanguageModel.temperature" :min="0" :max="2" :step="0.1" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeLanguageModelDialog()">取消</el-button>
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
</style>
