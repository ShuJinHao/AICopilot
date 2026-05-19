# AICopilot 后端阶段记录：契约冻结 + Agent 可观测 + 产物验收闭环

日期：2026-05-15

## 改动范围

本批只修改 AICopilot 后端、后端测试和后端文档：

- `src/core/AICopilot.Core.AiGateway`
- `src/services/AICopilot.AiGatewayService`
- `src/shared/AICopilot.SharedKernel`
- `src/hosts/AICopilot.HttpApi`
- `src/tests/AICopilot.BackendTests`
- `docs`

未修改 `src/vues`，未修改 Cloud/Edge。

## 接口变化

- 新增 `GET /api/aigateway/agent/task/{id}/tool-executions`
  - 支持 `pageIndex`、`pageSize`、`status`、`toolCode`
  - 返回分页 `ToolExecutionRecordDto`
  - 先校验 task owner，越权返回 NotFound
- `GET /api/aigateway/agent/task/{id}/audit-summary`
  - 保持列表返回
  - 合并 ToolExecutionRecord 与 FailureSummary
- `GET /api/aigateway/workspace/{code}`
  - 新增 `manifest[]`
  - manifest 的 `downloadUrl`、`generatedByStep`、`status` 均由后端计算
- `AgentTaskDto`
  - 新增 `failureSummary`

## 安全与治理

- Tool execution DTO 统一脱敏 API Key、token、password、secret、连接串、服务器绝对路径、SQL/表名。
- Runtime 写入 tool audit metadata 时补齐 `providerType`、`targetType`、`targetName`、`timeoutSeconds`、`auditLevel`。
- finalized workspace 禁止继续写 draft artifact。
- PDF/PPTX/XLSX 生成器返回空内容时视为 `artifact_generation_failed`，不创建占位 artifact。

## 错误码

新增：

- `tool_execution_not_found`
- `artifact_finalized`
- `artifact_generation_failed`
- `workspace_manifest_invalid`

## 验证项

新增/更新后端测试覆盖：

- ToolExecutionRecord 分页、过滤、权限隔离、脱敏。
- Agent audit summary 合并计划、工具记录、失败摘要。
- Workspace manifest draft/final 分离和后端 downloadUrl。
- finalized workspace 写入拒绝。
- Artifact 生成失败不创建占位产物。

## 验证命令与结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`
  - 通过，0 warnings，0 errors
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance|Suite=AgentArtifact|FullyQualifiedName~AcceptanceClosureVerificationTests.AgentArtifactClosure"`
  - 通过，27 tests
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`
  - 通过，450 tests
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`
  - 通过，44 tests
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`
  - 通过，6 tests
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`
  - 未发现 vulnerable package

## 剩余风险

- 当前 `src/vues` 存在非本批 dirty 内容，仍按外部阻塞项记录。
- 本批未新增动态 provider executor。
- CloudReadonly 工具仍默认 disabled，启用后继续依赖管理员配置和审批链路。
