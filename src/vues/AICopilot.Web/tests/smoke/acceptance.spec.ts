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
  { path: '/chat', text: '对话工作区' },
  { path: '/config', text: 'AI 配置' },
  { path: '/knowledge', text: '知识库' },
  { path: '/access', text: '权限治理' }
]) {
  test(`${route.path} protected route renders with authenticated user`, async ({ page }) => {
    await expectProtectedShell(page, route.path)
    await expect(page.getByText(route.text).first()).toBeVisible()
  })
}

test('inline agent run restores task, workspace, approvals, and artifacts', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop inline execution card covers the restored task data')

  await expectProtectedShell(page, '/chat')

  await expect(page.getByText('对话工作区')).toBeVisible()
  await expect(page.getByText(/LINE-A/).first()).toBeVisible()
  await expect(page.locator('.agent-workbench-panel')).toHaveCount(0)
  await expect(page.getByText('任务控制台')).toHaveCount(0)
  await expect(page.getByText('任务历史')).toHaveCount(0)
  await expect(page.getByTestId('inline-agent-run')).toBeVisible()
  await expect(page.locator('.canvas-status-strip')).toHaveCount(0)
  await expect(page.getByTestId('inline-status-card')).toHaveCount(0)
  await expect(page.getByTestId('inline-timeline-card')).toHaveCount(0)
  await expect(page.getByTestId('inline-steps-card')).toHaveCount(0)
  await expect(page.getByTestId('inline-runtime-card')).toBeVisible()
  await expect(page.getByTestId('inline-runtime-card').getByText('执行记录与安全边界')).toBeVisible()
  await expect(page.getByText('步骤、事件和安全边界已折叠')).toHaveCount(0)
  await expect(page.getByText(/\d+\/\d+ 步 ·/)).toHaveCount(0)
  await expect(page.getByText(/\d+ 个事件/)).toHaveCount(0)
  await expect(page.locator('.status-tile').first()).toBeHidden()
  await expect(page.locator('.boundary-inline')).toHaveCount(0)
  await expect(page.getByText('Cloud 只读边界')).toBeHidden()

  await expect(page.getByTestId('inline-approval-card')).toBeVisible()
  await expect(page.getByTestId('inline-approval-card').getByText('执行确认')).toBeVisible()
  await expect(page.getByTestId('inline-approval-card').getByText('工具审批')).toHaveCount(0)
  await expect(page.locator('.approval-row')).toHaveCount(1)
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeHidden()
  const approvalDetail = page.getByTestId('approval-detail-fold').first()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeHidden()
  await approvalDetail.locator('summary').click()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeVisible()
  await approvalDetail.locator('summary').click()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeHidden()

  await expect(page.getByTestId('inline-plan-card').getByText('工具目录')).toBeHidden()
  await page.getByTestId('plan-technical-details').locator('summary').click()
  await expect(page.getByTestId('inline-plan-card').getByText('工具目录')).toBeVisible()

  await expect(page.locator('.step-row').first()).toBeHidden()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeHidden()
  await expect(page.locator('.timeline-row').first()).toBeHidden()
  await page.getByTestId('inline-runtime-summary').click()
  await expect(page.locator('.status-tile').first()).toBeVisible()
  await expect(page.getByTestId('inline-boundary-row')).toBeVisible()
  await expect(page.getByText('Cloud 只读边界')).toBeVisible()
  await expect(page.locator('.step-row').first()).toBeVisible()
  await expect(page.locator('.timeline-row').first()).toBeVisible()
  await expect(page.getByText('工具待审批').first()).toBeVisible()
  await page.locator('.step-detail-fold').filter({ hasText: 'read_uploaded_file' }).locator('summary').click()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeVisible()
  await page.locator('.step-detail-fold').filter({ hasText: 'generate_pdf' }).locator('summary').click()
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeVisible()

  const artifactCard = page.getByTestId('inline-artifact-card')
  await expect(artifactCard).toBeVisible()
  await expect(artifactCard.getByText('WS-SMOKE-001', { exact: true })).toBeHidden()
  await expect(page.getByTestId('artifact-detail-fold')).toBeVisible()
  await expect(page.locator('.artifact-row').first()).toBeHidden()
  await expect(page.locator('.artifact-row').filter({ hasText: 'chart-data.json' }).first()).toBeHidden()
  await expect(page.getByText('AI 独立模拟业务库').first()).toBeHidden()
  await page.getByTestId('artifact-detail-fold').locator('summary').click()
  await expect(artifactCard.getByText('WS-SMOKE-001', { exact: true })).toBeVisible()
  await expect(page.locator('.artifact-row')).toHaveCount(2)
  await expect(page.locator('.artifact-row').first()).toBeVisible()
  await expect(page.locator('.artifact-row').filter({ hasText: 'chart-data.json' }).first()).toBeVisible()
  await expect(page.getByText('AI 独立模拟业务库').first()).toBeVisible()

  await expectNoHorizontalOverflow(page)
})

