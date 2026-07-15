import { expect, test, type Page } from '@playwright/test'

async function authenticate(page: Page) {
  await page.addInitScript(() => {
    window.sessionStorage.setItem('aicopilot.auth.token', 'smoke-token')
  })
}

async function expectNoHorizontalOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({
    width: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
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
  { path: '/access', text: '权限治理' },
]) {
  test(`${route.path} protected route renders with authenticated user`, async ({ page }) => {
    await expectProtectedShell(page, route.path)
    await expect(page.getByText(route.text).first()).toBeVisible()
  })
}

test('inline agent run restores task, workspace, approvals, and artifacts', { tag: '@desktop' }, async ({ page }) => {
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
  await expect(page.getByTestId('inline-runtime-card').getByText('思考与执行记录')).toBeVisible()
  await expect(page.getByText('步骤、事件和安全边界已折叠')).toHaveCount(0)
  await expect(page.getByText(/\d+\/\d+ 步 ·/)).toHaveCount(0)
  await expect(page.getByText(/\d+ 个事件/)).toHaveCount(0)
  await expect(page.locator('.status-tile').first()).toBeHidden()
  await expect(page.locator('.boundary-inline')).toHaveCount(0)
  await expect(page.getByText('Cloud 只读边界')).toBeHidden()

  await expect(page.getByTestId('inline-approval-card')).toBeVisible()
  await expect(page.getByTestId('inline-approval-card').getByText('需要确认后继续')).toBeVisible()
  await expect(page.getByTestId('inline-approval-card').getByText('工具审批')).toHaveCount(0)
  await expect(page.locator('.approval-row')).toHaveCount(1)
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeHidden()
  const approvalDetail = page.getByTestId('approval-detail-fold').first()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeHidden()
  await approvalDetail.locator('summary').click()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeVisible()
  await approvalDetail.locator('summary').click()
  await expect(approvalDetail.getByText('generate_pdf', { exact: true })).toBeHidden()

  await expect(page.getByText('工具目录').first()).toBeHidden()

  await expect(page.locator('.step-row').first()).toBeHidden()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeHidden()
  await expect(page.locator('.timeline-row').first()).toBeHidden()
  await page.getByTestId('inline-runtime-summary').click()
  await expect(page.getByTestId('plan-technical-details').getByText('工具目录')).toBeVisible()
  await expect(page.locator('.status-tile').first()).toBeVisible()
  await expect(page.getByTestId('inline-boundary-row')).toBeVisible()
  await expect(page.getByText('Cloud 只读边界')).toBeVisible()
  await expect(page.locator('.step-row').first()).toBeVisible()
  await expect(page.locator('.timeline-row').first()).toBeVisible()
  await expect(page.getByText('工具待审批').first()).toBeVisible()
  await page
    .locator('.step-detail-fold')
    .filter({ hasText: 'read_uploaded_file' })
    .locator('summary')
    .click()
  await expect(page.getByText('read_uploaded_file', { exact: true }).first()).toBeVisible()
  await page
    .locator('.step-detail-fold')
    .filter({ hasText: 'generate_pdf' })
    .locator('summary')
    .click()
  await expect(page.getByText('generate_pdf', { exact: true }).first()).toBeVisible()

  const artifactCard = page.getByTestId('inline-artifact-card')
  await expect(artifactCard).toBeVisible()
  await expect(artifactCard.getByText('WS-SMOKE-001', { exact: true })).toBeHidden()
  await expect(page.getByTestId('artifact-detail-fold')).toBeVisible()
  await expect(page.locator('.artifact-row').first()).toBeHidden()
  await expect(
    page.locator('.artifact-row').filter({ hasText: 'chart-data.json' }).first(),
  ).toBeHidden()
  await expect(page.getByText('AI 独立模拟业务库').first()).toBeHidden()
  await page.getByTestId('artifact-detail-fold').locator('summary').click()
  await expect(artifactCard.getByText('WS-SMOKE-001', { exact: true })).toBeVisible()
  await expect(page.locator('.artifact-row')).toHaveCount(2)
  await expect(page.locator('.artifact-row').first()).toBeVisible()
  await expect(
    page.locator('.artifact-row').filter({ hasText: 'chart-data.json' }).first(),
  ).toBeVisible()
  await expect(page.getByText('AI 独立模拟业务库').first()).toBeVisible()

  await expectNoHorizontalOverflow(page)
})

