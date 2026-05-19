# A助理 Agent 发布候选硬化报告

生成时间：2026-05-14 17:10

## 验收范围

- 本轮只改 `AICopilot`，未修改 `IIoT.CloudPlatform` 和 `IIoT.EdgeClient`。
- Cloud 业务数据继续保持只读；Agent、MCP、隐藏适配器仍不允许写 Cloud 业务。
- 本轮不新增 Agent 后端能力、不新增数据库迁移、不新增依赖，重点是中文文案、防回归检查、移动端工作台可用性和发布候选验收路径。

## 本轮交付

- 中文文案治理：确认 AICopilot 前端源码和交付文档以 UTF-8 保存；用户可见中文恢复为可读语义，技术标识、API、路由和类型名保持英文。
- 文案防回归：新增 `scripts/Test-TextEncoding.ps1`，扫描前端源码、smoke mock 和 `资料/` 文档中的常见乱码标记；最终验收脚本已接入该检查。
- Smoke 语义断言：前端 smoke 恢复对登录页、受保护页面、Agent 工作台、审批、产物、审计摘要、运行边界等关键中文语义的断言，不再只依赖结构存在。
- 移动端工作台硬化：`/chat` 保持桌面三栏布局；移动端新增 `Agent 工作台` 折叠入口，可访问任务、审批、产物、审计和运行边界，且通过无横向溢出检查。
- 配置页收口：`/config` 的 Agent/Workspace 区继续只读展示工作区根目录、固定目录、允许产物类型、历史参数、审批策略入口、最近验收记录和禁止任意路径写入说明。
- 交付收口：`资料/acceptance-closure-latest.md` 已刷新，`资料/stage-delivery-status.md` 已追加 Phase 11 记录。

## 自动化验收结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过，0 warning，0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj`：通过，419/419。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44/44。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6/6。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。
- `cd src/vues/AICopilot.Web && npm run test:unit`：通过，39/39。
- `cd src/vues/AICopilot.Web && npm run build`：通过；仅保留既有 Rollup pure-annotation 和 chunk-size warning。
- `cd src/vues/AICopilot.Web && npm run test:smoke`：通过，14 passed，2 skipped。跳过项为桌面/移动端互斥视口用例。
- `powershell -ExecutionPolicy Bypass -File scripts/Test-TextEncoding.ps1`：通过。
- `powershell -ExecutionPolicy Bypass -File scripts/Run-AcceptanceClosure.ps1 -ReportPath 资料/acceptance-closure-latest.md`：通过，报告内所有步骤均为 `PASSED`。

## 人工验收路径

1. 打开 `/login`，确认登录页中文标题、占位符、按钮和初始化状态无乱码。
2. 打开 `/chat`，确认桌面端左侧会话/任务、中间对话、右侧 Agent 工作台三栏恢复。
3. 在移动端宽度打开 `/chat`，点击 `Agent 工作台`，确认审批队列、任务步骤、产物与预览、审计摘要和运行边界可访问且页面不横向溢出。
4. 执行计划审批、工具审批、驳回和继续任务，确认按钮禁用态、错误提示和停止状态符合预期。
5. 检查产物卡片的类型、状态、版本、生成步骤、预览类型、下载入口和 finalize 状态；PDF/PPTX/XLSX 只下载不在线编辑。
6. 打开 `/config`，确认 Agent/Workspace 配置区中文可读，且没有任意服务器路径配置入口。
7. 查看 `资料/acceptance-closure-latest.md`，确认 `Check Text Encoding`、前端 smoke、架构边界和重点后端测试均为 `PASSED`。

## 剩余边界

- 本轮不新增后端 Agent 能力，不新增 API 路由，不新增数据库迁移，不新增依赖。
- 本轮不做在线 PDF/PPTX/XLSX 编辑器、复杂模板系统、长期后台 Agent、多 Agent 协作或企业多租户权限分层。
- 工作区写入仍只能走受控目录和现有文件服务，不开放 shell、任意服务器路径或 Cloud 写接口。
- Cloud/Edge 现有 dirty worktree 继续视为其他任务改动，本轮未修改、未恢复、未覆盖。
