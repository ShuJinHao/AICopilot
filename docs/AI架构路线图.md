# AICopilot AI 架构路线图

本文只记录当前目标架构、能力状态和后续退出门。业务和安全规则以 [AICopilot 业务规则](./AICopilot业务规则.md)、[Agent 工作流与异常契约](./Agent工作流与异常契约.md)、[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md) 和 [DDD 聚合根边界](./DDD聚合根边界.md) 为准；历史方案和阶段执行过程只通过 Git 追溯。

## 1. 当前源码架构与待对齐偏差

- 主聊天只有 Microsoft Agent Framework Harness 一条执行主链，并已接入官方 `AgentModeProvider`；`Plan` / `Execute` 由该 provider 持有并持久化在同一 `AgentSession`。
- 目标语义跟随 MAF：Plan 是交互式澄清、调查、调用受治理工具和形成 Todo 的行为模式，Execute 是自主连续完成 Todo 的行为模式；官方 `mode_get` / `mode_set` 均保留。模式与授权正交，不是安全隔离机制。
- 当前仍存在 `ToolSurfaceGuardChatClient` 隐藏 `mode_set`、按模式过滤工具，以及 Harness 指令禁止模型切换的项目私有兼容层，属于待退出兼容债，不是 MAF 原生能力。在后续运行时对齐批次完成前，MAF 原生模式尚未完成运行时对齐。
- Harness 裁剪只允许官方 `HarnessAgentOptions`、`AgentModeProviderOptions`、context provider、approval 与 `IChatClient` 扩展点；禁止 fork MAF、反射私有成员或复制模式状态机。MAF / Harness 升级使用独立 BOM 批次，核心包保持同版本，并以官方 release notes、公开 API 和真实框架合同测试为准。
- 主 Harness 保留 Todo、受治理工具、逐次批准、会话连续性 checkpoint 与中断语义；AgentSession checkpoint 不是 durable Tool checkpoint，Interrupted 后不恢复、不重放。系统不维护第二套任务编排或文件产物运行时。
- `ConversationTemplate.ModelId` 决定主回答模型。Harness 创建后从 `ScopedRuntimeAgent.ConfigurationSnapshot` 记录实际最终模型 provenance，请求不得临时覆盖。
- `BusinessQuery` 和 `KnowledgeQuery` 是服务端受治理工具，能否调用只取决于当前身份、Session、注册、安全元数据和批准边界，不由模式授予；前者由 `BusinessQueryFallbackPolicy` 固定执行 typed provider 优先，并仅在同源 `Unsupported` / `Unavailable` 时由服务端自动进入受控 Text-to-SQL，后者只检索当前用户授权知识库。
- `MainChatToolGate` 统一筛选本地与 MCP 工具；Cloud/MES/ERP 写入、生产控制和越权访问由身份、Tool Gate、`AiToolSafetyPolicy`、SQL AST guard、只读账号与 MCP 治理阻断，不依赖 Plan / Execute，模型、模式切换和人工批准都不能授予写权限。
- MCP 使用稳定版 `ModelContextProtocol 2.0.0`：HTTP discovery-first/AutoDetect、官方 Stdio transport、typed schema/annotation、每 server 30 秒 discovery deadline、登记驱动的调用 timeout、MCP 专用原生 structured-result 契约，以及 `RowVersion + schema/hint/governance fingerprint` 双重刷新；既有 MCP HTTP API、数据库结构和 `McpTransportType.Sse` 存量值保持不变。
- 模型端点池、限额、熔断、使用结算和审计以每次真实模型调用为粒度；进程内选择不代替 PostgreSQL 配额预约。
- 对话前端只使用当前 Session、Chat、Mode、Harness approval、history 与 Interrupted/ResetRequired 协议；不再调用已退出的任务、业务审批、Artifact、routing/runtime settings 或文件工作台接口。
- 消息历史直接按 `Message.Sequence` 分页。可持久的 AiGateway 面只包含 LanguageModel、ConversationTemplate、Session、Message、AgentSessionState、ToolRegistration 和 ModelQuotaReservation。

## 2. 能力状态

