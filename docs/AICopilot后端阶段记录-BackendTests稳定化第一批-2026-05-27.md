# AICopilot 后端阶段记录：BackendTests 稳定化第一批

日期：2026-05-27

## 阶段目标

- 收口 `AICopilot.BackendTests` 第一批稳定化改动，使后端测试从大面积失败恢复为可完整稳定运行。
- 本阶段只修改 `AICopilot` 后端、后端测试和本阶段记录。
- 本阶段不修改 `IIoT.CloudPlatform`、`IIoT.EdgeClient`，不拆分 `AgentTaskRuntime.cs`，不调整 MCP 独立容器或部署编排。

## 本批改动

- 修正 Cloud AiRead 只读路径策略：允许 `/api/...` 根相对路径，继续拒绝带 scheme/host 的绝对 URL、路径穿越、重复分隔符和非白名单路径。
- 稳定本地集成测试环境：测试启动 Aspire 前清理大小写 proxy 环境变量，补齐 loopback/no-proxy，放宽测试环境 identity-management rate limit，Dispose 时恢复原环境变量。
- 修复 macOS 测试兼容性：缩短 MCP named pipe 名称，补充 macOS PDF 字体候选，修正 Windows 风格配置路径拼接。
- 对齐当前业务与测试口径：Text-to-SQL 截断预览保持 50 行，TrialScenario 总数为 18 且 simulation-only 为 6，RAG 删除为 soft delete，Final Prompt 仅允许 Indexed/Active 文档，User 角色包含 PilotAuthorization 提交/查看权限。
- 收敛审计保存边界：移除 `PilotAuthorizationWorkflow` 中已有业务仓储保存点后的冗余显式 audit save；对无业务保存点的只读/工作流审计保存补齐测试白名单。
- 更新过期验收断言：生产 `query_cloud_data_readonly` 工具保持 disabled/hidden/non-executable，Mock MCP 工具 target 以当前内置注册为准，Production Operations ledger 只检查 ledger 本身不包含原始 payload。

## 影响模块

- `AICopilot.Services.Contracts`：Cloud AiRead path normalization。
- `AICopilot.AiGatewayService`：PilotAuthorization 审计保存边界。
- `AICopilot.Infrastructure`：PDF artifact 字体解析候选。
- `AICopilot.BackendTests`：测试环境 fixture、MCP bootstrap、RAG、DataAnalysis、身份权限、CloudReadonly、FreshDatabase、审计白名单等测试口径。

## 接口与部署

- 未新增或修改公开 API、DTO、数据库迁移、容器编排或外部部署模板。
- 未修改 `AICopilot/artifacts/docker-compose.yaml`。
- 未对已有部署数据库执行任何破坏性操作，也未自动修改 MCP 配置数据。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 通过：759
  - 失败：0
  - 跳过：0
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 通过：44
  - 失败：0
  - 跳过：0
- `git diff --check`
  - 通过，无 whitespace/error marker 问题。
- `git diff --name-only`
  - 仅包含 `AICopilot` 既有改动文件；新增阶段记录通过 `git status --short` 核对。

## 剩余风险

- `AgentTaskRuntime.cs` 仍为 2333 行，是下一批最重要的可维护性风险；本阶段只稳定测试，不做运行时大拆分。
- MCP 默认部署仍存在 Chiseled 镜像缺少 `npx` 的部署风险；本阶段不改 docker-compose 或容器结构。
- 完整 BackendTests 依赖 Docker/Aspire 集成环境，单次运行约 5 分钟，后续验证需要预留足够超时时间。
- 本阶段收口了测试稳定性和当前口径一致性，不代表完成 Agent Runtime 架构重构或 MCP 部署生产化。

## 下一阶段进入条件

- 进入 `AgentTaskRuntime.cs` 拆分批次前，应先冻结本阶段改动并以当前 759/759 BackendTests 作为回归基线。
- Runtime 拆分建议优先抽出工具执行、审批暂停/恢复、workspace/artifact 生成、run attempt/lease 管理等协作者，保持对外 `IAgentTaskRuntime` 契约不变。
- MCP 部署修复应作为独立部署批次处理，方向为独立 MCP 容器或显式 Node/npx runtime，不与 Runtime 重构同批执行。