test('chat shell does not preload internal operations or expose model selection', async ({
  page,
  isMobile,
}) => {
  const requestedPaths: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    requestedPaths.push(url.pathname)
  })

  await expectProtectedShell(page, '/chat')

  await expect(page.locator('.model-selector')).toHaveCount(0)
  await expect(page.locator('select[aria-label="选择模型"]')).toHaveCount(0)
  await expect(
    page.locator('nav[aria-label="主要导航"] button[aria-label="权限治理"]'),
  ).toHaveCount(0)
  await expect(page.locator('[aria-label="系统操作"] button[aria-label="权限治理"]')).toBeVisible()
  await expect(page.getByRole('button', { name: /聊天模式/ })).toHaveClass(/active/)
  await expect(page.getByRole('button', { name: /计划模式/ })).toBeVisible()
  const composerInput = page.locator('.command-composer textarea')
  await expect(composerInput).toHaveAttribute('placeholder', '输入一个简单问题，直接回答')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveAttribute('placeholder', '输入目标，先生成可确认的计划')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await expect(page.getByLabel('选择计划类型')).toBeVisible()
  await expect(page.getByLabel('选择计划类型')).toHaveValue('auto')
  await expect(page.getByText('插件能力', { exact: true })).toBeVisible()
  await expect(composerInput).not.toHaveAttribute('placeholder', /Goal|Shift \+ Enter/)
  await expect(page.getByText('只读分析').first()).toBeVisible()
  await expect(page.getByText('SimulationBusiness')).toHaveCount(0)
  await expect(page.locator('.agent-workbench-panel')).toHaveCount(0)
  await expect(page.getByText('任务控制台')).toHaveCount(0)
  await expect(page.getByText('任务历史')).toHaveCount(0)
  await expect(page.getByText('历史异常数').first()).toBeVisible()
  await expect(page.getByText('history_approval', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('回答模型：未知', { exact: true }).first()).toBeHidden()
  if (!isMobile) {
    await page.locator('.runtime-details summary').first().click()
    await expect(page.getByText('回答模型：未知', { exact: true }).first()).toBeVisible()
  }

  const forbiddenInitialLoads = [
    '/api/aigateway/language-model/chat-options',
    '/api/aigateway/agent/run-queue/summary',
    '/api/aigateway/agent/run-queue',
    '/api/aigateway/agent/worker/status',
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

  await expect(page.getByTestId('primary-model-config-card')).toBeVisible()
  await expect(page.getByTestId('agent-slot-intent')).toBeHidden()
  await expect(plannerSlot).toBeHidden()
  await expect(page.getByTestId('agent-slot-executor')).toBeHidden()
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
    '/api/aigateway/workspace-settings',
  ]

  for (const path of forbiddenInitialLoads) {
    expect(requestedPaths).not.toContain(path)
  }

  await page.getByRole('button', { name: '高级设置' }).click()
  await expect(page.getByTestId('agent-slot-intent')).toBeVisible()
  await expect(plannerSlot).toBeVisible()
  await expect(page.getByTestId('agent-slot-executor')).toBeVisible()
  await expect(page.getByText('IntentRoutingAgent', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('agent_planner', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('agent_executor', { exact: true }).first()).toBeHidden()
  await expect(page.getByText('受控 Agent 计划生成约束').first()).toBeHidden()
  await plannerSlot.getByText('配置详情').click()
  const plannerTemplateIdentifiers = plannerSlot.getByText('agent_planner', { exact: true })
  await expect(plannerTemplateIdentifiers).toHaveCount(2)
  await expect(plannerTemplateIdentifiers.first()).toBeVisible()
  await expect(
    plannerSlot.locator('.slot-technical-fold').getByText('计划生成', { exact: true }),
  ).toBeVisible()
  await expect(plannerSlot.getByText('受控 Agent 计划生成约束')).toBeVisible()
  await expect(
    page.getByText('你是 A助理的计划生成 Agent。只能输出计划，不能调用工具。'),
  ).toBeHidden()
  await plannerSlot.getByText('预览提示词').click()
  await expect(
    page.getByText('你是 A助理的计划生成 Agent。只能输出计划，不能调用工具。'),
  ).toBeVisible()
  await expect(page.getByText('模型池')).toHaveCount(0)
  await expect(page.getByText('工具目录')).toHaveCount(0)
  await expect(page.getByText('运行队列')).toHaveCount(0)
  await expectNoHorizontalOverflow(page)
})

test('knowledge page keeps retrieval and embedding controls collapsed by default', async ({
  page,
}) => {
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

test('chat stream renders widgets and approval card', { tag: '@desktop' }, async ({ page }) => {
  await expectProtectedShell(page, '/chat')

  await page.getByRole('button', { name: /聊天模式/ }).click()
  await page.locator('.command-composer textarea').fill('smoke check agent widgets')
  await page.locator('.send-button').click()

  const answerCard = page.locator('.device-log-answer').first()
  await expect(answerCard).toBeVisible()
  await expect(answerCard.getByText('结论')).toBeVisible()
  await expect(answerCard.getByText('关键指标')).toBeVisible()
  await expect(answerCard.getByText('不能直接执行的动作')).toBeVisible()
  await expect(answerCard.locator('.boundary-section')).toBeVisible()
  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeHidden()
  await expect(page.locator('.widget-frame').first()).toBeVisible()
  expect(await page.locator('.widget-frame').count()).toBeGreaterThanOrEqual(4)
  const messageRuntimeDetails = page.locator('.message .runtime-details').last()
  await expect(messageRuntimeDetails.getByText('运行详情')).toBeVisible()
  await expect(messageRuntimeDetails.getByText('结构化展示')).toBeHidden()
  await messageRuntimeDetails.locator('summary').click()
  await expect(messageRuntimeDetails.getByText('结构化展示')).toBeVisible()
  await expect(messageRuntimeDetails.getByText('图表').first()).toBeVisible()
  const approvalCard = page.locator('.approval-card').first()
  await expect(approvalCard).toBeVisible()
  await expect(approvalCard.getByText('需要确认后继续')).toBeVisible()
  await expect(
    approvalCard.getByText('此动作需要确认现场有人在岗，并再次确认执行前条件。'),
  ).toBeVisible()
  await expect(
    approvalCard.getByText('此工具需要确认现场有人在岗，并再次确认执行前条件。'),
  ).toHaveCount(0)
  await expect(approvalCard.getByText('高风险工具确认')).toBeHidden()
  await approvalCard.locator('.approval-detail-fold summary').click()
  await expect(approvalCard.getByText('高风险工具确认')).toBeVisible()
  await expect(page.getByText('diagnose_alarm', { exact: true })).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

test('mobile chat workspace keeps navigation and primary work area within viewport', { tag: '@mobile' }, async ({ page }) => {
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

function createDeferredSignal() {
  let resolve!: () => void
  const promise = new Promise<void>((complete) => {
    resolve = complete
  })

  return { promise, resolve }
}

async function delayRequest(page: Page, url: string) {
  const released = createDeferredSignal()
  const requested = page.waitForRequest(url)

  await page.route(url, async (route) => {
    await released.promise
    await route.continue()
  })

  return {
    waitUntilRequested: requested.then(() => undefined),
    release: released.resolve,
  }
}

function createSmokeSession(id: string, title: string) {
  return {
    id,
    title,
    onsiteConfirmedAt: null,
    onsiteConfirmedBy: null,
    onsiteConfirmationExpiresAt: null,
  }
}

async function fulfillSessionList(page: Page, sessions: ReturnType<typeof createSmokeSession>[]) {
  await page.route('**/api/aigateway/session/list', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(sessions),
    })
  })
}

async function exerciseInitialSessionHydration(
  page: Page,
  persistedSessionId: string | null,
  isMobile: boolean,
  delayedRequestUrl = '**/api/aigateway/session/list',
) {
  const forbiddenMutationPaths = new Set([
    '/api/aigateway/chat',
    '/api/aigateway/agent/task/plan-stream',
    '/api/aigateway/upload',
  ])
  const mutationRequests: string[] = []

  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'POST' && forbiddenMutationPaths.has(url.pathname)) {
      mutationRequests.push(url.pathname)
    }
  })

  await authenticate(page)
  await page.addInitScript((sessionId) => {
    if (sessionId) {
      window.sessionStorage.setItem('aicopilot.chat.currentSessionId', sessionId)
    } else {
      window.sessionStorage.removeItem('aicopilot.chat.currentSessionId')
    }
  }, persistedSessionId)

  const barrier = await delayRequest(page, delayedRequestUrl)
  await page.goto('/chat')
  await barrier.waitUntilRequested

  const composerInput = page.locator('.command-composer textarea')
  const submitButton = page.locator('.send-button')
  try {
    await expect(page.getByRole('button', { name: '运行配置' })).toBeDisabled()
    if (isMobile) {
      await page.getByRole('button', { name: '打开会话' }).click()
    }
    await expect(page.locator('button[aria-label="新建会话"]')).toBeDisabled()
    const deleteSessionButtons = page.locator('button[aria-label="删除会话"]')
    if (await deleteSessionButtons.count()) {
      await expect(deleteSessionButtons.first()).toBeDisabled()
    }
    if (isMobile) {
      await page.getByRole('button', { name: '关闭会话' }).click()
    }

    await page.getByRole('button', { name: /计划模式/ }).click()
    await composerInput.fill('保留这份初始化期间的计划草稿')
    await expect(submitButton).toBeDisabled()
    await page.getByRole('button', { name: /高级选项/ }).click()
    await page.getByLabel('选择计划类型').selectOption('general_report')
    await page.getByLabel('选择知识库').selectOption('kb1')

    await composerInput.press('Enter')
    await expect(composerInput).toHaveValue('保留这份初始化期间的计划草稿')
    expect(mutationRequests).toEqual([])
  } finally {
    barrier.release()
  }
  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(page.getByText(/LINE-A/).first()).toBeVisible()
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveValue('保留这份初始化期间的计划草稿')
  await expect(page.locator('.composer-options-panel')).toBeVisible()
  await expect(page.getByLabel('选择计划类型')).toHaveValue('general_report')
  await expect(page.getByLabel('选择知识库')).toHaveValue('kb1')
  expect(mutationRequests).toEqual([])
}

test('initial session hydration preserves the composer draft and advanced choices', async ({
  page,
  isMobile,
}) => {
  await exerciseInitialSessionHydration(page, null, isMobile)
})

test('cold initialization failure stays visible and never grants session authority', async ({
  page,
  isMobile,
}) => {
  await authenticate(page)
  await page.route('**/api/aigateway/session/list', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: '会话服务暂时不可用' }),
    })
  })

  await page.goto('/chat')

  await expect(page.locator('.message-viewport > .canvas-error')).toContainText(
    '会话服务暂时不可用',
  )
  await expect(page.locator('.canvas-toolbar')).toContainText('不可用')
  await expect(page.locator('.send-button')).toBeDisabled()
  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }
  await expect(page.locator('button[aria-label="新建会话"]')).toBeEnabled()
  await page.locator('button[aria-label="新建会话"]').click()

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(page.locator('.message-viewport > .canvas-error')).toHaveCount(0)
  await expect(page.locator('.canvas-toolbar')).toContainText('就绪')
  const composerInput = page.locator('.command-composer textarea')
  await composerInput.fill('恢复后已获得新会话动作权限')
  await expect(page.locator('.send-button')).toBeEnabled()
})

