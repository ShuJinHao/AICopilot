# AICopilot 后端产品化阶段记录 2026-05-15

## 完成内容

- 本阶段只修改 AICopilot 后端源码、后端权限目录和后端测试；未修改 Cloud、Edge，也未修改 `src/vues` 前端文件。
- Agent 任务状态机收敛：计划批准状态改为 `PlanApproved`，任务状态补齐 `Draft`、`Finalized`，步骤状态补齐 `Approved`、`Cancelled`。
- Workspace final 流程拆分：`/api/aigateway/workspace/{code}/finalize` 不再自动批准 `FinalOutput`，必须先存在已批准的 FinalOutput 审批。
- 新增 `/api/aigateway/workspace/{code}/submit-final-review`，用于把 workspace 草稿产物提交最终确认审批。
- 上传入口新增后端安全策略：文件扩展名白名单、危险扩展名拒绝、content-type 校验、基础 MIME sniffing、按类型大小限制、文件名净化、上传审计。

## 接口和权限变化

- 新增接口：`POST /api/aigateway/workspace/{code}/submit-final-review`。
- 新增权限：`AiGateway.SubmitFinalReview`，默认授予普通用户和管理员。
- `FinalizeWorkspace` 语义变更：只负责发布已审批 final，不再代替用户批准 final。
- `AgentTaskDto` 新增后端计算字段：`canRun`、`canSubmitFinalReview`、`canApproveFinal`，保留 `canRetry`。

## 验证结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName~UploadValidationTests|FullyQualifiedName~AgentArtifactDomainTests|FullyQualifiedName~AcceptanceClosureVerificationTests"`：通过，21 个测试通过。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`：通过，424 个测试通过。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44 个测试通过。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6 个测试通过。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。

## 剩余事项

- 未排除条件执行完整 BackendTests 时，`SecurityHardeningTests.FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload` 失败；原因是当前工作树存在不属于本阶段的 `src/vues` 前端 dirty 内容，且本阶段禁止修改前端。
- Fresh AI Gateway migration 合并清理、Tool Registry 产品化、Cloud 真实只读数据闭环仍未在本阶段完成。
- 当前工作树存在非本阶段产生的 `src/vues` 前端 dirty 文件；本阶段不处理这些文件。

## 本批次追加：Fresh Seed + RAG 权限 + Secret 契约

### 完成内容

- AiGateway 迁移收敛为 fresh baseline：移除旧 AiGateway 多段迁移，生成 `20260515030952_AiGatewayFreshBaseline`，并在 fresh baseline 中清理 `public` 下旧 AI Gateway 表，避免清库重建后出现双份 schema。
- RAG 增加 `OwnerUserId`、`AccessScope`，新增 additive migration `20260515030526_AddKnowledgeBaseAccessScope`；默认新建知识库归属当前用户，scope 为 `OwnerOnly`。
- RAG list/get/search/upload/update/delete/document list/document governance 全部按 owner/scope/admin 做后端校验；无权访问返回 NotFound，避免泄露标题、文档名、片段和来源。
- Agent plan 和 Agent runtime 对 `knowledgeBaseIds` 二次调用 `IKnowledgeBaseAccessChecker`；无权知识库不能进入计划或检索。
- AiGateway 上传到知识库前同时校验上传安全策略和 RAG 写权限；RAG 直传入口补齐危险扩展名、content-type、基础 MIME sniffing、大小、文件名净化、SHA256 和拒绝审计。
- 模型 DTO 密钥契约收敛：`LanguageModelDto`、`EmbeddingModelDto` 不再返回 raw `apiKey` 或 `apiKeyMasked`，只返回 `hasApiKey` 和固定 `apiKeyPreview="******"`。
- `MigrationWorkApp` 增加幂等 AiGateway defaults seed：默认 runtime settings、disabled 示例语言模型、内置 A 助理模板、默认 Agent 产物审批策略；无可用 routing model 时不激活 routing。
- `rag-worker` 增加 fail-closed 的后台 `ICurrentUser`，只用于满足 DI 校验，不提供业务权限，修复新增 RAG handler 依赖后 worker 无法启动的问题。

### 接口和契约变化

- `/api/aigateway/*` 路由不变；语言模型 DTO 字段变更为 `hasApiKey`、`apiKeyPreview`。
- `/api/rag/*` 路由不变；知识库列表按当前用户过滤，单个无权知识库统一 NotFound。
- `CreateKnowledgeBaseCommand` 支持可选 `accessScope`，默认 `OwnerOnly`；前端不传时保持后端默认私有。
- `RagDocumentUploadBridgeRequest` 增加 `contentType`、`fileSize`，用于 AiGateway 上传桥接到 RAG 时复用安全校验。

