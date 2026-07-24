# AICopilot 业务规则

本文档约束 `AICopilot` 自身业务边界。工作区总规则见 `../../docs/总规则.md`。

## 0. 改动收口门禁

- 工作区 `../../docs/总规则.md` 是唯一默认必读入口。进入 AICopilot 实际修改后，只读取本文档与本批模块相关的章节、相关源码和受影响测试；专题契约按边界触发，近期 git/GitHub 历史按回归或故障追溯条件读取。
- 只有形成长期规则、修复历史回归、处理生产事故或改变部署机制时，才更新 `../docs/改动复盘与规则沉淀.md`；普通业务修改和测试同批调整不写流水。
- 形成长期约束时直接写入本文档、专题契约或工作区总规则；项目 `AGENTS.md` 只保留按需路由和少量不可缺失的项目硬边界，不作为第二份详细规则库。
- 新增、删除或重命名后端错误码时，必须同批更新 `docs/frontend-integration-contract-package-2026-05-17.md`，并运行 `ErrorCodeCatalogTests`，确保前端错误契约不漂移。
- 默认只运行 Architecture/Security 与 owner 映射选出的受影响 Business；全量、coverage、mutation、duplication 和 CrossProject 只在用户明确要求时运行。
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

模型、prompt、plugin、MCP server、approval threshold 等运行行为优先使用配置或明确存储数据，不得隐藏在业务代码常量中。

系统已进入生产模式。当前正式生产工序只有 `cp / 正极模切` 与 `ap / 负极模切`；Cloud 是 AI 唯一真实生产数据源，AICopilot 全程只读。测试/示例工序、Simulation 数据和模型推断不得冒充生产事实。

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

Cloud tool 安全元数据只有 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 这一精确组合有效。`Diagnostics`、`LocalSuggestion`、`SideEffecting`、缺失/动态无法静态证明的声明都必须 fail-closed；动态 MCP 的 enum、alias、描述或 endpoint 不能证明可信 NonCloud，server/tool 必须统一使用精确只读组合，并在聚合注册、runtime builder、Plan 能力发现和每次 MCP 执行时经过同一 `AiToolSafetyPolicy` 评估。runtime MCP tool 必须显式携带独立 canonical `ToolName`，缺失时直接阻断，禁止回退到 runtime `Name` 或其它 alias。

Human-in-the-loop 不能把禁止的 Cloud 业务写入变成允许动作。
Human-in-the-loop 不能作为放开云端业务写入的理由。
当前默认不存在专门给 AICopilot 使用的云端写 API。

Cloud AiRead 设备契约：