test('stale persisted session cannot submit before fallback hydration', async ({
  page,
  isMobile,
}) => {
  await exerciseInitialSessionHydration(page, 'missing-session', isMobile)
})

test('matched persisted session cannot submit before activation completes', async ({
  page,
  isMobile,
}) => {
  await exerciseInitialSessionHydration(
    page,
    'smoke-session',
    isMobile,
    '**/api/aigateway/chat-message/list**',
  )
})

test('refreshing the active session preserves composer-local state', async ({ page }) => {
  await expectProtectedShell(page, '/chat')

  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('刷新当前会话时保留这份草稿')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await page.getByLabel('选择计划类型').selectOption('general_report')
  await page.getByLabel('选择知识库').selectOption('kb1')
  await page.getByRole('button', { name: /高级选项/ }).click()

  const barrier = await delayRequest(page, '**/api/aigateway/chat-message/list**')
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await barrier.waitUntilRequested

  try {
    await expect(page.locator('.send-button')).toBeDisabled()
    await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
    await expect(composerInput).toHaveValue('刷新当前会话时保留这份草稿')
    await expect(page.locator('.composer-options-panel')).toHaveCount(0)
  } finally {
    barrier.release()
  }

  await expect(page.getByText(/LINE-A/).first()).toBeVisible()
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveValue('刷新当前会话时保留这份草稿')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await expect(page.getByLabel('选择计划类型')).toHaveValue('general_report')
  await expect(page.getByLabel('选择知识库')).toHaveValue('kb1')
})

