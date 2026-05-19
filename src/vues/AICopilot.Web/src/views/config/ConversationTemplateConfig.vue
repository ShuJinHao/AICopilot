<script setup lang="ts">
import { computed } from 'vue'
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useConfigStore } from '@/stores/configStore'

const store = useConfigStore()
const chatModelOptions = computed(() =>
  store.languageModels.filter((model) => model.isEnabled && model.usages.includes('Chat')).map((model) => ({ label: model.name, value: model.id }))
)

async function confirmAction(title: string, message: string, action: () => Promise<void>) {
  if (!(await confirmAiAction(message, title))) return
  await action()
  showAiToast('success', '操作已完成')
}
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>会话模板</h2>
        <p>系统提示词和模型参数入口。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateConversationTemplateDialog()">
        <Plus class="h-4 w-4" />
        新增模板
      </AiButton>
    </div>
    <AiTableCard :empty="store.conversationTemplates.length === 0" empty-text="暂无会话模板">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>说明</th>
            <th>状态</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.conversationTemplates" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ row.description }}</td>
            <td><AiTag :tone="row.isEnabled ? 'success' : 'neutral'">{{ row.isEnabled ? '启用' : '停用' }}</AiTag></td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" @click="store.openEditConversationTemplateDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除模板', `确认删除 ${row.name}？`, () => store.deleteConversationTemplate(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.conversationTemplate" title="会话模板" width="660px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentConversationTemplate.name" /></label>
      <label><span>说明</span><AiInput v-model="store.currentConversationTemplate.description" /></label>
      <label><span>系统提示词</span><AiTextarea v-model="store.currentConversationTemplate.systemPrompt" :rows="10" /></label>
      <label><span>模型</span><AiSelect v-model="store.currentConversationTemplate.modelId" :options="chatModelOptions" placeholder="选择模型" /></label>
      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentConversationTemplate.isEnabled" /></div>
      <footer>
        <AiButton @click="store.closeConversationTemplateDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.conversationTemplate" @click="store.saveConversationTemplate()">
          {{ store.submittingStates.conversationTemplate ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';
</style>
