# Agent 工作流与异常契约

本文档约束 AICopilot Agent workflow、Plan/Chat 模式、MCP/Tool/Human-in-the-loop 边界、后端异常和前端错误展示。总计划见 `docs/AI架构治理清单.md`。

## 1. 统一工作流主干

- `AgentWorkflowPipeline` 是用户输入的统一工作流主干，负责意图理解、上下文编排和能力发现。
- Chat 模式和 Plan 模式必须复用统一管线；区别只在出口。
- Chat 出口可以直接回答，或按安全策略执行已允许的低风险只读动作。
- Plan 出口只能生成 `PlanDraft` 草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。
- 用户确认 `PlanDraft` 后才允许转换为 `ExecutablePlan` / `AgentTask`，进入 Skill、Tool、Schema、Guard、审批和 Worker 执行链路。
- Skill、Tool、MCP、Knowledge 或 DataSource 未匹配时，不能阻断 `PlanDraft`；只能在草案中说明能力缺口、降级为路线规划或要求用户补充目标。

## 2. 能力边界

以下能力必须保持分离：

- Intent routing。
- RAG 知识检索。
- DataAnalysis / Text-to-SQL。
- MCP 工具执行。
- Human-in-the-loop 审批。
- AgentTask worker 执行。

不得为了实现方便把这些能力合成一个大 agent、大 service 或绕过审批/工具边界的隐藏 adapter。`AgentWorkflowTopology` 的 `Tools`、`Knowledge`、`DataAnalysis`、`BusinessPolicy` 分支必须保持显式 fan-out/fan-in，不得拍平成串行或为新能力另起孤立链路。

### 2.1 分支完成状态与合流门禁

- 四个并行分支必须返回显式 `BranchResult` 完成状态：`Skipped` 表示当前路由没有该分支相关意图；`Empty` 表示相关分支已合法执行但没有真实结果或可用能力；`Succeeded` 表示有可进入最终上下文的真实载荷；`Failed` 表示分支没有完成并携带稳定错误码与安全摘要。禁止再用空字符串、空数组或空对象伪装异常。
- 分支是否 `Required` 必须由本次 routing intents 与对应 executor 的同一套相关性判定得出，不能把某类分支全局硬编码成永远必需或永远可选。路由判定与实际执行过滤条件必须保持一致。
- `Required + Failed` 必须在 fan-in 后、最终上下文聚合前停止最终回答，返回稳定且脱敏的 Chat Error chunk；`Required + Empty` 是合法完成，可以继续合流；可选分支失败不得伪造成成功载荷，也不得进入最终上下文。
- `Skipped`、`Empty`、`Failed` 的载荷一律不得进入 `ContextAggregatorExecutor`；只有 `Succeeded` 可以参与最终回答。调用方取消必须继续向上传播，不能转换成业务空结果或普通失败。
- 以上状态治理不得改成串行工作流。`Task.WhenAll`、`AgentWorkflowSink` 和四分支 fan-out/fan-in 仍是唯一主干；PlanDraft 的能力发现仍只生成草案，能力缺失或发现失败不得越权执行真实 Tool、MCP、Cloud 查询或 Worker。

Cloud 只读 Agent 当前正式能力限定为：

- `Analysis.Device.List/Detail/Status`：设备主数据以及 Cloud 权威 `softwareStatus`/运行心跳。
- `Analysis.DeviceLog.Latest/Range/ByLevel`：设备日志正式查询。
- `Analysis.Capacity.Range/ByDevice`：产能汇总/小时事实；`Analysis.Capacity.ByProcess` 尚不支持。
- `Analysis.ProductionData.Latest/Range/ByDevice`：正式生产记录。
- `Analysis.Process.List/Detail`：工序主数据列表与唯一精确详情。
- `Analysis.ClientRelease.List`：Cloud 返回的客户端发布版本列表。

