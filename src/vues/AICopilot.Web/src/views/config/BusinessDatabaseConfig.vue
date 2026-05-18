<script setup lang="ts">
import { Plus } from 'lucide-vue-next'
import AiActionGroup from '@/components/ai/AiActionGroup.vue'
import AiButton from '@/components/ai/AiButton.vue'
import AiDrawer from '@/components/ai/AiDrawer.vue'
import AiInput from '@/components/ai/AiInput.vue'
import AiSelect from '@/components/ai/AiSelect.vue'
import AiSwitch from '@/components/ai/AiSwitch.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import AiTextarea from '@/components/ai/AiTextarea.vue'
import { confirmAiAction, showAiToast } from '@/composables/useAiFeedback'
import { useConfigStore } from '@/stores/configStore'
import { DATA_SOURCE_EXTERNAL_SYSTEM, databaseState, externalSystemLabel, providerLabel } from '@/views/configLabels'

const store = useConfigStore()

const providerOptions = [
  { value: 1, label: 'PostgreSQL' },
  { value: 2, label: 'SQL Server' },
  { value: 3, label: 'MySQL' }
]

const externalSystemOptions = [
  { value: DATA_SOURCE_EXTERNAL_SYSTEM.Unknown, label: '未知' },
  { value: DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly, label: 'Cloud 只读' },
  { value: DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud, label: '非云系统' }
]

function stateTone(row: unknown) {
  const state = databaseState(row as never)
  if (state.type === 'success') return 'success'
  if (state.type === 'warning') return 'warning'
  if (state.type === 'danger') return 'danger'
  return 'neutral'
}

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
        <h2>只读业务数据源</h2>
        <p>数据分析只能连接已验证的只读数据源。</p>
      </div>
      <AiButton variant="primary" @click="store.openCreateBusinessDatabaseDialog()">
        <Plus class="h-4 w-4" />
        新增数据源
      </AiButton>
    </div>
    <div class="warning-note">
      配置管理台保存时始终强制只读；运行期仍会执行 SQL 安全拒绝、结果截断和连接只读校验。
    </div>
    <AiTableCard :empty="store.businessDatabases.length === 0" empty-text="暂无业务数据源">
      <table class="ai-table">
        <thead>
          <tr>
            <th>名称</th>
            <th>服务商</th>
            <th>外部系统</th>
            <th>状态</th>
            <th>说明</th>
            <th class="right">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.businessDatabases" :key="row.id">
            <td>{{ row.name }}</td>
            <td>{{ providerLabel(row.provider) }}</td>
            <td>{{ externalSystemLabel(row.externalSystemType) }}</td>
            <td><AiTag :tone="stateTone(row)">{{ databaseState(row).label }}</AiTag></td>
            <td>{{ row.description }}</td>
            <td>
              <AiActionGroup>
                <AiButton size="sm" @click="store.openEditBusinessDatabaseDialog(row.id)">编辑</AiButton>
                <AiButton size="sm" variant="danger" @click="confirmAction('删除数据源', `确认删除 ${row.name}？`, () => store.deleteBusinessDatabase(row.id))">
                  删除
                </AiButton>
              </AiActionGroup>
            </td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <section class="config-panel semantic-panel">
    <div class="panel-header">
      <div>
        <h2>语义映射健康度</h2>
        <p>确认语义视图是否存在、字段是否齐备。</p>
      </div>
    </div>
    <AiTableCard :empty="store.semanticSourceStatuses.length === 0" empty-text="暂无语义映射状态">
      <table class="ai-table">
        <thead>
          <tr>
            <th>目标</th>
            <th>业务库</th>
            <th>视图/来源</th>
            <th>状态</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in store.semanticSourceStatuses" :key="`${row.target}-${row.databaseName}`">
            <td>{{ row.target }}</td>
            <td>{{ row.databaseName }}</td>
            <td>{{ row.effectiveSourceName }}</td>
            <td>{{ row.status }}</td>
          </tr>
        </tbody>
      </table>
    </AiTableCard>
  </section>

  <AiDrawer v-model="store.dialogStates.businessDatabase" title="只读业务数据源" width="580px">
    <div class="ai-form">
      <label><span>名称</span><AiInput v-model="store.currentBusinessDatabase.name" /></label>
      <label><span>说明</span><AiInput v-model="store.currentBusinessDatabase.description" /></label>
      <label><span>连接串</span><AiTextarea v-model="store.currentBusinessDatabase.connectionString" :rows="4" /></label>
      <label><span>服务商</span><AiSelect v-model="store.currentBusinessDatabase.provider" :options="providerOptions" /></label>
      <label><span>外部系统</span><AiSelect v-model="store.currentBusinessDatabase.externalSystemType" :options="externalSystemOptions" /></label>
      <div class="form-row"><span>只读凭据已验证</span><AiSwitch v-model="store.currentBusinessDatabase.readOnlyCredentialVerified" /></div>
      <div class="form-row"><span>启用</span><AiSwitch v-model="store.currentBusinessDatabase.isEnabled" /></div>
      <footer>
        <AiButton @click="store.closeBusinessDatabaseDialog()">取消</AiButton>
        <AiButton variant="primary" :disabled="store.submittingStates.businessDatabase" @click="store.saveBusinessDatabase()">
          {{ store.submittingStates.businessDatabase ? '保存中' : '保存' }}
        </AiButton>
      </footer>
    </div>
  </AiDrawer>
</template>

<style scoped>
@import './shared-config.css';

.semantic-panel {
  margin-top: 4px;
}
</style>
