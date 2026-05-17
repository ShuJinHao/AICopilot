# AICopilot 后端阶段记录：前端联调契约包

日期：2026-05-17

## 改动范围

本批只修改 AICopilot 后端测试和后端契约文档：

- `src/tests/AICopilot.BackendTests/FrontendIntegrationContractTests.cs`
- `docs/frontend-integration-contract-package-2026-05-17.md`
- `docs/AICopilot后端阶段记录-前端联调契约包-2026-05-17.md`

未修改：

- `AICopilot/src/vues`
- `IIoT.CloudPlatform`
- `IIoT.EdgeClient`

当前 `src/vues` dirty 内容仍视为外部阻塞项，本批不处理，也不恢复前端源码断言测试。

## 契约文档

新增前端联调契约包，覆盖：

- `/api/aigateway/*` 核心路由：模型、runtime、session、upload、agent task、approval、workspace、artifact、tool registry、run queue、worker status。
- `/api/rag/*` 核心路由：embedding model、knowledge base、document upload/list/governance、search。
- DTO 稳定字段：`AgentTaskDto`、workspace/artifact manifest、tool registry、tool execution、run queue、worker status、language model、embedding model。
- 状态枚举：Agent task、step、approval、run queue、run attempt。
- 后端计算字段：`canRun`、`canRetry`、`canSubmitFinalReview`、`canApproveFinal`、`isRunQueued`、`isRunInProgress`、`downloadUrl`、`workspaceMatchesHttpApi`。
- 脱敏规则：不返回 API Key、token、连接串、服务器绝对路径、SQL/表名、大 payload 全文。
- 前端 mock 示例：计划任务、入队运行、工具审批、workspace manifest、run queue summary、worker status、RAG 无权访问、模型密钥脱敏。
- 错误码目录：`AuthProblemCodes`、`AppProblemCodes`、`CloudAiReadProblemCodes` 当前后端常量。

## 测试补充

新增 `FrontendIntegrationContractTests`：

- `OpenApiContractTests`：读取 `/openapi/v1.json`，断言核心 AICopilot/RAG 路由和 HTTP 方法存在。
- `FrontendContractSnapshotTests`：序列化关键 DTO 示例，断言前端依赖字段命名稳定。
- `ContractSecretRedactionTests`：断言契约示例不泄露密钥、token、连接串、绝对路径、SQL/表名。
- `ErrorCodeCatalogTests`：反射后端错误码常量，断言契约文档包含对应错误码。

## 验证结果

已执行：

- `dotnet build AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj --no-restore`
  - 通过，0 warning，0 error。
- `dotnet build AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --no-restore`
  - 通过，0 warning，0 error。
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=FrontendIntegrationContract&FullyQualifiedName!~OpenApiContractTests" --no-build`
  - 通过，5/5。
- `dotnet test AICopilot/src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload" --no-build`
  - 失败，443 passed，50 failed。
  - 失败根因一致：本机 Docker daemon 不可用，Aspire 测试 fixture 无法启动容器运行时。
  - 代表错误：`DistributedApplicationException: 找到容器运行时 'docker'，但它似乎运行不正常`。
  - 新增 `OpenApiContractTests` 也属于真实 HttpApi 启动测试，因此同样受此环境条件影响。
- `dotnet test AICopilot/src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj --no-restore`
  - 通过，44/44。
- `dotnet test AICopilot/src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj --no-restore`
  - 通过，6/6。
- `dotnet list AICopilot/src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`
  - 通过，未发现易受攻击包。

## 剩余风险

- OpenAPI 真实 `/openapi/v1.json` 验收需要 Docker/Aspire 集成环境可用后复跑。
- 完整 BackendTests 仍需要 Docker daemon 正常运行后复跑；本批没有通过跳过核心集成测试来掩盖该环境阻塞。
- 本批没有新增前端 mock server，也没有修改 `src/vues`；前端联调以契约文档和后端测试为交付物。
- CloudReadonly 工具仍默认 disabled；配方主数据和配方版本继续禁读。