test('chat shell does not preload internal operations or expose model selection', async ({ page }) => {
  const requestedPaths: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    requestedPaths.push(url.pathname)
  })

  await expectProtectedShell(page, '/chat')

  await expect(page.locator('.model-selector')).toHaveCount(0)
  await expect(page.locator('select[aria-label="选择模型"]')).toHaveCount(0)
  await expect(page.locator('nav[aria-label="主要导航"] button[aria-label="权限治理"]')).toHaveCount(0)
  await expect(page.locator('[aria-label="系统操作"] button[aria-label="权限治理"]')).toBeVisible()
  await expect(page.getByLabel('选择 Skill')).toBeVisible()
  await expect(page.getByLabel('选择 Skill')).toHaveValue('general_report')
  await expect(page.getByLabel('选择知识库')).toBeVisible()
  await expect(page.getByLabel('选择知识库')).toHaveValue('kb1')
  const composerInput = page.locator('.command-composer textarea')
  await expect(composerInput).toHaveAttribute('placeholder', '输入问题或目标')
  await expect(composerInput).not.toHaveAttribute('placeholder', /Goal|Shift \+ Enter/)
  await expect(page.getByText('只读分析').first()).toBeVisible()
  await expect(page.getByText('SimulationBusiness')).toHaveCount(0)
  await expect(page.locator('.agent-workbench-panel')).toHaveCount(0)
  await expect(page.getByText('任务控制台')).toHaveCount(0)
  await expect(page.getByText('任务历史')).toHaveCount(0)
  await expect(page.getByText('历史异常数').first()).toBeVisible()
  await expect(page.getByText('history_approval', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('回答模型：未知', { exact: true }).first()).toBeHidden()
  await page.locator('.runtime-details summary').first().click()
  await expect(page.getByText('回答模型：未知', { exact: true }).first()).toBeVisible()

  const forbiddenInitialLoads = [
    '/api/aigateway/language-model/chat-options',
    '/api/aigateway/agent/run-queue/summary',
    '/api/aigateway/agent/run-queue',
    '/api/aigateway/agent/worker/status'
  ]

  for (const path of forbiddenInitialLoads) {
    expect(requestedPaths).not.toContain(path)
  }
})

