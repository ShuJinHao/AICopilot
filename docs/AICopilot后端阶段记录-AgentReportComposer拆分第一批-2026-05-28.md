# AICopilot 后端阶段记录 - AgentReportComposer 拆分第一批 - 2026-05-28

## 本批目标

- 只改 `AICopilot` 后端。
- 拆分 `AgentReportComposer.cs`，把 report document、Markdown、HTML、Chart payload 和 formatting helper 从单体 composer 中剥离。
- 保持 `AgentReportComposer` 的 public static 入口、artifact/report 输出语义、chart v2 payload 和 source metadata 语义不变。
- 不新增 artifact 类型，不改 MCP 部署，不触碰 Cloud/Edge/UI/数据库迁移/部署编排。

## 实际改动

- `AgentReportComposer.cs` 收敛为薄门面，仅保留 `BuildReportDocument`、`BuildMarkdownReport`、`BuildHtmlReport`、`BuildChartPayload` 四个既有入口。
- 新增 `AgentReportDocumentBuilder.cs`，承接 report document、CloudReadonly source/table 和 metrics 构建。
- 新增 `AgentReportMarkdownRenderer.cs`，承接 Markdown report、Markdown table、BusinessQuery 和 CloudSandbox markdown sections。
- 新增 `AgentReportHtmlRenderer.cs`，承接 HTML report、HTML table、BusinessQuery 和 CloudSandbox html sections。
- 新增 `AgentReportChartPayloadBuilder.cs`，承接 chart v2 payload、numeric series、labels 和 sourceInfo。
- 新增 `AgentReportFormatting.cs`，承接 shared escape、bool/date/number/cell formatting 和 row limit 常量。

## 影响模块

- 项目：`AICopilot`
- 模块：`src/services/AICopilot.AiGatewayService/AgentTasks`
- 能力边界：Agent task report/artifact 组装内部结构拆分。
- 公开契约：未修改 `AgentReportDocument`、`AgentReportTable`、`AgentReportMetric`、`AgentReportSourceInfo` 等 contracts。
- 运行行为：未修改 chart `schemaVersion/type`、source marker、queryHash、rowCount、truncated、BusinessQuery/CloudSandbox section 文案和 artifact source metadata 语义。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO、数据库迁移、部署编排、MCP 配置或 DI 注册。
- 未创建分支、worktree、commit 或 PR。

## 验证命令与结果

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~AgentReportComposerTests|FullyQualifiedName~EnterpriseAgentWorkbenchP2Tests|FullyQualifiedName~ToolRegistryGovernanceTests|FullyQualifiedName~AgentArtifactGenerationTests|FullyQualifiedName~EnterpriseDynamicPlannerP3Tests|FullyQualifiedName~AgentRunQueueProductionOpsTests"
```

- 结果：通过 69 / 69，失败 0，跳过 0。

```bash
dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore
```

- 结果：通过 759 / 759，失败 0，跳过 0，耗时 4 m 42 s。

```bash
dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore
```

- 结果：通过 44 / 44，失败 0，跳过 0。

```bash
wc -l src/services/AICopilot.AiGatewayService/AgentTasks/AgentReport*.cs
```

- `AgentReportComposer.cs`：27 行。
- `AgentReportChartPayloadBuilder.cs`：137 行。
- `AgentReportDocumentBuilder.cs`：157 行。
- `AgentReportFormatting.cs`：101 行。
- `AgentReportHtmlRenderer.cs`：142 行。
- `AgentReportMarkdownRenderer.cs`：141 行。

## 剩余风险

- 本批只处理 `AgentReportComposer.cs`，没有处理 `AgentApprovalManagement.cs`、`AgentDynamicPlanner.cs` 和 workspace/tooling 侧更大的结构债。
- Report 输出逻辑仍是静态 helper 组织，未引入 DI 或可替换策略；这是为了保持本批为低风险搬移式拆分。
- 后续如果新增 artifact/report 类型，应优先落到对应 renderer 或 payload builder，避免重新堆回 composer。

## 下一阶段进入条件

- 从当前绿色基线继续：`BackendTests 759/759`、`ArchitectureTests 44/44`。
- 下一批建议继续在 `AgentTasks` 内处理 `AgentApprovalManagement.cs` 或 `AgentDynamicPlanner.cs`；如果转向全仓最大债务，应单独规划 `ArtifactVersioningManagement.cs`。
- 如果下一批涉及审批状态机、ToolRegistry data boundary、artifact source metadata 或公开 contracts，只允许在明确测试覆盖下迁移结构，不允许改变语义。