test('switching resolved sessions resets composer-local state', async ({ page, isMobile }) => {
  await authenticate(page)
  await fulfillSessionList(page, [
    createSmokeSession('smoke-session', '产线异常分析'),
    createSmokeSession('smoke-session-b', '第二会话'),
  ])

  await page.goto('/chat')
  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')

  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('切换会话后必须清理')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await page.getByLabel('选择计划类型').selectOption('general_report')
  await expect(page.locator('.composer-options-panel')).toBeVisible()

  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }
  await page.locator('.session-item').filter({ hasText: '第二会话' }).click()

  await expect(page.locator('.canvas-header h1')).toHaveText('第二会话')
  await expect(page.getByRole('button', { name: /聊天模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveValue('')
  await expect(page.locator('.composer-options-panel')).toHaveCount(0)

  await page.getByRole('button', { name: /计划模式/ }).click()
  await page.getByRole('button', { name: /高级选项/ }).click()
  await expect(page.getByLabel('选择计划类型')).toHaveValue('auto')
})

test('session navigation is serialized while the next session activates', async ({
  page,
  isMobile,
}) => {
  await authenticate(page)
  await fulfillSessionList(page, [
    createSmokeSession('smoke-session', '产线异常分析'),
    createSmokeSession('smoke-session-b', '第二会话'),
    createSmokeSession('smoke-session-c', '第三会话'),
  ])

  const released = createDeferredSignal()
  const requested = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/aigateway/chat-message/list' &&
      url.searchParams.get('sessionId') === 'smoke-session-b'
    )
  })
  await page.route('**/api/aigateway/chat-message/list**', async (route) => {
    const sessionId = new URL(route.request().url()).searchParams.get('sessionId')
    if (sessionId === 'smoke-session-b') {
      await released.promise
    }
    await route.continue()
  })

  await page.goto('/chat')
  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }

  const secondSession = page.locator('.session-item').filter({ hasText: '第二会话' })
  const thirdSession = page.locator('.session-item').filter({ hasText: '第三会话' })
  await secondSession.click()
  await requested
  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }
  try {
    await expect(thirdSession).toHaveAttribute('aria-disabled', 'true')
    await expect(page.locator('button[aria-label="新建会话"]')).toBeDisabled()
    await expect(page.locator('button[aria-label="删除会话"]').first()).toBeDisabled()
    await thirdSession.dispatchEvent('click')
  } finally {
    released.resolve()
  }
  await expect(page.locator('.canvas-header h1')).toHaveText('第二会话')
  await expect(thirdSession).toHaveAttribute('aria-disabled', 'false')

  await thirdSession.click()
  await expect(page.locator('.canvas-header h1')).toHaveText('第三会话')
})

