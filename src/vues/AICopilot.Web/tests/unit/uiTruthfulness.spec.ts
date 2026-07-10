import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const loginSource = readFileSync(
  fileURLToPath(new URL('../../src/views/LoginView.vue', import.meta.url)),
  'utf8'
)
const shellSource = readFileSync(
  fileURLToPath(new URL('../../src/components/layout/AppShell.vue', import.meta.url)),
  'utf8'
)
const emptyStateSource = readFileSync(
  fileURLToPath(new URL('../../src/components/chat/ChatEmptyState.vue', import.meta.url)),
  'utf8'
)

describe('UI truthfulness and industrial product boundaries', () => {
  it('does not present hard-coded demo metrics or online state as runtime facts on login', () => {
    expect(loginSource).not.toContain('只读数据源</span>\n            <strong>12</strong>')
    expect(loginSource).not.toContain('知识命中率')
    expect(loginSource).not.toContain('>在线<')
    expect(loginSource).not.toContain('<small>READY</small>')
    expect(loginSource).not.toContain('<small>OK</small>')
    expect(loginSource).not.toContain('<small>LOCKED</small>')
    expect(loginSource).not.toContain('class="orb')
    expect(loginSource).toContain('页面只展示后端返回的真实状态')
    expect(loginSource).toContain('未配置时返回真实错误')
  })

  it('uses a calm neutral shell and states the policy boundary instead of fake readiness', () => {
    expect(shellSource).toContain('background: var(--ai-bg)')
    expect(shellSource).not.toContain('radial-gradient')
    expect(shellSource).not.toContain('linear-gradient')
    expect(shellSource).toContain('Cloud 写入禁用')
    expect(shellSource).toContain('工业智能工作台')
  })

  it('uses suggestions that match formal Cloud Agent capabilities', () => {
    expect(emptyStateSource).toContain('最后上报运行状态和心跳时间')
    expect(emptyStateSource).toContain('列出工序主数据')
    expect(emptyStateSource).toContain('已发布客户端版本')
    expect(emptyStateSource).not.toContain('LINE-A 当前设备状态')
    expect(emptyStateSource).not.toContain('配方版本历史')
  })
})
