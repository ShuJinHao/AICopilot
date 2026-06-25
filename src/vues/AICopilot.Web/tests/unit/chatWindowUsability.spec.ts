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
  it('starts in chat mode', () => {
    expect(chatComposerSource).toContain("const composerMode = ref<ComposerMode>('chat')")
    expect(chatComposerSource).toContain("composerMode.value = 'chat'")
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

  it('clears session errors and closes advanced plan options when switching modes', () => {
    const start = chatComposerSource.indexOf('function setComposerMode(mode: ComposerMode)')
    const end = chatComposerSource.indexOf('function togglePluginTool', start)
    const block = chatComposerSource.slice(start, end)

    expect(block).toContain('planAdvancedOpen.value = false')
    expect(block).toContain('store.clearCurrentSessionError()')
  })

  it('keeps Add as an attachment action and hides skill controls behind Plan advanced options', () => {
    expect(chatComposerSource).toContain('@click="openFilePicker"')
    expect(chatComposerSource).not.toContain('composerOptionsOpen')
    expect(chatComposerSource).toContain("v-if=\"composerMode === 'plan' && planAdvancedOpen\"")
    expect(chatComposerSource).toContain('aria-label="选择计划类型"')
    expect(chatComposerSource).toContain('默认自动选择 Skill、工具和知识库')
  })

  it('keeps ChatWindow as an orchestration layer', () => {
    expect(chatWindowSource).toContain('<AgentRunThread v-if="hasInlineAgentRun" />')
    expect(chatWindowSource).toContain('<ChatComposer />')
    expect(chatWindowSource).not.toContain('function setComposerMode')
  })
})