### 验证结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`：通过，435 个测试通过。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44 个测试通过。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6 个测试通过。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。

### 剩余事项

- 完整 BackendTests 仍按本批次计划排除 `SecurityHardeningTests.FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload`；该断言依赖当前 dirty 的 `src/vues` 前端源码，本批次禁止处理前端。
- 本批次未做完整 Tool Registry 产品化，仍留到下一批。
- Cloud 只读工具闭环仍未进入本批次实现。

## 本批次追加：Tool Registry MVP + Planner/Runtime 工具治理

### 完成内容

- AiGateway fresh baseline 增加 `tool_registrations`、`tool_execution_records` 两张表，并更新 `AiGatewayDbContext` snapshot；fresh baseline 会先清理旧残留表，避免重复建表。
- 新增 Tool Registry 领域模型、EF 配置、仓储注册和默认 seed；内置 Agent runtime 工具已注册，`query_cloud_data_readonly` 作为 CloudReadOnly 占位工具默认 `isEnabled=false`、`requiresApproval=true`。
- `MigrationWorkApp` 幂等 upsert 默认工具注册；seed 不包含 shell 工具、不包含 Cloud 写工具、不包含真实连接串或 API Key。
- 新增 `/api/aigateway/tools` 管理接口：列表、详情、PATCH 更新；新增权限 `AiGateway.ToolRegistry.Read`、`AiGateway.ToolRegistry.Manage`。
- Planner 生成计划后通过 Tool Registry 校验工具存在、启用状态、blocked 状态、用户权限和 Cloud 只读边界；Cloud 只读占位未启用时返回稳定错误码 `cloud_readonly_tool_disabled`。
- Runtime 执行每个 step 前再次读取 Tool Registry；即使 plan 被篡改，也会拒绝 missing/disabled/blocked/unauthorized 工具。
- Runtime 审批判定改为 `step.RequiresApproval || tool.requiresApproval || riskLevel=RequiresApproval`，不再依赖硬编码工具名清单。
- 每次工具执行写入 `ToolExecutionRecord`，覆盖成功、失败、拒绝执行；输入摘要、输出摘要、错误和审计元数据会脱敏 API Key、token、密码、连接串和服务器绝对路径。
- MCP 工具暴露纳入 registry 口径：MCP 工具没有已启用注册时不会暴露给模型；启用后仍可由 registry 强制审批。
- 本批次仍未实现真实 Cloud 只读业务工具，不新增 Cloud 项目引用，不修改 Cloud/Edge，不修改 `src/vues`。

### 接口和契约变化

- 新增 `GET /api/aigateway/tools`、`GET /api/aigateway/tools/{toolCode}`、`PATCH /api/aigateway/tools/{toolCode}`。
- 新增 DTO：`ToolRegistrationDto`、`ToolExecutionRecordDto`；PATCH request 的 `auditLevel` 以字符串契约进入 Service 层解析，Controller 不引用 Core 枚举。
- 新增稳定错误码：`tool_not_registered`、`tool_disabled`、`tool_blocked`、`tool_permission_denied`、`tool_requires_approval`、`cloud_readonly_tool_disabled`。
- Tool Registry 管理权限默认进入 Admin 全量权限；普通 User 默认不具备工具管理权限。

### 验证结果

- `dotnet build src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj`：通过，0 warning，0 error。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=ToolRegistryGovernance"`：通过，7 个测试通过。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "Suite=FreshDatabase"`：通过，1 个测试通过。
- `dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTests.csproj --filter "FullyQualifiedName!~FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload"`：通过，442 个测试通过。
- `dotnet test src/tests/AICopilot.ArchitectureTests/AICopilot.ArchitectureTests.csproj`：通过，44 个测试通过。
- `dotnet test src/tests/AICopilot.AiEvalTests/AICopilot.AiEvalTests.csproj`：通过，6 个测试通过。
- `dotnet list src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。

### 剩余事项

- 完整 BackendTests 仍按本批次计划排除 `SecurityHardeningTests.FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload`；该断言依赖当前 dirty 的 `src/vues` 前端源码，本批次禁止处理前端。
- 当前 Runtime 执行 switch 仍保留，后续可在 Tool Registry 稳定后推进动态执行器或 provider executor 抽象。
- `query_cloud_data_readonly` 当前只注册并默认禁用；真实 Cloud 只读设备状态、日志、生产指标、配方版本查询留到下一批单独做。
- ToolExecutionRecord 查询接口本批次只完成 DTO 和写入基础，面向管理端的查询/分页可随前端工具治理页再补。
