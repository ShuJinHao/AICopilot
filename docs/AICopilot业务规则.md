# AICopilot 业务规则

本文档约束 `AICopilot` 自身业务边界。工作区总规则见 `../../docs/总规则.md`。

## 0. 改动收口门禁

- 工作区 `../../docs/总规则.md` 是唯一默认必读入口。进入 AICopilot 实际修改后，只读取本文档与本批模块相关的章节、相关源码和受影响测试；专题契约按边界触发，近期 git/GitHub 历史按回归或故障追溯条件读取。
- 长期规则直接进入本文或对应专题契约；真实事故只进入工作区 `../../docs/事故/`。禁止新建滚动复盘、阶段总结或日期式治理快照。
- 形成长期约束时直接写入本文档、专题契约或工作区总规则；项目 `AGENTS.md` 只保留按需路由和少量不可缺失的项目硬边界，不作为第二份详细规则库。
- 新增、删除或重命名后端错误码时，必须同批更新 `Agent工作流与异常契约.md` 的错误码目录，并运行错误码目录测试，确保前端错误契约不漂移。
- 默认只运行 Architecture/Security 与 owner 映射选出的受影响 Business；全量、coverage、mutation、duplication 和 CrossProject 只在用户明确要求时运行。
- 本文是 AICopilot 业务规则入口；专题契约和唯一架构路线图统一位于项目 `docs/`。

## 1. 核心职责

`AICopilot` 是分析助手和受控编排系统，不是制造业务主系统。

`AICopilot` 只承担 AI 助手和受控编排能力：

- Harness 主聊天：在同一 `AgentSession` 内提供 Plan/Execute、对话、知识检索、业务查询和受治理工具调用。
- RAG：基于文档和规则做问答、解释、总结。
- DataAnalysis / Text-to-SQL：基于只读数据源做查询、统计、分析。
- MCP 工具执行：只执行已配置、已授权、符合安全边界的工具。
- Human-in-the-loop：控制 AICopilot 自身高风险动作。

`AICopilot` 不是 Cloud 制造主数据系统，不是 Edge 现场运行系统。

模型、prompt、plugin、MCP server 和工具治理元数据优先使用明确配置或持久化数据，不得以隐藏常量绕过服务端门禁。

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
- 通过 MCP、Tool、Harness、后台任务或隐藏适配器间接调用 Cloud 写接口。

Cloud tool 安全元数据只有 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 这一精确组合有效。`Diagnostics`、`LocalSuggestion`、`SideEffecting`、缺失/动态无法静态证明的声明都必须 fail-closed；动态 MCP 的 enum、alias、描述或 endpoint 不能证明可信 NonCloud，server/tool 必须统一使用精确只读组合，并在聚合注册、runtime builder、Harness 工具发现和每次 MCP 执行时经过同一 `AiToolSafetyPolicy` 评估。runtime MCP tool 必须显式携带独立 canonical `ToolName`，缺失时直接阻断，禁止回退到 runtime `Name` 或其它 alias。

Human-in-the-loop 不能把禁止的 Cloud 业务写入变成允许动作。
当前默认不存在专门给 AICopilot 使用的云端写 API。
Cloud/MES/ERP 写入、生产控制和越权访问的硬阻断不依赖 Plan / Execute；模型或用户切换行为模式不能改变身份、Tool Gate、`AiToolSafetyPolicy`、SQL AST guard、只读账号、MCP 治理或批准结论。

Cloud AiRead 设备契约：