| 能力 | 源码状态 | 候选验证退出门 | 生产状态 |
|---|---|---|---|
| Harness 主聊天 | 部分对齐：单主链、官方 AgentModeProvider、stream/history 已接入；私有模式过滤层待退出 | 移除隐藏 `mode_set` 与按模式过滤工具，真实 MAF 合同及 exact-SHA Harness、HTTP/SSE 证据有效 | 未验收 |
| AgentSession 持久化 | 已收口：认证加密、TTL、版本、会话 checkpoint、Interrupted | exact-SHA 空库 baseline、并发、超限、损坏和重启边界有效 | 未验收 |
| 逐次工具批准 | 已收口：受保护绑定、单工具、所有权/权限/schema/摘要复验 | exact-SHA 批准、拒绝、重复决定、漂移和双待批违约全部 fail-closed | 未验收 |
| 对话前端 | 已收口：当前 Session/Approval/Interrupted 协议与安全展示 | exact-SHA lint、typecheck、受影响 Vitest 与 production build 有效 | 未验收 |
| AI-01 OIDC/JIT 身份 | 已建立：Cloud 身份与本地 AI 权限分离 | 首次并发、唯一冲突、SecurityStamp 与审计证据有效 | 未验收 |
| AI-02 Cloud 真实只读 | 已建立：八个 typed GET、服务端同源 fallback、AST guard | `CloudAiReadClientContractTests`、真实非生产 provider 和发布顺序验收 | 未验收 |
| MCP 2.0 受治理通道 | 已收口：discovery-first、独立 deadline、typed contract、MCP 专用 structured result、timeout/漂移撤下 | exact-SHA Stdio/HTTP conformance、Architecture/Security、Tool Gate、批准与 Interrupted 证据有效 | 未验收 |
| 工具与数据安全 | 已建立：权限、身份、注册元数据、脱敏和只读边界 | AIARCH001–007、Architecture/Security、`AgentSafetyApplicationTests` 与 Tool Gate 证据有效 | 未验收 |

AgentSession、逐次批准、对话前端和 MCP 2.0 的源码架构已收口；Harness 单主链已经建立，但 MAF 原生模式运行时仍有上述兼容债，不能提前标记为已收口。“源码已收口”只说明对应能力当前活动树应维持的结构，不等于 exact SHA 已通过候选验证、产物准备或生产验收。所有能力的生产状态继续保持“未验收”；旧分支、旧 CI run 和固定测试数不能证明当前候选完成。

## 3. MAF 原生模式运行时退出门

- 后续独立运行时批次必须移除 `HarnessToolSurfacePolicy` / `ToolSurfaceGuardChatClient` 的模式白名单和 `mode_set` 隐藏，并删除禁止模型切换的私有模式指令；未完成前不得宣称原生 MAF 对齐完成。
- 官方 `AgentModeProvider` 继续拥有 Plan / Execute、`mode_get` / `mode_set` 与 Session 状态。认证 owner 的带版本 API 继续调用官方 `SetModeAsync`，但不再是唯一切换入口。
- 两种模式使用同一套与模式无关的受治理工具目录和执行门禁；身份、权限、工具注册、数据边界、schema、批准及 Cloud/MES/ERP 写阻断必须在切换前后保持等价。
- 运行时对齐必须以官方公开 API 的真实框架合同测试和受影响 Architecture/Security/Business 证明，不 fork、不反射、不复制 MAF 模式状态机；本规则批次不实施这些运行时代码改动。

## 4. 候选验证退出门

- 候选必须是已合入、已推送且 `HEAD == origin/main` 的 clean `main` exact SHA；PR head、旧分支或本地未提交字节不能作为候选。
- 生产 baseline 只能来自获批的只读生产状态工作流，并必须是当前仓库历史中的有效完整 SHA；状态缺失、不健康或无法验证时停止，不得使用旧标签、文档或猜测值替代。
- 工作区 `Validate-Candidate` 必须先判定同一候选 SHA 的默认 CI：运行中禁止重复本地验证，失败或无效证据不得被本地结果覆盖，成功且范围匹配时复用，缺失范围才由统一入口补齐。
- 只有绑定同一候选 SHA、真实生产 baseline、Architecture/Security/Business 分类和 Analyzer-enabled Release production graph 的签名绿色证据，才允许在后续独立批次进入 `Prepare-Release`。
- 未完成候选验证、产物准备和真实部署前，生产状态始终保持“未验收”。

## 5. 验证约束

- 根据当前 diff 选择不可弱化的 Analyzer/Architecture/Security 与受影响 Business；不使用旧固定 runner/case 数。
- 任何 `dotnet test --filter` 必须先用同项目、同配置、同 filter 的 `--list-tests` 证明至少命中一项；0-hit 不是有效证据。
- baseline 只在空库或设计时模型中验证；普通开发批次不应用现有数据库、不清库、不部署。
- 全量、coverage、mutation、duplication、Quality、CrossProject、真实 Cloud live 与生产操作只能在用户当前轮明确授权时运行。

## 6. 明确不做

- 不建设任意用户上传 Agent 定义后直接执行的平台。
- 不让模型扩大 Tool、MCP、知识库、数据源或证据权限。
- 不以通用 SQL、MCP 或 Direct DB 替代已覆盖的 Cloud typed GET。
- 不用 Simulation、LLM 推断或当前健康评分冒充生产事实或预测模型结果。
