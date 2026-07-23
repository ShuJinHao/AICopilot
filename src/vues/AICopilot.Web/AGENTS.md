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
- `chatRunStatus`

错误状态统一走 `chatErrorStore`。不得在 `chatStore` 里维护全局 `agentErrorMessage`、`lastAgentError` 或同类裸 `ref`。

新增会话级数据时必须同时做四件事：

1. 加到 `SessionScopedState` 接口。
2. 在 `createSessionScopedState()` 工厂函数里给默认值。
3. 通过统一 reset 入口清理，不能在某个 action 里手写零散清理。
4. 补 `sessionScopedState.spec.ts` 断言 reset 会清掉该字段。

`chatStore` 可以暴露同名 computed 给模板用，但底层数据必须来自 `SessionScopedState`。

## 2.1 Chat Run Status

任何可能超过 1 秒的 Chat / Plan 流式请求、工具调用、DataAnalysis 或 Cloud 只读查询，都必须有用户可见运行状态，不能让用户面对空白等待。

运行状态必须按 `sessionId` 隔离，并尽量绑定当前 assistant message；切换会话不能显示其他会话的运行状态，回到原会话时状态仍应正确。

运行阶段只能来自真实 stream/request/chunk/error/complete 事实。允许从 `Intent`、`FunctionCall`、`FunctionResult`、`Text`、`Error` 和 stream complete 推导阶段；不得伪造进度、查询次数、返回行数、设备在线、Cloud 查询成功或 DataAnalysis 结果。

## 2.2 DataAnalysis Widget Rendering

DataAnalysis Widget 是后端结构化展示契约，不是 Markdown 或任意 ECharts 配置。前端只能渲染受控的 `StatsCard`、`Chart`、`DataTable` schema。

Widget payload 必须兼容后端 `type` / `Type` / `widget_type` 和 `data` / `Data` 字段形态；新增 Widget 协议兼容性时必须补 `widgetNormalizer` 或 `chunkReducer` 单元测试。

前端不得为 DeviceLog 自行编造级别分布、时间分布、问题分类、指标、证据表或返回行数；只能展示后端本轮 stream/history 中提供的 Widget 数据。

## 2.3 Runtime Details Folding

运行详情只能作为 assistant 消息的默认折叠辅助信息，数据来源必须是本轮 stream/history chunks、消息 metadata 和 `chatRunStatus`。不得把运行详情作为审批、工具执行、AgentTask、Cloud 查询或 Widget 的权威状态源。

工具参数和工具结果在运行详情中只能展示安全摘要，例如白名单业务过滤条件、查询次数、返回行数、截断状态和 Widget 类型；不得展开 SQL 原文、连接串、密码、token、sourceName、表/视图名、endpoint、内部字段、原始结果行或未脱敏错误原文。

## 2.4 DeviceLog Answer Presentation

DeviceLog 最终回答只有在识别到固定段落结构时，才允许渲染为结构化结果卡；普通聊天、规则问答、RAG 回答和段落不完整的文本必须回退到原 Markdown。

结构化结果卡只能重排已有回答文本，突出结论、收窄指标/记录/原因/建议/边界/查询范围的视觉层级；不得新增指标、改写结论、补充未查询数据或把“不能直接执行的动作”隐藏成不可见信息。

## 2.5 Product Truthfulness

- 登录页、Shell、空态和工作台预览不得硬编码在线、就绪、命中率、数据源数量、Agent 运行状态、Cloud 查询成功或其他运行时 KPI。
- 未登录或未请求后端状态时，只能展示静态能力说明和安全边界；运行状态必须来自后端真实响应或当前 stream/session 事实。
- 空查询不得解释为离线，数据源失败不得包装成成功；设备状态只能称为最后上报状态，除非后端返回正式 freshness 语义。
- 对话建议必须使用当前已支持且真实的业务能力，不得用演示产线、虚构设备、配方版本或假数据暗示系统已有结果。

## 2.6 Session Resolution and Hydration

