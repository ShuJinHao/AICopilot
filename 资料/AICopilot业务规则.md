# AICopilot 业务规则

本文档约束 `AICopilot` 自身业务边界。工作区总规则见 `../../docs/总规则.md`。

## 0. 改动收口门禁

- 修改 `AICopilot` 代码前，必须先读工作区 `AGENTS.md`、`../../docs/总规则.md`、项目 `AGENTS.md`、本文档、相关专题契约、相关源码、相关测试和近期 git/GitHub 历史。
- 已验收功能默认冻结；不能因为局部重构、测试修复、文档整理或 UI 调整顺手改变已有 AI 工作流、Cloud 只读边界、审批边界、工具边界、会话状态或错误契约。
- 每次代码改动完成前，必须更新项目滚动复盘文档 `../docs/改动复盘与规则沉淀.md`，最新记录放在最前。
- 每次复盘必须写明改动范围、改动原因、影响面、验证命令、验证结果和规则提炼结论。
- 必须判断是否形成长期规则；有则写入项目 `AGENTS.md`、本文档、专题契约或工作区长期规则，无则在复盘中写明“无新增长期规则”及原因。
- 新增、删除或重命名后端错误码时，必须同批更新 `docs/frontend-integration-contract-package-2026-05-17.md`，并运行 `ErrorCodeCatalogTests`，确保前端错误契约不漂移。
- 大范围架构、管道、权限、工作流或契约改动不得只以 filtered tests 作为完成依据；全量 `AICopilot.BackendTests` 未跑、CI 全量未确认或环境依赖失败时，最终回复和复盘必须明确标注。
- 最终回复必须列出复盘文档、规则沉淀位置和验证命令；缺任一项，不得称为完成。
- 默认只更新项目滚动复盘文档，不为每个任务新增单独流水文档；只有形成可长期复用的业务或技术契约，才新增专题文档。
- `资料/` 保留为 AICopilot 业务规则入口；执行复盘和改动沉淀流水统一放在项目 `docs/`。

## 1. 核心职责

`AICopilot` 是分析助手和受控编排系统，不是制造业务主系统。

`AICopilot` 只承担 AI 助手和受控编排能力：

- 意图路由：判断请求进入聊天、知识检索、数据分析或工具链。
- RAG：基于文档和规则做问答、解释、总结。
- DataAnalysis / Text-to-SQL：基于只读数据源做查询、统计、分析。
- MCP 工具执行：只执行已配置、已授权、符合安全边界的工具。
- Human-in-the-loop：控制 AICopilot 自身高风险动作。

`AICopilot` 不是 Cloud 制造主数据系统，不是 Edge 现场运行系统。

## 2. Cloud 只读边界

允许：

- AICopilot 对 `IIoT.CloudPlatform` 只能读取数据和规则。
- 读取已批准范围内的 Cloud 规则、接口说明、业务文档和只读数据。
- 分析、解释、汇总、检索、趋势判断和异常说明。
- 生成建议、草稿或排查思路。

禁止：

- 注册、修改、删除设备。
- 创建、修改、删除人员、角色、权限。
- 读取或修改未批准的配方主数据、设备配方清单、配方详情或配方版本。
- 写入、补录、删除或修正产能、日志、生产数据、过站数据。
- 触发 Cloud 业务流程、派发任务或代办审批。
- 直接写 Cloud 数据库。
- 直接写云端数据库。
- 通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器间接调用 Cloud 写接口。
- 通过 MCP、Tool、Agent workflow、后台任务或隐藏适配器间接调用云端写接口。

Human-in-the-loop 不能把禁止的 Cloud 业务写入变成允许动作。
Human-in-the-loop 不能作为放开云端业务写入的理由。
当前默认不存在专门给 AICopilot 使用的云端写 API。

Cloud AiRead 设备契约：

- `deviceId` 是正式 Cloud 设备身份参数，用于产能、日志、过站记录等业务读取。
- `deviceCode`/`ClientCode` 只用于设备查询、展示或 bootstrap 寻址，不得作为 `deviceId` 发送。
- 需要从自然语言里的设备编码定位设备时，必须先走显式设备查询/解析；无法唯一命中时要求用户补充，不做隐式兼容。
- AICopilot 的 Pilot 场景参数不得直接透传给 Cloud；只有 Cloud 端点真实声明的参数可以进入请求。

## 3. OIDC 身份边界

