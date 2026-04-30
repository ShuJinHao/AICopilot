import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/authStore'

type ProtectedAbility = 'chat' | 'config' | 'access'

function resolveAuthorizedPath(authStore: ReturnType<typeof useAuthStore>) {
  if (authStore.canUseChat) {
    return '/chat'
  }

  if (authStore.canViewConfig) {
    return '/config'
  }

  if (authStore.canManageAccess) {
    return '/access'
  }

  return '/forbidden'
}

function hasRouteAbility(
  authStore: ReturnType<typeof useAuthStore>,
  ability: ProtectedAbility | undefined
) {
  switch (ability) {
    case 'chat':
      return authStore.canUseChat
    case 'config':
      return authStore.canViewConfig
    case 'access':
      return authStore.canManageAccess
    default:
      return true
  }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/chat'
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue')
    },
    {
      path: '/chat',
      name: 'chat',
      component: () => import('@/views/ChatView.vue'),
      meta: { requiresAuth: true, ability: 'chat' as ProtectedAbility }
    },
    {
      path: '/config',
      name: 'config',
      component: () => import('@/views/ConfigView.vue'),
      meta: { requiresAuth: true, ability: 'config' as ProtectedAbility }
    },
    {
      path: '/access',
      name: 'access',
      component: () => import('@/views/AccessView.vue'),
      meta: { requiresAuth: true, ability: 'access' as ProtectedAbility }
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: () => import('@/views/ForbiddenView.vue'),
      meta: { requiresAuth: true }
    }
  ]
})

router.beforeEach(async (to) => {
  const authStore = useAuthStore()

  try {
    await authStore.ensureInitialized()
  } catch {
    return to.path === '/login' ? true : '/login'
  }

  if (!authStore.isInitialized) {
    return to.path === '/login' ? true : '/login'
  }

  if (authStore.isAuthenticated) {
    try {
      await authStore.ensureCurrentUser()
    } catch {
      return '/login'
    }
  }

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    return '/login'
  }

  if (to.path === '/login' && authStore.isAuthenticated) {
    return resolveAuthorizedPath(authStore)
  }

  if (to.path === '/') {
    return authStore.isAuthenticated ? resolveAuthorizedPath(authStore) : '/login'
  }

  if (to.meta.requiresAuth) {
    const ability = to.meta.ability as ProtectedAbility | undefined
    if (!hasRouteAbility(authStore, ability)) {
      return {
        path: '/forbidden',
        query: ability ? { ability } : undefined
      }
    }
  }

  return true
})

export default router