- `deviceId` 是正式 Cloud 设备身份参数，用于产能、日志、生产记录等业务读取。
- `deviceCode` 只用于设备查询或解析，`ClientCode` 只用于 Cloud 内部身份/寻址；二者不得作为 `deviceId` 发送，普通用户回答不得展示 Cloud ClientCode。
- `Analysis.Device.List/Detail` 只表达 `/api/v1/ai/read/devices` 的设备主数据；`Analysis.Device.Status` 只读取 `/api/v1/ai/read/device-client-states` 的 Cloud 权威 `softwareStatus`、运行心跳原值和唯一 freshness 时间。无心跳设备返回 `MissingRuntimeHeartbeat` 行；只有超过 24 小时才是 `RuntimeHeartbeatStale`，恰好 24 小时不 stale，Stale 不得冒充 Offline/Stopped；空集只表示授权范围内无匹配设备。
- `Analysis.Process.List/Detail` 只读取 `/api/v1/ai/read/processes`；支持 `processId` 精确过滤及 `keyword/processCode/processName` 搜索，详情必须唯一精确命中且搜索结果未截断，`processId` 必须作为正式 GUID 参数发送、不得塞入 keyword，不得回退其它数据源。
- `Analysis.ClientRelease.List` 的 Cloud business plugin 只读取 `/api/v1/ai/read/client-releases`，只允许 `channel/targetRuntime/status/includeArchived`；版本、hash、下载地址、发布说明和发布状态只能来自 Cloud 返回，不得生成或补齐。`Empty`、`NeedClarification`、`Unauthorized` 不 fallback；只有该插件返回 `Unsupported` 或同源 `Unavailable` 时，才允许按统一规则尝试同源 Text-to-SQL，绝不切换来源或进入 Simulation。
- AICopilot 的 Cloud AiRead 客户端和 endpoint allowlist 必须逐项覆盖 Cloud `AI只读接口契约.md` 已批准的正式 `GET /api/v1/ai/read/*` 表面；高频 DeviceLog/Capacity/ProductionData 接通不等于全量接口对齐。
- Cloud AiRead 客户端只保留八个正式 typed GET，不得暴露任意 method/path 传输、可配置 POST allowlist、legacy adapter 或双轨接口；非 GET 必须在发送 HTTP 请求前拒绝。
- `production-records` 当前正式提供 `typeKey/typeName/deviceId/deviceName`、弹夹/结果/时间公共字段及 schema 化 `fields`；CP/AP 业务字段为 `plcCode`、`plcName`、`startTime`、`punchingQuantity`、`punchingSpeed`。它不提供 `processName/stationName/deviceCode/ClientCode`，缺失字段保持不存在或空，不得用其他显示字段代填或推断。
- 生产语义固定映射：“正极模切”→`typeKey=cp`，“负极模切”→`typeKey=ap`；“正极模切05”“负极模切12”等带编号表达必须同时形成对应 typeKey 与中文 `plcName` 精确过滤。Cloud AiRead 客户端必须透传 `plcCode` / `plcName`，不得在模型回答阶段再做无证据筛选。
- CP/AP 回答优先展示中文客户端名、中文 PLC 名、弹夹号、冲切数量、冲切速度、开始/完成时间；不得向普通用户展示 Cloud ClientCode，也不得把 MES `P2-CPUC` / `P1-APUC` 当作 Cloud 身份。
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
- 当前唯一真实外部业务数据源是 Cloud；MES、ERP 后续只能通过统一 provider/profile registry 扩展。插件只注册 provider、dialect、schema、能力和执行 adapter，不复制 Runner、Guard、RepairLoop 或 Prompt。
- 每个分析任务首次执行前必须确认数据源、数据类型、设备/业务对象、时间范围和过滤条件；来源不唯一、信息不足或置信度不足时先询问。同一任务后续追问复用已确认 `BusinessQueryContext`。
- 数据能力统一为 `Device`、`DeviceLog`、`Capacity`、`ProductionRecord`、`Process`、`ClientRelease`，插件必须声明支持范围和结果契约。
- 插件结果统一为 `Success`、`Empty`、`NeedClarification`、`Unsupported`、`Unavailable`、`Unauthorized`。只有 `Unsupported` 或同一来源的 `Unavailable` 可由模型决定是否尝试同源 Text-to-SQL；`Empty` 是真实空集，`NeedClarification` 继续询问，权限/凭据失败不得绕过，禁止跨源 fallback。
- CP/AP 生产查询继续复用唯一 `ProductionRecord` 通用业务数据插件，不得按工序复制插件、端点、Runner 或结果语义。
- SQL 安全唯一 owner 是执行咽喉的共享 AST guard + 已选择 source profile；只允许单条只读查询，拒绝 DML、DDL、管理语句和多语句，表列范围来自 profile，数据库账号保持只读。
- 查询结果只用于分析展示，不产生业务写入。
- 不能为了分析便利放宽 `MaxRows`、read-only session 或 SQL 安全检查。
- Cloud 同时配置 typed business plugin 和受控 Text-to-SQL profile。Text-to-SQL 只由上述 `Unsupported`/同源 `Unavailable` 触发；Simulation 必须显式选择，不得暗中接管 Cloud 空集、失败或未确认来源。
- CloudReadOnly Text-to-SQL LLM prompt 可见的物理 schema 只能来自 `CloudReadOnlyGovernedSchema` 治理白名单，最多包含批准表名、列名、列类型、join hints 和必要业务描述；不得把连接串、凭据、role/权限细节、样例数据、查询结果、参数值、非白名单表字段或系统/敏感字段发给模型。
- CloudReadOnly Text-to-SQL 修复重试默认最多 3 次、硬上限 5 次；timeout、权限、凭据、非只读、系统表、敏感字段、多语句或写 SQL 默认不可修复、不重试。
- CloudReadOnly Text-to-SQL 修复历史不得保存完整 SQL、用户 prompt、连接串、参数值或敏感字段；上一轮失败 SQL 只允许在当前调用内以内存参数临时回传给 LLM 生成下一版，不能写入审计、日志、state、结果或持久化对象。
- Prompt 只负责澄清、方言、schema 与结构化输出，不维护写操作动词黑名单；Cloud 专用 runner/policy 不再复制结构只读 guard。
- Direct DB 语义映射中的工序名来自只读 `mfg_processes.process_name`；新增 join 表必须同步进入 `CloudReadOnlyGovernedSchema` 表/列/类型/join hint、所选 source profile 的 security schema、唯一共享 AST guard、只读 role 授权 SQL、授权探针、部署 preflight、RealSource 模板、架构测试和部署文档。
- DeviceLog 语义查询必须使用真实 Cloud PostgreSQL 日志级别枚举值 `ERROR`、`WARN`、`INFO`，不能生成 `Error`/`Warn`/`Info` 这类大小写不匹配条件。
- 用户要求“错误警告”“异常分析”“分析错误信息”等场景时，DeviceLog 必须支持 `ERROR + WARN` 多级别只读查询；不能只查 `ERROR` 后把 `WARN` 推断为没有。
- DeviceLog 自然语言中的工序/设备范围必须落到只读 `devices` / `mfg_processes` join 暴露的业务字段过滤；不得让最终回答模型按文字自行猜测设备、工序或范围。
- 用户追问其他日志级别、工序、设备或时间窗口时，意图路由/数据分析执行层必须重新生成并执行对应 `Analysis.DeviceLog.*` 查询；Final Agent 只能总结本轮查询结果，不能基于上一轮回答文本推断未查询级别“有/没有”。
- DataAnalysis 最终上下文必须携带本轮查询执行事实，包括语义 target/kind、filters、timeRange、limit、returnedRowCount 和证据边界；最终回答必须先核对执行事实再输出结论。
- DeviceLog 数据分析展示块和 Widget 只能从本轮只读查询返回行、`query_execution` 和 `semantic_summary` 派生；级别分布、时间分布、问题关键词分类、指标和证据表不得由模型编造、Markdown 解析、前端假数据或任意图表配置生成。
- DeviceLog 最终回答使用 `display_blocks` 时必须按“结论、关键指标、关键记录、可能原因、建议动作、不能直接执行的动作、查询范围”组织；可能原因必须标注为 AI 推断分析，建议动作只能是人工排查建议，不能写成已执行的控制、下发、写入或修复动作。
- Direct DB 设备主数据映射不得再连接最新日志或暴露 `status/lineName/updatedAt`；最新日志级别只通过 `Analysis.DeviceLog.*` 查询，不得包装为设备运行状态。
- 真实 Cloud Text-to-SQL 验证不得走 Simulation 数据源冒充真实结果；Simulation 只能用于明确标识的模拟链路。
- 创建或轮换 Cloud PostgreSQL 只读账号只能通过显式确认的受控自动化执行；只能创建/更新专用 readonly role，只授予白名单表 SELECT，不得授予写权限、schema create 权限、superuser、createdb、createrole 或 replication。
- Cloud PostgreSQL readonly role 授权的权威载体是 `deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql` 和 `check-readonly-grants.sql`；生产只允许对治理白名单表做显式表级 `GRANT SELECT`，不得使用 `GRANT SELECT ON ALL TABLES`、默认权限、未来表自动授权或列级/表级混用口径。
- 启用 CloudReadOnly 直连数据库时，部署必须先执行 readonly 授权 preflight；权限错误只能向用户暴露治理白名单内的表名和只读权限不足结论，不得输出连接串、role、密码、SQL 原文或非白名单对象。

