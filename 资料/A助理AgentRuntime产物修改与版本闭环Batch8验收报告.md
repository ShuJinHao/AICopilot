# A助理 Agent Runtime 产物修改与版本闭环 Batch 8 验收报告

生成时间：2026-05-18

## 范围

本批只实现 AICopilot 后端的文本类 draft 产物受控修改、版本历史、历史下载、差异查看和回滚。未修改 Cloud、Edge 和前端 `src/vues`，未新增 NuGet/npm 依赖，未新增数据库迁移，未接入真实 Cloud。

## 完成内容

- 新增 `AiGateway.EditArtifact` 权限，默认授予 `User` 和 `Admin`。
- 新增 artifact 内容和版本接口：
  - `GET /api/aigateway/artifact/{id}/content`
  - `PUT /api/aigateway/artifact/{id}/content`
  - `GET /api/aigateway/artifact/{id}/versions`
  - `GET /api/aigateway/artifact/{id}/versions/{version}/download`
  - `GET /api/aigateway/artifact/{id}/versions/{fromVersion}/diff/{toVersion}`
  - `POST /api/aigateway/artifact/{id}/versions/{version}/restore`
- 编辑范围限制为 Markdown、HTML、JSON、Chart、CSV draft 产物。
- PUT/restore 只接收 `content`、`expectedVersion`、`comment`，不接收路径、文件名或 MIME 类型。
- 更新前将旧内容归档到 workspace 受控目录：`draft/.versions/{artifactId}/v{version}/`，并写入 metadata JSON。
- 当前 artifact 路径保持稳定，更新后继续使用原 `draft/...` 或 `charts/...` 路径，`Artifact.Version` 递增。
- `expectedVersion` 不匹配时拒绝更新，避免并发覆盖。
- FinalReview 提交后锁定草稿，禁止继续修改或回滚。
- restore 会把历史版本内容恢复为新的 latest version，不删除已有历史。
- 新增后端行级文本 diff，包含 added、removed、unchanged、modified，并设置文本大小和行数上限。
- 新增审计动作：
  - `Agent.ArtifactUpdated`
  - `Agent.ArtifactVersionRestored`
  - `Agent.ArtifactVersionDownload`

## 权限与边界

- 只有任务 owner 且具备 `AiGateway.EditArtifact` 可以编辑或 restore。
- Admin、FinalOutput 审批人、Finalize 权限用户不能跨用户代改 draft artifact。
- 普通 User 可编辑自己的 draft 文本产物，但提交 FinalReview 后不可再改。
- Final artifact 不可编辑、不可 restore。
- 非文本产物 PDF/PPTX/XLSX/Image/Log 不支持内容编辑、diff 和 restore。
- 没有引入任意服务器路径写入：服务端仅使用 artifact 当前相对路径和固定 `.versions` 归档路径。

## 验证结果

- `dotnet build .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
  - 通过：0 warning，0 error。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch8ArtifactVersioning"`
  - 通过：5/5。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=AgentArtifact|Suite=Batch7ReportArtifacts"`
  - 通过：16/16。
- `dotnet test .\src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening|Suite=Batch6SecretProtection|Suite=FreshDatabase|FullyQualifiedName~IdentityAccessManagementTests"`
  - 通过：17/17。
- `.\scripts\Run-AgentSimulationAcceptance.ps1`
  - 通过：scope guard、后端 build、Simulation unit tests 3/3、Simulation Docker acceptance 1/1。

## 影响确认

- Cloud：未修改。
- Edge：未修改。
- 前端 `src/vues`：未修改本批文件，既有 dirty 状态保持不动。
- 真实 Cloud：未访问、未新增真实 Cloud 调用。
- Shell：未新增开放 shell 能力。
- 任意路径写入：未新增。归档路径由后端固定生成并继续通过 workspace path guard 和 file store 根目录保护。
- 数据库迁移：未新增。
- 新依赖：未新增。

## 剩余风险

- 当前版本历史是无迁移文件版，历史 metadata 依赖 workspace 文件完整性；如果人工删除 `.versions` 文件，历史下载和 diff 会返回可诊断错误。
- 二进制产物仍只能通过既有 Agent runtime 重新生成，Batch 8 不支持 PDF/PPTX/XLSX 手动编辑。
