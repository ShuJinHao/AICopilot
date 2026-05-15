import { expect, test, type Page } from '@playwright/test'

async function authenticate(page: Page) {
  await page.addInitScript(() => {
    window.sessionStorage.setItem('aicopilot.auth.token', 'smoke-token')
  })
}

async function expectNoHorizontalOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({
    width: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth
  }))

  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.width + 1)
}

async function expectProtectedShell(page: Page, path: string) {
  await authenticate(page)
  await page.goto(path)

  await expect(page).toHaveURL(new RegExp(`${path}$`))
  await expect(page.locator('main')).toBeVisible()
  await expect(page.locator('.user-zone')).toBeVisible()
  expect((await page.locator('main').innerText()).trim().length).toBeGreaterThan(0)
  await expectNoHorizontalOverflow(page)
}

test('login page renders operational shell without overflow', async ({ page }) => {
  await page.goto('/login')

  await expect(page.locator('.login-page')).toBeVisible()
  await expect(page.locator('.login-card')).toBeVisible()
  await expect(page.locator('.product-copy')).toBeVisible()
  await expect(page.locator('.login-form, .state-panel')).toBeVisible()
  await expect(page.getByText('制造 AI 运维工作台')).toBeVisible()
  await expect(page.getByText('登录控制台')).toBeVisible()
  await expect(page.getByPlaceholder('输入用户名')).toBeVisible()
  await expect(page.getByPlaceholder('输入密码')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

for (const route of [
  { path: '/chat', text: 'Chat Workspace' },
  { path: '/config', text: '运行配置' },
  { path: '/knowledge', text: '知识库' },
  { path: '/access', text: '权限治理' }
]) {
  test(`${route.path} protected route renders with authenticated user`, async ({ page }) => {
    await expectProtectedShell(page, route.path)
    await expect(page.getByText(route.text).first()).toBeVisible()
  })
}

test('agent workbench restores task, workspace, approvals, artifacts, and audit summary', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the three-column panel visible')

  await expectProtectedShell(page, '/chat')

  await expect(page.getByText('Chat Workspace')).toBeVisible()
  await expect(page.getByText(/LINE-A/).first()).toBeVisible()
  await expect(page.locator('.context-panel').getByText('Agent 工作台').first()).toBeVisible()
  await expect(page.getByText('审批队列')).toBeVisible()
  await expect(page.getByText('任务步骤')).toBeVisible()
  await expect(page.getByText('产物与预览')).toBeVisible()
  await expect(page.getByText('审计摘要')).toBeVisible()
  await expect(page.getByText('运行边界')).toBeVisible()
  await expect(page.getByText('WS-SMOKE-001').first()).toBeVisible()
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('chart-data.json')).toBeVisible()
  await expect(page.getByText('Agent.Plan')).toBeVisible()
  await expect(page.locator('.approval-row')).toHaveCount(1)
  await expect(page.locator('.artifact-row')).toHaveCount(2)
  await expectNoHorizontalOverflow(page)
})

test('chat stream renders widgets and approval card', async ({ page }) => {
  await expectProtectedShell(page, '/chat')

  await page.locator('.composer textarea').fill('smoke check agent widgets')
  await page.locator('.composer button').click()

  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeVisible()
  await expect(page.locator('.widget-frame').first()).toBeVisible()
  expect(await page.locator('.widget-frame').count()).toBeGreaterThanOrEqual(4)
  await expect(page.locator('.approval-card')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('mobile chat workspace keeps navigation and primary work area within viewport', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'mobile viewport only')

  await expectProtectedShell(page, '/chat')

  await expect(page.getByText('Chat Workspace')).toBeVisible()
  await expect(page.locator('.chat-main')).toBeVisible()
  await expect(page.locator('.chat-status-strip')).toBeVisible()
  await page.getByRole('button', { name: 'Agent 工作台' }).click()
  await expect(page.locator('.context-panel.mobile-open')).toBeVisible()
  await expect(page.getByText('审批队列')).toBeVisible()
  await expect(page.getByText('产物与预览')).toBeVisible()
  await expect(page.getByText('审计摘要')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})
