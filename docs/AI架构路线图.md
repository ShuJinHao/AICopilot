# AICopilot AI 架构路线图

本文只记录当前目标架构、能力状态和后续退出门。业务和安全规则以 [AICopilot 业务规则](./AICopilot业务规则.md)、[Agent 工作流与异常契约](./Agent工作流与异常契约.md)、[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md) 和 [DDD 聚合根边界](./DDD聚合根边界.md) 为准；历史方案和阶段执行过程只通过 Git 追溯。

## 1. 当前目标架构

- 主聊天只有 Microsoft Agent Framework Harness 一条执行主链，`Plan` / `Execute` 是同一 `AgentSession` 的服务端权威模式。
- 主 Harness 保留 Todo、受治理工具、逐次批准、持久化 checkpoint 与中断语义；不维护第二套任务编排或文件产物运行时。
- `ConversationTemplate.ModelId` 决定主回答模型。Harness 创建后从 `ScopedRuntimeAgent.ConfigurationSnapshot` 记录实际最终模型 provenance，请求不得临时覆盖。
- `BusinessQuery` 和 `KnowledgeQuery` 是 Execute-only 服务端工具；前者执行 typed provider 优先、仅同源 `Unsupported` / `Unavailable` 允许受控 Text-to-SQL，后者只检索当前用户授权知识库。
- `MainChatToolGate` 统一筛选本地与 MCP 工具；`ToolSurfaceGuardChatClient` 在 provider 请求和响应两侧 fail-closed。Cloud 业务数据永久只读，模型和人工批准都不能授予 Cloud 写权限。
- 模型端点池、限额、熔断、使用结算和审计以每次真实模型调用为粒度；进程内选择不代替 PostgreSQL 配额预约。
- 消息历史直接按 `Message.Sequence` 分页。可持久的 AiGateway 面只包含 LanguageModel、ConversationTemplate、Session、Message、AgentSessionState、ToolRegistration 和 ModelQuotaReservation。

## 2. 能力状态

| 能力 | 源码目标 | 验证退出门 | 生产状态 |
|---|---|---|---|
| Harness 主聊天 | 单主链、Plan/Execute、stream/history | Harness、HTTP/SSE 与前端受影响用例通过 | 未验收 |
| AgentSession 持久化 | 认证加密、TTL、版本、checkpoint、Interrupted | 空库 baseline、并发、超限、损坏和重启边界通过 | 未验收 |
| 逐次工具批准 | 受保护绑定、单工具、所有权/权限/schema/摘要复验 | 批准、拒绝、重复决定、漂移和双待批违约全部 fail-closed | 未验收 |
| AI-01 OIDC/JIT 身份 | Cloud 身份与本地 AI 权限分离 | 首次并发、唯一冲突、SecurityStamp 与审计通过 | 未验收 |
| AI-02 Cloud 真实只读 | 八个 typed GET、同源 fallback、AST guard | `CloudAiReadClientContractTests`、真实非生产 provider 和发布顺序验收 | 未验收 |
| 工具与数据安全 | 权限、身份、注册元数据、脱敏和只读边界 | AIARCH001–007、Architecture/Security、`AgentSafetyApplicationTests` 与 Tool Gate 用例通过 | 未验收 |

“源码目标”只说明当前活动树应维持的结构，不等于 exact SHA 已经过 CI、独立审核或生产验收。旧分支、旧 CI run 和固定测试数不能证明当前候选完成。

## 3. 后续路线

1. 先保持主链唯一性，完成受影响 Architecture/Security、Business、Persistence、HTTP/SSE 与前端验证，并由独立审核确认无 P0/P1。
2. 聊天布局与视觉重构只在当前传输契约上进行；不得借 UI 重构恢复已退出的 API 或客户端状态模型。
3. MCP 版本升级单独立项，必须先重做 runtime identity、schema、read-only hint、approval 和 Tool Gate 回归，不与主链退役同批。
4. 真实候选只能通过工作区 `Validate-Candidate` / `Prepare-Release` 进入产物阶段；未实际部署时生产状态必须保持“未验收”。

## 4. 验证约束

- 根据当前 diff 选择不可弱化的 Analyzer/Architecture/Security 与受影响 Business；不使用旧固定 runner/case 数。
- 任何 `dotnet test --filter` 必须先用同项目、同配置、同 filter 的 `--list-tests` 证明至少命中一项；0-hit 不是有效证据。
- baseline 只在空库或设计时模型中验证；普通开发批次不应用现有数据库、不清库、不部署。
- 全量、coverage、mutation、duplication、Quality、CrossProject、真实 Cloud live 与生产操作只能在用户当前轮明确授权时运行。

## 5. 明确不做

- 不建设任意用户上传 Agent 定义后直接执行的平台。
- 不让模型扩大 Tool、MCP、知识库、数据源或证据权限。
- 不以通用 SQL、MCP 或 Direct DB 替代已覆盖的 Cloud typed GET。
- 不用 Simulation、LLM 推断或当前健康评分冒充生产事实或预测模型结果。
