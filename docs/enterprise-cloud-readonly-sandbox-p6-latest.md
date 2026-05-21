# AICopilot Enterprise CloudReadonly Sandbox P6 Acceptance

- GeneratedAt: 2026-05-20 10:43:58
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled by default
- Sandbox Boundary: SandboxSmokeOnly; no Agent Runtime real Cloud read is enabled
- Test Mode: fake sandbox client and contract fixtures; real Cloud endpoint/token are optional smoke inputs only
- Build Output: C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6 for focused tests; AppHost contract tests use default Debug output so Aspire starts current binaries

## Summary

- Inherited P5 Acceptance Report Check: PASSED
- Enterprise CloudReadonly Sandbox P6 Scope Guard: PASSED
- Build HttpApi: PASSED
- Run P6 Focused Backend Tests: PASSED
- Run Cloud Readiness Contract Tests: PASSED
- Build Frontend: PASSED
- Frontend Sandbox Panel HTTP Smoke: PASSED

## P6 Sandbox Evidence

- Default Gate: CloudReadonly.Mode, CloudReadonly.Real, AllowProductionRead, CloudAiRead, and CloudReadonlySandbox remain disabled by default.
- Sandbox Config: RealSandboxSmoke uses CloudReadonlySandbox, not CloudReadonly.Mode=Real and not CloudAiRead.Enabled.
- Contract Endpoints: devices, capacity_summary, device_logs, and pass_station_records are the only smoke allowlist endpoints.
- Policy Rejection: Recipe, recipe version, write-semantics, unknown, and unsafe POST paths remain BlockedByPolicy.
- Tool Registry Gate: query_cloud_data_readonly remains disabled, hidden from Planner, and non-executable by Agent after sandbox smoke.
- Audit Shape: endpoint checks record endpoint code, method, path, status, duration, row count, truncated flag, result hash, and error code without token or payload plaintext.
- Frontend Smoke: config page exposes readiness and sandbox smoke status without token or full payload display when frontend checks are enabled.

## Details

### Inherited P5 Acceptance Report Check

```text
Using existing P5 acceptance report: .\docs\enterprise-cloud-readonly-readiness-p5-latest.md
Using existing P4 acceptance report: .\docs\enterprise-tool-governance-p4-latest.md
```

### Enterprise CloudReadonly Sandbox P6 Scope Guard

```text
Enterprise Data Governance scope guard passed. Checked 138 candidate file(s).
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:01:06.40
```

### Run P6 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-cloud-readonly-sandbox-p6\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    11，已跳过:     0，总计:    11，持续时间: 265 ms - AICopilot.BackendTests.dll (net10.0)
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

