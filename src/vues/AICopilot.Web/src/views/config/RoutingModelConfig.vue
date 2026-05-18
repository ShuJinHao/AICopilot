<script setup lang="ts">
import { computed } from 'vue'
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useConfigStore } from '@/stores/configStore'

const store = useConfigStore()
const routingCandidates = computed(() => store.languageModels.filter((model) => model.isEnabled && model.usages.includes('Routing')))
const routingCandidateOptions = computed(() => routingCandidates.value.map((model) => ({ label: `${model.provider} / ${model.name}`, value: model.id })))

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
        <h2>路由模型</h2>
        <p>配置意图识别使用的全局激活模型。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateRoutingModelDialog()">
        <Plus class="h-4 w-4" />
        新增路由模型
      </AiButton>
    </div>

    <div v-if="store.routingModels.length > 0 && !store.routingModels.some((item) => item.isActive)" class="warning-note">
      当前没有激活路由模型，系统会回退到 IntentRoutingAgent 模板绑定的模型。
    </div>

    <AiTableCard :empty="store.routingModels.length === 0" empty-text="暂无路由模型">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>模型</th>
            <th>服务商</th>
            <th>状态</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.routingModels" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ row.modelName }}</td>
            <td>{{ row.modelProvider }}</td>
            <td><AiTag :tone="row.isActive ? 'success' : 'neutral'">{{ row.isActive ? '激活' : '未激活' }}</AiTag></td>
            <td>
              <AiActionGroup>
                <AiButton v-if="!row.isActive" size="sm" variant="lime" @click="store.activateRoutingModel(row.id)">激活</AiButton>
                <AiButton size="sm" @click="store.openEditRoutingModelDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除路由模型', `确认删除 ${row.name}？`, () => store.deleteRoutingModel(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.routingModel" title="路由模型" width="520px">
    <div class="ai-form">
      <label>
        <span>名称</span>
        <AiInput v-model="store.currentRoutingModel.name" />
      </label>
      <label>
        <span>语言模型</span>
        <AiSelect v-model="store.currentRoutingModel.modelId" :options="routingCandidateOptions" placeholder="选择路由模型" />
      </label>
      <div class="form-row">
        <span>保存后激活</span>
        <AiSwitch v-model="store.currentRoutingModel.isActive" />
      </div>
      <footer>
        <AiButton @click="store.closeRoutingModelDialog()">取消</AiButton>
        <AiButton
          variant="primary"
          :disabled="!store.currentRoutingModel.modelId || store.submittingStates.routingModel"
          @click="store.saveRoutingModel()"
        >
          {{ store.submittingStates.routingModel ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';
</style>