test('returning to chat revokes retained session authority until reactivation', async ({
  page,
  isMobile,
}) => {
  const mutationRequests: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'POST' && url.pathname === '/api/aigateway/chat') {
      mutationRequests.push(url.pathname)
    }
  })

  await expectProtectedShell(page, '/chat')
  await page.getByRole('button', { name: '运行配置' }).click()
  await expect(page).toHaveURL(/\/config$/)

  const barrier = await delayRequest(page, '**/api/aigateway/approval/pending**')
  await page.getByRole('button', { name: 'AI 工作台' }).click()
  await barrier.waitUntilRequested

  const composerInput = page.locator('.command-composer textarea')
  try {
    await expect(page).toHaveURL(/\/chat$/)
    await expect(page.locator('.send-button')).toBeDisabled()
    if (isMobile) {
      await page.getByRole('button', { name: '打开会话' }).click()
    }
    await expect(page.locator('button[aria-label="新建会话"]')).toBeDisabled()
    if (isMobile) {
      await page.getByRole('button', { name: '关闭会话' }).click()
    }
    await expect(page.getByTestId('inline-agent-run')).toHaveCount(0)
    await composerInput.fill('返回页面重新激活前不能提交')
    await composerInput.press('Enter')
    await expect(composerInput).toHaveValue('返回页面重新激活前不能提交')
    expect(mutationRequests).toEqual([])
  } finally {
    barrier.release()
  }

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(composerInput).toHaveValue('返回页面重新激活前不能提交')
  expect(mutationRequests).toEqual([])
})

