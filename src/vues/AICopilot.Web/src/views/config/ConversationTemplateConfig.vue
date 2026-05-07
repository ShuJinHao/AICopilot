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
        <h2 class="panel-title">会话模板</h2>
        <p class="panel-subtitle">系统提示词和模型参数入口。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateConversationTemplateDialog()">新增模板</el-button>
    </div>
    <el-table :data="store.conversationTemplates" stripe>
      <el-table-column prop="name" label="名称" min-width="180" />
      <el-table-column prop="description" label="说明" min-width="240" show-overflow-tooltip />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? '启用' : '停用' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button link type="primary" @click="store.openEditConversationTemplateDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除模板', `确认删除 ${row.name}？`, () => store.deleteConversationTemplate(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.conversationTemplate" size="640px" title="会话模板">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentConversationTemplate.name" /></el-form-item>
      <el-form-item label="说明"><el-input v-model="store.currentConversationTemplate.description" /></el-form-item>
      <el-form-item label="系统提示词">
        <el-input v-model="store.currentConversationTemplate.systemPrompt" type="textarea" :rows="10" />
      </el-form-item>
      <el-form-item label="模型">
        <el-select v-model="store.currentConversationTemplate.modelId" filterable>
          <el-option v-for="model in store.languageModels" :key="model.id" :label="model.name" :value="model.id" />
        </el-select>
      </el-form-item>
      <el-form-item label="启用"><el-switch v-model="store.currentConversationTemplate.isEnabled" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeConversationTemplateDialog()">取消</el-button>
      <el-button
        type="primary"
        :loading="store.submittingStates.conversationTemplate"
        @click="store.saveConversationTemplate()"
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
