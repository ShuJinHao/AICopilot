import { createI18n } from 'vue-i18n'

export const i18n = createI18n({
  legacy: false,
  locale: localStorage.getItem('aicopilot.locale') || 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: {
    'zh-CN': {
      brand: {
        name: 'AICopilot',
        subtitle: '制造 AI 运维工作台'
      },
      nav: {
        chat: 'AI 工作台',
        config: '运行配置',
        knowledge: '知识库',
        access: '权限治理',
        cloud: '返回云平台',
        theme: '切换主题',
        logout: '退出'
      }
    },
    'en-US': {
      brand: {
        name: 'AICopilot',
        subtitle: 'Manufacturing AI Operations Workbench'
      },
      nav: {
        chat: 'AI Workbench',
        config: 'Runtime Config',
        knowledge: 'Knowledge Base',
        access: 'Access Governance',
        cloud: 'Back to Cloud',
        theme: 'Toggle theme',
        logout: 'Sign out'
      }
    }
  }
})