- `currentSessionId` 是从 `sessionStorage` 恢复的候选 ID；在 session list 返回并能解析出 `currentSession` / `resolvedSessionId` 前，它不是可操作会话。Chat、Plan、Upload、建议问题及其它服务端动作必须使用 resolved session，并由 UI guard 与 Pinia action 同时拒绝陈旧或不存在的 ID。
- 初始化期间允许用户编辑草稿、切换 Chat/Plan、展开高级选项并选择知识库，但不得提交或上传。首次 `null -> A`（包含无缓存、有效缓存和 stale fallback）与 `A -> A` 刷新属于水合，必须保留这些 composer-local 状态；重新进入 ChatView 必须在子组件渲染前同步撤销旧 resolved-session 动作权限并清空旧 runtime 投影。
- 只有从已解析会话离开的 `A -> B`、`A -> null` 才清空草稿、恢复 Chat 模式、关闭高级面板并重置高级选择；`A -> A` 刷新不得重置。
- 会话水合测试必须使用可控 session-list/history/approval 响应屏障，不得用 sleep、扩大 timeout 或重试掩盖竞态；至少覆盖 clean storage、stale storage、有效缓存待激活、`A -> A` 刷新、`A -> B` 切换、快速 `B -> C` 串行化、SPA 离开再返回，以及未解析 ID 不得产生写请求。所有屏障必须在 `finally` 中释放，等待请求必须受 Playwright timeout 管理。
- 会话激活期间必须同时禁用新建、删除和再次选择；UI 门禁之外，store action 仍须 fail-closed，不能仅依赖 disabled 样式。
- 任一会话绑定的流、任务 mutation、上传、预览、下载、在岗声明、轮询或历史分页在途时，必须保持统一 `isSessionTransitionBlocked` 临界区；期间不得选择、新建、删除会话或通过 SPA 导航离开 Chat。删除在发送 HTTP 前就必须同步撤销 resolved 权限；只有服务端列表对账确认目标仍存在时才恢复原 active/raw 会话。禁止用 `streamStore.stop()` 或清空 busy 布尔值冒充请求取消。
- Session/Task/Workspace/Artifact/Approval facade 必须先校验当前 roster、resolved session 和 canonical projection；相同 ID 但由调用方篡改的 URL、文件名、task/workspace 元数据也不得使用。任务、工作区和审批属于同一权威投影代次，任一步读取失败必须恢复上一代完整可信快照，不能留下“新 task + 旧 workspace/approval”的混合状态。
- Function approval 使用 per-session authority-unknown，Agent approval 使用 per-task authority-unknown。未知期间允许继续编辑本地草稿，但 Chat、Plan、审批、任务执行、最终审核和 finalize 必须同时在 UI 与 store action 层 fail-closed；对应权威刷新成功后才解除，不能靠清错误栏或切模式解除。
- DELETE/POST/SSE 超时或断链不等于服务端未提交。删除会话失败后必须先重新读取 session list：目标仍存在才能恢复，目标已消失按成功路径收口，列表也不可用则保持 unresolved 并要求刷新。非幂等 create/upload/stream mutation 的 client operation id、服务端唯一 receipt 和结果回放仍是后续持久化治理项；在此之前禁止自动重连或把 ACK-unknown 宣称为确定失败。
- `uploadedFiles` 与知识库等 composer 请求上限属于会话本地输入：`A -> A` 水合必须保留，真实 `A -> B` / 新建会话必须清空。初始化错误必须按来源保存，只能由同一来源成功重试清除；普通模式切换或清当前运行错误不得抹掉知识库/session 水合失败。

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
- “基于此结果追问”只能由用户在当前会话的 `Completed` AgentTask 结果卡上显式触发；前端只发送一次 `referencedAgentTaskId`，必须在发送、切换会话、删除会话、重置或进入 Plan 模式时清除。不得自动选择最近任务或把引用带到其他会话。
- 前端显示的 `EvidenceSetDigest` 只用于说明当前结果引用和跨格式一致性，不能作为授权或权威 Evidence 内容；后端拒绝引用时必须显示安全错误。新的设备、工序、日志级别或时间范围请求必须让后端重新查询，前端不得继续标成复用旧结果。

## Pre-change Checklist

提交前必须逐项确认：

- [ ] 读完本文件。
- [ ] 新增会话数据进入 `SessionScopedState` 和 `createSessionScopedState()`。
- [ ] 新增错误路径展示后端 `code` / `detail`，没有用固定死文案覆盖。
- [ ] 新增弹出面板有 click-outside 和 Escape 关闭。
- [ ] 文本 chunk 经过 think 标签兜底清洗。
- [ ] 前端单元测试覆盖状态 reset、错误解析或 chunk 处理变更。
- [ ] 登录页、Shell、空态和建议语没有硬编码运行时 KPI、在线/就绪状态或虚构业务结果。
- [ ] 所有会话写动作使用 resolved session；初始化水合与真实会话切换均有确定性回归测试。
- [ ] `npm run type-check` 和 selector/`vitest related` 选出的受影响测试通过；完整 `npm run test:unit` 只在用户显式 Quality/Full 授权时运行。
