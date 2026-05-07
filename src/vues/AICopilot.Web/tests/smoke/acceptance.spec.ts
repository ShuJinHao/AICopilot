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

test('login page renders operational shell without overflow', async ({ page }) => {
  await page.goto('/login')

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
    await authenticate(page)
    await page.goto(route.path)

    await expect(page.getByText(route.text).first()).toBeVisible()
    await expect(page.locator('.user-zone').getByText('系统管理员')).toBeVisible()
    await expectNoHorizontalOverflow(page)
  })
}

test('chat stream renders widgets, unknown widget fallback, and approval card', async ({ page }) => {
  await authenticate(page)
  await page.goto('/chat')

  await expect(page.getByText('Chat Workspace')).toBeVisible()
  await page.locator('.composer textarea').fill('生成一组只读图表和审批请求')
  await page.locator('.composer button').click()

  await expect(page.getByText('LINE-A 产能趋势')).toBeVisible()
  await expect(page.getByText('设备状态明细')).toBeVisible()
  await expect(page.getByText('良品率').first()).toBeVisible()
  await expect(page.getByText('无法识别的组件')).toBeVisible()
  await expect(page.getByText('人工审批请求')).toBeVisible()
  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('mobile chat workspace keeps navigation and approval content within viewport', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'mobile viewport only')

  await authenticate(page)
  await page.goto('/chat')

  await expect(page.getByText('Chat Workspace')).toBeVisible()
  await expect(page.getByText('产线异常分析')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})
