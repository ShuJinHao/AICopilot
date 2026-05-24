# AICopilot Enterprise CloudReadonly Sandbox Agent Trial P7 Acceptance

- GeneratedAt: 2026-05-20 11:22:30
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled by default
- Sandbox Agent Trial Boundary: SandboxAgentTrial; fixed templates only; no free Agent goal and no production Cloud read
- Test Mode: fake sandbox client and contract fixtures; real Cloud endpoint/token are optional smoke/trial inputs only
- Build Output: C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7 for focused tests

## Summary

- Inherited P6 Acceptance Report Check: PASSED
- Enterprise CloudReadonly Sandbox Agent Trial P7 Scope Guard: PASSED
- Build HttpApi: PASSED
- Run P7 Focused Backend Tests: PASSED
- Run Cloud Readiness Contract Tests: PASSED

## P7 Sandbox Agent Trial Evidence

- Default Gate: CloudReadonly.Mode, CloudReadonly.Real, AllowProductionRead, CloudAiRead, and CloudReadonlySandboxAgentTrial remain disabled by default.
- Tool Boundary: query_cloud_data_readonly remains disabled, hidden, and non-executable; P7 uses separate query_cloud_sandbox_readonly.
- Sandbox Tool: query_cloud_sandbox_readonly uses dataBoundary=CloudReadonlySandboxOnly and approvalPolicy=SandboxAgentTrial.
- Trial Gate: sandbox trial requires CloudReadonlySandbox configuration, P6 RealSandboxSmoke pass, fixed template id, permission, and approval.
- Fixed Templates: devices, capacity summary, device logs, pass-station records, device exception analysis, and capacity delivery analysis.
- Result Markers: sourceType=CloudReadonly, sourceMode=CloudReadonlySandbox, isSandbox=true, isSimulation=false, sourceLabel=Cloud 鍙 Sandbox锛堥潪鐢熶骇锛? boundary=SandboxAgentTrial.
- Audit Shape: endpoint code, status, duration, row count, truncated flag, query/result hash, and error code without token or full payload plaintext.
- Frontend Smoke: readiness/config shell remains available and must not display token, API Key, connection string, password, or full payload.

## Details

### Inherited P6 Acceptance Report Check

```text
Using existing P6 acceptance report: .\docs\enterprise-cloud-readonly-sandbox-p6-latest.md
```

### Enterprise CloudReadonly Sandbox Agent Trial P7 Scope Guard

```text
Enterprise Data Governance scope guard passed. Checked 143 candidate file(s).
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:01:04.33
```

### Run P7 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-agent-trial-p7\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    11，已跳过:     0，总计:    11，持续时间: 127 ms - AICopilot.BackendTests.dll (net10.0)
```

### Run Cloud Readiness Contract Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\shared\AICopilot.SharedKernel\bin\Debug\net10.0\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\core\AICopilot.Core.Rag\bin\Debug\net10.0\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\core\AICopilot.Core.AiGateway\bin\Debug\net10.0\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\core\AICopilot.Core.DataAnalysis\bin\Debug\net10.0\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\core\AICopilot.Core.McpServer\bin\Debug\net10.0\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\shared\AICopilot.Visualization\bin\Debug\net10.0\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.Services.Contracts\bin\Debug\net10.0\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.EntityFrameworkCore\bin\Debug\net10.0\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.AiRuntime\bin\Debug\net10.0\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.Dapper\bin\Debug\net10.0\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.Embedding\bin\Debug\net10.0\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.EventBus\bin\Debug\net10.0\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\shared\AICopilot.AgentPlugin\bin\Debug\net10.0\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\infrastructure\AICopilot.Infrastructure\bin\Debug\net10.0\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\shared\AICopilot.AgentPlugin.Runtime\bin\Debug\net10.0\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.Services.CrossCutting\bin\Debug\net10.0\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.AiGatewayService\bin\Debug\net10.0\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.DataAnalysisService\bin\Debug\net10.0\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.IdentityService\bin\Debug\net10.0\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.RagService\bin\Debug\net10.0\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.ServiceDefaults\bin\Debug\net10.0\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.DataWorker\bin\Debug\net10.0\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\services\AICopilot.McpService\bin\Debug\net10.0\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.HttpApi\bin\Debug\net10.0\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.MigrationWorkApp\bin\Debug\net10.0\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.RagWorker\bin\Debug\net10.0\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\hosts\AICopilot.AppHost\bin\Debug\net10.0\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.Testing.McpServer\bin\Debug\net10.0\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.BackendTests\bin\Debug\net10.0\AICopilot.BackendTests.dll
C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.BackendTests\bin\Debug\net10.0\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    24，已跳过:     0，总计:    24，持续时间: 1 m - AICopilot.BackendTests.dll (net10.0)
```

## Remaining Risk

- P7 proves controlled sandbox Agent trial behavior with fake/fixture clients; it does not prove production Cloud read access.
- A real sandbox endpoint/token, if provided later, must stay under SandboxAgentTrial and must not enable Real CloudReadonly.
- Free Agent goals and production Cloud data remain out of scope until a separate phase explicitly opens that boundary.

