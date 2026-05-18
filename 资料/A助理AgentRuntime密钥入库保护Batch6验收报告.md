# A助理 Agent Runtime 密钥入库保护 Batch 6 验收报告

## 完成内容

- 新增 `ISecretProtector` 统一密钥保护抽象，并在 AICopilot 基础设施层注册实现。
- `LanguageModel.ApiKey` 与 `EmbeddingModel.ApiKey` 不再通过 EF 透明解密；数据库字段保留 `encv1:` 密文。
- 创建/更新语言模型和向量模型时，非空 ApiKey 在保存前统一加密。
- 语言模型连接测试、OpenAI/Anthropic Chat provider、Embedding generator 在调用模型 SDK 前按需解密。
- 收紧旧明文兼容：非空但不带 `encv1:` 前缀的存储值会失败，并提示重新保存配置。
- DTO、审计摘要、列表/详情接口继续只暴露 `HasApiKey` 与固定脱敏 preview，不输出明文。

## 实际改动范围

- AICopilot 后端服务层：语言模型命令、模型测试命令、向量模型命令。
- AICopilot 基础设施层：密钥保护实现、运行时 provider、Embedding generator、EF ApiKey 映射。
- AICopilot 后端测试：Batch 6 密钥入库保护测试、运行时边界测试、既有配置回归测试更新。
- 未修改 `IIoT.CloudPlatform`。
- 未修改 `IIoT.EdgeClient`。
- 未修改 `src/vues` 前端文件；当前前端 dirty 文件为既有改动。
- 未引入真实 Cloud 访问，未开放 shell，未引入任意服务器路径写入。

## 验证结果

- `dotnet build src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug`
  - 通过：0 warning，0 error。
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --filter "FullyQualifiedName~SecretStringEncryptorTests|Suite=Batch6SecretProtection"`
  - 通过：9 个测试。
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "FullyQualifiedName~Phase25RuntimeSmokeTests.ConfigurationCrud_ShouldManageAllFormalConfigurationsAndMaskSensitiveValues|FullyQualifiedName~Phase25RuntimeSmokeTests.UpdateLanguageModel_WithBlankApiKey_ShouldPreserveExistingSecret"`
  - 通过：2 个测试。
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "FullyQualifiedName~RagMcpAuditCommandTests"`
  - 通过：3 个测试。
- `dotnet test src\tests\AICopilot.BackendTests\AICopilot.BackendTests.csproj -c Debug --no-build --filter "Suite=Batch5ApprovalHardening"`
  - 通过：2 个测试。
- `.\scripts\Run-AgentSimulationAcceptance.ps1`
  - 通过：范围护栏、后端构建、3 个 Simulation 单元测试、1 个 Docker acceptance 测试。

## 剩余风险

- 已存在数据库中的明文 ApiKey 不兼容读取，需要管理员在管理接口重新保存密钥。
- 本批不处理 MCP、Cloud token 或其他未来凭据类型。
- 本批不做前端联调；前端配置页如需提示“旧明文需要重存”，应在后续前端批次单独处理。