- `deviceId` 是正式 Cloud 设备身份参数，用于产能、日志、生产记录等业务读取。
- `deviceCode` 只用于设备查询或解析，`ClientCode` 只用于 Cloud 内部身份/寻址；二者不得作为 `deviceId` 发送，普通用户回答不得展示 Cloud ClientCode。
- `Analysis.Device.List/Detail` 只表达 `/api/v1/ai/read/devices` 的设备主数据；`Analysis.Device.Status` 只读取 `/api/v1/ai/read/device-client-states` 的 Cloud 权威 `softwareStatus`、运行心跳原值和唯一 freshness 时间。无心跳设备返回 `MissingRuntimeHeartbeat` 行；只有超过 24 小时才是 `RuntimeHeartbeatStale`，恰好 24 小时不 stale，Stale 不得冒充 Offline/Stopped；空集只表示授权范围内无匹配设备。
- `Analysis.Process.List/Detail` 只读取 `/api/v1/ai/read/processes`；支持 `processId` 精确过滤及 `keyword/processCode/processName` 搜索，详情必须唯一精确命中且搜索结果未截断，`processId` 必须作为正式 GUID 参数发送、不得塞入 keyword，不得回退其它数据源。
- `Analysis.ClientRelease.List` 的 Cloud business plugin 只读取 `/api/v1/ai/read/client-releases`，只允许 `channel/targetRuntime/status/includeArchived`；版本、hash、下载地址、发布说明和发布状态只能来自 Cloud 返回，不得生成或补齐。其查询路径和 fallback 资格只由 Cloud 专题契约定义。
- AICopilot 的 Cloud AiRead 客户端和 endpoint allowlist 必须逐项覆盖 Cloud `AI只读接口契约.md` 已批准的正式 `GET /api/v1/ai/read/*` 表面；高频 DeviceLog/Capacity/ProductionData 接通不等于全量接口对齐。
- Cloud AiRead 客户端只保留八个正式 typed GET，不得暴露任意 method/path 传输、可配置 POST allowlist、legacy adapter 或双轨接口；非 GET 必须在发送 HTTP 请求前拒绝。
- `production-records` 当前正式提供 `typeKey/typeName/deviceId/deviceName`、弹夹/结果/时间公共字段及 schema 化 `fields`；CP/AP 业务字段为 `plcCode`、`plcName`、`clipSlot`、`startTime`、`punchingQuantity`、`punchingSpeed`。`clipSlot` 只接受 Cloud 返回的 `MG1/MG2` 事实，不得由弹夹号或 PLC 名推断。它不提供 `processName/stationName/deviceCode/ClientCode`，缺失字段保持不存在或空，不得用其他显示字段代填或推断。
- 生产语义固定映射：“正极模切”→`typeKey=cp`，“负极模切”→`typeKey=ap`；“正极模切05”“负极模切12”等带编号表达必须同时形成对应 typeKey 与中文 `plcName` 精确过滤。Cloud AiRead 客户端必须透传 `plcCode` / `plcName`，不得在模型回答阶段再做无证据筛选。
- CP/AP 回答优先展示中文客户端名、中文 PLC 名、弹夹位、弹夹号、冲切数量、冲切速度、开始/完成时间；不得向普通用户展示 Cloud ClientCode，也不得把 MES `P2-CPUC` / `P1-APUC` 当作 Cloud 身份。
- 产能分组语义固定为：Cloud 小时产能按接口返回的小时、设备和 PLC 维度分组；每个分组的 `totalCount` / AI `outputQty` 表示该分组内的“完工弹夹数”，不是查询返回的记录数、冲切数量或分页总数。`plcName` 必须保留到 AI typed DTO、结构化行和摘要，跨分组汇总只能求和该指标，不得把命中行数当产能。
- 需要从自然语言里的设备编码定位设备时，必须先走显式设备查询/解析；无法唯一命中时要求用户补充，不做隐式兼容。
- AICopilot 的 Pilot 场景参数不得直接透传给 Cloud；只有 Cloud 端点真实声明的参数可以进入请求。
- Cloud provider / AI consumer 跨版本发布顺序固定为：Cloud 先发布向后兼容的 provider 契约并用仍在生产的 AI consumer 验证，随后才发布依赖该契约的 AICopilot；字段收紧或删除必须等所有生产 consumer 完成迁移后再单独发布。禁止先发布依赖尚未生产存在字段的 AI consumer，也禁止以同一工作区源码存在代替两个生产版本的顺序和真实数据验收。

## 3. OIDC 身份边界

- Cloud OIDC 只解决身份、账号有效性、员工有效性。
- AICopilot 保留本地 AI 用户、AI 角色、AI 权限、SecurityStamp、本地禁用、审计和 emergency admin。
- Cloud role 不直接映射 AI role。
- AICopilot 不读取 Cloud Cookie、不接收 Cloud 密码、不直连 Cloud 用户表。

### 3.1 JIT 首次身份绑定并发

