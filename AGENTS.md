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

## Topic Contracts

- AI 修复总计划和逐项治理清单：`docs/AI架构治理清单.md`。
- 部署安全、HTTP-only、secret、镜像、SSH 和 runner 边界：`docs/AICopilot安全部署契约.md`。
- Cloud 只读、Cloud AiRead、CloudReadOnly Direct DB 和 Text-to-SQL 边界：`docs/Cloud只读数据分析契约.md`。
- Agent workflow、Plan/Chat、MCP/Tool/Human-in-the-loop、异常和前端错误边界：`docs/Agent工作流与异常契约.md`。

触碰对应能力时必须先读相应专题契约；专题契约与本文档冲突时，先停止并修正文档口径，不得按印象继续改代码。

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
- AICopilot `CloudAiReadEndpointPolicy` 和 `ICloudAiReadClient` 必须逐项覆盖 Cloud `AI只读接口契约.md` 已批准的正式 `GET /api/v1/ai/read/*` 表面；不能只接高频端点后宣称 Cloud-AI 接口已全部对齐。
- Cloud AiRead 客户端只允许八个正式 typed GET，不得暴露任意 method/path 传输、可配置 POST allowlist、legacy adapter 或双轨接口；`production-records` 正式工序/工位字段缺失时必须保持空，不得用 `typeName` 或 `typeKey` 代填。
- Cloud 只读读取只能向 Cloud 发送真实端点参数；`scenarioId`、`from`、`to`、`pilotWindowId`、`boundary` 等试点元数据不得透传给 Cloud。
- 内部开发允许通过 DataAnalysis `CloudReadOnly` 只读数据源直连真实 Cloud PostgreSQL 做 Text-to-SQL 验证；必须使用已验证只读数据库账号、白名单表字段和只读 SQL guard，不得写 Cloud 业务数据，也不得用 Simulation 冒充真实数据。
- `Device`、`DeviceLog`、`Capacity`、`ProductionData`、`Process`、`ClientRelease` 六类已覆盖正式 `Analysis.*` 语义必须只走 Cloud AiRead 八个 typed GET；Cloud 关闭、空集、400/401/403/429/5xx、timeout 或非法 JSON 均不得回退 Direct DB、Text-to-SQL、Simulation、MCP 或隐藏适配器。
- CloudReadOnly Direct DB / Text-to-SQL 只服务上述正式语义覆盖外的低频自由探索和治理白名单内补充分析；探索型 Text-to-SQL 只能在这类未覆盖问题或其 Direct DB SQL 失败后受控 fallback，不能执行、拆分、重命名或旁路任何已覆盖 `Analysis.*` intent。
- CloudReadOnly Text-to-SQL fallback 必须同时满足已验证只读凭据、共享白名单 schema、LLM 结构化生成、SELECT-only SQL guard、BusinessQuery safety policy 双层表/列白名单、只读执行和 hash-only 审计；生产 fallback 不得退化为规则 SQL 模板。
- CloudReadOnly Text-to-SQL LLM prompt 可见的物理 schema 仅限 `CloudReadOnlyGovernedSchema` 批准的表名、列名、列类型、join hints 和必要业务描述；不得暴露连接串、凭据、role/权限细节、样例数据、查询结果、参数值、非白名单表字段或系统/敏感字段。
- CloudReadOnly Text-to-SQL 修复重试默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- CloudReadOnly Text-to-SQL 修复历史不得保存完整 SQL、用户 prompt、连接串、参数值或敏感字段；上一轮失败 SQL 只允许在当前调用内以内存参数临时回传给 LLM 生成下一版，不能写入审计、日志、state、结果或持久化对象。
- Legacy `DataSourceSelectionMode.TextToSql` / Business Text-to-SQL draft runtime 仍只服务 SimulationBusiness；Agent 侧 CloudReadOnly 查询只能走 governed fallback runner，不能复用会生成 SimulationBusiness SQL 的旧 draft runtime。
- Direct DB 语义映射如需展示工序名，只能通过只读 join `mfg_processes.process_name`；新增 join 表必须同步更新 `CloudReadOnlyGovernedSchema` 表/列/类型/join hint、SQL guard 白名单、BusinessQuery safety schema、只读 role 授权 SQL、授权探针、部署 preflight、RealSource 模板、架构测试和部署文档。
- DeviceLog 语义查询的日志级别必须使用 Cloud PostgreSQL 真实枚举值 `ERROR`、`WARN`、`INFO`；“错误+警告/异常分析”必须生成多级别只读查询，不能只查单一 `ERROR` 后推断整体无异常。
- DeviceLog 查询涉及工序或设备自然语言范围时，必须走只读 `devices` / `mfg_processes` join 暴露的业务字段过滤；不得只靠最终回答模型从文本中猜测范围。
- DataAnalysis 追问其他日志级别、工序、设备或时间窗口时，必须在路由/执行层重新生成并执行 `Analysis.DeviceLog.*` 意图；Final Agent 只能总结本轮 `query_execution` 证据，不能基于上一轮回答文本推断未查询级别“有/没有”。
- DataAnalysis 最终上下文必须携带本轮查询执行事实，包括语义 target/kind、filters、timeRange、limit、returnedRowCount 和 evidence boundary；最终回答必须先核对这些事实再下结论。
- Cloud PostgreSQL readonly role 授权的权威载体是 `deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和 `check-readonly-grants.sql`；生产只允许对治理白名单表做显式表级 `GRANT SELECT`，不得使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
- CloudReadOnly 直连数据库启用时，服务器 `deploy-release.sh` 必须先运行 `scripts/check-cloud-readonly-grants.sh` 做 fail-fast preflight；权限错误对用户可见诊断只能暴露治理白名单表名，不得输出连接串、role、密码、SQL 原文或非白名单对象。
- `Analysis.Device.List/Detail` 只表达 `/devices` 的设备主数据；`Analysis.Device.Status` 只表达 `/device-client-states` 的 Cloud 权威 `softwareStatus`、运行心跳原值和唯一 freshness 时间。无心跳设备返回 `MissingRuntimeHeartbeat` 行；仅 `asOfUtc - lastRuntimeHeartbeatAtUtc > 24h` 为 `RuntimeHeartbeatStale`，恰好 24 小时不 stale；Stale 不得翻译为 Offline/Stopped。状态空集只表示授权范围内无匹配设备，最新日志级别只属于 `Analysis.DeviceLog.*`。
- `Analysis.Process.List/Detail` 只能读取 Cloud `/processes`：支持 `processId` 精确过滤以及 `keyword/processCode/processName` 搜索；详情必须在未截断结果中唯一精确命中，`processId` 必须作为正式 GUID 参数发送、不得冒充 keyword，也不得回退其他数据源。
- `Analysis.ClientRelease.List` 只能读取 Cloud `/client-releases`，只允许 `channel/targetRuntime/status/includeArchived` 正式过滤条件；版本、hash、下载地址、发布说明和发布状态只能来自 Cloud 返回，不得由模型补写，也不得回退 Direct DB、Text-to-SQL 或 Simulation。
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
- 聚合根边界以 `docs/DDD聚合根边界.md` 为长期技术契约；新增或调整 `IAggregateRoot<>`、`IRepository<T>` 注册、EF `DbSet`、Agent runtime timeline/queue/audit 记录前必须先核对该文档和架构测试。
- `IAggregateRoot<>` 只用于能独立维护业务不变量和生命周期的领域根；队列项、timeline 投影、工具执行审计、worker 心跳、执行尝试记录不得作为新增聚合根方向，历史债务只能减少不能增加。
- `DataSourcePermissionGrant` 当前保留为独立聚合根但标记待评估；后续如果能完全归入 `BusinessDatabase` 生命周期，应下沉为子实体或专用权限记录并移出白名单。
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
- Cloud 只读 Agent 当前正式能力覆盖 `Analysis.Device.List/Detail/Status`、`Analysis.DeviceLog.Latest/Range/ByLevel`、`Analysis.Capacity.Range/ByDevice`、`Analysis.ProductionData.Latest/Range/ByDevice`、`Analysis.Process.List/Detail` 和 `Analysis.ClientRelease.List`；`Analysis.Capacity.ByProcess` 尚未形成正式聚合契约，不得宣称可用。全部已覆盖能力必须复用统一语义定义和唯一 Cloud AiRead 实现，不能另起隐藏查询链、伪造返回或降级到其他数据源。
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
- HttpApi JWT 配置必须通过唯一运行时校验入口 fail-fast：Issuer、Audience 非空，SecretKey 至少 64 字符，token lifetime 大于 0；部署脚本校验不能替代运行时校验，错误不得回显 secret。
- AICopilot 生产 Docker 构建必须使用 Harbor mirror 的基础镜像，包括 .NET ASP.NET runtime、Node、Nginx、PostgreSQL、RabbitMQ 和 Qdrant；workflow、Dockerfile 和应急构建脚本不得默认从 Docker Hub 或 MCR 拉生产基础镜像。
- AICopilot 生产发布标准路径是本机构建镜像、推送 Harbor、再通过 SSH 触发服务器 `deploy-release.sh`；`aicopilot-image` / `aicopilot-deploy` 只允许作为带确认词的灾备入口。单镜像 build/push 默认 15 分钟超时，Harbor 检查默认 2 分钟超时，SSH deploy 默认 30 分钟超时；超时必须停止并诊断 Docker buildx、Harbor tag、服务器 compose/logs 和 release 状态，不得继续等待 GitHub Actions 或 shell 命令自然超时。
- 工作区根 `deploy/Deploy.ps1 -Target AICopilot` 是日常应用唯一对外入口；正式发布从 fresh `origin/main` tip 创建 detached worktree，不要求或修改本地脏工作树。每次只构建选中服务、生成不可变 OCI 请求并向稳定 Runner 投递一次；`Auto` 在 SSH TCP 不可达时使用 `aicopilot-routine-request.yml` self-hosted Runner。后端服务自动包含 migration；`Invoke-WorkspaceDeploy.ps1` 仅用于部署基础设施维护和旧事务诊断。
- 稳定服务器 Runner 只能独立安装/升级，日常应用发布不得同步 Compose 或 support files；migration 前必须备份 PostgreSQL，常驻服务失败必须回滚镜像，cleanup/GC/深度 attestation 不得阻塞健康应用发布。
- AICopilot support sync 必须包含 compose 并使用 staging + SHA256 + reservation token；support install 与 release 必须绑定同一全局锁和 digest，禁止同步 `.env`、release state、锁和备份。持久状态变更前失败必须恢复 `.env` 与 release state；active/stale lock、普通退出码、真实 timeout `124`、信号退出和同 SHA 健康幂等必须通过行为测试。
- 正式 release/no-op 必须同时绑定 workspace plan/profile digest、完整 Git SHA、显式服务闭包、support/services/image manifest、应用 immutable OCI digest、服务器 `.env` canonical fingerprint 和真实运行容器身份。全局配置 fingerprint 漂移时禁止部分服务发布；HttpApi/DataWorker/RagWorker 必须显式闭包 migration。常驻容器必须 Running、非 Restarting、非 OOM、RestartCount 稳定，已有 Docker Health 时必须 healthy，才允许提交或 no-op。
- support、compose、release state、PostgreSQL/RabbitMQ/Qdrant 和全部常驻 runtime 必须作为同一恢复事务验收；基础设施回滚必须使用事务前冻结的真实 RepoDigest/runtime image id，不能重新解析可变 tag。恢复或阻断证据落盘不确定时必须返回 `86` 并保留永久 blocked/backup 证据；reservation adopt/cancel 必须是原子 transition，SSH 断联后必须按 invocation token 对账，active/unknown 返回 `87` 且禁止取消、重试或猜测成功。
- AICopilot 当前内网生产部署红线是 HTTP-only：不得把 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 OIDC HTTPS metadata 强制校验列为当前修复门槛；后续如需切换 HTTPS，必须由用户单独批准传输层方案和证书来源。HTTP 部署下只能使用兼容 HTTP 的安全加固：内网隔离、端口收敛、同源代理、CORS 白名单、短期 token、不在 URL/日志/审计中暴露 secret、非 root 容器、强 secret、API 只读边界和除 HSTS 外的安全响应头。Cloud OIDC 使用 HTTP issuer 时必须显式启用内网 HTTP OIDC，只允许 loopback、私网 IPv4 或保留内网 DNS 后缀（`.internal.example`、`.internal`、`.lan`、`.local`），公共 HTTP 域名仍必须拒绝。
- AICopilot Web 到 HttpApi 的标准生产路径必须走 nginx 同源 `/api/` 反代；HttpApi CORS 默认不开放跨源。确需浏览器直连后端时，只能配置 `Cors:AllowedOrigins` 中的精确 http/https origin，禁止 `*`、通配子域、带 path/query/fragment 的 origin 或运行时任意放行。
- AICopilot 部署模板、文档示例、滚动复盘、历史诊断记录、workflow 默认值、脚本默认值、migration seed 和 fresh DB seed 不得写入真实内网 IP、弱 secret、`CHANGE_ME`、`dummy-key` 或默认 `root@` 发布目标；真实地址、token 和密码只能来自服务器 `.env`、GitHub secrets、管理员录入或命令行环境变量。root SSH 只允许显式设置 `ALLOW_ROOT_SSH_DEPLOY=true` 的应急路径，不得作为标准发布口径。
- 如果当前真实部署根目录、稳定 Runner、Docker Root Dir、基础设施维护目标、工作区入口参数或标准部署用户与模板不同，必须先更新工作区 `deploy/Deploy.ps1`、`deploy/Invoke-WorkspaceDeploy.ps1`、`deploy/profiles/*.json`、`AICopilot 项目部署与维护指南.md`、`deploy/enterprise-ai/README.md` 和工作区部署总览，再允许继续改脚本或发布。
- 如果当前 `AICopilot` 与 `Cloud` 共用同一台生产宿主机，必须在工作区总入口明确写出共享宿主机事实、共享标准发布人和两个独立部署根；不得把同机双部署根问题写成两套互不相关的环境。
- root 应急路径一旦写入 `releases/*`、`current-release.summary.md` 或 deploy support files，关闭任务前必须恢复 owner/mode，并重新验证标准 non-root `./deploy-release.sh --validate-only`；不得留下 root-owned 状态文件后直接收口。
- 模型 smoke 的 `AICOPILOT_MODEL_SMOKE_API_KEY=dummy-key` 只允许作为真实模型网关的显式兼容例外，必须同时设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true` 或手工 smoke 命令传 `--allow-dummy-key`；默认 preflight 必须拒绝该弱值。
- AICopilot 模型、Embedding 和 endpoint pool 覆盖 API key 的受保护格式必须使用 `encv2:` AES-GCM；旧 `encv1:` 只能通过 migration worker 或一次性迁移命令重加密，不得在运行时 provider 中继续作为正常解密格式兼容。`AiRuntime:ProviderReliability` endpoint `ApiKey` 和 `ApiKeyEnvironmentVariable` 指向的环境变量值也必须存 `encv2:` 密文，不能放明文。
- 私有模型生产播种只能从服务器真实 `.env` 或本机私密部署手册读取真实值；仓库默认只能使用 `model.internal.example` 占位 URL、空 API key 和禁用状态。新库 seed 的私有模型 context window 固定按 64k（`65536`）口径配置，真实 base URL、API key 和是否启用由 `AICOPILOT_PRIVATE_MODEL_*` 环境变量控制，API key 入库前必须加密为 `encv2:`。
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
