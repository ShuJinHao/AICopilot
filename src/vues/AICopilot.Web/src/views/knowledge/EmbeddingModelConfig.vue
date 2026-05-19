<script setup lang="ts">
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiNumberInput from '@/components/ai/AiNumberInput.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useRagStore } from '@/stores/ragStore'

const store = useRagStore()

async function confirmDelete(message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, '确认操作'))) return
  await action()
  showAiToast('success', '操作已完成')
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>嵌入模型</h2>
        <p>知识库向量化使用的模型配置。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateEmbeddingModelDialog()">
        <Plus class="h-4 w-4" />
        新增模型
      </AiButton>
    </div>
    <AiTableCard :empty="store.embeddingModels.length === 0" empty-text="暂无嵌入模型">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>服务商</th>
            <th>模型</th>
            <th>维度</th>
            <th>状态</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.embeddingModels" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ row.provider }}</td>
            <td>{{ row.modelName }}</td>
            <td>{{ row.dimensions }}</td>
            <td><AiTag :tone="row.isEnabled ? 'success' : 'neutral'">{{ row.isEnabled ? '启用' : '停用' }}</AiTag></td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" @click="store.openEditEmbeddingModelDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmDelete(`确认删除模型 ${row.name}？`, () => store.deleteEmbeddingModel(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.embeddingModel" title="嵌入模型" width="580px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentEmbeddingModel.name" /></label>
      <label><span>服务商</span><AiInput v-model="store.currentEmbeddingModel.provider" /></label>
      <label><span>接口地址</span><AiInput v-model="store.currentEmbeddingModel.baseUrl" /></label>
      <label><span>模型名称</span><AiInput v-model="store.currentEmbeddingModel.modelName" /></label>
      <label><span>密钥</span><AiInput v-model="store.currentEmbeddingModel.apiKey" type="password" autocomplete="new-password" /></label>
      <label><span>维度</span><AiNumberInput v-model="store.currentEmbeddingModel.dimensions" :min="1" /></label>
      <label><span>最大令牌数</span><AiNumberInput v-model="store.currentEmbeddingModel.maxTokens" :min="1" /></label>
      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentEmbeddingModel.isEnabled" /></div>
      <footer>
        <AiButton @click="store.closeEmbeddingModelDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.embeddingModel" @click="store.saveEmbeddingModel()">
          {{ store.submittingStates.embeddingModel ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-knowledge.css';
</style>