- Cloud 身份首次登录命中同名、启用且尚未绑定其他 Cloud 身份的本地 AI 账号时，不得自动覆盖或创建重名账号；必须在短期 external cookie 有效期内要求用户用该账号的本地密码确认。确认请求只接收密码，本地用户名必须由已验证 Cloud profile 的 `employeeNo -> preferred_username -> sub` 规则推导，不得由浏览器提交。
- 本地密码确认成功只建立 Cloud identity 与现有 AI 用户的一对一绑定，必须保留该用户已有 AI 角色、权限、SecurityStamp 治理和禁用状态；Cloud role 仍不得映射或覆盖 AI role。历史账号的 SecurityStamp 为空或空白时，只允许在取得 binding invariant lock 后的同一 Identity 事务内初始化并持久化随机值，再以 fresh-read 的最终值签发 token；已有非空 SecurityStamp 不得因登录或绑定被轮换。密码不得进入 JWT、Pinia、storage、URL、日志或审计。
- 首次 JIT、Bootstrap 管理员收编和既有账号密码确认必须共用同一 Identity 事务内 binding invariant guard；一次取得并按稳定顺序锁定 `(provider, tenantId, externalUserId)`、规范化本地用户名，以及已知或本次预分配的 `(userId, provider)`，随后以绕过 EF tracked entity 缓存的 fresh-read 重新读取用户与绑定，禁止用加锁前结果作绑定、禁用状态或 SecurityStamp 决定。完全相同的既有绑定幂等复用，任一侧指向其他身份则稳定拒绝且不得覆盖。
- 两个请求并发争用同一 Cloud 身份、本地用户或规范化用户名时，事务内 fresh-read 与数据库唯一约束必须共同保证至多形成一个用户和一个绑定；完全相同请求的迟到重试只能返回同一用户，不同身份的迟到请求必须稳定返回 `external_identity_conflict`。只有规范化用户名、外部身份和用户/provider 三个已知唯一约束允许映射该错误码；连接失败、事务失败、未知约束和其它数据库异常必须保持系统失败，禁止伪装成账号冲突、泄露数据库细节、创建第二用户或自动覆盖。
- 密码错误允许在原 external cookie 有效期与登录限流内重试；取消、过期、账号禁用、无本地密码、Cloud 身份失效和不可恢复冲突必须清除 external cookie。本地账号缺少密码、任一侧已有其他绑定、Cloud profile 用户名命中其他本地账号时必须返回具体安全说明，不得统一显示模糊的“账号冲突”。
- 同名确认要求、密码拒绝、绑定冲突、确认取消、外部会话失效和登录成功都必须写结构化 Identity 审计；审计不得记录密码、token、cookie、URL 凭据或原始认证材料。
- EdgeClient 不参与 Cloud-AICopilot OIDC 身份对齐。

## 4. RAG 规则

- RAG 只能用于知识检索、规则解释和文档问答。
- 文档内容不能反向覆盖 Cloud 已确认业务规则。
- RAG 结果与 Cloud 规则冲突时，以 Cloud 规则为准，并报告冲突。

## 5. DataAnalysis 规则

- DataAnalysis 只能连接只读业务数据源。
- 当前唯一真实外部业务数据源是 Cloud；MES、ERP 后续只能通过统一 provider/profile registry 扩展。插件只注册 provider、dialect、schema、能力和执行 adapter，不复制 Runner、Guard、RepairLoop 或 Prompt。
- 业务查询对用户保持 typed provider 优先；fallback 是服务端内部受控能力，不是模型选择项。模型只看到 `BusinessQuery`，不得直接获得独立 Text-to-SQL 工具，也不得决定、触发或绕过 fallback。
- fallback 不能削弱身份、权限、只读账号、数据源绑定或 SQL 安全边界；Simulation 数据不得冒充真实 Cloud 结果。
- CP/AP 生产查询继续复用唯一 `ProductionRecord` 通用业务数据插件，不得按工序复制插件、端点、Runner 或结果语义。
- SQL 安全唯一 owner 是执行咽喉的共享 AST guard + 已选择 source profile；只允许单条只读查询，拒绝 DML、DDL、管理语句和多语句，表列范围来自 profile，数据库账号保持只读。
- 查询结果只用于分析展示，不产生业务写入。
- 不能为了分析便利放宽 `MaxRows`、read-only session 或 SQL 安全检查。
- typed-first、结构化结果矩阵、查询确认、Text-to-SQL prompt/重试/审计、Simulation 与 fallback 决策的唯一技术正文是 [Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)；本文不复制其 policy 或实现矩阵。

## 6. MCP 规则