- Cloud OIDC 只解决身份、账号有效性、员工有效性。
- AICopilot 保留本地 AI 用户、AI 角色、AI 权限、SecurityStamp、本地禁用、审计和 emergency admin。
- Cloud role 不直接映射 AI role。
- AICopilot 不读取 Cloud Cookie、不接收 Cloud 密码、不直连 Cloud 用户表。
- EdgeClient 不参与 Cloud-AICopilot OIDC 身份对齐。

## 4. RAG 规则

- RAG 只能用于知识检索、规则解释和文档问答。
- 文档内容不能反向覆盖 Cloud 已确认业务规则。
- RAG 结果与 Cloud 规则冲突时，以 Cloud 规则为准，并报告冲突。

## 5. DataAnalysis 规则

- DataAnalysis 只能连接只读业务数据源。
- SQL 必须经过只读 guardrail。
- 查询结果只用于分析展示，不产生业务写入。
- 不能为了分析便利放宽 `MaxRows`、read-only session 或 SQL 安全检查。
- 内部开发直连真实 Cloud PostgreSQL 时，只能注册为 DataAnalysis `CloudReadOnly` 只读业务数据源，必须使用已验证只读数据库账号，并绑定白名单表字段和只读 SQL guard。
- 内部真实 Cloud 语义查询优先走 DataAnalysis `CloudReadOnly` Direct DB 映射；Cloud AiRead 只作为未来外部系统只读 API 接入口封存，不能在内部映射存在时压过 Direct DB。
- CloudReadOnly 探索型 Text-to-SQL 只能作为强语义 intent / Direct DB SQL 失败后的受控 fallback；不得拆分或重命名既有 `Analysis.*` intent，不得用 fallback 压过已可执行的强语义路径。
- CloudReadOnly Text-to-SQL fallback 必须使用共享白名单 schema、已验证只读凭据、LLM 结构化生成、SELECT-only SQL guard、BusinessQuery safety policy 双层表/列白名单、只读执行、最多受控修复重试和 hash-only 审计；生产 fallback 不得退化为规则 SQL 模板。
- CloudReadOnly Text-to-SQL LLM prompt 可见的物理 schema 只能来自 `CloudReadOnlyGovernedSchema` 治理白名单，最多包含批准表名、列名、列类型、join hints 和必要业务描述；不得把连接串、凭据、role/权限细节、样例数据、查询结果、参数值、非白名单表字段或系统/敏感字段发给模型。
- CloudReadOnly Text-to-SQL 修复重试默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- CloudReadOnly Text-to-SQL 修复历史不得保存完整 SQL、用户 prompt、连接串、参数值或敏感字段；上一轮失败 SQL 只允许在当前调用内以内存参数临时回传给 LLM 生成下一版，不能写入审计、日志、state、结果或持久化对象。
- Legacy `DataSourceSelectionMode.TextToSql` / Business Text-to-SQL draft runtime 仍只服务 SimulationBusiness；Agent 侧 CloudReadOnly 查询只能走 governed fallback runner，不能复用会生成 SimulationBusiness SQL 的旧 draft runtime。
- Direct DB 语义映射中的工序名来自只读 `mfg_processes.process_name`；新增 join 表必须同步进入 `CloudReadOnlyGovernedSchema` 表/列/类型/join hint、SQL guard、BusinessQuery safety schema、只读 role 授权 SQL、授权探针、部署 preflight、RealSource 模板、架构测试和部署文档。
- DeviceLog 语义查询必须使用真实 Cloud PostgreSQL 日志级别枚举值 `ERROR`、`WARN`、`INFO`，不能生成 `Error`/`Warn`/`Info` 这类大小写不匹配条件。
- 用户要求“错误警告”“异常分析”“分析错误信息”等场景时，DeviceLog 必须支持 `ERROR + WARN` 多级别只读查询；不能只查 `ERROR` 后把 `WARN` 推断为没有。
- DeviceLog 自然语言中的工序/设备范围必须落到只读 `devices` / `mfg_processes` join 暴露的业务字段过滤；不得让最终回答模型按文字自行猜测设备、工序或范围。
- 用户追问其他日志级别、工序、设备或时间窗口时，意图路由/数据分析执行层必须重新生成并执行对应 `Analysis.DeviceLog.*` 查询；Final Agent 只能总结本轮查询结果，不能基于上一轮回答文本推断未查询级别“有/没有”。
- DataAnalysis 最终上下文必须携带本轮查询执行事实，包括语义 target/kind、filters、timeRange、limit、returnedRowCount 和证据边界；最终回答必须先核对执行事实再输出结论。
- DeviceLog 数据分析展示块和 Widget 只能从本轮只读查询返回行、`query_execution` 和 `semantic_summary` 派生；级别分布、时间分布、问题关键词分类、指标和证据表不得由模型编造、Markdown 解析、前端假数据或任意图表配置生成。
- DeviceLog 最终回答使用 `display_blocks` 时必须按“结论、关键指标、关键记录、可能原因、建议动作、不能直接执行的动作、查询范围”组织；可能原因必须标注为 AI 推断分析，建议动作只能是人工排查建议，不能写成已执行的控制、下发、写入或修复动作。
- Direct DB 设备 `status` 字段当前是最新 `device_logs.level`，对用户展示必须称为“最新日志级别”，不得包装为实时在线/运行状态。
- 真实 Cloud Text-to-SQL 验证不得走 Simulation 数据源冒充真实结果；Simulation 只能用于明确标识的模拟链路。
- 创建或轮换 Cloud PostgreSQL 只读账号只能通过显式确认的受控自动化执行；只能创建/更新专用 readonly role，只授予白名单表 SELECT，不得授予写权限、schema create 权限、superuser、createdb、createrole 或 replication。
- Cloud PostgreSQL readonly role 授权的权威载体是 `deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和 `check-readonly-grants.sql`；生产只允许对治理白名单表做显式表级 `GRANT SELECT`，不得使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
- 启用 CloudReadOnly 直连数据库时，部署必须先执行 readonly 授权 preflight；权限错误只能向用户暴露治理白名单内的表名和只读权限不足结论，不得输出连接串、role、密码、SQL 原文或非白名单对象。

## 6. MCP 规则

- MCP 是受控工具入口，不是 Cloud 业务写入口。
- 工具描述必须说明是否只读。
- 涉及文件、外部系统、命令执行或其他副作用的工具必须保持审批约束。
- 不允许配置直接或间接调用 Cloud 写接口的 MCP 工具。

## 7. Human-in-the-loop 规则

- Human-in-the-loop 是 AICopilot 自身高风险动作的安全闸门。
- 它不能覆盖 Cloud 业务只读规则。
- 若未来允许调用 Cloud AI-facing API，审批规则必须与 Cloud 权限、Cloud 审计和接口契约一起设计。

## 8. 对话产品规则

- 主产品形态是 Codex-like 对话流，不是任务控制台、试点运营台或系统调试台。
- 普通用户默认只看到用户问题、AI 最终回答、Plan/Goal 摘要、审批卡和结果卡。
- 模型名、路由模型、意图置信度、工具调用、工具参数、运行事件、中间步骤和风险细节默认折叠到运行详情。
- 运行详情只能基于本轮 stream/history chunks、消息 metadata 和按会话隔离的运行状态生成安全摘要；不得作为审批、工具执行、AgentTask、Cloud 查询或 Widget 的权威状态源。
- 运行详情不得展开 SQL 原文、连接串、密码、token、sourceName、表/视图名、endpoint、内部字段、原始工具结果行或未脱敏错误原文；工具参数和结果只能展示白名单业务过滤条件、查询次数、返回行数、截断状态、Widget 类型等安全事实。
- DeviceLog 固定段落最终回答可以在前端渲染为结构化结果卡，但只能重排已有回答文本；不得新增指标、补未查询数据、改写模型结论或把普通回答强行套成 DeviceLog 结果页。
- AI 对话中任何可能超过 1 秒的工具调用、DataAnalysis 或 Cloud 只读查询，必须有用户可见、按会话隔离的运行状态；状态只能来自本轮 stream/request/chunk/error/complete 执行事实，不得用假进度、假查询次数或假返回行数填充。
- 前端必须完整展示后端错误契约中的 `code`、`detail`、`userFacingMessage` 和失败类 `AgentEvent` 详情；不得用泛化文案覆盖真实诊断信息。
- 前端会话级 Agent 运行状态必须按会话隔离；新建会话、切换会话和切换 Plan/Chat 模式时不得残留其他会话的任务、错误、工作区、产物或上传文件。
- 模型推理标签例如 `<mm:think>`、`<think>` 或裸 `mm:think` 不得出现在用户可见正文；如保留，只能进入默认折叠的运行详情。
- `render_payload_json` 只能恢复稳定消息内容，例如文本、图表或错误结果；不得作为审批、工具调用、意图识别或运行状态的权威来源。
- 审批、AgentTask、AgentStep、Artifact 和 Workspace 的当前状态必须从各自权威聚合读取，并通过 `message_events` / session timeline 投影进入对话流。
- 历史消息刷新不得把 `Intent`、`FunctionCall`、`FunctionResult`、`ApprovalRequest` 或 `Metadata` chunk 作为普通消息重新摊开。
- 开发阶段已物理删除 Trial/Pilot/Production Readiness 运营线；后续不得把旧试点运营能力重新接回普通产品导航。

### 8.1 统一工作流与 Plan 模式语义

- AICopilot 的用户输入必须经过统一工作流主干：意图理解、上下文编排、能力发现，然后按模式选择出口。
- `AgentWorkflowPipeline` 是统一工作流主干；旧名 `ChatWorkflowOrchestrator` 是历史命名歧义，不得被理解为聊天模式专属基建。
- Chat 模式和 Plan 模式的区别只在出口，不在管线：Chat 出口直接回复或按安全策略执行低风险只读动作；Plan 出口只生成计划草案。
- `PlanDraft` 是 AI 对用户目标的理解、路线规划、能力说明和待确认步骤；它不是可执行任务，不得入队 Worker。
- `ExecutablePlan` 是用户确认 `PlanDraft` 后生成的可执行计划；此时才允许做 Skill、Tool、Schema、Guard 和审批校验。
- `AgentTask` 是真正进入执行阶段的任务对象；它必须来自已确认的 `ExecutablePlan` 或等价确认状态。
- 用户确认前，Plan 模式禁止 Cloud 业务查询、MCP 工具执行、Tool 执行、DataAnalysis 真实查询、Worker 入队和任何会产生业务副作用的动作。
- Skill、Tool、MCP、Knowledge 或 DataSource 未匹配时，不能阻断 `PlanDraft`；应在草案中说明能力缺口、降级为路线规划或要求用户补充业务对象。
- 执行阶段失败后的重试应基于已确认的 `ExecutablePlan` / `AgentTask` 重新入队，不应重新生成 `PlanDraft` 或丢失用户确认。

## 9. 文档入口

- 长期规则入口只保留 `AGENTS.md`、本文档、项目 `docs/改动复盘与规则沉淀.md` 和工作区 `docs/历史核心记录.md`。
- 部署入口只保留 `AICopilot 项目部署与维护指南.md` 和 `deploy/enterprise-ai`。
- 阶段计划、批次验收报告、PR 草案和一次性 acceptance 输出不得继续作为执行入口；有效结论必须沉淀到长期规则或部署指南后再清理。
- 清理文档时必须先检查引用，避免留下指向已删除阶段文件的脚本、测试或说明。
- 旧的 Simulation/Real/Sandbox/Pilot 阶段说明只可作为历史材料，不得覆盖当前部署指南和生产验收口径。

## 10. 工程边界

- `AiCopilotDbContext` 是主基础设施迁移上下文，`AuditDbContext` 负责审计查询和运行时审计写入，`DataAnalysisDbContext` 只承载数据分析配置，`OutboxDbContext` 承载 outbox。
- 审计写入必须遵守 Audit writer decision tree：有业务保存点的命令应把业务变更和审计行放在同一事务；`auditLogWriter.SaveChangesAsync` 只允许出现在没有业务保存点且已被白名单记录的执行路径。
- Outbox 多实例调度必须使用 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略，不能让多 worker 重复发布同一消息。
- MCP runtime 配置变更必须进入 runtime registry refresh cycle，禁用、删除或配置变更后不能继续暴露未来工具解析。
- 身份安全以 security stamp 驱动会话失效；Cloud role 不直接成为 AICopilot 本地 role。
- 多 DbContext 迁移历史必须通过 `__EFMigrationsHistory` 的上下文隔离或迁移历史表拆分规则治理，不能让单一上下文回滚污染其他上下文状态。
- 新增或接线 `IStreamPipelineBehavior` 后，必须核对所有公开 `IStreamRequest` 的 `AuthorizeRequirement`，测试种子角色必须覆盖对应权限；无权限场景应返回干净 401/403，不能表现为 SSE 已写 200 后断流。
- 简单集合转换默认优先用 LINQ 表达意图，`IQueryable` 必须优先下推过滤、投影、排序和分页；热路径、状态机、流式枚举、数组/`Span<T>` 紧循环允许 `for`/`foreach`。
- 工程质量门禁优先抓重复枚举、先物化再过滤、N+1 查询、O(n²) 嵌套、重复扫描和错误数据结构；CA1851 先作为 warning 运行，基线清理后再考虑升级为 error。