以上能力必须复用统一语义定义、`CloudReadonlyAgentPlanService` 和唯一 Cloud AiRead 客户端；成功、合法空集、语义规划失败或 Cloud 数据源不可用时都必须返回真实边界，不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。`PlanAgentTaskCoordinator` 只能创建和维护草案，不得持有查询客户端或执行查询；语义 intent 只能在用户确认草案后创建，运行时工具只能在确认后的执行链调用。

`Analysis.Recipe.*` 的具体配方数据请求必须在调用语义规划器前返回禁读边界，即使规划器本会失败也不能进入 provider、数据库或 fallback。Chat 语义执行器只持有 Cloud AiRead 客户端、语义规划器和日志器；Direct DB、physical mapping、SQL generator、数据库审计与 Text-to-SQL fallback 只能留在各自独立治理链，不能重新挂回正式语义执行器。

### 2.2 DataAnalysis 最终上下文边界

- `analysis.metadata` 与 `business_data_preview` 必须共用同一份大小写不敏感字段标签映射；同一 raw field 的 metadata name/description 和 preview property key 必须一致，重名标签只在该唯一入口稳定加后缀。
- raw field 为 SQL、表/视图、数据源、数据库、host/user 等 formatter 内部显示字段，或命中 `CloudReadOnlyGovernedSchema.BlockedFieldFragments` 的 key/credential/secret/token/password 等共享敏感标识时，必须整项丢弃，不能换一个业务名后继续输出其值。
- 标签候选只允许 metadata description 再回退 raw field；指令型/内部文本、控制字符、换行或超过 80 字符的候选不得成为 JSON key，两个候选都不安全时使用固定业务 fallback。
- preview 只承载最多 3 个可识别 dictionary row 的扁平标量。值只走唯一 `SanitizeValue` 入口；JSON null/bool/number/string 可映射为同等标量，CLR 只显式允许 string、bool/数值、date、Guid 和 enum。JSON object/array 及其余任意 CLR object/collection 一律输出既有脱敏占位，不调用自定义 `ToString()` 透出内容，不递归展开第二层 key。
- Semantic/FreeForm Widget 在 formatter 之前由各自真实 plan/summary/rows 生成，不消费最终 prompt label map；不得因最终上下文治理复制第二套 Widget 标签或值清洗器。

## 3. Cloud 写入禁止

- Agent workflow、MCP、Tool、后台任务、直接 SQL 和隐藏 adapter 均不得创建、修改、删除、补录、审批、派发或触发 Cloud 业务数据。
- Human-in-the-loop 不是 Cloud 业务写入授权。
- 如果未来需要 Cloud AI-facing 写接口，必须由用户明确批准新的跨仓库接口契约、权限模型、审计模型和回滚策略；不得在 AICopilot 内部先行实现。

## 4. 异常响应契约

后端未知异常必须走稳定 ProblemDetails：

- 必须返回稳定 `code`、`detail`、`userFacingMessage` 和 `correlationId`。
- 用户可见文案必须是安全摘要，不能包含 raw exception message、SQL、prompt、token、endpoint、连接串、密码、API key 或内部 provider 细节。
- `UseCaseExceptionHandler` catch-all 不得把原始 exception 对象交给 logger 形成敏感日志。
- 新增、删除或重命名错误码时，必须同步更新 `docs/frontend-integration-contract-package-2026-05-17.md` 并运行错误码目录测试。

## 5. 日志和持久化脱敏

生产路径日志、审计、任务失败摘要和持久化失败原因必须只记录安全字段：

- traceId / correlationId。
- exception type / error type。
- failure code / reason code。
- SQL length / SQL hash。
- query hash / question hash。
- intent routing response length / SHA-256 / response type / parse state。
- 固定业务错误码和固定用户文案。

不得记录：

- raw exception message。
- raw exception 对象，即 `LogError(ex, ...)`、`LogWarning(exception, ...)`、`LogError(e, ...)`、`LogWarning(cleanupException, ...)` 等把异常变量作为 logger 首参的重载。
- SQL 原文、用户 prompt、参数值。
- token、API key、密码、连接串。
- endpoint、sourceName、表名、视图名、内部字段。
- 原始工具参数、原始工具结果行或未脱敏 provider 返回。
- intent routing 原始响应、intent reasoning、用户 prompt 或查询原文；路由诊断只能记录长度、SHA-256、类型和解析状态。

