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

  await page.getByTestId('agent-tab-approvals').click()
  await expect(page.locator('.approval-row')).toHaveCount(1)
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeVisible()

  await page.getByTestId('agent-tab-steps').click()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeVisible()

  await page.waitForTimeout(1000)
  await page.getByTestId('agent-tab-artifacts').click({ force: true })
  await page.waitForTimeout(250)
  await page.getByTestId('agent-tab-artifacts').click({ force: true })
  await page.waitForFunction(() => sessionStorage.getItem('aicopilot.ui.agentWorkbenchTab') === 'artifacts')
  await expect(page.getByText('WS-SMOKE-001').first()).toBeVisible()
  await expect(page.locator('.artifact-row')).toHaveCount(2)
  await expect(page.getByText('chart-data.json').first()).toBeVisible()
  await expect(page.getByText('AI 独立模拟业务库').first()).toBeVisible()

  await page.getByTestId('agent-tab-audit').click()
  await expect(page.getByText('Agent.Plan')).toBeVisible()

  await page.getByTestId('agent-tab-boundary').click()
  await expect(page.getByText('Cloud 只读边界')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('config agent operations panel renders run queue and worker status', async ({ page }) => {
  await expectProtectedShell(page, '/config')

  await page.getByRole('tab', { name: 'Agent', exact: true }).click()

  await expect(page.getByTestId('agent-config-workspace')).toBeVisible()
  await expect(page.getByTestId('agent-config-run-queue')).toBeVisible()
  await expect(page.getByTestId('agent-config-worker-status')).toBeVisible()
  await expect(page.getByText('AICopilot.DataWorker')).toBeVisible()
  await expect(page.getByText('Queued', { exact: true }).first()).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('agent trial panel shows P11 pilot readiness rehearsal evidence', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the trial panel visible')

  await expectProtectedShell(page, '/chat')

  await page.getByTestId('agent-tab-trial').click()
  await expect(page.getByTestId('p11-pilot-readiness-panel')).toBeVisible()
  await expect(page.getByTestId('p11-no-production-read')).toBeVisible()
  await expect(page.getByTestId('p11-no-production-data')).toBeVisible()
  await expect(page.getByTestId('p11-config-package')).toBeVisible()

  await page.getByRole('button', { name: /审批演练/ }).click()
  await expect(page.getByTestId('p11-approval-rehearsal')).toBeVisible()

  await page.getByRole('button', { name: /fake contract/ }).click()
  await expect(page.getByTestId('p11-contract-rehearsal')).toBeVisible()
  await expect(page.getByText('devices', { exact: true })).toBeVisible()
  await expect(page.getByText('recipe_versions', { exact: true })).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('agent trial panel shows P12 production readonly pilot gate', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the trial panel visible')

  await expectProtectedShell(page, '/chat')

  await page.getByTestId('agent-tab-trial').click()
  await expect(page.getByTestId('p12-production-pilot-panel')).toBeVisible()
  await expect(page.getByTestId('p12-fixed-template-marker')).toBeVisible()
  await expect(page.getByTestId('p12-production-readonly-marker')).toBeVisible()
  await expect(page.getByTestId('p12-gated-marker')).toBeVisible()
  await expect(page.getByTestId('p12-allowlist')).toContainText('devices')

  await page.getByRole('button', { name: /Fixed scenario/ }).click()
  const run = page.getByTestId('p12-production-pilot-run')
  await expect(run).toBeVisible()
  await expect(run).toContainText('CloudReadonlyProductionPilot')
  await expect(run).toContainText('ProductionPilot')
  await expectNoHorizontalOverflow(page)
})

test('agent trial panel shows P13 production controlled pilot intent gate', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the trial panel visible')

  await expectProtectedShell(page, '/chat')

  await page.getByTestId('agent-tab-trial').click()
  const panel = page.getByTestId('p13-production-controlled-panel')
  await expect(panel).toBeVisible()
  await expect(page.getByTestId('p13-controlled-marker')).toContainText('CloudReadonlyProductionControlledPilot')
  await expect(page.getByTestId('p13-boundary-marker')).toContainText('ProductionControlledPilot')
  await expect(page.getByTestId('p13-allowlist')).toContainText('devices')

  await panel.locator('textarea').fill('分析最近一天设备清单')
  await panel.getByRole('button', { name: /Intent \+ Plan/ }).click()
  await page.getByTestId('agent-tab-trial').click()
  await expect(page.getByTestId('p13-production-controlled-intent')).toContainText('pcg-smoke-devices')
  await expect(page.getByTestId('p13-production-controlled-intent')).toContainText('DeviceList')

  await page.getByTestId('p13-production-controlled-panel').getByRole('button', { name: /Direct smoke/ }).click()
  const run = page.getByTestId('p13-production-controlled-run')
  await expect(run).toBeVisible()
  await expect(run).toContainText('CloudReadonlyProductionControlledPilot')
  await expect(run).toContainText('ProductionControlledPilot')
  await expectNoHorizontalOverflow(page)
})

test('agent trial panel shows P14 production operations gate', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the trial panel visible')

  await expectProtectedShell(page, '/chat')

  await page.getByTestId('agent-tab-trial').click()
  const panel = page.getByTestId('p14-production-operations-panel')
  await expect(panel).toBeVisible()
  await expect(page.getByTestId('p14-non-ga-marker')).toContainText('not full production rollout')
  await expect(page.getByTestId('p14-run-ledger')).toContainText('CloudReadonlyProductionControlledPilot')
  await expect(page.getByTestId('p14-run-ledger')).toContainText('sha256:p13-result-devices')

  await panel.getByRole('button', { name: /Emergency stop/ }).click()
  await expect(page.getByTestId('p14-emergency-stop-state')).toContainText('Active')

  await panel.getByRole('button', { name: /Clear stop/ }).click()
  await expect(page.getByTestId('p14-emergency-stop-state')).toContainText('Clear')

  await panel.getByRole('button', { name: /P15 readiness/ }).click()
  await expect(page.getByTestId('p14-ga-readiness')).toContainText('ReadyForP15Planning')
  await expectNoHorizontalOverflow(page)
})

test('agent trial panel shows P15 planning authorization gate', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop workbench keeps the trial panel visible')

  await expectProtectedShell(page, '/chat')

  await page.getByTestId('agent-tab-trial').click()
  const panel = page.getByTestId('p15-planning-gate-panel')
  await expect(panel).toBeVisible()
  await expect(page.getByTestId('p15-planning-marker')).toContainText('not real Pilot execution')
  await expect(page.getByTestId('p15-not-ga-marker')).toContainText('Not GA')
  await expect(page.getByTestId('p15-allowlist')).toContainText('devices')
  await expect(page.getByTestId('p15-allowlist')).toContainText('pass_station_records')
  await expect(page.getByTestId('p15-blocked-status')).toContainText('ReadyForP16PlanningBlocked')
  await expect(page.getByTestId('p15-blocker-list')).toContainText('P12/P13 persistence')
  await expect(page.getByTestId('p15-blocker-list')).toContainText('Artifact refs backfill')
  await expect(page.getByTestId('p15-blocker-list')).toContainText('Rows retention')
  await expectNoHorizontalOverflow(page)
})

test('chat stream renders widgets and approval card', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop stream rendering covers widgets and approval card; mobile layout is covered separately')

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
  await expect(page.getByTestId('agent-tab-approvals')).toBeVisible()
  await expect(page.getByTestId('agent-tab-artifacts')).toBeVisible()
  await expect(page.getByTestId('agent-tab-audit')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})