## 6. MCP 规则

- MCP 是受控工具入口，不是 Cloud 业务写入口。
- 工具描述必须说明是否只读。
- 涉及文件、外部系统、命令执行或其他副作用的工具必须保持审批约束。
- 不允许配置直接或间接调用 Cloud 写接口的 MCP 工具。
- 动态配置 MCP 目标默认无法证明为可信 NonCloud；调用方传入 `NonCloud`、opaque URL/alias 或不含 `cloud` 的名称都不得放宽边界。只有 server/tool 同时为 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 并通过动词、hint、schema 和 risk 检查才能注册或暴露。
- 聚合注册、runtime builder 对旧持久化记录的复验、Plan 能力发现和 `McpAgentToolExecutor` 每次调用必须复用同一条 MCP 安全策略；禁止恢复 hostname/token heuristic、伪 allowlist、fallback 或仅启动时检查。
- 本地非 MCP 工具仍按其正式 capability/risk/审批策略处理；MCP fail-closed 不等于删除必要的本地副作用工具。

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
- `ExecutablePlan` 是用户确认无 capability gap 的 `PlanDraft` 后生成的可执行计划；此时才允许做 Tool、Schema、Guard 和审批校验。
- `AgentTask` 是真正进入执行阶段的任务对象；它必须来自已确认的 `ExecutablePlan` 或等价确认状态。
- 用户确认前，Plan 模式禁止 Cloud 业务查询、MCP 工具执行、Tool 执行、DataAnalysis 真实查询、Worker 入队和任何会产生业务副作用的动作。
- Cloud 只读 Agent 当前正式能力覆盖 `Analysis.Device.List/Detail/Status`、`Analysis.DeviceLog.Latest/Range/ByLevel`、`Analysis.Capacity.Range/ByDevice`、`Analysis.ProductionData.Latest/Range/ByDevice`、`Analysis.Process.List/Detail` 和 `Analysis.ClientRelease.List`；`Analysis.Capacity.ByProcess` 尚无正式聚合契约。意图只能在用户确认后进入执行链，且必须复用唯一 Cloud AiRead 语义实现。
- Tool、MCP、Knowledge、DataSource、Provider 或资源未匹配时，不能阻断 `PlanDraft`；必须记录稳定 capability gap，且草案保持 node-free、不可确认、不可入队。
- 执行阶段失败后的重试应基于已确认的 `ExecutablePlan` / `AgentTask` 重新入队，不应重新生成 `PlanDraft` 或丢失用户确认。
- 一个目标包含设备、日志、产能等多个 Cloud 只读意图时，计划和执行必须保留全部已确认意图；每个查询的对象、时间范围、筛选条件和上限分别冻结。Simulation、MCP 或其他来源永不 fallback；同源 Text-to-SQL 只接受对应插件的 `Unsupported`/`Unavailable` 结构化结果。
- 多分支执行只能把已授权 Evidence 合流给综合分析 Agent；AI 推断必须明确标为 `LlmInference`，建议必须明确标为 `Recommendation`，不得把 AI 推断、健康评估或建议说成 Cloud 已观测事实或预测模型结果。
- 同一完成任务生成的回答、图表、Markdown、HTML、PDF、PPTX 和 XLSX 必须绑定同一 `EvidenceSetDigest`，跨格式关键事实不得漂移。
- 用户只有主动选择“基于此结果追问”时，Chat 才能引用当前会话内该 `Completed` 任务的封存证据；引用是一次性的，不能自动取最近任务、跨会话/跨用户复用或从旧回答文本反推事实。用户改变设备、工序、日志级别或时间范围时必须重新执行新的只读查询。

