<script setup lang="ts">
import { computed } from 'vue'
import { RefreshCw } from 'lucide-vue-next'
import AiButton from '@/components/ai/AiButton.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AiTableCard from '@/components/ai/AiTableCard.vue'
import { useConfigStore } from '@/stores/configStore'
import { fallbackScopeLabel, outputTokenBudgetLabel } from '@/views/configLabels'

const store = useConfigStore()
const providerFallbackRoutes = computed(() => store.providerReliability?.fallbackProviders ?? [])
</script>

<template>
  <section class="config-panel">
    <div class="panel-header">
      <div>
        <h2>服务商可靠性</h2>
        <p>只读查看模型服务商回退、熔断和输出令牌预算配置。</p>
      </div>
      <AiButton :disabled="store.loadingStates.providerReliability" @click="store.refreshProviderReliability()">
        <RefreshCw class="h-4 w-4" :class="{ 'animate-spin': store.loadingStates.providerReliability }" />
        刷新
      </AiButton>
    </div>

    <div class="warning-note">
      高风险链路固定不回退：MCP 工具调用、审批恢复、副作用工具、数据分析 SQL 工具链。
    </div>

    <template v-if="store.providerReliability">
      <div class="reliability-grid">
        <div class="reliability-item">
          <span>回退状态</span>
          <AiTag :tone="store.providerReliability.fallbackEnabled ? 'success' : 'neutral'">
            {{ store.providerReliability.fallbackEnabled ? '已启用' : '未启用' }}
          </AiTag>
        </div>
        <div class="reliability-item">
          <span>失败阈值</span>
          <strong>{{ store.providerReliability.circuitBreakerFailureThreshold }}</strong>
        </div>
        <div class="reliability-item">
          <span>熔断时长</span>
          <strong>{{ store.providerReliability.circuitBreakerOpenSeconds }} 秒</strong>
        </div>
        <div class="reliability-item">
          <span>输出令牌上限</span>
          <strong>{{ outputTokenBudgetLabel(store.providerReliability.maxOutputTokens) }}</strong>
        </div>
      </div>

      <div class="policy-lists">
        <section>
          <h3>允许配置回退的场景</h3>
          <div class="scope-list">
            <AiTag v-for="scope in store.providerReliability.fallbackAllowedScopes" :key="scope" tone="success">
              {{ fallbackScopeLabel(scope) }}
            </AiTag>
          </div>
        </section>
        <section>
          <h3>固定禁止回退的场景</h3>
          <div class="scope-list">
            <AiTag v-for="scope in store.providerReliability.fallbackBlockedScopes" :key="scope" tone="danger">
              {{ fallbackScopeLabel(scope) }}
            </AiTag>
          </div>
        </section>
      </div>

      <AiTableCard title="服务商回退路由" :empty="providerFallbackRoutes.length === 0" empty-text="未配置回退服务商">
        <table class="ai-table">
          <thead>
            <tr>
              <th>来源服务商</th>
              <th>回退服务商</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in providerFallbackRoutes" :key="row.provider">
              <td>{{ row.provider }}</td>
              <td>
                <span v-if="row.fallbackProviders.length === 0" class="muted">未配置</span>
                <AiTag v-for="provider in row.fallbackProviders" v-else :key="provider" tone="neutral" class="mr-1">
                  {{ provider }}
                </AiTag>
              </td>
            </tr>
          </tbody>
        </table>
      </AiTableCard>
    </template>

    <div v-else class="empty-state">当前账号没有查看服务商可靠性配置的权限，或配置尚未加载。</div>
  </section>
</template>

<style scoped>
.config-panel {
  display: grid;
  gap: 14px;
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

h2,
h3 {
  margin: 0;
  color: var(--ai-text);
  font-weight: 950;
}

.panel-header p {
  margin: 6px 0 0;
  color: var(--ai-text-muted);
  font-weight: 650;
}

.warning-note {
  border: 1px solid #fed7aa;
  border-radius: 18px;
  background: #fff7ed;
  padding: 12px 14px;
  color: #b45309;
  font-size: 13px;
  font-weight: 800;
}

.reliability-grid,
.policy-lists {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.policy-lists {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.reliability-item,
.policy-lists section,
.empty-state {
  display: grid;
  gap: 8px;
  border: 1px solid var(--ai-border);
  border-radius: 18px;
  background: var(--ai-surface-soft);
  padding: 14px;
}

.reliability-item span,
.muted {
  color: var(--ai-text-muted);
  font-size: 12px;
  font-weight: 800;
}

.reliability-item strong {
  color: var(--ai-text);
  font-size: 22px;
  font-weight: 950;
}

.scope-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.ai-table {
  min-width: 100%;
  border-collapse: separate;
  border-spacing: 0;
}

.ai-table th,
.ai-table td {
  border-bottom: 1px solid var(--ai-border);
  padding: 13px 16px;
  text-align: left;
  font-size: 13px;
}

.ai-table th {
  background: var(--ai-surface-soft);
  color: var(--ai-text-muted);
  font-weight: 900;
}

.ai-table td {
  color: var(--ai-text);
  font-weight: 700;
}

@media (max-width: 960px) {
  .reliability-grid,
  .policy-lists {
    grid-template-columns: 1fr;
  }
}
</style>