已通过! - 失败:     0，通过:    17，已跳过:     0，总计:    17，持续时间: 57 s - AICopilot.BackendTests.dll (net10.0)
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
[2mdist/[22m[35massets/ConfigView-IqEEZi8_.css                                        [39m[1m[2m 21.45 kB[22m[1m[22m[2m │ gzip:   2.70 kB[22m
[2mdist/[22m[35massets/ChatView-BD_-ZV_K.css                                          [39m[1m[2m 23.04 kB[22m[1m[22m[2m │ gzip:   4.31 kB[22m
[2mdist/[22m[35massets/index-DvFWv-08.css                                             [39m[1m[2m 26.98 kB[22m[1m[22m[2m │ gzip:   6.29 kB[22m
[2mdist/[22m[36massets/_plugin-vue_export-helper-DlAUqK2U.js                          [39m[1m[2m  0.09 kB[22m[1m[22m[2m │ gzip:   0.10 kB[22m
[2mdist/[22m[36massets/loader-circle-7wCIoJMa.js                                      [39m[1m[2m  0.14 kB[22m[1m[22m[2m │ gzip:   0.15 kB[22m
[2mdist/[22m[36massets/x-CNb6FSlR.js                                                  [39m[1m[2m  0.52 kB[22m[1m[22m[2m │ gzip:   0.30 kB[22m
[2mdist/[22m[36massets/StatsWidget-Cme7AcQ-.js                                        [39m[1m[2m  0.67 kB[22m[1m[22m[2m │ gzip:   0.42 kB[22m
[2mdist/[22m[36massets/shield-check-C-OQVYCd.js                                       [39m[1m[2m  0.70 kB[22m[1m[22m[2m │ gzip:   0.41 kB[22m
[2mdist/[22m[36massets/AiCheckbox.vue_vue_type_script_setup_true_lang-DDKWCR8P.js     [39m[1m[2m  0.73 kB[22m[1m[22m[2m │ gzip:   0.46 kB[22m
[2mdist/[22m[36massets/DataTableWidget-DPp9tNXn.js                                    [39m[1m[2m  1.04 kB[22m[1m[22m[2m │ gzip:   0.62 kB[22m
[2mdist/[22m[36massets/CloudOidcCompleteView-DLQbBRPp.js                              [39m[1m[2m  1.62 kB[22m[1m[22m[2m │ gzip:   0.98 kB[22m
[2mdist/[22m[36massets/ForbiddenView-DYl05ykL.js                                      [39m[1m[2m  3.06 kB[22m[1m[22m[2m │ gzip:   1.60 kB[22m
[2mdist/[22m[36massets/AiTableCard.vue_vue_type_script_setup_true_lang-B1x_eGVw.js    [39m[1m[2m  6.71 kB[22m[1m[22m[2m │ gzip:   2.44 kB[22m
[2mdist/[22m[36massets/LoginView-D_JwgZyF.js                                          [39m[1m[2m  7.89 kB[22m[1m[22m[2m │ gzip:   3.53 kB[22m
[2mdist/[22m[36massets/AiNumberInput.vue_vue_type_script_setup_true_lang-Dd3MQgLD.js  [39m[1m[2m  8.93 kB[22m[1m[22m[2m │ gzip:   2.59 kB[22m
[2mdist/[22m[36massets/AccessView-BBAAVqm6.js                                         [39m[1m[2m 22.69 kB[22m[1m[22m[2m │ gzip:   6.50 kB[22m
[2mdist/[22m[36massets/KnowledgeView-BWtu_22V.js                                      [39m[1m[2m 29.27 kB[22m[1m[22m[2m │ gzip:   7.64 kB[22m
[2mdist/[22m[36massets/AiButton.vue_vue_type_script_setup_true_lang-0cQy5JCD.js       [39m[1m[2m 29.76 kB[22m[1m[22m[2m │ gzip:   9.69 kB[22m
[2mdist/[22m[36massets/AiTag.vue_vue_type_script_setup_true_lang-BwT_VcLV.js          [39m[1m[2m 58.27 kB[22m[1m[22m[2m │ gzip:  20.38 kB[22m
[2mdist/[22m[36massets/ConfigView-BpwJAcqR.js                                         [39m[1m[2m 87.95 kB[22m[1m[22m[2m │ gzip:  20.84 kB[22m
[2mdist/[22m[36massets/ChatView-DljEJwGO.js                                           [39m[1m[2m143.79 kB[22m[1m[22m[2m │ gzip:  60.54 kB[22m
[2mdist/[22m[36massets/index-BeGWeqp7.js                                              [39m[1m[2m212.04 kB[22m[1m[22m[2m │ gzip:  77.14 kB[22m
[2mdist/[22m[36massets/ChartWidget-CciyMdwd.js                                        [39m[1m[2m547.15 kB[22m[1m[22m[2m │ gzip: 184.75 kB[22m
[32m✓ built in 11.37s[39m
```

### Frontend Sandbox Panel HTTP Smoke

```text
Frontend HTTP smoke passed at http://127.0.0.1:5182/config
```

## Remaining Risk

- P6 proves sandbox-only readiness and fake contract behavior; it does not prove production Cloud read access.
- A real sandbox endpoint/token, if provided later, must still be used only as RealSandboxSmoke and must not enter Agent Runtime.
- P7 must add a separate controlled Agent Sandbox Trial gate before any real Cloud data can be used in Agent outputs.

