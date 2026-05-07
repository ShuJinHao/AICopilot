<script setup lang="ts">
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'
import { DATA_SOURCE_EXTERNAL_SYSTEM, databaseState, externalSystemLabel, providerLabel } from '@/views/configLabels'

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
        <h2 class="panel-title">只读业务数据源</h2>
        <p class="panel-subtitle">数据分析只能连接已验证的只读数据源。</p>
      </div>
      <el-button type="primary" :icon="Plus" @click="store.openCreateBusinessDatabaseDialog()">新增数据源</el-button>
    </div>
    <el-alert
      class="safety-alert"
      type="info"
      :closable="false"
      show-icon
      title="配置管理台保存时始终强制只读；运行期仍会执行 SQL 安全拒绝、结果截断和连接只读校验。"
    />
    <el-table :data="store.businessDatabases" stripe>
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column label="服务商" width="120">
        <template #default="{ row }">{{ providerLabel(row.provider) }}</template>
      </el-table-column>
      <el-table-column label="外部系统" width="140">
        <template #default="{ row }">{{ externalSystemLabel(row.externalSystemType) }}</template>
      </el-table-column>
      <el-table-column label="状态" width="130">
        <template #default="{ row }">
          <el-tag :type="databaseState(row).type">{{ databaseState(row).label }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="description" label="说明" min-width="220" show-overflow-tooltip />
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <div class="table-actions">
            <el-button link type="primary" @click="store.openEditBusinessDatabaseDialog(row.id)">编辑</el-button>
            <el-button
              link
              type="danger"
              @click="confirmAction('删除数据源', `确认删除 ${row.name}？`, () => store.deleteBusinessDatabase(row.id))"
            >
              删除
            </el-button>
          </div>
        </template>
      </el-table-column>
    </el-table>
  </section>

  <section class="panel semantic-panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">语义映射健康度</h2>
        <p class="panel-subtitle">确认语义视图是否存在、字段是否齐备。</p>
      </div>
    </div>
    <el-table :data="store.semanticSourceStatuses" stripe>
      <el-table-column prop="target" label="目标" width="150" />
      <el-table-column prop="databaseName" label="业务库" min-width="160" />
      <el-table-column prop="effectiveSourceName" label="视图/来源" min-width="180" />
      <el-table-column prop="status" label="状态" min-width="140" />
    </el-table>
  </section>

  <el-drawer v-model="store.dialogStates.businessDatabase" size="560px" title="只读业务数据源">
    <el-form label-position="top">
      <el-form-item label="名称"><el-input v-model="store.currentBusinessDatabase.name" /></el-form-item>
      <el-form-item label="说明"><el-input v-model="store.currentBusinessDatabase.description" /></el-form-item>
      <el-form-item label="连接串">
        <el-input v-model="store.currentBusinessDatabase.connectionString" type="textarea" :rows="4" />
      </el-form-item>
      <el-form-item label="服务商">
        <el-select v-model="store.currentBusinessDatabase.provider">
          <el-option :value="1" label="PostgreSQL" />
          <el-option :value="2" label="SQL Server" />
          <el-option :value="3" label="MySQL" />
        </el-select>
      </el-form-item>
      <el-form-item label="外部系统">
        <el-select v-model="store.currentBusinessDatabase.externalSystemType">
          <el-option :value="DATA_SOURCE_EXTERNAL_SYSTEM.Unknown" label="未知" />
          <el-option :value="DATA_SOURCE_EXTERNAL_SYSTEM.CloudReadOnly" label="Cloud 只读" />
          <el-option :value="DATA_SOURCE_EXTERNAL_SYSTEM.NonCloud" label="非云系统" />
        </el-select>
      </el-form-item>
      <el-form-item label="只读凭据已验证">
        <el-switch v-model="store.currentBusinessDatabase.readOnlyCredentialVerified" />
      </el-form-item>
      <el-form-item label="启用"><el-switch v-model="store.currentBusinessDatabase.isEnabled" /></el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="store.closeBusinessDatabaseDialog()">取消</el-button>
      <el-button
        type="primary"
        :loading="store.submittingStates.businessDatabase"
        @click="store.saveBusinessDatabase()"
      >
        保存
      </el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.semantic-panel {
  margin-top: 14px;
}

.safety-alert {
  margin-bottom: 12px;
}

:deep(.el-drawer__body) {
  overflow: auto;
}
</style>
