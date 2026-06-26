# AICopilot 业务规则

本文档约束 `AICopilot` 自身业务边界。工作区总规则见 `../../docs/总规则.md`。

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

## 6. MCP 规则

- MCP 是受控工具入口，不是 Cloud 业务写入口。
- 工具描述必须说明是否只读。
- 涉及文件、外部系统、命令执行或其他副作用的工具必须保持审批约束。
- 不允许配置直接或间接调用 Cloud 写接口的 MCP 工具。

## 7. Human-in-the-loop 规则

- Human-in-the-loop 是 AICopilot 自身高风险动作的安全闸门。
- 它不能覆盖 Cloud 业务只读规则。
- 若未来允许调用 Cloud AI-facing API，审批规则必须与 Cloud 权限、Cloud 审计和接口契约一起设计。

## 8. 文档入口

- 长期规则入口只保留 `AGENTS.md`、本文档和工作区 `docs/历史核心记录.md`。
- 部署入口只保留 `AICopilot 项目部署与维护指南.md` 和 `deploy/enterprise-ai`。
- 阶段计划、批次验收报告、PR 草案和一次性 acceptance 输出不得继续作为执行入口；有效结论必须沉淀到长期规则或部署指南后再清理。
- 清理文档时必须先检查引用，避免留下指向已删除阶段文件的脚本、测试或说明。
- 旧的 Simulation/Real/Sandbox/Pilot 阶段说明只可作为历史材料，不得覆盖当前部署指南和生产验收口径。

## 9. 工程边界

- `AiCopilotDbContext` 是主基础设施迁移上下文，`AuditDbContext` 负责审计查询和运行时审计写入，`DataAnalysisDbContext` 只承载数据分析配置，`OutboxDbContext` 承载 outbox。
- 审计写入必须遵守 Audit writer decision tree：有业务保存点的命令应把业务变更和审计行放在同一事务；`auditLogWriter.SaveChangesAsync` 只允许出现在没有业务保存点且已被白名单记录的执行路径。
- Outbox 多实例调度必须使用 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略，不能让多 worker 重复发布同一消息。
- MCP runtime 配置变更必须进入 runtime registry refresh cycle，禁用、删除或配置变更后不能继续暴露未来工具解析。
- 身份安全以 security stamp 驱动会话失效；Cloud role 不直接成为 AICopilot 本地 role。
- 多 DbContext 迁移历史必须通过 `__EFMigrationsHistory` 的上下文隔离或迁移历史表拆分规则治理，不能让单一上下文回滚污染其他上下文状态。
