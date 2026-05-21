# AICopilot Enterprise CloudReadonly Production Pilot P12 Acceptance

- GeneratedAt: 2026-05-21 11:44:36
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; production Pilot remains fixed-template, windowed, and approval-gated
- Default State: CloudReadonlyProductionPilot.Enabled=false; query_cloud_data_readonly remains disabled, hidden, and non-executable
- Build Output: C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12 for focused tests

## Summary

- Inherited P11 Acceptance: PASSED
- Enterprise CloudReadonly Production Pilot P12 Scope Guard: PASSED
- Build HttpApi: PASSED
- Run P12 Focused Backend Tests: PASSED
- Run CloudReadonly Route Contract Tests: PASSED
- Build Frontend: PASSED
- Frontend Production Pilot Playwright Smoke: PASSED

## P12 Production Pilot Evidence

- Pilot Window: start/end, owner, approver policy, rollback policy, max time range, max rows, timeout, and endpoint allowlist are represented.
- Gate: P11 RehearsalPassed, default production flags, protected ToolRegistry state, CloudAiRead configuration, and approved window state determine Ready/Blocked.
- Allowed Endpoints: devices, capacity_summary, device_logs, pass_station_records only.
- Refusals: Recipe, Recipe version, write path, unknown endpoint, out-of-window endpoint, missing P11 gate, expired/paused window, over maxRows, and over time range are blocked.
- Tool Registry: query_cloud_data_readonly stays closed; query_cloud_production_pilot_readonly is disabled by default and only temporarily usable through the P12 Pilot Window gate.
- Outputs: sourceMode=CloudReadonlyProductionPilot, sourceLabel=Cloud production readonly Pilot, boundary=ProductionPilot, pilotWindowId, endpointCode, query/result hash, row count, truncation, and approval status are required.

## Details

### Inherited P11 Acceptance

```text
==> Inherited P10 Acceptance Report Check
==> Enterprise CloudReadonly Pilot Readiness P11 Scope Guard
==> Build HttpApi
==> Run P11 Focused Backend Tests
==> Run CloudReadonly Route Contract Tests
Enterprise CloudReadonly Pilot Readiness P11 acceptance report written to: .\docs\enterprise-cloud-readonly-pilot-readiness-p11-latest.md
```

### Enterprise CloudReadonly Production Pilot P12 Scope Guard

```text
Enterprise Data Governance scope guard passed. Checked 30 candidate file(s).
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:54.03
```

### Run P12 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-production-pilot-p12\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:     4，已跳过:     0，总计:     4，持续时间: 124 ms - AICopilot.BackendTests.dll (net10.0)
```

### Run CloudReadonly Route Contract Tests

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

已通过! - 失败:     0，通过:    18，已跳过:     0，总计:    18，持续时间: 3 s - AICopilot.BackendTests.dll (net10.0)
```

### Build Frontend