test('failed active-session refresh restores action authority and composer state', async ({
  page,
  isMobile,
}) => {
  await expectProtectedShell(page, '/chat')
  if (!isMobile) {
    await expect(page.getByTestId('inline-agent-run')).toBeVisible()
  }

  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('刷新失败也必须保留这份草稿')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await page.getByLabel('选择计划类型').selectOption('general_report')
  await page.getByLabel('选择知识库').selectOption('kb1')
  await page.getByRole('button', { name: /高级选项/ }).click()

  await page.route(
    '**/api/aigateway/chat-message/list**',
    async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: '会话刷新暂时不可用' }),
      })
    },
    { times: 1 },
  )
  const failedResponse = page.waitForResponse(
    (response) =>
      response.url().includes('/api/aigateway/chat-message/list') && response.status() === 503,
  )

  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await failedResponse

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(page.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveValue('刷新失败也必须保留这份草稿')
  await expect(page.locator('.message-viewport > .canvas-error')).toBeVisible()
  if (!isMobile) {
    await expect(page.getByTestId('inline-agent-run')).toBeVisible()
  }
  await page.getByRole('button', { name: /高级选项/ }).click()
  await expect(page.getByLabel('选择计划类型')).toHaveValue('general_report')
  await expect(page.getByLabel('选择知识库')).toHaveValue('kb1')
})

