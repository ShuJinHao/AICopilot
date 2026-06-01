# AICopilot 后端阶段记录 - CloudAiReadClient 拆分第一批 - 2026-05-28

## 本批目标

- 只修改 `AICopilot` 后端基础设施层。
- 拆分 `CloudAiReadClient.cs`，将 675 行单体收敛为 Cloud AiRead 只读客户端薄门面。
- 保持 `CloudAiReadClient` public 类型、构造参数、`ICloudAiReadClient` 实现和所有 public 方法签名不变。
- 不改变 Cloud AiRead endpoint allowlist、CloudReadonly 只读边界、request/response wire shape、配置结构、数据库结构或部署配置。

## 实际改动类别

- `CloudAiReadClient.cs` 保留为入口门面：负责 enabled/config check、endpoint policy 评估、public 方法编排、semantic target dispatch 和路径常量。
- 新增 `CloudAiReadHttpTransport.cs`：承接 HTTP request 构建、Bearer token、timeout、HTTP status/error/JSON 异常映射。
- 新增 `CloudAiReadQueryParameterBuilder.cs`：承接 devices/capacity/device-log/pass-station query 参数构建、filter/time range/format helper。
- 新增 `CloudAiReadDocumentAdapter.cs`：承接 Cloud AiRead JSON document 到 Cloud AiRead DTO/result 的映射入口，保持 internal 类型名不变。
- 新增 `CloudAiReadJsonValueReader.cs`：承接 records 提取、truncation 判定、string/decimal/date/additional fields JSON helper。

## 影响模块

- `AICopilot/src/infrastructure/AICopilot.Infrastructure/CloudRead/`
- 影响能力：Cloud AiRead 只读 HTTP 客户端内部结构。
- 不影响能力：CloudReadonly 权限边界、CloudAiReadEndpointPolicy、Cloud AiRead contracts、DI 注册、数据库、部署、MCP 配置。

## 未修改范围

- 未修改 `IIoT.CloudPlatform/**`。
- 未修改 `IIoT.EdgeClient/**`。
- 未修改 `AICopilot/src/vues/**`。
- 未修改公开 contracts/DTO。
- 未修改数据库迁移、容器编排、MCP 配置。
- 未新增 NuGet/npm/容器依赖。

## 验证命令与结果

- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore --filter "FullyQualifiedName~CloudAiReadClientTests|FullyQualifiedName~SemanticAnalysisRunnerTests|FullyQualifiedName~EnterpriseCloudReadonlyReadinessP5Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxP6Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxAgentTrialP7Tests|FullyQualifiedName~EnterpriseCloudReadonlySandboxExpansionP8Tests|FullyQualifiedName~EnterpriseCloudReadonlyPilotReadinessP11Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionPilotP12Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionControlledPilotP13Tests|FullyQualifiedName~EnterpriseCloudReadonlyProductionOperationsP14Tests|FullyQualifiedName~ToolRegistryGovernanceTests"`
  - 结果：通过 150/150。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 结果：通过 759/759。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 结果：通过 44/44。
- `git diff --check`
  - 结果：通过，无 whitespace error 输出。
- `git diff --name-only`
  - 结果：当前工作树包含前序多批 AICopilot 后端拆分改动；本批实际新增/修改文件限定在 CloudRead 拆分文件与本阶段记录。
- `wc -l src/infrastructure/AICopilot.Infrastructure/CloudRead/*CloudAiRead*.cs`
  - `CloudAiReadClient.cs`：143 行。
  - `CloudAiReadDocumentAdapter.cs`：147 行。
  - `CloudAiReadHttpTransport.cs`：107 行。
  - `CloudAiReadJsonValueReader.cs`：169 行。
  - `CloudAiReadQueryParameterBuilder.cs`：147 行。

## 剩余风险

- 本批是搬移式拆分，未新增 Cloud AiRead endpoint，未改变真实 Cloud 调用协议。
- 当前验证覆盖了 CloudAiReadClient、SemanticAnalysisRunner、CloudReadonly P5/P6/P7/P8/P11/P12/P13/P14 和 ToolRegistry 相关链路。
- 未进行真实外部 Cloud 环境联调；本批不涉及外部协议变更，仍以后续部署/联调批次为准。

## 下一阶段进入条件

- 继续从当前绿色基线进入下一批结构债处理。
- 优先选择仍超过 500 行、边界清晰且测试覆盖集中的 AICopilot 后端文件。
- 若后续涉及 Cloud AiRead endpoint allowlist、CloudReadonly 权限边界、wire shape、配置结构、数据库或部署配置，必须单独开批并重新确认范围。
