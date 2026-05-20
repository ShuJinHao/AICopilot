# A助理企业级数据源与知识治理 P4 范围冻结

## 阶段定位

P4 只修改 `AICopilot`，目标是把 P3 动态 Planner 背后的工具能力收口为企业级 Tool Registry / Mock MCP 治理基线。
本阶段不修改 `IIoT.CloudPlatform`、不修改 `IIoT.EdgeClient`、不启用 Real CloudReadonly、不接真实 Cloud 生产数据、不连接真实外部 MCP server。

## 固定边界

- 执行业务数据源仍只允许 `SimulationBusiness`。
- 动态 Planner 只能看到当前用户可用、已启用、符合任务上下文和数据边界的工具摘要。
- 工具执行必须经过后端 tool code、schema、权限、风险等级、审批策略和数据边界校验。
- `Critical` 风险工具在 P4 只能登记和审计展示，默认不可执行。
- P4 默认只允许内置工具和 in-process Mock MCP provider。
- 不提供真实外部 MCP endpoint 默认启用入口。
- 不开放 shell，不允许任意服务器路径读写，不允许 Cloud 写语义。
- 不输出明文 API Key、连接串、token、密码、完整敏感 SQL 或完整敏感上下文。

## 本阶段能力

- Tool Registry 增加分类、业务域、provider 类型、风险等级、schema 版本、catalog version、审批策略、数据边界、Planner 可见性和 Agent 可执行性。
- 新增工具数据边界：`NoData`、`SimulationBusinessOnly`、`RagContextOnly`、`ArtifactDraftOnly`。
- 新增 Mock MCP provider，内置四个 mock 工具：
  - `mock_mcp_health_check`
  - `mock_mcp_kpi_formula_lookup`
  - `mock_mcp_artifact_quality_check`
  - `mock_mcp_external_ticket_preview`
- Mock 工具调用返回 `isMock=true`、`providerKind=MockMcp`、`toolRunId`、`toolCatalogVersion`、耗时、状态和 `resultHash`。
- Agent 计划文档记录工具目录版本、可见工具数量、风险摘要、Mock MCP only 标识和工具审批点。
- 前端配置页增加 Tool Registry 治理入口，Agent 工作台计划预览展示工具目录、风险摘要和 Mock MCP 状态。

## 验收入口

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseToolGovernanceP4Acceptance.ps1
```

验收报告输出：

```text
docs/enterprise-tool-governance-p4-latest.md
```

## 进入下一阶段条件

- P3 继承验收通过。
- P4 scope guard 通过，确认 Cloud/Edge 未改、Real CloudReadonly 默认禁用、真实外部 MCP 默认未启用。
- P4 后端聚焦测试通过。
- 前端 build 和 Tool Registry smoke 通过。
- 验收报告记录工具目录版本、Mock MCP 调用样例、审批样例、非法工具拒绝样例、Agent 闭环结果和剩余风险。