- MCP 是受控工具入口，不是 Cloud 业务写入口。
- 工具描述必须说明是否只读。
- 涉及文件、外部系统、命令执行或其他副作用的工具必须保持审批约束。
- 不允许配置直接或间接调用 Cloud 写接口的 MCP 工具。
- 动态配置 MCP 目标默认无法证明为可信 NonCloud；调用方传入 `NonCloud`、opaque URL/alias 或不含 `cloud` 的名称都不得放宽边界。只有 server/tool 同时为 `CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true` 并通过动词、hint、schema 和 risk 检查才能注册或暴露。
- MCP 聚合注册、runtime registry refresh、tool plugin builder 和 `MainChatToolGate` 必须复用同一条安全策略；禁止 hostname/token heuristic、伪 allowlist、隐式 fallback 或仅启动时检查。
- MCP client 固定使用稳定版 `ModelContextProtocol 2.0.0`，不得引入 preview、Tasks、Apps 或其它扩展包，也不得压制 `MCP9005` / `MCP9006` / `MCP9007` / `MCPEXP*` 警告。HTTP 存量 `McpTransportType.Sse` 与数据库/API 值保持不变，内部统一使用 v2 discovery-first `AutoDetect`，由 SDK 自动回退旧 initialize 握手，不维护第二套兼容 client。
- Stdio 只使用官方 transport，关闭任意父进程环境继承，只传递 SDK 安全默认环境变量；stderr 只能记录脱敏后的长度与错误类别，禁止记录原文、命令输出、token 或环境变量值。
- discovery 必须从 `ProtocolTool` typed API 读取 canonical tool name、`inputSchema`、`outputSchema` 与 `readOnly` / `destructive` / `idempotent` annotation。每个 server 的连接与 `ListTools` discovery 固定使用独立 30 秒 deadline；超时必须立即撤下旧插件、隔离登记并继续后续 server，迟到任务只能被观察和释放，不能复活 registration。身份缺失、`inputSchema` 缺失、任一 schema 非法、output schema 缺失或 hint 与本地治理元数据冲突时不得注册；既有登记必须标记不可执行并从 Harness 工具面撤下，禁止反射读取 annotation 或退回 runtime alias。
- 全局 `ToolOutputSchemaContractV1` 保持 object-root；只有 provider 为 MCP 时使用独立 `McpToolOutputSchemaContractV1` 接受受支持的 scalar、array、object。MCP structured result 必须按该本地封闭 schema 和 bounded inline-output 策略验证；验证通过后保留远端原生 JSON 类型交给模型，非 object 结果不得包装成旧 `{ result: ... }`。缺少 structured content、远端 error、类型/字段不匹配或未知 shape 全部 fail-closed，文本 content 不能替代 schema-bound 结果。
- runtime refresh 每轮同时比较数据库 `RowVersion` 与远端工具 schema/hint、有效注册治理快照组成的指纹；治理快照必须包含 `TimeoutSeconds`。每次 MCP 调用按登记 timeout 执行并在超时时返回稳定 `tool_execution_timeout`，caller cancellation 与宿主停止继续保留取消语义，日志不得包含参数、endpoint 或远端原文。工具删除、schema/hint 漂移、权限/审批/审计/数据边界/schema version/timeout 治理变化、身份冲突或 discovery 失败时必须撤下旧运行时插件并隔离旧登记；即使 `RowVersion` 未变化也不得继续使用陈旧工具。
- 本地非 MCP 工具仍按其正式 capability/risk/审批策略处理；MCP fail-closed 不等于删除必要的本地副作用工具。

## 7. Human-in-the-loop 规则

- Human-in-the-loop 是 AICopilot 自身高风险动作的安全闸门。
- 它不能覆盖 Cloud 业务只读规则。
- 若未来允许调用 Cloud AI-facing API，审批规则必须与 Cloud 权限、Cloud 审计和接口契约一起设计。

### 7.1 逐次工具批准产品边界

- 需要批准的受治理工具必须逐次向用户说明规范工具身份和安全参数摘要，不提供永久批准或“不再询问”。
- 批准只允许当前身份和会话执行当次已展示动作；身份、权限、工具或参数发生变化时必须重新判断并 fail-closed。
- 人工批准不能覆盖 Cloud/MES/ERP 写入、生产控制、越权访问或其它永久硬阻断。Harness 批准协议、受保护绑定和异常时序只由 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) 定义。

## 8. 对话产品规则

