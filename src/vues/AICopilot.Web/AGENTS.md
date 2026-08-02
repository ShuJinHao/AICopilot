# AICopilot.Web Frontend Rules

修改 AICopilot 前端前必须读完本文件。本文件约束 `src/vues/AICopilot.Web` 下的 Vue、Pinia、SSE 协议处理、消息渲染和前端测试。

## 1. Backend Errors Are Contract Data

前端必须完整接入后端错误信息。后端返回的 `code`、`detail`、`userFacingMessage`、`suggestedAction` 是产品诊断信息，不是调试噪音。

| 来源 | 格式 | 处理位置 | 要求 |
| --- | --- | --- | --- |
| SSE 流内 `ChunkType.Error` | `{ code, detail, userFacingMessage }` | `protocol/chunkReducer.ts` -> `resolveChatErrorMessage` | default 分支必须优先使用后端安全文案 |
| SSE 连接失败 | `ApiError` + ProblemDetails / validation body | `services/chatService.ts` -> `toFriendlyMessage` | 必须解析 `detail` / `errors`，不能只按 status 泛化 |
| SSE 流内 `ChunkType.AgentEvent` | `AgentEventPayload` | `protocol/chunkReducer.ts` | 只记录当前 Harness 会话状态事件 |
| ASP.NET validation | `{ errors: ... }` | `stores/chatErrorStore.ts` | 必须提取字段错误或错误数组 |

新增错误路径必须补 `chatErrorStore` 或 `chunkReducer` 单元测试。

## 2. Session And Approval Authority

- `currentSessionId` 是从 `sessionStorage` 恢复的候选 ID；只有在 session list 中解析成 `resolvedSessionId` 后才可操作。
- Chat、Mode、Approval 和 History 必须使用 resolved session，UI guard 与 Pinia action 都必须 fail-closed。
- 会话激活、流式请求、批准续流和历史分页期间必须保持统一 `isSessionTransitionBlocked` 临界区。
- 批准权威只来自持久化 AgentSession 绑定和 `/approval/pending`。未知期间不得发起新 Chat 或批准。
- 批准提交请求固定为 `{ sessionId, callId, decision }`；不得回传 target、tool、schema、参数摘要或其它客户端可篡改身份。
- 主聊天请求固定为 `{ sessionId, message }`；不得临时指定回答模型或携带任务引用。
- `Interrupted` / `ResetRequired` 会话不得继续发言或批准，必须引导用户新建会话。
- SSE mutation 无幂等键，禁止自动重连或将断链解释为服务端一定未提交。

## 2.1 Chat Run Status

任何可能超过 1 秒的 Chat 流式请求、工具调用或 Cloud 只读查询都必须有用户可见运行状态。

运行状态必须按 `sessionId` 隔离，并尽量绑定当前 assistant message。阶段只能从真实 request、`FunctionCall`、`FunctionResult`、`AgentEvent`、`Text`、`Error` 和 stream complete 推导；不得伪造进度、查询次数、返回行数或 Cloud 查询成功。

## 2.2 Widget Rendering

Widget 是后端结构化展示契约，不是任意 ECharts 配置。前端只能渲染受控的 `StatsCard`、`Chart`、`DataTable` schema，不得自行编造指标、证据行、设备状态或结论。

## 2.3 Runtime Details Folding

运行详情只是 assistant 消息的默认折叠辅助信息，数据来源必须是本轮 stream/history chunks、实际回答模型 metadata 和 `chatRunStatus`。不得展示已退役的路由模型 provenance。

工具参数和结果只能展示安全摘要；不得展开 SQL 原文、连接串、密码、token、sourceName、表/视图名、endpoint、内部字段或未脱敏错误原文。

## 2.4 Product Truthfulness

- 登录页、Shell、空态和对话建议不得硬编码在线、就绪、命中率、数据源数量或其它运行时 KPI。
- 空查询不得解释为离线，数据源失败不得包装成成功。
- 对话建议必须使用当前已支持的真实业务能力，不得用假数据暗示系统已有结果。

## 3. ChatWindow Boundary

`ChatWindow.vue` 只做页面编排。会话列表、消息流、输入框、Harness 模式切换和批准卡应保持独立组件边界。

## 4. Model Thinking Tags Are Not User Text

`<mm:think>...</mm:think>`、`<think>...</think>` 及残缺标签不得出现在用户可见消息正文里。后端 `AgentStreamRuntime` 是主清洗层，前端 `chunkReducer.ts` 是防漏兜底层。

## 5. Harness Plan / Execute Discipline

- Plan / Execute 模式是持久化 AgentSession 状态，切换必须提交 `expectedVersion`，冲突后重读 Session。
- Plan 模式不得暴露任何可执行工具；Execute 模式的工具面仍必须经 Tool Gate、权限、schema 和批准绑定。
- 主聊天一轮只支持一个待批工具调用；前端不得尝试排队、部分保留或自动批准多个调用。

## Pre-change Checklist

- [ ] 读完本文件。
- [ ] 新增错误路径展示后端安全 `code` / `detail`。
- [ ] 文本 chunk 经过 think 标签兜底清洗。
- [ ] 前端单元测试覆盖状态 reset、错误解析或 chunk 处理变更。
- [ ] 所有会话写动作使用 resolved session，并且激活期间 fail-closed。
- [ ] `npm run type-check`、`npm run lint:check`、受影响 Vitest selector 和 production build 通过；全量单元测试只在明确授权时运行。
