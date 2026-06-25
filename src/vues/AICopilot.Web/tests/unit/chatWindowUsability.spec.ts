import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const chatWindowSource = readFileSync(
  fileURLToPath(new URL('../../src/components/chat/ChatWindow.vue', import.meta.url)),
  'utf8'
)
const chatComposerSource = readFileSync(
  fileURLToPath(new URL('../../src/components/chat/ChatComposer.vue', import.meta.url)),
  'utf8'
)
const chatEmptyStateSource = readFileSync(
  fileURLToPath(new URL('../../src/components/chat/ChatEmptyState.vue', import.meta.url)),
  'utf8'
)

describe('ChatWindow usability defaults', () => {
  it('starts in plan mode', () => {
    expect(chatComposerSource).toContain("const composerMode = ref<ComposerMode>('plan')")
    expect(chatComposerSource).toContain("composerPrimaryLabel = computed(() => composerMode.value === 'plan' ? '生成计划' : '发送')")
  })

  it('does not render the inline agent run panel for an error alone', () => {
    const start = chatWindowSource.indexOf('const hasInlineAgentRun = computed(() =>')
    const end = chatWindowSource.indexOf('async function createPlanFromSuggestion', start)
    const block = chatWindowSource.slice(start, end)

    expect(block).toContain('latestTask.value')
    expect(block).not.toContain('store.errorMessage')
  })

  it('uses chat-first empty state copy', () => {
    expect(chatEmptyStateSource).toContain('直接开始对话')
    expect(chatEmptyStateSource).toContain('需要拆解步骤时，再手动切换到计划模式')
  })

  it('clears session errors and closes options when switching modes', () => {
    const start = chatComposerSource.indexOf('function setComposerMode(mode: ComposerMode)')
    const end = chatComposerSource.indexOf('function togglePluginTool', start)
    const block = chatComposerSource.slice(start, end)

    expect(block).toContain('composerOptionsOpen.value = false')
    expect(block).toContain('store.clearCurrentSessionError()')
  })

  it('keeps ChatWindow as an orchestration layer', () => {
    expect(chatWindowSource).toContain('<AgentRunThread v-if="hasInlineAgentRun" />')
    expect(chatWindowSource).toContain('<ChatComposer />')
    expect(chatWindowSource).not.toContain('function setComposerMode')
  })
})
