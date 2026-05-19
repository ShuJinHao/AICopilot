# A助理 Agent Runtime 离线 Simulation 功能闭环 Batch 11 交付冻结报告

- 批次：Batch 11 文档、验收和交付冻结
- 日期：2026-05-18
- 范围：AICopilot 后端离线 Simulation Agent Runtime Batch 0-10 交付冻结
- Cloud/Edge 触碰：否
- 前端 `src/vues` 触碰：否
- 真实 Cloud 访问引入：否
- shell 能力引入：否
- 任意服务器路径写入引入：否
- 新增依赖：否
- 数据库迁移：否
- 当前数据源口径：`sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`

## 冻结结论

Batch 0-10 的后端离线 Simulation 闭环已冻结为可验收状态。当前能力覆盖：

- 离线 Simulation 范围冻结、静态护栏、后端验收脚本。
- `CloudReadonly.Mode` 的 `Disabled` / `Simulation` / `Real` 三态配置，其中默认仍为 `Disabled`。
- 集中制造业 Simulation 数据集和隔离 provider resolver 执行路径。
- Planner、Tool Registry、审批、DB 队列、DataWorker、CloudReadonly Simulation 查询、图表、Markdown/HTML/PDF/PPTX/XLSX、workspace draft、FinalReview、FinalOutput approval、finalize、artifact download、审计、tool execution、run queue、worker status 后端闭环。
- ToolCall / FinalOutput / finalize 审批权限硬化。
- 语言模型和向量模型 ApiKey 新写入加密入库、运行时按需解密，旧明文拒绝读取。
- 报告模板、图表 payload 和五类产物来源标记一致性增强。
- 文本类 draft artifact 的受控修改、版本历史、差异查看和回滚。
- MCP 工具治理只读视图，覆盖 allowlist、注册、runtime 可用性和漂移状态。
- Agent Run Queue / DataWorker 的生产运维指标、retry backoff、stale lease fail、幂等 cancel 和运维审计。

## 安全边界

- 当前交付仍是离线 Simulation 闭环，不代表已经完成真实 Cloud 接入。
- 模拟数据不得伪装为真实数据；所有 Simulation 输出必须保留 `sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`。
- `CloudReadonly` 默认仍为 `Disabled`；真实读取必须在后续独立计划中显式启用 `Real`，并满足既有 `CloudAiRead.Enabled=true` 双重开关。
- AICopilot 不写 Cloud 业务数据，不直接写 Cloud 数据库，不通过 MCP、Tool、Agent workflow 或隐藏适配器绕过 Cloud 业务入口。
- 本批未新增 shell 工具能力，未新增任意服务器路径写入入口，未放宽审批、权限、审计或安全边界。
- 前端 `src/vues` dirty 文件属于既有改动，本批未修改、未格式化、未暂存。

## 配置默认值

```json
{
  "CloudReadonly": {
    "Mode": "Disabled",
    "Simulation": {
      "Enabled": false,
      "SeedData": true,
      "DataSet": "ManufacturingDemo",
      "AlwaysMarkAsSimulation": true
    },
    "Real": {
      "Enabled": false,
      "AllowProductionRead": false
    }
  },
  "AgentRunQueue": {
    "LeaseDurationSeconds": 300,
    "HeartbeatActiveWindowSeconds": 30,
    "MaxRetryAttempts": 3,
    "RetryBackoffSeconds": 30,
    "MaxRetryBackoffSeconds": 300,
    "StaleLeaseAction": "Fail"
  }
}
```

## 验收结果

- PASS：`dotnet build .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|Suite=Batch7ReportArtifacts|Suite=Batch8ArtifactVersioning"`，15/15
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch9McpToolGovernance|Suite=Batch10RunQueueOps"`，12/12
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=ToolRegistryGovernance"`，42/42
- PASS：`dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentSimulationAcceptance"`，4/4
- PASS：`.\scripts\Run-AgentSimulationAcceptance.ps1`
- PASS：`.\scripts\Test-AgentSimulationScope.ps1 -ChangedFiles <Batch11文档清单>`

## 后续进入条件

- 真实 Cloud 接入：必须另起计划，明确真实只读 API/数据域、权限、审计、回退、配置开关和验收数据，不得混入当前 Simulation 冻结批次。
- 真实前端联调：必须另起计划，先确认是否允许处理现有 `src/vues` dirty 文件，再做 UI/接口联调。
- 发布候选验收：必须在目标环境重新执行完整验收脚本，并以最新生成报告为准。
- 自动恢复 stale lease 或外部告警推送：必须另起运维增强计划，先设计去重、产物幂等和告警通道。

## 剩余风险

- 当前报告冻结的是后端离线 Simulation 闭环，不覆盖真实 Cloud 数据质量、真实前端交互质量或生产部署容量。
- 既有前端 dirty 文件仍未纳入本批处理。
- 旧数据库中的明文模型密钥不兼容读取，管理员需要通过管理接口重新保存密钥。
- MCP 治理视图只治理已同步结果，本阶段不主动连接外部 MCP server、不执行 MCP 工具。
