<script setup lang="ts">
import { computed } from 'vue'
import { ShieldX } from 'lucide-vue-next'
import { useRoute, useRouter } from 'vue-router'
import AiButton from '@/components/ai/AiButton.vue'
import AiTag from '@/components/ai/AiTag.vue'
import AppShell from '@/components/layout/AppShell.vue'
import {
  ACCESS_MANAGEMENT_PERMISSIONS,
  CHAT_REQUIRED_PERMISSIONS,
  collectConfigReadPermissions,
  collectKnowledgeReadPermissions
} from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'

type ProtectedAbility = 'chat' | 'config' | 'knowledge' | 'access'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const ability = computed<ProtectedAbility | undefined>(() => {
  const raw = route.query.ability
  return raw === 'chat' || raw === 'config' || raw === 'knowledge' || raw === 'access' ? raw : undefined
})

const forbiddenContent = computed(() => {
  switch (ability.value) {
    case 'chat':
      return {
        title: '没有 AI 工作台权限',
        description: '当前账号缺少进入会话、审批和工具链主流程所需的完整权限。',
        mode: 'all' as const,
        permissions: [...CHAT_REQUIRED_PERMISSIONS]
      }
    case 'config':
      return {
        title: '没有运行配置权限',
        description: '当前账号缺少查看模型、模板、MCP 或数据源配置所需的读取权限。',
        mode: 'any' as const,
        permissions: collectConfigReadPermissions()
      }
    case 'knowledge':
      return {
        title: '没有知识库权限',
        description: '当前账号缺少进入知识库页面所需的 RAG 读取权限。',
        mode: 'any' as const,
        permissions: collectKnowledgeReadPermissions()
      }
    case 'access':
      return {
        title: '没有权限治理权限',
        description: '当前账号缺少用户、角色或审计治理所需的权限。',
        mode: 'any' as const,
        permissions: [...ACCESS_MANAGEMENT_PERMISSIONS]
      }
    default:
      return {
        title: '无权访问',
        description: '当前账号没有访问该页面的权限。',
        mode: 'any' as const,
        permissions: [] as string[]
      }
  }
})

async function goPrimaryPage() {
  if (authStore.canUseChat) return router.replace('/chat')
  if (authStore.canViewConfig) return router.replace('/config')
  if (authStore.canManageKnowledge) return router.replace('/knowledge')
  if (authStore.canManageAccess) return router.replace('/access')
}
</script>

<template>
  <AppShell>
    <div class="forbidden-page">
      <section class="forbidden-panel">
        <span class="forbidden-icon"><ShieldX class="h-10 w-10" /></span>
        <h1>{{ forbiddenContent.title }}</h1>
        <p>{{ forbiddenContent.description }}</p>

        <div class="account-panel">
          <span>当前账号</span>
          <div>
            <AiTag tone="neutral">{{ authStore.userName || '未登录' }}</AiTag>
            <AiTag tone="neutral">{{ authStore.roleName || '未分配角色' }}</AiTag>
          </div>
        </div>

        <div v-if="forbiddenContent.permissions.length > 0" class="permission-panel">
          <p>{{ forbiddenContent.mode === 'all' ? '需要同时具备以下全部权限：' : '至少需要具备以下权限中的一项：' }}</p>
          <div class="permission-tags">
            <AiTag v-for="permission in forbiddenContent.permissions" :key="permission" tone="neutral">
              {{ permission }}
            </AiTag>
          </div>
        </div>

        <AiButton variant="primary" @click="goPrimaryPage">返回可用页面</AiButton>
      </section>
    </div>
  </AppShell>
</template>

<style scoped>
.forbidden-page {
  display: grid;
  height: 100%;
  place-items: center;
}

.forbidden-panel {
  display: grid;
  width: min(100%, 760px);
  justify-items: center;
  gap: 16px;
  border: 1px solid var(--ai-border);
  border-radius: 30px;
  background: var(--ai-surface);
  padding: 36px;
  text-align: center;
  box-shadow: var(--ai-shadow-shell);
}

.forbidden-icon {
  display: grid;
  height: 72px;
  width: 72px;
  place-items: center;
  border-radius: 26px;
  background: #fff7ed;
  color: #b45309;
}

.forbidden-panel h1 {
  margin: 0;
  color: var(--ai-text);
  font-size: 28px;
  font-weight: 950;
}

.forbidden-panel > p {
  margin: 0;
  color: var(--ai-text-muted);
  font-weight: 700;
}

.account-panel,
.permission-panel {
  display: grid;
  width: min(680px, 100%);
  gap: 12px;
  border: 1px solid var(--ai-border);
  border-radius: 20px;
  background: var(--ai-surface-soft);
  padding: 16px;
  text-align: left;
}

.account-panel > span,
.permission-panel p {
  margin: 0;
  color: var(--ai-text-muted);
  font-size: 13px;
  font-weight: 850;
}

.account-panel div,
.permission-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
</style>
