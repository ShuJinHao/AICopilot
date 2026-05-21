# A助理 Agent 工作台 Simulation 前后端联调验收报告

日期：2026-05-19

## 验收目标

用真实前端页面和 AICopilot 后端 Simulation Runtime 跑通 Agent 工作台产品闭环。

## 必验场景

- 会话创建、模型选择、普通消息发送、历史消息恢复。
- 上传文件、选择知识库、创建 `CloudDataReport` Agent 任务。
- 展示 `plannerMode`、`riskLevel`、`steps`、`toolCode`、`requiresApproval`、`cloudReadonlyIntent`。
- Plan approval、ToolCall approval、FinalOutput approval 均进入后端审批链路。
- 任务入队后展示 `runQueueStatus`、`isRunQueued`、`isRunInProgress`。
- DataWorker 执行后生成 `charts/chart-data.json`、`draft/report.md`、`draft/report.html`、`draft/report.pdf`、`draft/report.pptx`、`draft/report.xlsx`。
- 图表、报告、审计和 tool execution 均展示 `sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`。
- 文本类 draft artifact 支持编辑、版本、diff、restore；final review 之后禁止继续编辑。
- final artifact 只能通过后端 `downloadUrl` 下载。
- Run Queue、Worker Status、MCP、Tool Registry、审计视图可查看。

## 当前状态

- 前端构建通过。
- 前端 unit 和 smoke 通过。
- 后端 Simulation acceptance 与发布候选硬化 suite 通过。
- smoke mock 已覆盖 Run Queue、Worker Status 和 Simulation 标签。
- 真实 HttpApi + DataWorker + Web UI 联调已启动并通过核心闭环。

## 2026-05-19 Integration 联调记录

### 环境

- 分支：`integration/aicopilot-agent-workbench-simulation`
- 基线：`origin/main @ eb75372`
- 来源：已合并 #46 后端 Simulation Runtime 与 #47 Agent Workbench Simulation RC。
- 运行方式：Aspire AppHost 临时 fresh Docker 环境，`AppHost__PersistentContainers=false`。
- Dashboard：`http://localhost:15132`
- HttpApi：`http://localhost:5181`
- Web UI：`http://localhost:54253`
- CloudReadonly：运行时临时启用 `CloudReadonly__Mode=Simulation`。
- CloudAiRead：`CloudAiRead__Enabled=false`。
- Real CloudReadonly：未启用。
- Docker：Linux Docker 可用；本次没有 Docker skip。

首次 AppHost 启动复用了本机已有持久化 Postgres 卷，因旧卷密码与本次临时参数不一致，Postgres 被 Aspire 标为 unhealthy，HttpApi/DataWorker 等待。处理方式是不删除、不重置旧卷，停止该 AppHost 后改用 `AppHost__PersistentContainers=false` 启动临时 fresh 环境。

### 自动验证

- PASS：`dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`
- PASS：`dotnet build src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj`
- PASS：`npm run build`
- PASS：`npm run test:unit`，11 个文件 / 42 个测试。
- PASS：`npm run test:smoke`，15 passed / 3 skipped。
- PASS：7 个后端 suite 分开执行：
  - `AgentSimulationAcceptance`：4/4
  - `Batch5ApprovalHardening`：3/3
  - `Batch6SecretProtection`：6/6
  - `Batch7ReportArtifacts`：2/2
  - `Batch8ArtifactVersioning`：5/5
  - `Batch9McpToolGovernance`：4/4
  - `Batch10RunQueueOps`：8/8
- PASS：`scripts/Run-AgentSimulationAcceptance.ps1`，含 Docker acceptance。
- PASS：`scripts/Test-AgentWorkbenchReleaseCandidateScope.ps1 -BaseRef origin/main`
- PASS：`scripts/Test-TextEncoding.ps1`
- PASS：`git diff --check`
- PASS with note：`npm audit --registry=https://registry.npmjs.org --audit-level=high` 返回 0；仍报告 `brace-expansion` 1 个 moderate 级别告警，非 high 阻断。

合并 7 个 suite 到同一个 `dotnet test` OR filter 进程时出现过 1 次 `429 Too Many Requests`，失败用例为 `AgentApprovalPermissionHardeningTests.PrivilegedApprover_ShouldCrossUserApproveToolFinalOutput_AndFinalize`。按 CI workflow 的分 suite 执行方式重跑后全部通过，记录为验证跑法噪声。

### Simulation 任务闭环

- 登录：真实 HttpApi，本地 admin 账号。
- 模型/模板：创建联调用 OpenAI 配置与会话模板，前端模型选择展示 `OpenAI / integration-simulation-lm-*`。
- Agent task：创建 `CloudDataReport`/Simulation 周报任务。
- Plan approval：后端审批链路通过。
- Run Queue：任务入队，DataWorker 领取执行。
- ToolCall approval：逐步审批 `query_cloud_data_readonly`、报告产物生成工具。
- CloudReadonly Simulation：tool execution 输出包含 `sourceMode=Simulation`、`isSimulation=true`、`sourceLabel=模拟 Cloud 只读数据`。
- Artifact：生成 6 个产物：
  - `charts/chart-data.json`
  - `draft/report.md`
  - `draft/report.html`
  - `draft/report.pdf`
  - `draft/report.pptx`
  - `draft/report.xlsx`
- FinalOutput approval：通过。
- Finalize：workspace 状态变为 `Finalized`，final artifact 下载成功。
- 任务：`task_20260519014845_7bd0187e647c441188`
- 工作区：`ws_20260519014845_09f24aa3dd9849dc9c66`

### 前端验证

- 真实 Web UI 登录成功。
- `/chat` 展示任务历史、Completed 状态、workspace code、模型选择、工作台统计、产物列表和 Simulation 标签。
- 产物 tab 展示图表预览和 6 个 artifact。
- UI 点击 `下载产物` 下载 `chart-data.json` 成功，文件大小 1845 bytes。
- `/config` Agent tab 展示 `Run Queue`、`Worker Status`、Active Worker、Workspace 一致性。
- `aicopilot-dataworker:*` 心跳健康，workspace 显示一致。

### 上传 / RAG 验证

- 创建联调用 fake embedding model，base URL 指向本地 fake OpenAI-compatible embedding endpoint。
- 创建知识库 `Integration RAG 1779155603010`。
- 上传 `line-a-simulation-rag.txt`。
- RagWorker 完成索引，文档状态为已入库，chunkCount=1。
- `/knowledge` 页面展示知识库、文档、治理标签。
- 检索 `LINE-A 产能波动` 返回 1 条结果，score≈1.0，命中文本包含“设备维护窗口”。

## 未进入范围

- 未启用 Real CloudReadonly。
- 未连接真实 Cloud 生产数据。
- 未修改 Cloud/Edge 仓库。
- 未开放 shell 或任意服务端路径写入。
