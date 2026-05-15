# A助理 Agent 发布审查 PR 草稿

## 标题

`aicopilot: deliver controlled A Assistant agent workbench`

## 摘要

本 PR 将 AICopilot 从助手式问答扩展为受控 Agent 产物工作台，范围仅限 `AICopilot`。Cloud 业务数据保持只读；没有修改 `IIoT.CloudPlatform` 或 `IIoT.EdgeClient`；没有引入任意 shell、任意服务器路径写入、后台自主 Agent、在线 Office/PDF 编辑器或 Cloud 写接口。

## 本地提交分组

- `88782a8 aicopilot: add dynamic model routing`：动态模型协议、路由模型配置、连接测试、实际模型元数据回传。
- `89e38f8 aicopilot: govern prompts and session memory`：A助理内置模板治理、会话标题/摘要/消息计数、运行历史配置。
- `a9edaa0 aicopilot: bind uploads to rag sources`：上传记录、知识库入库桥接、RAG 来源字段和低置信度提示。
- `8536416 aicopilot: add controlled agent artifacts`：AgentTask、审批请求、受控工作区、产物生成、下载/finalize 审计。
- `aa9277f aicopilot: wire aigateway agent persistence`：AiGateway API、EF 配置、迁移、DbContext 和依赖注入接线。
- `ffc19d1 aicopilot: productize agent workbench ui`：`/chat` 三栏工作台、移动端抽屉、产物/审批/审计展示、`/config` 可见性。
- `e4056e4 aicopilot: add agent acceptance coverage`：后端验收、AI 安全、前端单测/smoke、编码检查和最终验收脚本。
- `docs: prepare aicopilot review package`：交付报告、审查清单、PR 草稿和阶段记录。

## 主要接口与数据变化

- 新增或扩展 AiGateway 能力：`runtime-settings`、`template/reset-builtins`、`session/rename`、`upload`、`agent/task/*`、`agent/approval/*`、`workspace/{code}`、`artifact/{id}/download`、`workspace/{code}/finalize`、`agent/task/{id}/audit-summary`。
- 新增 AiGateway 持久化：动态模型/路由配置、Prompt 治理、会话元数据、运行配置、上传记录、Agent 任务、步骤、审批请求、工作区和产物。
- 新增基础产物生成：Markdown、HTML、CSV/JSON 表格数据、图表数据、PDF、PPTX、XLSX 草稿；正式输出仍必须通过 workspace finalize。
- 新增稳定依赖：`DocumentFormat.OpenXml 3.5.1`、`PDFsharp-MigraDoc 6.2.4`，漏洞扫描通过。

## 验证结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过，0 警告 0 错误。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj`：419/419 通过。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：44/44 通过。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：6/6 通过。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击包。
- `cd src/vues/AICopilot.Web && npm run test:unit`：39/39 通过。
- `cd src/vues/AICopilot.Web && npm run build`：通过，仅既有 Rollup annotation/chunk-size 警告。
- `cd src/vues/AICopilot.Web && npm run test:smoke`：14 个可运行用例通过，2 个视口限定用例跳过。
- `powershell -ExecutionPolicy Bypass -File scripts/Test-TextEncoding.ps1`：通过。
- `git diff --check`：通过。
- `powershell -ExecutionPolicy Bypass -File scripts/Run-AcceptanceClosure.ps1 -ReportPath 资料/acceptance-closure-latest.md`：所有步骤 `PASSED`，报告生成时间 `2026-05-15 09:15:26`。

## 人工验收建议

1. 登录 `/chat`，确认桌面三栏工作台和移动端 Agent 抽屉可用。
2. 上传 CSV/JSON/XLSX，生成计划，确认计划批准前不执行工具。
3. 批准计划并运行，确认低风险工具执行、高风险工具进入审批队列。
4. 驳回审批，确认任务停止或进入可重试状态。
5. 批准产物和 finalize，确认正式产物只在 finalize 后进入 `final/`。
6. 查看审计摘要，确认计划、审批、工具、产物、下载、finalize 均可追踪。
7. 登录 `/config`，确认 Agent/Workspace 配置只读展示且不允许配置任意服务器路径。

## 剩余边界

- 不包含完整企业权限分层、长期后台自主 Agent、多 Agent 协作、复杂模板系统或在线编辑器。
- PDF/PPTX/XLSX 是基础导出草稿，不是高保真模板引擎。
- Cloud/Edge 对齐、远端 push、GitHub PR 创建和审查反馈处理需要单独授权。
