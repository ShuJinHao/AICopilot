# AICopilot Instructions

修改 `AICopilot` 前先读：

- 工作区总规则：`../docs/总规则.md`
- AI 业务规则：`资料/AICopilot业务规则.md`
- 改动复盘：`docs/改动复盘与规则沉淀.md`
- 历史核心记录：`../docs/历史核心记录.md`

## Positioning

`AICopilot` 是 AI 助手和受控编排系统，不是云端仓库，不是客户端仓库，也不是制造业务主数据来源。

默认只修改 `AICopilot`。修改 Cloud 或 Edge 必须由用户在当前轮明确授权。

## Change Closure

- 修改 AICopilot 代码前，必须先读工作区总规则、本文档、`资料/AICopilot业务规则.md`、相关专题契约、相关源码、相关测试和近期 git/GitHub 历史。
- 已验收功能默认冻结；不能因为局部重构、测试修复或文档整理顺手改变已有 AI 工作流、Cloud 只读边界、审批边界、工具边界或前端错误契约。
- 每次代码改动完成前，必须更新项目滚动复盘文档 `docs/改动复盘与规则沉淀.md`，最新记录放在最前。
- 每次复盘必须写明改动范围、改动原因、影响面、验证命令、验证结果和规则提炼结论。
- 必须判断是否形成长期规则；有则写入本文档、`资料/AICopilot业务规则.md`、专题契约或工作区长期规则，无则在复盘中写明“无新增长期规则”及原因。
- 新增、删除或重命名后端 `AuthProblemCodes`、`AppProblemCodes`、`CloudAiReadProblemCodes` 时，必须同批更新 `docs/frontend-integration-contract-package-2026-05-17.md`，并运行 `ErrorCodeCatalogTests`。
- 大范围架构、管道、权限、工作流或契约改动不得只以 filtered tests 作为完成依据；全量 `AICopilot.BackendTests` 未跑、CI 全量未确认或环境依赖失败时，最终回复和复盘必须明确标注。
- 最终回复必须列出复盘文档、规则沉淀位置和验证命令；缺任一项，不得称为完成。
- 默认只更新项目滚动复盘文档，不为每个任务新增单独流水文档；只有形成可长期复用的业务或技术契约，才新增专题文档。

## Cloud Business Read-only Boundary