### 8.2 当前内网 HTTP 部署红线

- AICopilot 当前生产形态是内网 HTTP 部署，入口、Cloud OIDC、Cloud AiRead、Harbor 和模型服务均按内网 HTTP 口径治理。
- 当前修复计划不得把 HTTPS redirection、HSTS、nginx 443 listener、证书申请/续期或 `RequireHttpsMetadata=true` 作为硬门槛；这些项需要额外证书方案和用户单独批准，不能夹带进 AI 端安全整改。
- HTTP 部署不等于放松其他安全边界。必须继续执行内网隔离、端口收敛、同源代理、CORS 白名单、短期 token、强 secret、非 root 容器、只读 Cloud 边界、敏感信息脱敏和除 HSTS 外的安全响应头。
- Cloud OIDC 使用 HTTP issuer 时必须显式启用内网 HTTP OIDC，只允许 loopback、私网 IPv4 或保留内网 DNS 后缀（`.internal.example`、`.internal`、`.lan`、`.local`）；公共 HTTP 域名即使开启内网 HTTP 开关也必须拒绝。
- 文档、测试和部署 preflight 出现 HTTPS/HSTS/443/certificate 强制项时，必须先改回 HTTP-only 口径，再继续执行其他安全修复。
- Web 到 HttpApi 的标准生产路径必须是 nginx 同源 `/api/` 反代；HttpApi CORS 默认不开放跨源。确需浏览器直连后端时，只允许配置精确 http/https origin，禁止 `*`、通配子域、带 path/query/fragment 的 origin 或运行时任意放行。
- macOS Keychain 是本机生产密钥唯一 canonical 来源；仓库只提交无值 schema。现有非 git 私密手册/旧 env 只允许一次性静默迁移且不删除旧文件，标准部署不得回退读取。服务器只消费由部署生成的受限 `.env`。
- Cloud readonly 连接、AiRead token、模式开关和 readonly role 不得通过 GitHub secrets 加手动 workflow 写入生产。新环境或清空重建只由工作区 `Deploy-FromZero.ps1` 从 Keychain 建立；明确批准的独立基础设施维护只可调用内部 apply/check 脚本并消费服务器受限 `.env`，不得形成第二套 secret 真值或应用重建入口。
- Cloud/AI 人员管理员账号与 Cloud PostgreSQL 只读技术角色不得混用：人员管理员可以使用纯数字工号，readonly role 使用独立技术名称；canonical schema 的数据库项必须接受生产真实库名 `iiot-db`，不能套用要求字母开头且禁止连字符的角色名校验。
- 如果当前真实部署根目录、稳定 Runner、Docker Root Dir、基础设施维护目标、工作区入口参数或标准部署用户与模板不同，必须先更新工作区 `deploy/Deploy.ps1`、`deploy/Invoke-WorkspaceDeploy.ps1`、`deploy/profiles/*.json`、项目部署指南/README 和工作区部署总览，再允许继续改脚本或发布。
- 如果当前 `AICopilot` 与 `Cloud` 共用同一台生产宿主机，必须在工作区总入口明确写出共享宿主机事实、共享标准发布人和两个独立部署根；不得把同机双部署根问题写成两套互不相关的环境。
- root 应急路径一旦写入 `releases/*`、`current-release.summary.md` 或 deploy support files，关闭任务前必须恢复 owner/mode，并重新验证标准 non-root `./deploy-release.sh --validate-only`；不得留下 root-owned 状态文件后直接收口。
- 工作区根 `deploy/Deploy-Changed.ps1` 是日常应用唯一入口；正式发布只接受 clean、已提交的本地 `main`，可 push 现有 HEAD但不得创建提交或修改 tracked 文件。它复用同 SHA 证据，只补受影响 Architecture/Security/DeploymentContract，再按依赖闭包发布受影响镜像；全量、coverage、mutation、duplication 和 CrossProject 不属于部署。失败只停止报告，不修代码。
- 工作区 `deploy/Deploy-FromZero.ps1` 是三端从零部署唯一入口；AI 阶段只执行 Cloud readonly 凭据/权限、AICopilot migration、真实模型 seed 和健康验证。缺 Keychain 根密钥时远端零写入；不得创建设备、注册 `ClientCode` 或轮换设备 bootstrap secret。
- `deploy/enterprise-ai/tests/TestDeploymentPolicy.ps1` 只由受影响 selector 归入 `DeploymentContract`；普通项目 build 不得无条件触发部署测试。删除生产状态 inspect、受影响服务集合、migration 闭包或恢复日常全量 fallback 时必须失败。
- 内网 HTTP Harbor 推送后的不可变镜像解析必须选择唯一 `linux/amd64` manifest digest；`buildx` HTTPS inspection 失败时可使用 `docker manifest inspect --insecure --verbose`，但不得退化为 tag、attestation digest 或跳过 digest-bound request。
- AICopilot 后端多服务在同一候选内顺序 publish 时，源码 detached worktree 与按 service 隔离的 .NET SDK artifacts 根都必须放在工作区统一 deployment artifacts 根；不得落入 macOS `TMPDIR=/private/var/folders/...`、共享 `bin/obj`、混用 `/private/var` 与 `/var` 路径别名或靠调整服务顺序规避依赖图污染。
- support release 必须包含 compose、执行 staging/SHA256 校验，并让 support reservation、全局 release lock 和 deploy 使用同一 token/digest；`.env`、release state、锁和备份不得进入同步包。健康前失败必须恢复持久状态；active/stale lock、真实退出码、timeout、信号释放锁和同 SHA 健康幂等必须有行为回归。
- 正式发布和健康 no-op 必须绑定 workspace plan/profile、固定 Git SHA、显式服务闭包、immutable OCI、全局配置 fingerprint 与实际运行容器身份；配置 fingerprint 漂移时普通部署停止并要求独立配置维护或从零部署，不得自动扩大成全量服务发布。后端服务闭包仍显式包含 migration。
- support、compose、release state、三项基础设施和全部常驻 runtime 必须一起恢复并验证；基础设施身份以事务前冻结的 RepoDigest/runtime image id 为准，不得用可变 tag 冒充旧运行态。恢复或阻断证据不确定统一返回 `86` 并永久 fail-closed；reservation 原子 transition 与断联对账失败/active/unknown 返回 `87`，禁止自动取消或重试。
- 模型 smoke 的 `AICOPILOT_MODEL_SMOKE_API_KEY=dummy-key` 只允许作为真实模型网关的显式兼容例外，必须同时设置 `AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true` 或手工 smoke 命令传 `--allow-dummy-key`；默认 preflight 必须拒绝该弱值。
- HttpApi JWT 配置必须由唯一运行时入口校验：Issuer、Audience 非空，SecretKey 至少 64 字符，AccessTokenExpirationMinutes 大于 0；绕过部署脚本直接启动也必须 fail-fast，错误不得回显 secret，默认有效期保持 30 分钟。

