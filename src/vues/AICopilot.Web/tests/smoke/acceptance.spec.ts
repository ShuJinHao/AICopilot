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
  await expect(page.locator('.ai-topbar')).toBeVisible()
  await expect(page.locator('.topbar-status')).toBeVisible()
  expect((await page.locator('main').innerText()).trim().length).toBeGreaterThan(0)
  await expectNoHorizontalOverflow(page)
}

test('login page renders operational shell without overflow', async ({ page, isMobile }) => {
  await page.goto('/login')

  await expect(page.locator('.login-page')).toBeVisible()
  await expect(page.locator('.login-shell')).toBeVisible()
  await expect(page.locator('.login-title')).toBeVisible()
  await expect(page.locator('.login-form')).toBeVisible()
  if (!isMobile) {
    await expect(page.locator('.ai-preview-panel')).toBeVisible()
  }
  await expect(page.getByText('制造 AI 运维工作台')).toBeVisible()
  await expect(page.getByText('登录 A 助理')).toBeVisible()
  await expect(page.getByPlaceholder('输入用户名')).toBeVisible()
  await expect(page.getByPlaceholder('输入密码')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

for (const route of [
  { path: '/chat', text: 'AI CANVAS' },
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

  await expect(page.getByText('AI CANVAS')).toBeVisible()
  await expect(page.getByText(/LINE-A/).first()).toBeVisible()
  await expect(page.locator('.agent-workbench-panel').getByText('任务控制台').first()).toBeVisible()

  await page.locator('.agent-tabs').getByRole('button', { name: /审批/ }).click()
  await expect(page.locator('.approval-row')).toHaveCount(1)
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeVisible()

  await page.locator('.agent-tabs').getByRole('button', { name: /步骤/ }).click()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeVisible()

  await page.locator('.agent-tabs').getByRole('button', { name: /产物/ }).click()
  await expect(page.getByText('WS-SMOKE-001').first()).toBeVisible()
  await expect(page.getByText('chart-data.json')).toBeVisible()
  await expect(page.getByText('模拟 Cloud 只读数据')).toBeVisible()
  await expect(page.locator('.artifact-row')).toHaveCount(2)

  await page.locator('.agent-tabs').getByRole('button', { name: /审计/ }).click()
  await expect(page.getByText('Agent.Plan')).toBeVisible()

  await page.locator('.agent-tabs').getByRole('button', { name: /边界/ }).click()
  await expect(page.getByText('Cloud 只读边界')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('config agent operations panel renders run queue and worker status', async ({ page }) => {
  await expectProtectedShell(page, '/config')

  await page.getByRole('tab', { name: 'Agent', exact: true }).click()

  await expect(page.getByText('Agent / Workspace')).toBeVisible()
  await expect(page.getByText('Run Queue')).toBeVisible()
  await expect(page.getByText('Worker Status')).toBeVisible()
  await expect(page.getByText('AICopilot.DataWorker')).toBeVisible()
  await expect(page.getByText('Queued', { exact: true }).first()).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('chat stream renders widgets and approval card', async ({ page }) => {
  await expectProtectedShell(page, '/chat')

  await page.locator('.command-composer textarea').fill('smoke check agent widgets')
  await page.locator('.send-button').click()

  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeVisible()
  await expect(page.locator('.widget-frame').first()).toBeVisible()
  expect(await page.locator('.widget-frame').count()).toBeGreaterThanOrEqual(4)
  await expect(page.locator('.approval-card')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('mobile chat workspace keeps navigation and primary work area within viewport', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'mobile viewport only')

  await expectProtectedShell(page, '/chat')

  await expect(page.getByText('AI CANVAS')).toBeVisible()
  await expect(page.locator('.ai-canvas')).toBeVisible()
  await expect(page.locator('.canvas-status-strip')).toBeVisible()
  await page.getByRole('button', { name: 'Agent', exact: true }).click()
  await expect(page.locator('.agent-workbench-panel.mobile-open')).toBeVisible()
  await expect(page.getByRole('button', { name: /审批/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /产物/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /审计/ })).toBeVisible()
  await expectNoHorizontalOverflow(page)
})
