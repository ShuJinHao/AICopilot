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

## Unified Agent Workflow

- `AgentWorkflowPipeline` 是 AICopilot 用户输入的统一工作流主干；当前旧名 `ChatWorkflowOrchestrator` 只可作为待消歧历史名，不代表“仅聊天可用”。
- Chat 模式和 Plan 模式都必须复用统一管线的意图理解、上下文编排和能力发现；两者只能在出口行为不同。
- Chat 出口可以按现有安全策略直接生成回答，或执行已允许的低风险只读动作。
- Plan 出口只能生成 `PlanDraft` 计划草案；用户确认前不得执行 Cloud 查询、MCP 工具、Tool 调用、Worker 入队或其他真实业务动作。
- `PlanAgentTaskCommand` 只能负责计划草案/任务状态的持久化和编排入口，不得独立实现意图理解、工具发现、Skill 选择或 Tool catalog 强校验。
- Skill、Tool、MCP 或 DataSource 未匹配时，不得阻断 `PlanDraft` 生成；只能在草案里说明能力缺口或要求用户补充目标。
- 用户确认 `PlanDraft` 后，才允许转换为 `ExecutablePlan` / `AgentTask`，并进入 Skill、Tool、Schema、Guard、审批和 Worker 执行链路。

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
- 模型、prompt、plugin、MCP server、approval threshold 等运行行为优先用配置或明确存储数据，不藏在代码里。
- 容器部署必须显式配置并挂载可写的 `FileStorage:RootPath` 和 `ArtifactWorkspace:RootPath`；不得依赖容器内 `LocalApplicationData` 默认路径或 `/app` 目录写入运行产物。

## Execution

改动前确认：

- 触碰能力：routing、RAG、SQL analysis、MCP、approval、host wiring、frontend、persistence。
- 是否只在 `AICopilot` 内。
- 是否会暗示 Cloud 或 Edge 也要改。
- 是否涉及 Cloud 业务写入边界。

业务不清楚时先问，不猜。
