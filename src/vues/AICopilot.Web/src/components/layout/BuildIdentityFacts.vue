<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  formatBuildIdentity,
  loadApiBuildIdentity,
  resolveWebBuildIdentity,
  type BuildIdentityLoadResult,
} from '@/services/buildIdentityService'

const props = withDefaults(defineProps<{
  variant?: 'shell' | 'auth'
}>(), {
  variant: 'shell',
})

const webIdentity = resolveWebBuildIdentity(
  import.meta.env.VITE_AICOPILOT_SOURCE_SHA as string | undefined,
  import.meta.env.VITE_AICOPILOT_RELEASE_TAG as string | undefined,
)
const apiResult = ref<BuildIdentityLoadResult | null>(null)

const apiLabel = computed(() => {
  if (!apiResult.value) return '查询中'
  if (apiResult.value.status === 'error') return '不可用'
  return formatBuildIdentity(apiResult.value.fact)
})

const apiTitle = computed(() => {
  if (!apiResult.value) return '正在读取 API 运行构建事实'
  if (apiResult.value.status === 'error') return 'API 构建信息请求失败'
  return identityTitle(apiResult.value.fact.releaseTag, apiResult.value.fact.sourceCommit)
})

function identityTitle(releaseTag: string | null, sourceCommit: string | null) {
  return releaseTag && sourceCommit
    ? `${releaseTag} · ${sourceCommit}`
    : '构建身份未注入'
}

onMounted(async () => {
  apiResult.value = await loadApiBuildIdentity()
})
</script>

<template>
  <div
    class="build-identity-facts"
    :class="`build-identity-facts--${props.variant}`"
    aria-label="AICopilot 构建事实"
    aria-live="polite"
  >
    <span
      class="build-identity-fact"
      data-testid="web-build-identity"
      :title="identityTitle(webIdentity.releaseTag, webIdentity.sourceCommit)"
    >
      <span>Web 构建</span>
      <code>{{ formatBuildIdentity(webIdentity) }}</code>
    </span>
    <span
      class="build-identity-fact"
      data-testid="api-build-identity"
      :title="apiTitle"
    >
      <span>API 运行构建</span>
      <code>{{ apiLabel }}</code>
    </span>
  </div>
</template>

<style scoped>
.build-identity-facts {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.build-identity-fact {
  display: inline-flex;
  min-height: 30px;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--ai-border);
  border-radius: 999px;
  padding: 4px 10px;
  background: var(--ai-surface);
  color: var(--ai-text-muted);
  font-size: 11px;
  font-weight: 800;
  box-shadow: var(--ai-shadow-xs);
}

.build-identity-fact code {
  color: var(--ai-text);
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 11px;
  font-variant-numeric: tabular-nums;
  font-weight: 800;
}

.build-identity-facts--auth {
  width: 100%;
  justify-content: center;
  border-top: 1px solid var(--ai-border);
  padding-top: 18px;
}

html.dark .build-identity-fact {
  background: rgba(29, 29, 34, 0.82);
}

@media (max-width: 520px) {
  .build-identity-facts--auth {
    align-items: stretch;
    flex-direction: column;
  }

  .build-identity-facts--auth .build-identity-fact {
    justify-content: space-between;
  }
}
</style>
