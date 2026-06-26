# AICopilot.Web Frontend Rules

修改 AICopilot 前端前必须读完本文件。本文件约束 `src/vues/AICopilot.Web` 下的 Vue、Pinia、SSE 协议处理、消息渲染和前端测试。

## 1. Backend Errors Are Contract Data

前端必须完整接入后端错误信息。后端返回的 `code`、`detail`、`userFacingMessage`、`suggestedAction` 是产品诊断信息，不是调试噪音。

| 来源 | 格式 | 处理位置 | 要求 |
| --- | --- | --- | --- |
| SSE 流内 `ChunkType.Error` | `{ code, detail, userFacingMessage }` | `protocol/chunkReducer.ts` -> `addErrorChunk` -> `resolveChatErrorMessage` | default 分支必须优先使用 `userFacingMessage` / `detail`，不能直接给死文案 |
| SSE 连接失败，HTTP 400/403/500 | `ApiError` + 可能有 `ProblemDetails` / validation body | `services/chatService.ts` -> `sendEventStream.onopen` -> `toFriendlyMessage` | 必须解析 response body 的 `detail` / `errors`，不能只按 status code 泛化 |
| SSE 流内 `ChunkType.AgentEvent`，例如 `stage=plan_draft_failed` | `AgentEventPayload { code, detail, suggestedAction }` | `protocol/chunkReducer.ts` -> `addAgentEventChunk` | 失败类事件的 `detail` 必须进入用户可见错误展示区 |
| ASP.NET validation | `{ errors: ... }` 或 RFC ProblemDetails + `errors` | `stores/chatErrorStore.ts` | 必须提取字段错误或错误数组 |

`请求没有通过后端校验` 只能作为所有其他解析路径都失败时的最后兜底。新增错误路径必须补 `chatErrorStore` 或 `chunkReducer` 单元测试。

## 2. Session State Must Be Scoped

以下数据属于当前会话，必须放在 `SessionScopedState` 容器内：

- `agentTasks`
- `agentApprovals`
- `agentAuditSummary`
- `timelineEvents`
- `currentWorkspace`
- `currentArtifactPreview`
- `chartPreview`
- `uploadedFiles`
- `isAgentBusy`

错误状态统一走 `chatErrorStore`。不得在 `chatStore` 里维护全局 `agentErrorMessage`、`lastAgentError` 或同类裸 `ref`。

新增会话级数据时必须同时做四件事：

1. 加到 `SessionScopedState` 接口。
2. 在 `createSessionScopedState()` 工厂函数里给默认值。
3. 通过统一 reset 入口清理，不能在某个 action 里手写零散清理。
4. 补 `sessionScopedState.spec.ts` 断言 reset 会清掉该字段。

`chatStore` 可以暴露同名 computed 给模板用，但底层数据必须来自 `SessionScopedState`。

## 3. ChatWindow Boundary

`ChatWindow.vue` 只能做页面编排。会话列表、消息流、输入框、模式切换、Agent run block、审批卡、产物预览等复杂逻辑应逐步拆成组件或 composable。

当前阶段不强制一次性拆分，但新功能不允许继续把大块领域逻辑堆进 `ChatWindow.vue`。超过 30 行的新逻辑块应先抽出。

## 4. Popover Closing Policy

所有 popover、modal、dropdown、options panel 必须同时满足：

- 点击外部关闭。
- Escape 关闭。
- 切会话时关闭。
- 切 Plan / Chat 模式时关闭。

不允许只靠按钮 toggle。

## 5. Model Thinking Tags Are Not User Text

以下内容不得出现在用户可见消息正文里：

- `<mm:think>...</mm:think>`
- `<think>...</think>`
- `mm:think...`
- 残缺的 think 开闭标签

后端 `AgentStreamRuntime` 是主清洗层，前端 `chunkReducer.ts` 是防漏兜底层。若后续保留 thinking 内容，只能放入“运行详情”折叠区，默认折叠。

## 6. Plan / Chat Mode Discipline

- 切换模式时必须清除当前会话错误状态。
- Plan 模式错误不能带到 Chat 模式，Chat 模式错误也不能带到 Plan 模式。
- 切换模式时必须关闭 composer options panel。
- Plan 模式确认前不得执行 Cloud 查询、MCP 工具、Tool 调用或 Worker 入队。

## Pre-change Checklist

提交前必须逐项确认：

- [ ] 读完本文件。
- [ ] 新增会话数据进入 `SessionScopedState` 和 `createSessionScopedState()`。
- [ ] 新增错误路径展示后端 `code` / `detail`，没有用固定死文案覆盖。
- [ ] 新增弹出面板有 click-outside 和 Escape 关闭。
- [ ] 文本 chunk 经过 think 标签兜底清洗。
- [ ] 前端单元测试覆盖状态 reset、错误解析或 chunk 处理变更。
- [ ] `npm run type-check` 和 `npm run test:unit` 通过。