```text
> aicopilot-web@0.0.0 build
> run-p type-check "build-only {@}" --


> aicopilot-web@0.0.0 type-check
> vue-tsc --build


> aicopilot-web@0.0.0 build-only
> vite build

[36mvite v7.3.2 [32mbuilding client environment for production...[36m[39m
transforming...
node.exe : [33mnode_modules/@vueuse/core/dist/index.js (3362:0): A comment
At line:1 char:1
+ & "D:\Program Files\NodeJs/node.exe" "D:\Program Files\NodeJs/node_mo ...
+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([33mnode_modul...2:0): A comment:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError


"/* #__PURE__ */"

in "node_modules/@vueuse/core/dist/index.js" contains an annotation that Rollup cannot interpret due to the position of
 the comment. The comment will be removed to avoid issues.[39m
[33mnode_modules/@vueuse/core/dist/index.js (5780:22): A comment

"/* #__PURE__ */"

in "node_modules/@vueuse/core/dist/index.js" contains an annotation that Rollup cannot interpret due to the position of
 the comment. The comment will be removed to avoid issues.[39m
[32m✓[39m 3179 modules transformed.
rendering chunks...
computing gzip size...
[2mdist/[22m[32mindex.html                                                            [39m[1m[2m  0.43 kB[22m[1m[22m[2m │ gzip:   0.28 kB[22m
[2mdist/[22m[35massets/ChartWidget-NnENIwdR.css                                       [39m[1m[2m  0.55 kB[22m[1m[22m[2m │ gzip:   0.26 kB[22m
[2mdist/[22m[35massets/StatsWidget-6FoPIn84.css                                       [39m[1m[2m  0.72 kB[22m[1m[22m[2m │ gzip:   0.35 kB[22m
[2mdist/[22m[35massets/CloudOidcCompleteView-D3eRkjsi.css                             [39m[1m[2m  0.91 kB[22m[1m[22m[2m │ gzip:   0.39 kB[22m
[2mdist/[22m[35massets/ForbiddenView-DzMAW-i-.css                                     [39m[1m[2m  1.12 kB[22m[1m[22m[2m │ gzip:   0.42 kB[22m
[2mdist/[22m[35massets/DataTableWidget-DRKbuU01.css                                   [39m[1m[2m  1.22 kB[22m[1m[22m[2m │ gzip:   0.48 kB[22m
[2mdist/[22m[35massets/AccessView-DT6UKuCB.css                                        [39m[1m[2m  3.80 kB[22m[1m[22m[2m │ gzip:   0.99 kB[22m
[2mdist/[22m[35massets/AiTag-B_gdhv0e.css                                             [39m[1m[2m  4.23 kB[22m[1m[22m[2m │ gzip:   1.17 kB[22m
[2mdist/[22m[35massets/LoginView-DuMLKUmK.css                                         [39m[1m[2m  8.02 kB[22m[1m[22m[2m │ gzip:   2.06 kB[22m
[2mdist/[22m[35massets/KnowledgeView-5XuNPxge.css                                     [39m[1m[2m 12.86 kB[22m[1m[22m[2m │ gzip:   1.79 kB[22m
[2mdist/[22m[35massets/ConfigView-Dd_XNiGM.css                                        [39m[1m[2m 21.45 kB[22m[1m[22m[2m │ gzip:   2.69 kB[22m
[2mdist/[22m[35massets/ChatView-BI83UQXL.css                                          [39m[1m[2m 26.30 kB[22m[1m[22m[2m │ gzip:   4.70 kB[22m
[2mdist/[22m[35massets/index-DvFWv-08.css                                             [39m[1m[2m 26.98 kB[22m[1m[22m[2m │ gzip:   6.29 kB[22m
[2mdist/[22m[36massets/_plugin-vue_export-helper-DlAUqK2U.js                          [39m[1m[2m  0.09 kB[22m[1m[22m[2m │ gzip:   0.10 kB[22m
[2mdist/[22m[36massets/loader-circle-Q3MnqKFI.js                                      [39m[1m[2m  0.14 kB[22m[1m[22m[2m │ gzip:   0.15 kB[22m
[2mdist/[22m[36massets/x-P1peqcOm.js                                                  [39m[1m[2m  0.52 kB[22m[1m[22m[2m │ gzip:   0.30 kB[22m
[2mdist/[22m[36massets/StatsWidget-CTO3cKqK.js                                        [39m[1m[2m  0.67 kB[22m[1m[22m[2m │ gzip:   0.42 kB[22m
[2mdist/[22m[36massets/shield-check-B8pTXEOS.js                                       [39m[1m[2m  0.70 kB[22m[1m[22m[2m │ gzip:   0.41 kB[22m
[2mdist/[22m[36massets/AiCheckbox.vue_vue_type_script_setup_true_lang-Brjwpa8R.js     [39m[1m[2m  0.73 kB[22m[1m[22m[2m │ gzip:   0.46 kB[22m
[2mdist/[22m[36massets/DataTableWidget-vKTsPxct.js                                    [39m[1m[2m  1.04 kB[22m[1m[22m[2m │ gzip:   0.62 kB[22m
[2mdist/[22m[36massets/CloudOidcCompleteView-BtqroL-4.js                              [39m[1m[2m  1.62 kB[22m[1m[22m[2m │ gzip:   0.97 kB[22m
[2mdist/[22m[36massets/ForbiddenView-C6XxX9P9.js                                      [39m[1m[2m  3.06 kB[22m[1m[22m[2m │ gzip:   1.60 kB[22m
[2mdist/[22m[36massets/AiTableCard.vue_vue_type_script_setup_true_lang-Ba2phYDH.js    [39m[1m[2m  6.71 kB[22m[1m[22m[2m │ gzip:   2.44 kB[22m
[2mdist/[22m[36massets/LoginView-CpbI--Wg.js                                          [39m[1m[2m  7.89 kB[22m[1m[22m[2m │ gzip:   3.53 kB[22m
[2mdist/[22m[36massets/AiNumberInput.vue_vue_type_script_setup_true_lang-D4G02xCX.js  [39m[1m[2m  8.93 kB[22m[1m[22m[2m │ gzip:   2.59 kB[22m
[2mdist/[22m[36massets/AccessView-BzxLxM0S.js                                         [39m[1m[2m 22.69 kB[22m[1m[22m[2m │ gzip:   6.50 kB[22m
[2mdist/[22m[36massets/KnowledgeView-DH_XUZTs.js                                      [39m[1m[2m 29.27 kB[22m[1m[22m[2m │ gzip:   7.64 kB[22m
[2mdist/[22m[36massets/AiButton.vue_vue_type_script_setup_true_lang-SA5QoVDY.js       [39m[1m[2m 29.76 kB[22m[1m[22m[2m │ gzip:   9.69 kB[22m
[2mdist/[22m[36massets/AiTag.vue_vue_type_script_setup_true_lang-BQ8gRIjR.js          [39m[1m[2m 58.27 kB[22m[1m[22m[2m │ gzip:  20.38 kB[22m
[2mdist/[22m[36massets/ConfigView-BKRH8wun.js                                         [39m[1m[2m 93.04 kB[22m[1m[22m[2m │ gzip:  21.65 kB[22m
[2mdist/[22m[36massets/ChatView-BaWeytYw.js                                           [39m[1m[2m161.04 kB[22m[1m[22m[2m │ gzip:  64.29 kB[22m
[2mdist/[22m[36massets/index-DefItFeB.js                                              [39m[1m[2m221.19 kB[22m[1m[22m[2m │ gzip:  78.91 kB[22m
[2mdist/[22m[36massets/ChartWidget-BZaf0kCp.js                                        [39m[1m[2m547.15 kB[22m[1m[22m[2m │ gzip: 184.75 kB[22m
[32m✓ built in 9.95s[39m
```

### Frontend Production Pilot Playwright Smoke

```text
> aicopilot-web@0.0.0 test:smoke
> playwright test --config=playwright.smoke.config.ts --grep P12 production readonly pilot


Running 2 tests using 2 workers

  ok 2 [desktop] › tests\smoke\acceptance.spec.ts:127:1 › agent trial panel shows P12 production readonly pilot gate (2.4s)
  -  1 [mobile] › tests\smoke\acceptance.spec.ts:127:1 › agent trial panel shows P12 production readonly pilot gate

  1 skipped
  1 passed (11.6s)
```

## Remaining Risk

- P12 does not open free-goal production queries, Recipe/version reads, write paths, or Cloud/Edge linkage.
- Real endpoint/token smoke remains optional and must be run only inside an explicit Pilot Window with approvals.
- Production Pilot status is an initial fixed-template gate, not a broad production rollout.