test('failed cross-session activation preserves the committed session draft', async ({
  page,
  isMobile,
}) => {
  await authenticate(page)
  await fulfillSessionList(page, [
    createSmokeSession('smoke-session', '产线异常分析'),
    createSmokeSession('smoke-session-b', '第二会话'),
  ])
  await page.goto('/chat')
  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')

  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('切换失败必须留在 A 会话的草稿')
  await page.getByRole('button', { name: /高级选项/ }).click()
  await page.getByLabel('选择计划类型').selectOption('general_report')

  const released = createDeferredSignal()
  const requested = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/aigateway/chat-message/list' &&
      url.searchParams.get('sessionId') === 'smoke-session-b'
    )
  })
  await page.route('**/api/aigateway/chat-message/list**', async (route) => {
    const sessionId = new URL(route.request().url()).searchParams.get('sessionId')
    if (sessionId !== 'smoke-session-b') {
      await route.continue()
      return
    }

    await released.promise
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: '第二会话暂时不可用' }),
    })
  })

  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }
  await page.locator('.session-item').filter({ hasText: '第二会话' }).click()
  await requested

  await expect(composerInput).toBeDisabled()
  await expect(page.getByRole('button', { name: /高级选项/ })).toBeDisabled()
  released.resolve()

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(composerInput).toBeEnabled()
  await expect(composerInput).toHaveValue('切换失败必须留在 A 会话的草稿')
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  if (!(await page.locator('.composer-options-panel').isVisible())) {
    await page.getByRole('button', { name: /高级选项/ }).click()
  }
  await expect(page.getByLabel('选择计划类型')).toHaveValue('general_report')
})

test('failed new-session creation preserves the committed session draft', async ({
  page,
  isMobile,
}) => {
  await expectProtectedShell(page, '/chat')
  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('新建失败不得丢失原会话草稿')

  const released = createDeferredSignal()
  const requested = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return request.method() === 'POST' && url.pathname === '/api/aigateway/session'
  })
  await page.route('**/api/aigateway/session', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.continue()
      return
    }

    await released.promise
    await route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: '新建会话暂时不可用' }),
    })
  })

  if (isMobile) {
    await page.getByRole('button', { name: '打开会话' }).click()
  }
  await page.locator('button[aria-label="新建会话"]').click()
  await requested
  if (isMobile) {
    await page.getByRole('button', { name: '关闭会话' }).click()
  }

  await expect(composerInput).toBeDisabled()
  released.resolve()

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(composerInput).toBeEnabled()
  await expect(composerInput).toHaveValue('新建失败不得丢失原会话草稿')
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
})

test('failed SPA reinitialization restores the prior session and hydration draft', async ({
  page,
  isMobile,
}) => {
  await expectProtectedShell(page, '/chat')
  if (!isMobile) {
    await expect(page.getByTestId('inline-agent-run')).toBeVisible()
  }
  await page.getByRole('button', { name: '运行配置' }).click()
  await expect(page).toHaveURL(/\/config$/)

  const released = createDeferredSignal()
  const requested = page.waitForRequest('**/api/aigateway/session/list')
  await page.route(
    '**/api/aigateway/session/list',
    async (route) => {
      await released.promise
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ title: '会话列表暂时不可用' }),
      })
    },
    { times: 1 },
  )
  const failedResponse = page.waitForResponse(
    (response) =>
      response.url().includes('/api/aigateway/session/list') && response.status() === 503,
  )

  await page.getByRole('button', { name: 'AI 工作台' }).click()
  await requested

  const composerInput = page.locator('.command-composer textarea')
  await page.getByRole('button', { name: /计划模式/ }).click()
  await composerInput.fill('重挂载失败也必须保留这份水合草稿')
  await expect(page.getByRole('button', { name: '运行配置' })).toBeDisabled()

  released.resolve()
  await failedResponse

  await expect(page.locator('.canvas-header h1')).toHaveText('产线异常分析')
  await expect(page.getByRole('button', { name: '运行配置' })).toBeEnabled()
  await expect(page.getByRole('button', { name: /计划模式/ })).toHaveClass(/active/)
  await expect(composerInput).toHaveValue('重挂载失败也必须保留这份水合草稿')
  await expect(page.locator('.message-viewport > .canvas-error')).toBeVisible()
  if (!isMobile) {
    await expect(page.getByTestId('inline-agent-run')).toBeVisible()
  }
})