少量 `ex.Message` 只能作为内部分类器输入；输出仍必须是固定安全文案、hash、code 或 failure classification。

## 6. 前端错误展示

- 普通 API、SSE open/error、AgentEvent、ApprovalRequest、AgentTask、Chat Error chunk、OIDC、auth、RAG、Config、artifact、upload、route guard 等失败路径必须进入会话错误栏、页面错误栏、dialog error 或安全 fallback。
- 前端必须优先展示后端 ProblemDetails 的 `userFacingMessage`、validation errors、`detail`、`title`。
- 未知 Chat Error code 不得直接展示 raw `detail`。
- 不允许用户操作失败只 `catch {}` 或只写 console 而没有可见状态。
- 纯解析 fallback 可以降级展示或记录安全摘要，但不能伪造成功状态。

## 7. 运行详情

- 运行详情默认折叠。
- 运行详情只能展示工具名、查询次数、返回行数、截断状态、Widget 类型、业务过滤条件和安全摘要。
- 运行详情不得展开 SQL 原文、连接串、password、token、endpoint、sourceName、tableName、databaseName、内部路径、原始工具结果行或未脱敏错误。
- 运行详情不是审批、AgentTask、Cloud 查询或 Widget 的权威状态源；权威状态必须来自对应聚合和 session timeline 投影。

## 8. 源码归属

- 统一工作流：`src/services/AICopilot.AiGatewayService/Workflows/AgentWorkflowPipeline.cs`。
- PlanDraft / ExecutablePlan：`src/services/AICopilot.AiGatewayService/AgentTasks`。
- Tool / MCP / approval：`src/services/AICopilot.AiGatewayService/Tools`、`src/services/AICopilot.McpService`、`src/infrastructure/AICopilot.Infrastructure/Mcp`。
- 后端错误边界：`src/hosts/AICopilot.HttpApi/Infrastructure/UseCaseExceptionHandler.cs`、`src/shared/AICopilot.SharedKernel/Result`。
- SQL/DataAnalysis 脱敏：`src/infrastructure/AICopilot.Dapper`、`src/services/AICopilot.DataAnalysisService`。
- runtime/provider/worker 脱敏：`src/infrastructure/AICopilot.AiRuntime`、`src/services/AICopilot.AiGatewayService/AgentTasks`、`src/services/AICopilot.AiGatewayService/Workflows/Executors`。
- 前端错误：`src/vues/AICopilot.Web/src/services`、`src/vues/AICopilot.Web/src/stores`、`src/vues/AICopilot.Web/src/protocol`、`src/vues/AICopilot.Web/src/views`。
- 运行详情：`src/vues/AICopilot.Web/src/protocol/runtimeDetails.ts`、`src/vues/AICopilot.Web/src/components/chat/MessageRuntimeDetailsPanel.vue`。

## 9. 验收命令

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "AgentWorkflowBranchSemanticsTests|ClaudeFollowupClosureTests|ToolRegistryGovernanceTests|ChatErrorContractTests|SecurityHardeningTests|TextToSqlReadOnlyTests" --no-restore
cd src/vues/AICopilot.Web && npm run test:unit -- chatErrorStore frontendErrorHandling runtimeDetails
rg -n "Log(Critical|Error|Warning|Information|Debug|Trace)\\(\\s*[a-zA-Z_][a-zA-Z0-9_]*\\s*,|catch\\s*\\{\\s*\\}" src/hosts src/infrastructure src/services src/vues/AICopilot.Web/src
```

## 10. 外部依赖

- 本契约不授权 Cloud 业务写接口，也不替代 CloudPlatform 权限、审计或接口契约。
- 真实生产日志、前端线上错误和 AgentTask worker 行为仍需发布后通过日志、trace、UI 和任务记录验收。
