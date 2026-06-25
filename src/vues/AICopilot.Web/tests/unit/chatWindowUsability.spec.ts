import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const chatWindowSource = readFileSync(
  fileURLToPath(new URL('../../src/components/chat/ChatWindow.vue', import.meta.url)),
  'utf8'
)

describe('ChatWindow usability defaults', () => {
  it('starts in chat mode', () => {
    expect(chatWindowSource).toContain("const composerMode = ref<ComposerMode>('chat')")
    expect(chatWindowSource).toContain("composerPrimaryLabel = computed(() => composerMode.value === 'plan' ? '生成计划' : '发送')")
  })

  it('does not render the inline agent run panel for an error alone', () => {
    const start = chatWindowSource.indexOf('const hasInlineAgentRun = computed(() =>')
    const end = chatWindowSource.indexOf('const timelineEventItems = computed(() =>', start)
    const block = chatWindowSource.slice(start, end)

    expect(block).toContain('latestTask.value')
    expect(block).not.toContain('store.errorMessage')
  })

  it('uses chat-first empty state copy', () => {
    expect(chatWindowSource).toContain('直接开始对话')
    expect(chatWindowSource).toContain('需要拆解步骤时，再手动切换到计划模式')
  })

  it('clears session errors and closes options when switching modes', () => {
    const start = chatWindowSource.indexOf('function setComposerMode(mode: ComposerMode)')
    const end = chatWindowSource.indexOf('function togglePluginTool', start)
    const block = chatWindowSource.slice(start, end)

    expect(block).toContain('composerOptionsOpen.value = false')
    expect(block).toContain('store.clearCurrentSessionError()')
  })
})
