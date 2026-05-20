# AICopilot Enterprise Trial Operations P10 Acceptance

- GeneratedAt: 2026-05-20 15:37:16
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly and production tools remain disabled
- Trial Sources: SimulationBusiness and CloudReadonlySandbox only
- P11 Gate Meaning: ReadyForP11Planning allows planning only; it does not enable production data
- Build Output: C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10 for focused tests

## Summary

- Inherited P9 Acceptance Report Check: PASSED
- Enterprise Trial Operations P10 Scope Guard: PASSED
- Build HttpApi: PASSED
- Run P10 Focused Backend Tests: PASSED
- Run Migration Ownership Tests: PASSED
- Build Frontend: PASSED
- Frontend Trial Operations HTTP Smoke: PASSED

## P10 Trial Operations Evidence

- Campaign: Draft/Active/Paused/Completed/Archived campaign status and allowed source boundaries are persisted.
- Scenario Runs: Agent tasks can be attached only by reference with task id, artifact ids, source mode, boundary, query/result hashes, and approval status.
- Risk Register: Open/Mitigating/Resolved/ClosedAsOutOfScope risks are recorded by severity/category/source reference/resolution hash.
- Pilot Readiness: P9 final lock, sandbox/source boundaries, approval closure, Tool Registry production-tool closure, scope guard, and unresolved high/critical risks gate P11 planning.
- Evidence Package: output contains metrics, references, source modes, boundary and hash samples only; no token, API Key, connection string, full SQL, or full sandbox payload.
- Frontend: Agent workbench shows trial campaign, source boundaries, attached run records, readiness checks, and evidence metrics from backend responses.

## Details

### Inherited P9 Acceptance Report Check

```text
Using existing P9 acceptance report: .\docs\enterprise-artifact-workspace-p9-latest.md
```

### Enterprise Trial Operations P10 Scope Guard

```text
Enterprise Data Governance scope guard passed. Checked 176 candidate file(s).
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:01:07.76
```

### Run P10 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-trial-operations-p10\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3，持续时间: 168 ms - AICopilot.BackendTests.dll (net10.0)
```

### Run Migration Ownership Tests

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

已通过! - 失败:     0，通过:     7，已跳过:     0，总计:     7，持续时间: 4 s - AICopilot.BackendTests.dll (net10.0)
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
[2mdist/[22m[35massets/ConfigView-Bb9n4EuW.css                                        [39m[1m[2m 21.45 kB[22m[1m[22m[2m │ gzip:   2.69 kB[22m
[2mdist/[22m[35massets/ChatView-BKyl380C.css                                          [39m[1m[2m 26.30 kB[22m[1m[22m[2m │ gzip:   4.70 kB[22m
[2mdist/[22m[35massets/index-DvFWv-08.css                                             [39m[1m[2m 26.98 kB[22m[1m[22m[2m │ gzip:   6.29 kB[22m
[2mdist/[22m[36massets/_plugin-vue_export-helper-DlAUqK2U.js                          [39m[1m[2m  0.09 kB[22m[1m[22m[2m │ gzip:   0.10 kB[22m
[2mdist/[22m[36massets/loader-circle-DOoVEIH0.js                                      [39m[1m[2m  0.14 kB[22m[1m[22m[2m │ gzip:   0.15 kB[22m
[2mdist/[22m[36massets/x-CuPbADUE.js                                                  [39m[1m[2m  0.52 kB[22m[1m[22m[2m │ gzip:   0.30 kB[22m
[2mdist/[22m[36massets/StatsWidget-Dlf9c2v6.js                                        [39m[1m[2m  0.67 kB[22m[1m[22m[2m │ gzip:   0.42 kB[22m
[2mdist/[22m[36massets/shield-check-D88zU4Q-.js                                       [39m[1m[2m  0.70 kB[22m[1m[22m[2m │ gzip:   0.41 kB[22m
[2mdist/[22m[36massets/AiCheckbox.vue_vue_type_script_setup_true_lang-1Aom8ou-.js     [39m[1m[2m  0.73 kB[22m[1m[22m[2m │ gzip:   0.46 kB[22m
[2mdist/[22m[36massets/DataTableWidget-Djs8m-1Z.js                                    [39m[1m[2m  1.04 kB[22m[1m[22m[2m │ gzip:   0.62 kB[22m
[2mdist/[22m[36massets/CloudOidcCompleteView-DsOYVCad.js                              [39m[1m[2m  1.62 kB[22m[1m[22m[2m │ gzip:   0.98 kB[22m
[2mdist/[22m[36massets/ForbiddenView-DQsgtICH.js                                      [39m[1m[2m  3.06 kB[22m[1m[22m[2m │ gzip:   1.60 kB[22m
[2mdist/[22m[36massets/AiTableCard.vue_vue_type_script_setup_true_lang-D-SvI639.js    [39m[1m[2m  6.71 kB[22m[1m[22m[2m │ gzip:   2.44 kB[22m
[2mdist/[22m[36massets/LoginView-C6TEREsp.js                                          [39m[1m[2m  7.89 kB[22m[1m[22m[2m │ gzip:   3.53 kB[22m
[2mdist/[22m[36massets/AiNumberInput.vue_vue_type_script_setup_true_lang-AuGglErw.js  [39m[1m[2m  8.93 kB[22m[1m[22m[2m │ gzip:   2.59 kB[22m
[2mdist/[22m[36massets/AccessView-CE4vyqcS.js                                         [39m[1m[2m 22.69 kB[22m[1m[22m[2m │ gzip:   6.50 kB[22m
[2mdist/[22m[36massets/KnowledgeView-C6uWK4N5.js                                      [39m[1m[2m 29.27 kB[22m[1m[22m[2m │ gzip:   7.65 kB[22m
[2mdist/[22m[36massets/AiButton.vue_vue_type_script_setup_true_lang-HOJbjZNq.js       [39m[1m[2m 29.76 kB[22m[1m[22m[2m │ gzip:   9.69 kB[22m
[2mdist/[22m[36massets/AiTag.vue_vue_type_script_setup_true_lang-mTtICmO9.js          [39m[1m[2m 58.27 kB[22m[1m[22m[2m │ gzip:  20.38 kB[22m
[2mdist/[22m[36massets/ConfigView-_Kx-LVey.js                                         [39m[1m[2m 92.92 kB[22m[1m[22m[2m │ gzip:  21.62 kB[22m
[2mdist/[22m[36massets/ChatView-CjA7G-8Z.js                                           [39m[1m[2m153.47 kB[22m[1m[22m[2m │ gzip:  62.90 kB[22m
[2mdist/[22m[36massets/index-CUX2Mr7i.js                                              [39m[1m[2m216.39 kB[22m[1m[22m[2m │ gzip:  78.16 kB[22m
[2mdist/[22m[36massets/ChartWidget-d3LoSSuD.js                                        [39m[1m[2m547.15 kB[22m[1m[22m[2m │ gzip: 184.75 kB[22m
[32m✓ built in 13.87s[39m
```

### Frontend Trial Operations HTTP Smoke

```text
Frontend HTTP smoke passed at http://127.0.0.1:5190/chat
```

## Remaining Risk

- P10 does not enable Real CloudReadonly, production Agent queries, production tools, or Cloud/Edge linkage.
- ReadyForP11Planning is an evidence gate for planning P11 only; production Pilot still requires a separate explicit plan and authorization.
- P10 is not a full multi-user trial management suite; it is the minimum internal trial ledger, risk register, readiness gate, and sanitized evidence package.