### 8.3 模型密钥保护格式

- 模型、Embedding 和 endpoint pool 覆盖 API key 的受保护存储格式必须是 `encv2:`，加密算法使用 AES-GCM 并校验 authentication tag。
- `AiRuntime:ProviderReliability` endpoint `ApiKey` 和 `ApiKeyEnvironmentVariable` 指向的环境变量值也必须存 `encv2:` 密文；scheduler 只能做受保护格式校验并把密文交给 runtime provider，不能解密、记录或接受明文。
- 旧 `encv1:` 只允许在 migration worker 或一次性迁移命令中读取并重加密为 `encv2:`；运行时 provider 不得把 `encv1:` 当正常密钥格式继续兼容。
- 明文、旧格式密文、缺失主密钥或 authentication tag 校验失败，都必须进入配置修复或迁移流程，不得静默降级为可用密钥。

### 8.4 私有模型生产 seed

- fresh DB seed 默认创建禁用的私有 OpenAI-compatible 模型记录，只能使用 `model.internal.example` 占位 URL、空 API key 和 64k context window；仓库、示例 `.env`、测试和长期文档不得写真实模型网关内网地址或真实 key。
- 生产服务器必须通过受限 `.env` 明确配置 `AICOPILOT_PRIVATE_MODEL_*`；本机真实值只从 macOS Keychain 读取，不再以非 git 私密手册作为标准真值。
- 当前私有模型标准 context window 是 64k，即 `AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS=65536`；模型名和 API key 不允许硬编码在运行时代码里。
- migration worker 播种私有模型时，API key 入库前必须经过 `SecretStringEncryptor` 加密为 `encv2:`；已存在同 provider/model 的记录不强行覆盖现场 base URL、启用状态或运行参数，只做密钥格式修复。

## 9. 文档入口

