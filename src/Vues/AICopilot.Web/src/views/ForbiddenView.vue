<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppShell from '@/components/layout/AppShell.vue'
import {
  ACCESS_MANAGEMENT_PERMISSIONS,
  CHAT_REQUIRED_PERMISSIONS,
  collectConfigReadPermissions
} from '@/security/permissions'
import { useAuthStore } from '@/stores/authStore'

type ProtectedAbility = 'chat' | 'config' | 'access'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const ability = computed<ProtectedAbility | undefined>(() => {
  const raw = route.query.ability
  if (raw === 'chat' || raw === 'config' || raw === 'access') {
    return raw
  }

  return undefined
})

const forbiddenContent = computed(() => {
  switch (ability.value) {
    case 'chat':
      return {
        title: '没有聊天访问权限',
        description: '当前账号缺少进入聊天主链所需的完整权限。',
        mode: 'all' as const,
        permissions: [...CHAT_REQUIRED_PERMISSIONS]
      }
    case 'config':
      return {
        title: '没有配置访问权限',
        description: '当前账号缺少进入配置治理页面所需的读取权限。',
        mode: 'any' as const,
        permissions: collectConfigReadPermissions()
      }
    case 'access':
      return {
        title: '没有治理访问权限',
        description: '当前账号缺少进入权限与治理页面所需的治理权限。',
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
  if (authStore.canUseChat) {
    await router.replace('/chat')
    return
  }

  if (authStore.canViewConfig) {
    await router.replace('/config')
    return
  }

  if (authStore.canManageAccess) {
    await router.replace('/access')
  }
}
</script>

<template>
  <AppShell>
    <div class="forbidden-page">
      <el-result
        icon="warning"
        :title="forbiddenContent.title"
        :sub-title="forbiddenContent.description"
      >
        <template #extra>
          <div class="result-extra">
            <div class="account-panel">
              <span class="account-label">当前账号</span>
              <div class="account-values">
                <el-tag type="info">{{ authStore.userName || '未登录' }}</el-tag>
                <el-tag type="info">{{ authStore.roleName || '未分配角色' }}</el-tag>
              </div>
            </div>

            <div v-if="forbiddenContent.permissions.length > 0" class="permission-panel">
              <p class="permission-hint">
                {{
                  forbiddenContent.mode === 'all'
                    ? '需要同时具备以下全部权限：'
                    : '至少需要具备以下权限中的一项：'
                }}
              </p>
              <div class="permission-tags">
                <el-tag
                  v-for="permission in forbiddenContent.permissions"
                  :key="permission"
                  type="info"
                >
                  {{ permission }}
                </el-tag>
              </div>
            </div>

            <el-button type="primary" @click="goPrimaryPage">返回可用页面</el-button>
          </div>
        </template>
      </el-result>
    </div>
  </AppShell>
</template>

<style scoped>
.forbidden-page {
  min-height: calc(100vh - 140px);
  display: grid;
  place-items: center;
}

.result-extra {
  display: grid;
  gap: 18px;
  justify-items: center;
}

.account-panel,
.permission-panel {
  width: min(720px, 100%);
  display: grid;
  gap: 12px;
  padding: 16px 18px;
  border-radius: 18px;
  background: #f8fafc;
  border: 1px solid rgba(148, 163, 184, 0.2);
}

.account-label,
.permission-hint {
  color: #475569;
  font-size: 13px;
}

.account-values,
.permission-tags {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
}
</style>