- 主产品形态是 Codex-like 对话流，不是任务控制台、试点运营台或系统调试台。
- 普通用户默认只看到用户问题、AI 回答、Plan/Execute 产品模式、批准卡、inline Widget 和安全错误；不得显示“服务端权威”“Harness 主链”等实现术语。
- 实际最终模型、工具调用、安全参数摘要、运行事件和风险细节默认折叠到运行详情。
- 运行详情只能基于本轮 stream/history chunks、消息 metadata 和按会话隔离的运行状态生成安全摘要；不得作为批准、工具执行、Cloud 查询或 Widget 的权威状态源。
- 运行详情不得展开 SQL 原文、连接串、密码、token、sourceName、表/视图名、endpoint、内部字段、原始工具结果行或未脱敏错误原文；工具参数和结果只能展示白名单业务过滤条件、查询次数、返回行数、截断状态、Widget 类型等安全事实。
- DeviceLog 固定段落最终回答可以在前端渲染为结构化结果卡，但只能重排已有回答文本；不得新增指标、补未查询数据、改写模型结论或把普通回答强行套成 DeviceLog 结果页。
- AI 对话中任何可能超过 1 秒的工具调用、DataAnalysis 或 Cloud 只读查询，必须有用户可见、按会话隔离的运行状态；状态只能来自本轮 stream/request/chunk/error/complete 执行事实，不得用假进度、假查询次数或假返回行数填充。
- 前端必须完整展示后端错误契约中的 `code`、`detail`、`userFacingMessage` 和失败类 `AgentEvent` 详情；不得用泛化文案覆盖真实诊断信息。
- 前端会话级运行状态必须按 Session 隔离；新建或切换会话时不得残留其他会话的运行状态、错误、批准或 Widget。
- 批准卡只展示规范工具身份和与运行详情共用的白名单安全参数摘要，不得显示原始参数或“不再询问”；批准/拒绝提交后立即锁定，刷新后以 `/approval/pending` 为唯一权威。
- `Interrupted` / `ResetRequired` 只提供明确的“新建会话”主操作，发送、模式切换和批准保持禁用，不得恢复或自动重放旧 turn。
- 对话会话栏在窄屏使用抽屉；`1920×1080`、`1366×768`、`1024×768` 下工具栏、消息流和固定输入区不得重叠，触控操作命中区域不得小于 40px，light/dark token 必须同步维护。
- 模型推理标签例如 `<mm:think>`、`<think>` 或裸 `mm:think` 不得出现在用户可见正文；如保留，只能进入默认折叠的运行详情。
- `render_payload_json` 只能恢复稳定消息内容，例如文本、Widget 或错误结果；不得作为批准、工具调用或运行状态的权威来源。
- 历史消息按 `Message.Sequence` 分页；`FunctionCall`、`FunctionResult`、`ApprovalRequest` 或 `Metadata` chunk 不得作为普通文本消息重新摊开。
- 开发阶段已物理删除 Trial/Pilot/Production Readiness 运营线；后续不得把旧试点运营能力重新接回普通产品导航。

### 8.1 Plan / Execute 产品语义

- `Plan` 用于帮助用户交互式澄清、调查并形成 Todo；`Execute` 用于自主、连续地完成 Todo。
- Plan / Execute 是行为状态，不是安全隔离或授权边界；模式与授权正交，切换模式不得扩大或缩小用户权限、可用工具、数据边界或批准策略。
- 空态文案和建议必须使用当前行为模式：`Plan` 建议澄清、调查和形成待办，`Execute` 建议连续完成待办；建议操作不得伪造权限变化或替用户隐式切换模式。
- MAF / Harness 的模式持有、模型工具、会话持久化、公开模式接口、裁剪扩展点和升级流程只由 [Agent 工作流与异常契约](./Agent工作流与异常契约.md) 定义，本产品规则不复制实现正文。

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
- `AI架构路线图.md` 只记录当前未完成架构方向、阶段状态和退出门，不保存历史测试数量、任务流水或 Rule ID 账本；历史实现通过 Git 追溯。
- 当前长期专题契约包括 `docs/AICopilot安全部署契约.md`、`docs/Cloud只读数据分析契约.md`、`docs/Agent工作流与异常契约.md` 和 `docs/DDD聚合根边界.md`；触碰部署、Cloud 只读、Text-to-SQL、Harness、MCP/Tool、异常、前端错误、聚合/repository 或 DB owner 时必须先读对应契约。
- 只有修改 `src/vues/AICopilot.Web` 时才读取该目录的 `AGENTS.md`；后端、部署和数据查询任务不得顺带加载前端会话/UI 规则。
- 部署说明只保留 `../deploy/enterprise-ai/README.md`；工作区 `../../deploy/Deploy-Changed.ps1` 和 `../../deploy/Deploy-FromZero.ps1` 是操作入口，`deploy/enterprise-ai` 仅是被统一入口调用的 AI 内部实现与支持目录。
- 阶段计划、批次验收报告、PR 草案和一次性 acceptance 输出不得继续作为执行入口；有效结论必须沉淀到长期规则或部署指南后再清理。
- 清理文档时必须先检查引用，避免留下指向已删除阶段文件的脚本、测试或说明。
- 旧的 Simulation/Real/Sandbox/Pilot 阶段说明只可作为历史材料，不得覆盖当前部署指南和生产验收口径。