- AICopilot 可以读取已批准范围内的 Cloud 业务数据，用于分析、解释、汇总、检索和建议。
- AICopilot 不得注册、修改、删除、补录、审批、派发或触发 Cloud 业务数据和业务流程。
- AICopilot 不得直接写 Cloud 数据库。
- AICopilot 不得通过 MCP、Tool、Agent workflow、后台任务、直接 SQL 或隐藏适配器间接调用 Cloud 写接口。
- Human-in-the-loop 不是放开 Cloud 写入的理由。
- AICopilot must not create, update, delete, backfill, approve, dispatch, or trigger Cloud business records.
- AICopilot must not directly write to the Cloud database.
- Human-in-the-loop approval is not permission to write Cloud business data.
- Cloud AI-facing APIs are read-only contract surfaces unless the user explicitly approves a new cross-repository write contract.
- Cloud AiRead 正式设备参数是 `deviceId`；`deviceCode` 只能用于设备查询/解析，不得被当作 `deviceId` 发送给 Cloud。
- Cloud 只读读取只能向 Cloud 发送真实端点参数；`scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等试点元数据不得透传给 Cloud。
- 内部开发允许通过 DataAnalysis `CloudReadOnly` 只读数据源直连真实 Cloud PostgreSQL 做 Text-to-SQL 验证；必须使用已验证只读数据库账号、白名单表字段和只读 SQL guard，不得写 Cloud 业务数据，也不得用 Simulation 冒充真实数据。
- 内部真实 Cloud 语义查询优先走 DataAnalysis `CloudReadOnly` Direct DB 映射；Cloud AiRead 封存为未来外部系统只读 API 接入口，不能在内部映射存在时压过 Direct DB。
- CloudReadOnly 探索型 Text-to-SQL 只能作为强语义 intent / Direct DB SQL 失败后的受控 fallback，不能拆分或重命名既有 `Analysis.*` intent，也不能压过可用的强语义路径。
- CloudReadOnly Text-to-SQL fallback 必须同时满足已验证只读凭据、共享白名单 schema、LLM 结构化生成、SELECT-only SQL guard、BusinessQuery safety policy 双层表/列白名单、只读执行和 hash-only 审计；生产 fallback 不得退化为规则 SQL 模板。
- CloudReadOnly Text-to-SQL LLM prompt 可见的物理 schema 仅限 `CloudReadOnlyGovernedSchema` 批准的表名、列名和必要业务描述；不得暴露连接串、凭据、role/权限细节、样例数据、查询结果、参数值、非白名单表字段或系统/敏感字段。
- CloudReadOnly Text-to-SQL 修复重试默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- CloudReadOnly Text-to-SQL 修复历史不得保存完整 SQL、用户 prompt、连接串、参数值或敏感字段；上一轮失败 SQL 只允许在当前调用内以内存参数临时回传给 LLM 生成下一版，不能写入审计、日志、state、结果或持久化对象。
- Legacy `DataSourceSelectionMode.TextToSql` / Business Text-to-SQL draft runtime 仍只服务 SimulationBusiness；Agent 侧 CloudReadOnly 查询只能走 governed fallback runner，不能复用会生成 SimulationBusiness SQL 的旧 draft runtime。
- Direct DB 语义映射如需展示工序名，只能通过只读 join `mfg_processes.process_name`；新增 join 表必须同步更新 SQL guard 白名单、BusinessQuery safety schema、只读 role 授权 workflow、RealSource 模板和测试。
- Direct DB 设备字段 `status` 当前表示最新一条 `device_logs.level`，展示口径必须写成“最新日志级别”，不得暗示为实时在线/运行状态。
- Simulation 只允许作为显式离线演示/测试资产，默认关闭；Real Cloud 查询为空、失败或未配置时不得 fallback 到 Simulation 冒充真实数据。
- AICopilot 自动化如需创建或轮换 Cloud PostgreSQL 只读账号，只能创建/更新专用 readonly role，只能授予已批准白名单表 SELECT，必须要求显式确认词；不得授予写权限、schema create 权限、superuser、createdb、createrole 或 replication。
- 开发阶段已物理删除 Trial/Pilot/Production Readiness 运营线；不得把旧试点运营能力重新接回普通产品导航、Skill 或后台接口。

## OIDC Boundary

Cloud-AICopilot OIDC 身份对齐的长期结论见 `../docs/历史核心记录.md`：

- Cloud 只证明身份、账号有效性、员工有效性。
- AICopilot 保留本地 AI 用户、AI 角色、AI 权限、SecurityStamp、本地禁用、审计和 emergency admin。
- Cloud role 不直接映射 AI role。
- AICopilot 不读取 Cloud Cookie、不接收 Cloud 密码、不直连 Cloud 用户表。
- `IIoT.EdgeClient` 不参与该身份对齐，除非用户后续单独授权。

## Architecture

- 遵守 DDD 分层和依赖倒置。
- `src/core` 放领域核心。
- `src/services` 放命令、查询、workflow、应用编排、MediatR handler。
- `src/infrastructure` 放 EF Core、Dapper、embedding、event bus、provider、MCP 技术细节。
- `src/hosts` 保持薄，只做组合根、API、worker、启动 wiring。
- `src/shared` 只放真正共享的抽象和 shared kernel。
- `src/vues` 放前端逻辑，不回填到 service 或 host。
- 修改 AICopilot 前端前必须先读 `src/vues/AICopilot.Web/AGENTS.md`，遵守前端错误契约、会话状态和 UI 状态规则。
- 不为旧 Cloud 读取路径、旧工具 schema、旧配置模式或旧文档入口保留兼容 adapter；需要跨仓库对齐时同步更新契约、服务注册和测试。
- HTTP API Controller 必须默认要求授权，或对有意公开的端点显式标注匿名访问；不能依赖“忘记加 `[Authorize]`”形成公开接口。
- MediatR `IStreamRequest` 与普通 `IRequest` 是两条管道；所有公开 stream 请求必须声明 `AuthorizeRequirement`，后续接入 `IStreamPipelineBehavior` 时不得把事务或审计边界套到 SSE/stream 长连接上。
- MediatR 横切行为只能通过 `AddAICopilotMediatRPipeline` 统一注册；service 模块只注册本模块 handler 和业务服务，不能分散注册 authorization、validation、telemetry、transaction 或 audit behavior。
- 使用 MediatR handler 的 host 组合根必须先调用统一管道入口，再注册 service 模块；worker host 不能因为挂统一管道而强依赖登录用户态。
- Stream 细粒度权限必须由 `AuthorizationStreamBehavior` 在进入 stream handler 前校验；stream behavior 只能逐项透传 `IAsyncEnumerable`，不得预读、缓存或把 SSE/stream 转成一次性结果。
- 新增或接线 `IStreamPipelineBehavior` 后，必须核对所有公开 `IStreamRequest` 的 `AuthorizeRequirement`，测试种子角色必须覆盖对应权限；无权限场景应返回干净 401/403，不能表现为 SSE 已写 200 后断流。
- MediatR 管道顺序固定为 Telemetry -> Validation -> Authorization；新增 Validation behavior 时必须同步至少一个真实 validator 和测试，不能提交空壳管道。
- MediatR telemetry 必须复用现有 OpenTelemetry / structured logging，只记录 request type、kind、耗时、结果和异常类型；不得记录 prompt 全文、SQL、token、连接串、API key 或业务数据明细。
- 事务/审计拥有者必须显式且唯一：repository 保存的业务+审计原子性由 `AuditTransactionCoordinator` 拥有，Identity 用户/角色事务由 `ITransactionalExecutionService`/`EfTransactionalExecutionService` 拥有；MediatR behavior 不得开启事务、保存审计、调用 `SaveChangesAsync` 或包裹 stream/handler 形成隐式事务边界。

## Unified Agent Workflow

- `AgentWorkflowPipeline` 是 AICopilot 用户输入的统一工作流主干；当前旧名 `ChatWorkflowOrchestrator` 只可作为待消歧历史名，不代表“仅聊天可用”。
- Chat 模式和 Plan 模式都必须复用统一管线的意图理解、上下文编排和能力发现；两者只能在出口行为不同。
- Chat 出口可以按现有安全策略直接生成回答，或执行已允许的低风险只读动作。
- Plan 出口只能生成 `PlanDraft` 计划草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。
- `PlanAgentTaskCommand` 只能负责计划草案/任务状态的持久化和编排入口，不得独立实现意图理解、工具发现、Skill 选择或 Tool catalog 强校验。
- Skill、Tool、MCP 或 DataSource 未匹配时，不得阻断 `PlanDraft` 生成；只能在草案里说明能力缺口或要求用户补充目标。
- 用户确认 `PlanDraft` 后，才允许转换为 `ExecutablePlan` / `AgentTask`，并进入 Skill、Tool、Schema、Guard、审批和 Worker 执行链路。
- Agent workflow 的阶段和并行分支必须由 `AgentWorkflowTopology` 显式声明；`Tools`、`Knowledge`、`DataAnalysis`、`BusinessPolicy` 四个分支必须保持 `Task.WhenAll` + `AgentWorkflowSink` fan-out/fan-in 模式，不得为了“管道化”拍平成串行或为新能力另起一条孤立链路。

## Capability Boundaries

以下能力必须分开：

- Intent routing。
- RAG 知识检索。
- DataAnalysis / Text-to-SQL。
- MCP 工具执行。
- Human-in-the-loop 审批。

不能因为实现方便把它们合成一个大 agent 或大 service。跨能力改动前必须说明边界影响。

## Safety

- Preview/prerelease NuGet 包默认禁止。
- 已知漏洞依赖默认阻断。
- Known-vulnerable dependencies are forbidden; NU190x and npm audit findings must be handled before acceptance.
- 禁止硬编码 API key、token、secret、license、provider credential、数据库凭据、MCP 凭据。
- AICopilot 生产 Docker 构建必须使用 Harbor mirror 的基础镜像，包括 .NET ASP.NET runtime、Node、Nginx、PostgreSQL、RabbitMQ 和 Qdrant；workflow、Dockerfile 和应急构建脚本不得默认从 Docker Hub 或 MCR 拉生产基础镜像。
- AICopilot 生产发布标准路径是本机构建镜像、推送 Harbor、再通过 SSH 触发服务器 `deploy-release.sh`；`aicopilot-image` / `aicopilot-deploy` 只允许作为带确认词的灾备入口。单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续等待 GitHub Actions 或 shell 命令自然超时。
- 模型、prompt、plugin、MCP server、approval threshold 等运行行为优先用配置或明确存储数据，不藏在代码里。
- 容器部署必须显式配置并挂载可写的 `FileStorage:RootPath` 和 `ArtifactWorkspace:RootPath`；不得依赖容器内 `LocalApplicationData` 默认路径或 `/app` 目录写入运行产物。

## Execution

改动前确认：

- 触碰能力：routing、RAG、SQL analysis、MCP、approval、host wiring、frontend、persistence。
- 是否只在 `AICopilot` 内。
- 是否会暗示 Cloud 或 Edge 也要改。
- 是否涉及 Cloud 业务写入边界。

业务不清楚时先问，不猜。

## Code Quality

- 简单数据转换、过滤、投影、分组默认优先使用 LINQ 表达业务意图，尤其 `IQueryable` 必须把过滤、投影、排序和分页下推到数据库。
- 性能热路径、状态机、解析器、流式枚举、数组/`Span<T>` 紧循环允许使用 `for`/`foreach`；不能为了“看起来函数式”牺牲清晰复杂度或引入额外分配。
- 禁止先 `ToList()` / `ToArray()` 再 `Where()` / `Select()` 做本可下推或延迟执行的过滤投影；确需多次遍历时必须显式物化一次并说明原因。
- 真正要拦的是重复枚举、N+1 查询、O(n²) 嵌套、重复扫描和错误数据结构；CA1851 先保持 warning，修完基线后再考虑升为 error。
