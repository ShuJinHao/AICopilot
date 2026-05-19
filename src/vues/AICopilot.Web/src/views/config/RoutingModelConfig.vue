<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'

const store = useConfigStore()
const routingCandidates = computed(() =>
  store.languageModels.filter((model) => model.isEnabled && model.usages.includes('Routing'))
)

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
        <h2 class="panel-title">路由模型</h2>
        <p class="panel-subtitle">配置意图识别使用的全局激活模型。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateRoutingModelDialog()">新增路由模型</el-button>
    </div>

    <el-alert
      v-if="store.routingModels.length > 0 && !store.routingModels.some((item) => item.isActive)"
      type="warning"
      show-icon
      :closable="false"
      title="当前没有激活路由模型，系统会回退到 IntentRoutingAgent 模板绑定的模型。"
    />

    <el-table :data="store.routingModels" stripe>
      <el-table-column prop="name" label="名称" min-width="180" />
      <el-table-column prop="modelName" label="模型" min-width="180" />
      <el-table-column prop="modelProvider" label="服务商" width="140" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="row.isActive ? 'success' : 'info'">{{ row.isActive ? '激活' : '未激活' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="220" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button v-if="!row.isActive" link type="success" @click="store.activateRoutingModel(row.id)">激活</el-button>
            <el-button link type="primary" @click="store.openEditRoutingModelDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除路由模型', `确认删除 ${row.name}？`, () => store.deleteRoutingModel(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.routingModel" size="520px" title="路由模型">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentRoutingModel.name" /></el-form-item>
      <el-form-item label="语言模型">
        <el-select v-model="store.currentRoutingModel.modelId" filterable>
          <el-option
            v-for="model in routingCandidates"
            :key="model.id"
            :label="`${model.provider} / ${model.name}`"
            :value="model.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="保存后激活">
        <el-switch v-model="store.currentRoutingModel.isActive" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeRoutingModelDialog()">取消</el-button>
      <el-button
        type="primary"
        :disabled="!store.currentRoutingModel.modelId"
        :loading="store.submittingStates.routingModel"
        @click="store.saveRoutingModel()"
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