## 10. 工程边界

- AICopilot DDD 聚合根、审计、Outbox 和运行时记录的长期技术契约见 `../docs/DDD聚合根边界.md`；新增或调整聚合根、仓储注册、EF `DbSet`、AgentSession 或配额记录时必须同步更新架构测试。
- 生产分层固定为：`src/core` 领域核心，`src/services` 命令、查询与应用编排，`src/infrastructure` EF/Dapper/embedding/event bus/provider/MCP 技术实现，`src/hosts` 只做组合根和启动 wiring，`src/shared` 只放真正共享抽象，`src/vues` 只放前端逻辑；不得跨层回填实现。
- HTTP Controller 必须默认授权，或对确需公开的 action 显式声明匿名。MediatR 普通与 stream 横切行为只允许由 `AddAICopilotMediatRPipeline` 统一注册，顺序固定为 Telemetry → Validation → Authorization；service 模块不得复制注册。stream 授权必须在进入 handler 前完成并逐项透传，禁止预读或缓冲；telemetry 只记录类型、阶段、耗时、结果和异常类型，不得记录 prompt、SQL、token、连接串、API key 或业务明细。
- `ProblemDetails.extensions` 的 `code`、`traceId` 是大小写不敏感的保留键；复制 descriptor extensions 时必须丢弃全部大小写变体，再分别以 descriptor code 和当前请求 trace 写入唯一 canonical 小写键，禁止调用方覆盖或制造歧义键。
- 架构 Analyzer/ArchitectureTests 严格保护分层、聚合、owner 和 Cloud 只读边界；领域、Application、Contract、Persistence、HTTP、UI 与 Eval 业务测试随功能同批正常增删改移。测试清单只描述当前提交实际发现和执行的结果，不提交固定 case 数、required runner roster 或业务覆盖率 baseline。
- `AICopilot.Architecture.Analyzers` 是生产编译的架构 owner；`AIARCH001`–`AIARCH007` 必须保持 `Error + IsEnabledByDefault + NotConfigurable`，CompilationEnd 规则同时保留 `CompilationEnd` tag。Analyzer/Architecture 夹具必须拒绝 `NoWarn`、外部 severity 降级、pragma/suppression 和 Analyzer 关闭。`AIARCH004` 继续证明 enabled Admin 减员在 transaction delegate 内且 invariant guard 先行支配；`AIARCH006` 以当前 `BusinessQueries` 的完全限定 client/provider/profile/context/connector/guard 判定 Cloud root 并检查完整 reachable graph。同名 fake、alias、DTO 或字符串不得扩大例外。
- 跨项目 Analyzer 调用图必须由源生成器输出版本化、定长上限、精确 `producer assembly + contract assembly + documentation method id` 摘要；消费方必须校验数量、producer 身份和全量内容一致。正式 `IAuditLogWriter` 只允许按完全限定 contract identity 截断审计边；`IModelQuotaReservationStore` 只允许 `TryReserveAsync`、`SettleAsync`、`ReclaimExpiredAsync` 三个契约方法截断配额边，且唯一生产实现是 `PostgresModelQuotaReservationStore`，只能经 `AiGatewayTransactionRunner` 写唯一 `AiGatewayDbContext`。
- `AIARCH001` 必须对当前真实的 `AICopilot.*` 生产项目使用显式分类；任何未分类生产项目无论出现在引用源或目标都必须 fail-closed。
- Aggregate runner 只能是 Pure 且只直接依赖 core/shared；Application runner 只能是 Pure 且不得直接依赖 host、EF/Dapper、Aspire/Persistence fixture；文件持久化测试必须进入 `PersistenceFilesystemTests`。五个 TestKit 不得依赖 test SDK、xUnit/NUnit/MSTest 或断言 package，生命周期适配和断言 helper 留在 runner。
- Runner/TestKit 依赖边界只认指定 Configuration 下 MSBuild evaluated `ProjectReference` / `PackageReference` 图，必须包含隐式 `Directory.Build.*`、递归 import、生效复合条件、逐 TargetFramework item 和 TestKit 传递闭包；raw XML 扫描不能作为证据，评估异常或缺失规范化 identity 必须 fail-closed。Direct kind boundary、Pure closure、TestKit consumers 和 production→TestKit 禁令必须复用同一图。
- 测试 runner 必须以项目元数据声明 kind/runtime/owner 并进入 `AICopilot.slnx`；不得用 Phase/Batch/Suite filter 代替物理 owner。默认 lane 只对 selector 选中的 runner 生成 TRX，并要求 `discovered = executed = passed`、`failed = 0`、`skipped = 0`，不保存历史 case 总数。
- compatibility 项跨多个公开 legacy surface 时必须登记完整精确符号并集；同一 caller 命中多个 surface 时按可解析的 distinct caller member 去重，任一 surface 新增 caller 都命中同一既有上限。MCP TestKit executable 与 in-process server 暴露同一 canonical tool 时，名称和只读/破坏性 annotation 必须一致；变更后用精确 FQN 枚举证明恰好 1 项并真实执行对应 E2E。
- Coverage、duplication、mutation 和 compatibility 统一属于用户显式 `Quality` 模式，不进入 push/PR、nightly 或普通部署默认链。运行时必须绑定 clean committed HEAD、当前生产源码/程序集/PDB 和真实 ancestor；报告数量从本次实际执行动态得出，不固定历史 runner/case 数，也不得阻止业务测试随业务同批增删改。
- compatibility baseline 只记录真实兼容/迁移项，bootstrap 后只能删除或收紧 deadline/call-site 上限，禁止新增 ID、扩大调用方或换名回避。普通 abstraction 不进 baseline；inventory 必须证明唯一活跃声明、至少一个真实可执行调用点、`AI-ORDINARY-*` 身份且不含兼容生命周期字段，注释、字符串和声明不算调用方。
- 用户显式 `Quality` 模式下的重复度门禁只治理生产源码，不扫描或冻结 `src/tests`、`src/testing` 与前端测试。生产重复以 `path+line` 计数每个出现实例，同文件重复不得被去重；同时锁定汇总指标和每个 signature 的实例数/重复行/重复 token。base 尚无重复度 baseline 时只允许一次 candidate-exact bootstrap；base 已有 baseline 后只能在真实重复先减少时收紧，不得用总量持平、signature swap、放宽或重生成 baseline 换绿。
- `IAggregateRoot<>` 只用于独立维护业务不变量和生命周期的领域根；`AgentSessionState`、`ModelQuotaReservation`、Outbox、审计和 worker 状态都不得作为新聚合根。
- `DataSourcePermissionGrant` 正式冻结为 DataAnalysis bounded context 的独立聚合根：它拥有独立 `DataSourcePermissionGrantId`、`RowVersion`、授权/撤销生命周期、repository、审计写入和 `(BusinessDatabaseId, TargetType, TargetValue)` 唯一目标约束。它与 `BusinessDatabase` 跨聚合仅引用 `BusinessDatabaseId`，`BusinessDatabase` 不持有授权子实体集合；该归属是正式长期边界。
- `AiCopilotDbContext` 是主基础设施迁移上下文，也是 Outbox 与 persistence commit marker 的唯一 migration owner；`AuditDbContext` 负责审计查询和运行时审计写入，`DataAnalysisDbContext` 只承载数据分析配置，`OutboxDbContext` 与 `PersistenceCommitMarkerDbContext` 只作为运行时短生命周期参与者，不拥有 migration。
- 没有真实事件生产者的 DbContext 不得复制 Outbox `DbSet`、映射或 `SaveChangesAsync` 领域事件扫描；DataAnalysis/MCP 不写 Outbox，AiGateway `Session` 领域事件和 RAG delayed integration-event factory 只能在 repository commit participant 内物化到短生命周期 `OutboxDbContext`，业务 Context 不映射共享 Outbox。
- 审计写入必须遵守 Audit writer decision tree：有业务保存点的命令应把业务变更和审计行放在同一事务；`auditLogWriter.SaveChangesAsync` 只允许出现在没有业务保存点且已被白名单记录的执行路径。
- Outbox 多实例调度必须使用 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略，不能让多 worker 重复发布同一消息。
- 普通 repository 的业务、Outbox、审计和数据库 durable commit marker 必须由唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 在同一数据库事务中提交；commit marker 只保障数据库事务结果验证，不是 Agent durable 编排或 Tool checkpoint。每个 execution-strategy attempt 对业务 Context 只允许一次 `SaveChangesAsync(false)`，事务确认后才 `AcceptAllChanges`、清领域事件或清 RAG factory buffer。Identity 通过 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用同一 engine；非成功 `Result` 必须回滚 UserManager/RoleManager 已触发的所有中间保存，拒绝审计只能在回滚后另行提交，禁止恢复 `EfTransactionalExecutionService` 或复制第二套 transaction/retry。
- EF execution-strategy 必须使用官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 或等价官方入口，禁止手写业务重试循环。commit-unknown 不能用 `SaveChanges(false)`、Outbox 或 audit 是否存在推断成功；必须写入同事务 durable marker，并由 fresh context 在独立超时与 execution strategy 下验证，真实 PostgreSQL 必须覆盖 commit-ACK 丢失、verification transient/persistent failure、caller cancellation 和数据库生成 identity 重放。
- marker 写入后不得再让 caller cancellation 中断 commit/verification；fresh verification 无法确认时返回稳定 503 `persistence_commit_outcome_unknown` 和非敏感 commit id，不自动重放业务。RAG `UploadDocument` 必须先写持久化对账日志再写物理文件，并复用同一 commit id；请求与 DataWorker 通过 PostgreSQL advisory lease 互斥。结果未知时保留文件和日志，后台看到 marker 才保留文件并清日志，看不到 marker 才删除文件。知识库文件唯一写入口是 RAG Document API。
- RAG/AiGateway 数据库绑定上传调用方必须复用唯一 `PersistenceFileCommitProtocol`；repository 未消费预留 commit id 时，确认必须 fail-closed、回滚未提交文件并保留失败信号，不得因 callback 正常返回就清除 journal。
- 标准容器共享卷只允许受信任的 AICopilot 后端写入。当前路径边界拒绝既有 symlink/reparse traversal，但不把同 UID 恶意进程在检查与打开之间替换目录的 TOCTOU 视为已解决；扩大威胁模型前必须增加容器权限隔离或 dirfd/`openat` 原子路径操作。
- 容器必须把 RAG 可写 `FileStorage:RootPath` 固定在共享卷 `/var/lib/aicopilot/storage`，不得回退容器层、`/app`、`LocalApplicationData` 或共享卷外路径。当前 durable local file/journal backend 只支持 Linux/macOS，生产固定 Linux；Windows 必须明确拒绝该 backend。
- HttpApi 与 DataWorker 必须共享 `/var/lib/aicopilot`；commit marker 默认保留 30 天并按 `created_at_utc` 索引，保留期必须长于对账延迟，有待处理日志的 marker 不得删除。对账日志不可读时必须停止 marker 清理。相关改动由 selector 选择真实 PostgreSQL、migration 和部署配置验证；全量仅在用户显式授权时运行。
- MCP runtime 配置与远端 discovery 事实都必须进入 runtime registry refresh cycle；禁用、删除、配置变化、schema/hint 漂移或 discovery 失败后不能继续暴露旧工具解析。
- 身份安全以 security stamp 驱动会话失效；Cloud role 不直接成为 AICopilot 本地 role。
- 多 DbContext 迁移历史必须通过 `__EFMigrationsHistory` 的上下文隔离或迁移历史表拆分规则治理，不能让单一上下文回滚污染其他上下文状态。
- 新增或接线 `IStreamPipelineBehavior` 后，必须核对所有公开 `IStreamRequest` 的 `AuthorizeRequirement`，测试种子角色必须覆盖对应权限；无权限场景应返回干净 401/403，不能表现为 SSE 已写 200 后断流。
- 简单集合转换默认优先用 LINQ 表达意图，`IQueryable` 必须优先下推过滤、投影、排序和分页；热路径、状态机、流式枚举、数组/`Span<T>` 紧循环允许 `for`/`foreach`。
- 工程质量门禁优先抓重复枚举、先物化再过滤、N+1 查询、O(n²) 嵌套、重复扫描和错误数据结构；CA1851 先作为 warning 运行，基线清理后再考虑升级为 error。
