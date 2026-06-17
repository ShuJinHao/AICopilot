# A助理前后端 Integration 分支说明

日期：2026-05-19

> 历史记录：本文只记录 2026-05-19 的 Simulation 联调分支状态，不再作为当前部署、Cloud 只读或生产验收入口。当前口径以 `../AICopilot 项目部署与维护指南.md`、`../AGENTS.md`、`../资料/AICopilot业务规则.md` 和 `frontend-integration-contract-package-2026-05-17.md` 为准。

## 分支

当前分支：`integration/aicopilot-agent-workbench-simulation`

创建基线：

```text
origin/main @ eb75372
+ 已合并后端 Simulation Runtime PR #46
+ 已合并 Agent Workbench Simulation RC PR #47
```

## 运行边界

- Integration 分支只用于联调和验收，不直接代表最终主线。
- 不接真实 Cloud 生产数据。
- `CloudReadonly.Mode` 默认仍保持 `Disabled`，联调运行时通过环境变量或联调配置显式启用 `Simulation`。
- `CloudAiRead.Enabled=false` 默认不变。
- 不通过普通 Real CloudReadonly 双轨入口接生产 Cloud 数据；真实读取后续必须走 Cloud AiRead / P12 / P13 受控入口。
- Cloud/Edge 仓库已有脏改不纳入本分支。

## 联调配置

```json
{
  "CloudReadonly": {
    "Mode": "Simulation",
    "Simulation": {
      "Enabled": true,
      "SeedData": true,
      "DataSet": "ManufacturingDemo",
      "AlwaysMarkAsSimulation": true
    }
  },
  "CloudAiRead": {
    "Enabled": false
  }
}
```

本次本地联调使用 `AppHost__PersistentContainers=false` 启动临时 fresh Docker 资源，避免复用本机已有 `postgres-aicopilot` 持久化卷。未删除、重置或修改任何既有 Docker volume。

运行入口：

- Dashboard：`http://localhost:15132`
- HttpApi：`http://localhost:5181`
- Web UI：`http://localhost:54253`

## 验收入口

- 前端：`npm run build`、`npm run test:unit`、`npm run test:smoke`
- 后端：`scripts/Run-AgentSimulationAcceptance.ps1`
- Scope guard：`scripts/Test-AgentWorkbenchReleaseCandidateScope.ps1`

## 2026-05-19 联调状态

- Docker：可用，Linux 模式。
- AppHost：临时 fresh 环境启动成功，`aicopilot-httpapi`、`data-worker`、`rag-worker`、`aicopilot-webui` 均运行。
- Simulation Runtime：已通过真实 HttpApi + DataWorker 跑通 CloudReadonly Simulation 任务闭环。
- 前端：已用真实 Web UI 登录、查看任务、产物、Run Queue、Worker Status，并通过 UI 下载 artifact。
- RAG：已用本地 fake embedding endpoint 验证知识库创建、文档上传、索引、检索预览。
- Real CloudReadonly：未启用。
