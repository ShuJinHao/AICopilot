<script setup lang="ts">
import { computed } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { useConfigStore } from '@/stores/configStore'
import { fallbackScopeLabel, outputTokenBudgetLabel } from '@/views/configLabels'

const store = useConfigStore()
const providerFallbackRoutes = computed(() => store.providerReliability?.fallbackProviders ?? [])
</script>

<template>
  <section class="panel">
    <div class="panel-header">
      <div>
        <h2 class="panel-title">服务商可靠性</h2>
        <p class="panel-subtitle">只读查看模型服务商回退、熔断和输出令牌预算配置。</p>
      </div>
      <el-button
        :icon="Refresh"
        :loading="store.loadingStates.providerReliability"
        @click="store.refreshProviderReliability()"
      >
        刷新
      </el-button>
    </div>

    <el-alert
      class="safety-alert"
      type="warning"
      :closable="false"
      show-icon
      title="高风险链路固定不回退：MCP 工具调用、审批恢复、副作用工具、数据分析 SQL 工具链。"
    />

    <template v-if="store.providerReliability">
      <div class="reliability-grid">
        <div class="reliability-item">
          <span class="metric-label">回退状态</span>
          <el-tag :type="store.providerReliability.fallbackEnabled ? 'success' : 'info'">
            {{ store.providerReliability.fallbackEnabled ? '已启用' : '未启用' }}
          </el-tag>
        </div>
        <div class="reliability-item">
          <span class="metric-label">失败阈值</span>
          <strong>{{ store.providerReliability.circuitBreakerFailureThreshold }}</strong>
        </div>
        <div class="reliability-item">
          <span class="metric-label">熔断时长</span>
          <strong>{{ store.providerReliability.circuitBreakerOpenSeconds }} 秒</strong>
        </div>
        <div class="reliability-item">
          <span class="metric-label">输出令牌上限</span>
          <strong>{{ outputTokenBudgetLabel(store.providerReliability.maxOutputTokens) }}</strong>
        </div>
      </div>

      <div class="policy-lists">
        <section>
          <h3>允许配置回退的场景</h3>
          <div class="scope-list">
            <el-tag v-for="scope in store.providerReliability.fallbackAllowedScopes" :key="scope" type="success">
              {{ fallbackScopeLabel(scope) }}
            </el-tag>
          </div>
        </section>
        <section>
          <h3>固定禁止回退的场景</h3>
          <div class="scope-list">
            <el-tag v-for="scope in store.providerReliability.fallbackBlockedScopes" :key="scope" type="danger">
              {{ fallbackScopeLabel(scope) }}
            </el-tag>
          </div>
        </section>
      </div>

      <el-table :data="providerFallbackRoutes" stripe>
        <el-table-column prop="provider" label="来源服务商" width="180" />
        <el-table-column label="回退服务商" min-width="260">
          <template #default="{ row }">
            <template v-if="row.fallbackProviders.length > 0">
              <el-tag v-for="provider in row.fallbackProviders" :key="provider" class="tool-tag">
                {{ provider }}
              </el-tag>
            </template>
            <span v-else>未配置</span>
          </template>
        </el-table-column>
      </el-table>
    </template>

    <el-empty v-else description="当前账号没有查看服务商可靠性配置的权限，或配置尚未加载。" />
  </section>
</template>

<style scoped>
.safety-alert {
  margin-bottom: 12px;
}

.tool-tag {
  margin-right: 4px;
}

.reliability-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  margin-bottom: 14px;
}

.reliability-item {
  display: grid;
  gap: 6px;
  align-content: start;
  min-width: 0;
  padding: 10px;
  border: 1px solid var(--app-border);
  border-radius: 8px;
  background: var(--app-surface-muted);
}

.policy-lists {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 14px;
}

.policy-lists h3 {
  margin: 0 0 8px;
  font-size: 14px;
}

.scope-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

@media (max-width: 960px) {
  .reliability-grid,
  .policy-lists {
    grid-template-columns: 1fr;
  }
}
</style>