test('config renders fixed agent slots without internal operations preload', async ({ page }) => {
  const requestedPaths: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    requestedPaths.push(url.pathname)
  })

  await expectProtectedShell(page, '/config')

  const plannerSlot = page.getByTestId('agent-slot-planner')

  await expect(page.getByTestId('agent-slot-intent')).toBeVisible()
  await expect(plannerSlot).toBeVisible()
  await expect(page.getByTestId('agent-slot-executor')).toBeVisible()
  await expect(page.getByText('IntentRoutingAgent', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('agent_planner', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('agent_executor', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('受控 Agent 计划生成约束').first()).toBeHidden()
  await plannerSlot.getByText('配置详情').click()
  await expect(plannerSlot.getByText('agent_planner', { exact: true })).toBeVisible()
  await expect(plannerSlot.getByText('受控 Agent 计划生成约束')).toBeVisible()
  await expect(page.getByText('你是 A助理的计划生成 Agent。只能输出计划，不能调用工具。')).toBeHidden()
  await plannerSlot.getByText('预览提示词').click()
  await expect(page.getByText('你是 A助理的计划生成 Agent。只能输出计划，不能调用工具。')).toBeVisible()
  await expect(page.getByText('模型池')).toHaveCount(0)
  await expect(page.getByText('工具目录')).toHaveCount(0)
  await expect(page.getByText('运行队列')).toHaveCount(0)

  const forbiddenInitialLoads = [
    '/api/aigateway/provider-reliability',
    '/api/data-analysis/business-database/list',
    '/api/mcp/server/list',
    '/api/aigateway/approval-policy/list',
    '/api/aigateway/tools',
    '/api/aigateway/tools/catalog',
    '/api/aigateway/agent/run-queue/summary',
    '/api/aigateway/agent/run-queue',
    '/api/aigateway/agent/worker/status',
    '/api/aigateway/workspace-settings'
  ]

  for (const path of forbiddenInitialLoads) {
    expect(requestedPaths).not.toContain(path)
  }
  await expectNoHorizontalOverflow(page)
})

test('knowledge page keeps retrieval and embedding controls collapsed by default', async ({ page }) => {
  await expectProtectedShell(page, '/knowledge')

  await expect(page.getByText('报警处置手册.pdf')).toBeVisible()
  await expect(page.getByRole('button', { name: /测试检索/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /高级设置/ })).toBeVisible()
  await expect(page.getByText('检索预览')).toHaveCount(0)
  await expect(page.getByText('嵌入模型')).toHaveCount(0)
  await expect(page.getByText(/切分|向量索引/)).toHaveCount(0)
  await expect(page.getByText('片段数').first()).toBeHidden()
  await expect(page.getByText('高级治理').first()).toBeHidden()

  await page.getByRole('button', { name: /新增知识库/ }).click()
  const knowledgeDrawer = page.locator('aside').filter({ hasText: '知识库' }).first()
  await expect(knowledgeDrawer.getByText('名称')).toBeVisible()
  await expect(knowledgeDrawer.getByText('嵌入模型')).toBeHidden()
  await knowledgeDrawer.locator('.knowledge-base-advanced summary').click()
  await expect(knowledgeDrawer.getByText('嵌入模型')).toBeVisible()
  await page.getByRole('button', { name: '关闭' }).click()

  const documentDetail = page.locator('.document-detail-fold').first()
  await documentDetail.locator('summary').click()
  await expect(documentDetail.getByText('片段数')).toBeVisible()

  await page.getByRole('button', { name: /测试检索/ }).click()
  await expect(page.getByText('检索预览')).toBeVisible()

  await page.getByRole('button', { name: /高级设置/ }).click()
  await expect(page.getByText('嵌入模型')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('chat stream renders widgets and approval card', async ({ page, isMobile }) => {
  test.skip(isMobile, 'desktop stream rendering covers widgets and approval card; mobile layout is covered separately')

  await expectProtectedShell(page, '/chat')

  await page.locator('.command-composer textarea').fill('smoke check agent widgets')
  await page.locator('.send-button').click()

  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeHidden()
  await expect(page.locator('.widget-frame').first()).toBeVisible()
  expect(await page.locator('.widget-frame').count()).toBeGreaterThanOrEqual(4)
  const approvalCard = page.locator('.approval-card').first()
  await expect(approvalCard).toBeVisible()
  await expect(approvalCard.getByText('需要确认后继续')).toBeVisible()
  await expect(approvalCard.getByText('此动作需要确认现场有人在岗，并再次确认执行前条件。')).toBeVisible()
  await expect(approvalCard.getByText('此工具需要确认现场有人在岗，并再次确认执行前条件。')).toHaveCount(0)
  await expect(approvalCard.getByText('高风险工具确认')).toBeHidden()
  await approvalCard.locator('.approval-detail-fold summary').click()
  await expect(approvalCard.getByText('高风险工具确认')).toBeVisible()
  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('mobile chat workspace keeps navigation and primary work area within viewport', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'mobile viewport only')

  await expectProtectedShell(page, '/chat')

  await expect(page.getByText('对话工作区')).toBeVisible()
  await expect(page.locator('.ai-canvas')).toBeVisible()
  await expect(page.locator('.canvas-status-strip')).toHaveCount(0)
  await expect(page.getByTestId('inline-runtime-card')).toBeVisible()
  await expect(page.locator('.agent-workbench-panel')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Agent', exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: '打开会话' }).click()
  await expect(page.locator('.mobile-drawer.left')).toBeVisible()
  await expectNoHorizontalOverflow(page)
})
