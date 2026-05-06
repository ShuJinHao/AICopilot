<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
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
  return raw === 'chat' || raw === 'config' || raw === 'knowledge' || raw === 'access'
    ? raw
    : undefined
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
      <div class="forbidden-panel">
        <el-result icon="warning" :title="forbiddenContent.title" :sub-title="forbiddenContent.description">
          <template #extra>
            <div class="result-extra">
              <div class="account-panel">
                <span>当前账号</span>
                <div>
                  <el-tag type="info">{{ authStore.userName || '未登录' }}</el-tag>
                  <el-tag type="info">{{ authStore.roleName || '未分配角色' }}</el-tag>
                </div>
              </div>

              <div v-if="forbiddenContent.permissions.length > 0" class="permission-panel">
                <p>
                  {{
                    forbiddenContent.mode === 'all'
                      ? '需要同时具备以下全部权限：'
                      : '至少需要具备以下权限中的一项：'
                  }}
                </p>
                <div class="permission-tags">
                  <el-tag v-for="permission in forbiddenContent.permissions" :key="permission" type="info">
                    {{ permission }}
                  </el-tag>
                </div>
              </div>

              <el-button type="primary" @click="goPrimaryPage">返回可用页面</el-button>
            </div>
          </template>
        </el-result>
      </div>
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
  width: min(100%, 760px);
  border: 1px solid var(--app-border);
  border-radius: 8px;
  background: var(--app-surface);
}

.result-extra,
.account-panel,
.permission-panel {
  display: grid;
  gap: 12px;
}

.account-panel,
.permission-panel {
  width: min(680px, 100%);
  border: 1px solid var(--app-border);
  border-radius: 8px;
  padding: 14px;
  background: var(--app-surface-muted);
  text-align: left;
}

.account-panel div,
.permission-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.permission-panel p {
  margin: 0;
  color: var(--app-text-muted);
}
</style>
