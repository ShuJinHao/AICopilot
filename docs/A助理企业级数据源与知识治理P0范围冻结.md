# A助理企业级数据源与知识治理 P0 范围冻结

## 范围

- 仅修改 `AICopilot`。
- 不修改 `IIoT.CloudPlatform`。
- 不修改 `IIoT.EdgeClient`。
- 不接真实 Cloud 生产数据。
- 不启用 `Real CloudReadonly`。
- 不开放 shell 工具。
- 不允许任意服务器路径写入。

## 本轮能力

- 新增独立 `SimulationBusiness` 数据源类型，不复用现有 `CloudReadonly Simulation` 口径。
- 新增业务数据源企业元数据：分类、标签、业务域、敏感级别、负责人部门、默认/最大查询限制、Chat/Agent 可选开关。
- 新增业务库只读查询结果契约，所有 SimulationBusiness 查询结果必须带：
  - `sourceType=BusinessDatabase`
  - `sourceMode=SimulationBusiness`
  - `isSimulation=true`
  - `sourceLabel=AI 独立模拟业务库`
- 新增可重复生成的 PostgreSQL 模拟业务库 seed plan，默认 `Medium` profile。
- 新增 DataSource 权限集：`DataSource.Read`、`DataSource.Query`、`DataSource.Manage`、`DataSource.SchemaView`、`DataSource.AuditView`。
- 新增 RAG 分类、版本字段、软删除状态和补充说明模型。
- 新增模型 endpoint/pool 快照与调度底座，API Key 只允许写入和脱敏状态读取。

## 静态护栏

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-EnterpriseDataGovernanceScope.ps1 -IncludeWorkingTree
```

护栏检查：

- Cloud/Edge 路径不进入本轮 diff。
- `CloudReadonly.Mode` 默认保持 `Disabled`。
- `CloudAiRead.Enabled` 默认保持 `false`。
- 不新增 shell 执行能力。
- 不在非授权文件中新增危险 SQL 模式。
- 不输出明文 API Key、连接串、token、密码。
- `SimulationBusiness`、查询 hash、Simulation 来源标识不可缺失。

## 验收入口

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-EnterpriseDataGovernanceP0Acceptance.ps1
```

该入口包含 scope guard、后端 build、聚焦后端测试和前端 build。生成报告默认写入：

```text
docs/enterprise-data-governance-p0-latest.md
```
