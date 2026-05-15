# A助理 Agent 产品化工作台验收报告

生成时间：2026-05-14 16:31

## 验收范围

- 本轮只改 `AICopilot`，未修改 `IIoT.CloudPlatform` 和 `IIoT.EdgeClient`。
- Cloud 业务数据继续保持只读；Agent、MCP、隐藏适配器仍不允许写 Cloud 业务。
- 当前 Cloud/Edge 仓库本身存在既有 dirty worktree，本轮仅做边界检查，没有 reset、checkout、覆盖或恢复这些改动。

## 本轮交付

- `/chat` 从最小 Agent 面板升级为三栏产品化工作台：
  - 左侧：会话列表和 Agent 任务历史。
  - 中间：对话、模型状态、任务状态、消息流和输入区。
  - 右侧：Agent 任务概览、审批队列、步骤时间线、产物预览/下载、审计摘要、运行边界和数据来源。
- 新增前端工作台级 composable，把 task、workspace、approval、artifact、audit summary 聚合成稳定 UI 状态，避免复杂判断散落在模板里。
- 刷新恢复能力补齐：进入 `/chat` 后自动恢复当前会话、最新 task、workspace、pending approval、artifact、chart preview 和 audit summary。
- 产物体验补齐：产物卡片统一展示类型、状态、版本、生成步骤、预览类型、下载入口和 finalize 状态；图表数据在前端预览，PDF/PPTX/XLSX 保持下载不在线编辑。
- 审批体验补齐：工作台内合并展示计划/工具/产物/finalize 审批队列，支持批准、驳回和任务继续入口；驳回后任务进入停止或可重试状态，不继续后续步骤。
- `/config` 增强 Agent/Workspace 只读运行摘要，展示工作区根目录、固定目录、允许产物类型、历史参数、审批策略入口、最近验收时间和禁止任意服务器路径写入说明。
- 前端 smoke mock 和 smoke 用例更新，覆盖登录、受保护路由、三栏工作台恢复、审批队列、产物卡片、审计摘要、聊天流组件、移动端无横向溢出。

## 自动化验收结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过，0 warning，0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj`：通过，419/419。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44/44。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6/6。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。
- `cd src/vues/AICopilot.Web && npm run test:unit`：通过，39/39。
- `cd src/vues/AICopilot.Web && npm run build`：通过；仅保留既有 Rollup pure-annotation 和 chunk-size warning。
- `cd src/vues/AICopilot.Web && npm run test:smoke`：通过，14 passed，2 skipped。跳过项为桌面/移动端互斥视口用例。
- `powershell -ExecutionPolicy Bypass -File scripts/Run-AcceptanceClosure.ps1 -ReportPath 资料/acceptance-closure-latest.md`：通过，报告内所有步骤均为 `PASSED`。

最新脚本报告：`资料/acceptance-closure-latest.md`，生成时间 2026-05-14 16:31:25。

## 人工验收路径

1. 打开 `/chat`，确认左侧会话/任务历史、中间对话区、右侧 Agent 工作台同时可用。
2. 刷新页面，确认当前会话、最新任务、workspace code、pending approval、artifact、chart preview 和 audit summary 自动恢复。
3. 上传 CSV/JSON/XLSX，生成 Agent 计划，批准计划后执行低风险步骤。
4. 遇到高风险工具或最终输出确认时，在审批队列中批准或驳回；驳回后确认后续步骤不会继续执行。
5. 检查产物卡片的类型、状态、版本、生成步骤、预览类型、下载入口和 finalize 状态。
6. finalize 前确认 `final/` 不出现正式产物；finalize 后确认正式产物可下载，任务审计摘要可查。
7. 打开 `/config`，确认 Agent/Workspace 配置摘要只读展示，且不提供任意服务器路径配置入口。
8. 在移动端宽度打开 `/chat`，确认主工作区不横向溢出，右侧复杂工作台按设计收起。

## 剩余边界

- 本轮不新增后端大能力，不新增 Agent 后台自主运行、多 Agent 协作、复杂模板系统或在线 Office/PDF 编辑器。
- PDF/PPTX/XLSX 继续作为受控草稿/正式产物下载，不在线编辑。
- 工作区写入仍只能走 `IArtifactWorkspaceFileStore` 和固定目录结构，不开放 shell、任意服务器路径或 Cloud 写接口。
- Cloud/Edge dirty worktree 已观察但未修改；后续 Cloud/Edge 处理需要单独授权。