- 当前规则入口只保留 `AGENTS.md`、本文档和按边界触发的专题契约；项目复盘与工作区历史记录只供命中追溯条件时定向检索，不是规则入口。
- `docs/AI架构治理清单.md` 是历史治理状态与 Rule ID 索引，不是默认执行入口。只有当前任务命中具体 `AIARCH`/`AI-SEC` 编号、修复历史回归或需要追溯未关闭风险时，才按编号读取对应条目；不得全文加载，也不得把其中的旧候选、旧数量或旧全量验收口径当成当前门禁。
- 当前长期专题契约包括 `docs/AICopilot安全部署契约.md`、`docs/Cloud只读数据分析契约.md`、`docs/Agent工作流与异常契约.md` 和 `docs/DDD聚合根边界.md`；触碰部署、Cloud 只读、Text-to-SQL、Agent workflow、MCP/Tool、异常、前端错误、聚合/repository 或 DB owner 时必须先读对应契约。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`；后端、部署和数据查询任务不得顺带加载前端会话/UI 规则。
- 部署说明只保留 `AICopilot 项目部署与维护指南.md`；工作区 `../../deploy/Deploy-Changed.ps1` 和 `../../deploy/Deploy-FromZero.ps1` 是操作入口，`deploy/enterprise-ai` 仅是被统一入口调用的 AI 内部实现与支持目录。
- 阶段计划、批次验收报告、PR 草案和一次性 acceptance 输出不得继续作为执行入口；有效结论必须沉淀到长期规则或部署指南后再清理。
- 清理文档时必须先检查引用，避免留下指向已删除阶段文件的脚本、测试或说明。
- 旧的 Simulation/Real/Sandbox/Pilot 阶段说明只可作为历史材料，不得覆盖当前部署指南和生产验收口径。

## 10. 工程边界

- AICopilot DDD 聚合根、投影、队列、审计和运行时记录的长期技术契约见 `../docs/DDD聚合根边界.md`；新增或调整聚合根、仓储注册、EF `DbSet`、Agent runtime timeline/queue/audit 记录时必须同步更新架构测试。
- 生产分层固定为：`src/core` 领域核心，`src/services` 命令、查询、workflow 与应用编排，`src/infrastructure` EF/Dapper/embedding/event bus/provider/MCP 技术实现，`src/hosts` 只做组合根和启动 wiring，`src/shared` 只放真正共享抽象，`src/vues` 只放前端逻辑；不得跨层回填实现。
- HTTP Controller 必须默认授权，或对确需公开的 action 显式声明匿名。MediatR 普通与 stream 横切行为只允许由 `AddAICopilotMediatRPipeline` 统一注册，顺序固定为 Telemetry → Validation → Authorization；service 模块不得复制注册。stream 授权必须在进入 handler 前完成并逐项透传，禁止预读或缓冲；telemetry 只记录类型、阶段、耗时、结果和异常类型，不得记录 prompt、SQL、token、连接串、API key 或业务明细。
- `ProblemDetails.extensions` 的 `code`、`traceId` 是大小写不敏感的保留键；复制 descriptor extensions 时必须丢弃全部大小写变体，再分别以 descriptor code 和当前请求 trace 写入唯一 canonical 小写键，禁止调用方覆盖或制造歧义键。
- 架构 Analyzer/ArchitectureTests 严格保护分层、聚合、owner 和 Cloud 只读边界；领域、Application、Workflow、Contract、Persistence、HTTP、UI 与 Eval 业务测试随功能同批正常增删改移。测试清单只描述当前提交实际发现和执行的结果，不提交固定 case 数、required runner roster 或业务覆盖率 baseline，也不得成为业务测试变更的额外授权或账本。
- `AICopilot.Architecture.Analyzers` 是生产编译的架构 owner；`AIARCH001`–`AIARCH007` 必须保持 `Error + IsEnabledByDefault + NotConfigurable`，CompilationEnd 规则同时保留 `CompilationEnd` tag，由独立 `AICopilot.Architecture.AnalyzerTests` 同时保有语义夹具和真实临时 csproj 正/反编译及 suppression 夹具。Analyzer/Architecture 夹具必须拒绝 `NoWarn`、`.editorconfig/.globalconfig` severity、`#pragma warning disable`、`SuppressMessage/UnconditionalSuppressMessage`和 Analyzer 关闭。Analyzer 属性和例外必须按完全限定 symbol identity 识别；`AIARCH004` 必须证明减员在 transaction delegate 内且 invariant guard 先于变更，inline/stored 与 field/property 中的 lambda/method-group 都必须在 CompilationEnd 形成 edge-aware caller→delegate 语义边，synthetic transaction edge 不能掩护同一 target 的真实直接调用；public/internal/private/protected 中没有源码 incoming edge 的真实宿主入口均须检查，不能靠可见性降级绕过。`AIARCH006` 必须对所有源码方法先按该方法自身的直接调用/构造/泛型解析、签名/字段/ctor 正式 client 或正式 workflow symbol 判定 Cloud root，命中后才检查完整 reachable graph；本地 DI factory、private helper 返回、object creation、field/property delegate 和 Cloud root 内的 interface dispatch 都不能绕过。Repository、command、Dapper 和 MCP 写边只接受专题契约列出的完全限定 symbol；同名伪 repository/command/`SqlMapper`/MCP executor、Generic orchestrator、仅计划/DTO 类型或方法名均不得扩大入口或写边。规则例外只能是专题契约中记录的完全限定类型/项目，禁止 `NoWarn`、降级 warning、optional gate 或恢复同义字符串/Regex 影子路径。
- 跨项目 Analyzer 调用图必须由源生成器输出版本化、定长上限、精确 `producer assembly + contract assembly + documentation method id` 摘要；消费方必须校验数量、producer 身份和全量内容一致。object creation 的 delegate 实参必须按构造参数 symbol 绑定，普通 invocation 的 delegate 实参按调用参数绑定；隐式 optional delegate 默认值不得伪造未知 callback，真实无法解析的 delegate 仍须 fail-closed。正式 `IAuditLogWriter` 只允许按完全限定 contract identity 截断 AICopilot 审计边；正式 `IModelQuotaReservationStore` 只允许 `TryReserveAsync`、`SettleAsync`、`ReclaimExpiredAsync` 三个契约方法截断 AICopilot 模型配额边，且唯一生产实现必须是 `PostgresModelQuotaReservationStore`，只能经 `AgentExecutionTransactionRunner` 写唯一 `AiGatewayDbContext`。同名接口、其他实现、其他 DbContext、adapter/wrapper 或实现类额外写方法均不得获得例外；两类例外都不得写 Cloud 业务数据。
- `AIARCH001` 必须对当前真实的 `AICopilot.*` 生产项目使用显式分类；任何未分类生产项目无论出现在引用源或目标都必须 fail-closed。
- Aggregate runner 只能是 Pure 且只直接依赖 core/shared；Application runner 只能是 Pure 且不得直接依赖 host、EF/Dapper、Aspire/Persistence fixture；文件持久化测试必须进入 `PersistenceFilesystemTests`。五个 TestKit 不得依赖 test SDK、xUnit/NUnit/MSTest 或断言 package，生命周期适配和断言 helper 留在 runner。
- Runner/TestKit 依赖边界只认指定 Configuration 下 MSBuild evaluated `ProjectReference` / `PackageReference` 图，必须包含隐式 `Directory.Build.*`、递归 import、生效复合条件、逐 TargetFramework item 和 TestKit 传递闭包；raw XML 扫描不能作为证据，评估异常或缺失规范化 identity 必须 fail-closed。Direct kind boundary、Pure closure、TestKit consumers 和 production→TestKit 禁令必须复用同一图。
- 测试 runner 必须以项目元数据声明 kind/runtime/owner 并进入 `AICopilot.slnx`；不得用 Phase/Batch/Suite filter 代替物理 owner。默认 lane 只对 selector 选中的 runner 生成 TRX，并要求 `discovered = executed = passed`、`failed = 0`、`skipped = 0`，不保存历史 case 总数。
- compatibility 项跨多个公开 legacy surface 时必须登记完整精确符号并集；同一 caller 命中多个 surface 时按可解析的 distinct caller member 去重，任一 surface 新增 caller 都命中同一既有上限。MCP TestKit executable 与 in-process server 暴露同一 canonical tool 时，名称和只读/破坏性 annotation 必须一致；变更后用精确 FQN 枚举证明恰好 1 项并真实执行对应 E2E。
- Coverage、duplication、mutation 和 compatibility 统一属于用户显式 `Quality` 模式，不进入 push/PR、nightly 或普通部署默认链。运行时必须绑定 clean committed HEAD、当前生产源码/程序集/PDB 和真实 ancestor；报告数量从本次实际执行动态得出，不固定历史 runner/case 数，也不得阻止业务测试随业务同批增删改。
- compatibility baseline 只记录真实兼容/迁移项，bootstrap 后只能删除或收紧 deadline/call-site 上限，禁止新增 ID、扩大调用方或换名回避。普通 abstraction 不进 baseline；inventory 必须证明唯一活跃声明、至少一个真实可执行调用点、`AI-ORDINARY-*` 身份且不含兼容生命周期字段，注释、字符串和声明不算调用方。
- 用户显式 `Quality` 模式下的重复度门禁只治理生产源码，不扫描或冻结 `src/tests`、`src/testing` 与前端测试。生产重复以 `path+line` 计数每个出现实例，同文件重复不得被去重；同时锁定汇总指标和每个 signature 的实例数/重复行/重复 token。base 尚无重复度 baseline 时只允许一次 candidate-exact bootstrap；base 已有 baseline 后只能在真实重复先减少时收紧，不得用总量持平、signature swap、放宽或重生成 baseline 换绿。
- `IAggregateRoot<>` 只用于独立维护业务不变量和生命周期的领域根；队列项、timeline 投影、工具执行审计、worker 心跳和执行尝试记录不得作为新增聚合根方向，历史债务只能减少不能增加。
- `DataSourcePermissionGrant` 当前保留为独立聚合根但标记待评估；后续如果授权生命周期可归入 `BusinessDatabase`，应下沉为子实体或专用权限记录并移出聚合根白名单。
- `AiCopilotDbContext` 是主基础设施迁移上下文，也是 Outbox 与 persistence commit marker 的唯一 migration owner；`AuditDbContext` 负责审计查询和运行时审计写入，`DataAnalysisDbContext` 只承载数据分析配置，`OutboxDbContext` 与 `PersistenceCommitMarkerDbContext` 只作为运行时短生命周期参与者，不拥有 migration。
- 没有真实事件生产者的 DbContext 不得复制 Outbox `DbSet`、映射或 `SaveChangesAsync` 领域事件扫描；DataAnalysis/MCP 不写 Outbox，AiGateway `Session` 领域事件和 RAG delayed integration-event factory 只能在 repository commit participant 内物化到短生命周期 `OutboxDbContext`，业务 Context 不映射共享 Outbox。
- 审计写入必须遵守 Audit writer decision tree：有业务保存点的命令应把业务变更和审计行放在同一事务；`auditLogWriter.SaveChangesAsync` 只允许出现在没有业务保存点且已被白名单记录的执行路径。
- Outbox 多实例调度必须使用 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略，不能让多 worker 重复发布同一消息。
- 普通 repository 的业务、Outbox、审计和 durable commit marker 必须由唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 在同一数据库事务中提交；每个 execution-strategy attempt 对业务 Context 只允许一次 `SaveChangesAsync(false)`，事务确认后才 `AcceptAllChanges`、清领域事件或清 RAG factory buffer。Identity 通过 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用同一 engine；非成功 `Result` 必须回滚 UserManager/RoleManager 已触发的所有中间保存，拒绝审计只能在回滚后另行提交，禁止恢复 `EfTransactionalExecutionService` 或复制第二套 transaction/retry。
- EF execution-strategy 必须使用官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 或等价官方入口，禁止手写业务重试循环。commit-unknown 不能用 `SaveChanges(false)`、Outbox 或 audit 是否存在推断成功；必须写入同事务 durable marker，并由 fresh context 在独立超时与 execution strategy 下验证，真实 PostgreSQL 必须覆盖 commit-ACK 丢失、verification transient/persistent failure、caller cancellation 和数据库生成 identity 重放。
- marker 写入后不得再让 caller cancellation 中断 commit/verification；fresh verification 无法确认时返回稳定 503 `persistence_commit_outcome_unknown` 和非敏感 commit id，不自动重放业务。RAG `UploadDocument` 与 AiGateway SessionTemp/AgentInput `UploadRecord` 必须先写持久化对账日志再写物理文件，并复用同一 commit id；请求与 DataWorker 通过 PostgreSQL advisory lease 互斥，结果未知时保留文件和日志，后台看到 marker 才保留文件并清日志，看不到 marker 才删除文件。RAG 删除事件必须按 storage path 查 journal、取得同一 lease 并在锁内退休 journal 后再删文件；journal 不可读或 lease 活跃必须重试，禁止直接删或记录原始客户端路径。知识库文件唯一写入口是 RAG Document API，禁止恢复 AiGateway KB shadow scope/bridge；`ArtifactWorkspace` 多文件必须走独立 file-set journal/manifest/fencing/checkpoint/rollback/reconciliation。源码接入不等于 `AI-PERSIST-01d / AI-SEC-047` 已关单，必须通过真实文件系统 + PostgreSQL 故障矩阵；历史 KB shadow 清库仍属 `AI-PERSIST-01e / AI-SEC-048`。
- RAG/AiGateway 数据库绑定上传调用方必须复用唯一 `PersistenceFileCommitProtocol`；repository 未消费预留 commit id 时，确认必须 fail-closed、回滚未提交文件并保留失败信号，不得因 callback 正常返回就清除 journal。
- 标准容器共享卷只允许受信任的 AICopilot 后端写入。当前路径边界拒绝既有 symlink/reparse traversal，但不把同 UID 恶意进程在检查与打开之间替换目录的 TOCTOU 视为已解决；扩大威胁模型前必须增加容器权限隔离或 dirfd/`openat` 原子路径操作。
- 容器必须把可写 `FileStorage:RootPath` 和 `ArtifactWorkspace:RootPath` 固定在共享卷的 `/var/lib/aicopilot/storage` 与 `/var/lib/aicopilot/artifact-workspaces`，不得回退容器层、`/app`、`LocalApplicationData` 或共享卷外路径。当前 durable local file/journal backend 只支持 Linux/macOS，生产固定 Linux；Windows 必须明确拒绝该 backend，不能用 `MoveFileEx` 或空操作冒充父目录 durability barrier。
- HttpApi 与 DataWorker 必须共享 `/var/lib/aicopilot`；commit marker 默认保留 30 天并按 `created_at_utc` 索引，保留期必须长于对账延迟，有待处理日志的 marker 不得删除。对账日志不可读时必须停止 marker 清理。相关改动由 selector 选择真实 PostgreSQL、migration 和部署配置验证；全量仅在用户显式授权时运行。
- MCP runtime 配置变更必须进入 runtime registry refresh cycle，禁用、删除或配置变更后不能继续暴露未来工具解析。
- 身份安全以 security stamp 驱动会话失效；Cloud role 不直接成为 AICopilot 本地 role。
- 多 DbContext 迁移历史必须通过 `__EFMigrationsHistory` 的上下文隔离或迁移历史表拆分规则治理，不能让单一上下文回滚污染其他上下文状态。
- 新增或接线 `IStreamPipelineBehavior` 后，必须核对所有公开 `IStreamRequest` 的 `AuthorizeRequirement`，测试种子角色必须覆盖对应权限；无权限场景应返回干净 401/403，不能表现为 SSE 已写 200 后断流。
- 简单集合转换默认优先用 LINQ 表达意图，`IQueryable` 必须优先下推过滤、投影、排序和分页；热路径、状态机、流式枚举、数组/`Span<T>` 紧循环允许 `for`/`foreach`。
- 工程质量门禁优先抓重复枚举、先物化再过滤、N+1 查询、O(n²) 嵌套、重复扫描和错误数据结构；CA1851 先作为 warning 运行，基线清理后再考虑升级为 error。
