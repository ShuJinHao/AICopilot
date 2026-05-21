# AICopilot Enterprise Data Governance P1 Acceptance

- GeneratedAt: 2026-05-19 12:40:50
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled
- Test Mode: fake/mock model endpoints and SimulationBusiness data source; real API keys are not required

## Summary

- Enterprise Data Governance Scope Guard: PASSED
- Build HttpApi: PASSED
- Compile BackendTests Sources: PASSED
- Run P0 And P1 Focused Backend Tests: PASSED
- Build Frontend: PASSED

## P1 Evidence

- Text-to-SQL: deterministic draft generation, schema allowlist, sensitive field blocklist, and read-only SQL guardrail are covered by focused backend tests.
- Agent/Data output: business query result contracts preserve sourceMode, isSimulation, sourceLabel, queryHash, row count, and truncation status.
- Prompt Policy: policy version activation and hash-only audit metadata are covered by compile and focused domain tests.
- RAG Governance: category/supplement commands, soft-delete semantics, and CriticalOverride effective-window behavior are covered.
- Model Pool: LeastInFlight, WeightedRoundRobin, concurrency saturation, sticky streaming, fallback, and circuit statistics are covered with mock endpoints.
- Secrets: API keys remain write-only in configuration-facing contracts; acceptance output does not print plaintext keys.

## Details

### Enterprise Data Governance Scope Guard

```text
powershell : warning: in the working copy of 'src/core/AICopilot.Core.AiGateway/Aggregates/LanguageModel/LanguageModelU
sage.cs', LF will be replaced by CRLF the next time Git touches it
At C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\scripts\Run-EnterpriseDataGovernanceP1Acceptance.ps1:48 char:5
+     powershell -ExecutionPolicy Bypass -File .\scripts\Test-Enterpris ...
+     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: (warning: in the... Git touches it:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError

warning: in the working copy of 'src/core/AICopilot.Core.AiGateway/Aggregates/Tools/BuiltInToolRegistrations.cs', LF wi
ll be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.AiGateway/Ids/AiGatewayIds.cs', LF will be replaced by CRLF th
e next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.DataAnalysis/Aggregates/BusinessDatabase/BusinessDataExternalS
ystemType.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.DataAnalysis/Aggregates/BusinessDatabase/BusinessDatabase.cs',
 LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.Rag/Aggregates/KnowledgeBase/Document.cs', LF will be replaced
 by CRLF the next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.Rag/Aggregates/KnowledgeBase/KnowledgeBase.cs', LF will be rep
laced by CRLF the next time Git touches it
warning: in the working copy of 'src/core/AICopilot.Core.Rag/Ids/RagIds.cs', LF will be replaced by CRLF the next time
Git touches it
warning: in the working copy of 'src/hosts/AICopilot.HttpApi/Controllers/AiGatewayController.cs', LF will be replaced b
y CRLF the next time Git touches it
warning: in the working copy of 'src/hosts/AICopilot.HttpApi/Controllers/DataAnalysisController.cs', LF will be replace
d by CRLF the next time Git touches it
warning: in the working copy of 'src/hosts/AICopilot.HttpApi/Controllers/RagController.cs', LF will be replaced by CRLF
 the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.AiRuntime/AgentRuntimeFactory.cs', LF will be replaced by
 CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.AiRuntime/DependencyInjection.cs', LF will be replaced by
 CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.AiRuntime/ModelProviderReliability.cs', LF will be replac
ed by CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.EntityFrameworkCore/AiGatewayDbContext.cs', LF will be re
placed by CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/DataAnalysis/BusinessDa
tabaseConfiguration.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.EntityFrameworkCore/Configuration/Rag/DocumentConfigurati
on.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.EntityFrameworkCore/DependencyInjection.cs', LF will be r
eplaced by CRLF the next time Git touches it
warning: in the working copy of 'src/infrastructure/AICopilot.EntityFrameworkCore/RagDbContext.cs', LF will be replaced
 by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/AgentTasks/AgentReportComposer.cs', LF will be
 replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskCommands.cs', LF will be r
eplaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskPlanDocument.cs', LF will
be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskRuntime.cs', LF will be re
placed by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/Agents/ChatExecutionMetadataAccessor.cs', LF w
ill be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/Commands/LanguageModels/TestLanguageModel.cs',
 LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/DependencyInjection.cs', LF will be replaced b
y CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/Queries/LanguageModels/LanguageModelDtoMapper.
cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.AiGatewayService/Workflows/Executors/FinalAgentBuildExecutor.cs
', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseContractM
apper.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseManagemen
t.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.DataAnalysisService/DependencyInjection.cs', LF will be replace
d by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.IdentityService/Authorization/PermissionCatalog.cs', LF will be
 replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.RagService/Documents/DocumentManagement.cs', LF will be replace
d by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.RagService/Queries/KnowledgeBases/SearchKnowledgeBase.cs', LF w
ill be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.Services.Contracts/Contracts/AgentWorkspaceContracts.cs', LF wi
ll be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.Services.Contracts/Contracts/AiRuntimeContracts.cs', LF will be
 replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.Services.Contracts/Contracts/DataSourceContracts.cs', LF will b
e replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.Services.Contracts/Contracts/FinalAgentContextContracts.cs', LF
 will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'src/services/AICopilot.Services.Contracts/Contracts/RagContracts.cs', LF will be repla
ced by CRLF the next time Git touches it
Enterprise Data Governance P0 scope guard passed. Checked 70 candidate file(s).
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:01:06.13
```

### Compile BackendTests Sources

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.BackendTests -> C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.BackendTests\bin\Debug\net10.0\AICopilot.BackendTests.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:13.17
```

### Run P0 And P1 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    57，已跳过:     0，总计:    57，持续时间: 1 s - AICopilot.BackendTests.dll (net10.0)
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
[32m✓[39m 3175 modules transformed.
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
[2mdist/[22m[35massets/ConfigView-BmvmzkkU.css                                        [39m[1m[2m 17.91 kB[22m[1m[22m[2m │ gzip:   2.40 kB[22m
[2mdist/[22m[35massets/ChatView-DP45zVoJ.css                                          [39m[1m[2m 21.04 kB[22m[1m[22m[2m │ gzip:   3.99 kB[22m
[2mdist/[22m[35massets/index-ca-YXKSy.css                                             [39m[1m[2m 26.96 kB[22m[1m[22m[2m │ gzip:   6.28 kB[22m
[2mdist/[22m[36massets/_plugin-vue_export-helper-DlAUqK2U.js                          [39m[1m[2m  0.09 kB[22m[1m[22m[2m │ gzip:   0.10 kB[22m
[2mdist/[22m[36massets/loader-circle-CXOXsOMg.js                                      [39m[1m[2m  0.14 kB[22m[1m[22m[2m │ gzip:   0.15 kB[22m
[2mdist/[22m[36massets/x-BDuxLgmP.js                                                  [39m[1m[2m  0.52 kB[22m[1m[22m[2m │ gzip:   0.30 kB[22m
[2mdist/[22m[36massets/StatsWidget-C-T_HEl0.js                                        [39m[1m[2m  0.67 kB[22m[1m[22m[2m │ gzip:   0.42 kB[22m
[2mdist/[22m[36massets/shield-check-CKpCEcsb.js                                       [39m[1m[2m  0.70 kB[22m[1m[22m[2m │ gzip:   0.41 kB[22m
[2mdist/[22m[36massets/AiCheckbox.vue_vue_type_script_setup_true_lang-DVloRa91.js     [39m[1m[2m  0.73 kB[22m[1m[22m[2m │ gzip:   0.46 kB[22m
[2mdist/[22m[36massets/DataTableWidget-CDM5EUTQ.js                                    [39m[1m[2m  1.04 kB[22m[1m[22m[2m │ gzip:   0.62 kB[22m
[2mdist/[22m[36massets/CloudOidcCompleteView-Bhl7-5wK.js                              [39m[1m[2m  1.62 kB[22m[1m[22m[2m │ gzip:   0.97 kB[22m
[2mdist/[22m[36massets/ForbiddenView-qlUdsZst.js                                      [39m[1m[2m  3.06 kB[22m[1m[22m[2m │ gzip:   1.60 kB[22m
[2mdist/[22m[36massets/AiTableCard.vue_vue_type_script_setup_true_lang-D6QVHR_w.js    [39m[1m[2m  6.71 kB[22m[1m[22m[2m │ gzip:   2.44 kB[22m
[2mdist/[22m[36massets/LoginView-BiWb_gmG.js                                          [39m[1m[2m  7.89 kB[22m[1m[22m[2m │ gzip:   3.53 kB[22m
[2mdist/[22m[36massets/AiNumberInput.vue_vue_type_script_setup_true_lang-DaYRdgqy.js  [39m[1m[2m  8.93 kB[22m[1m[22m[2m │ gzip:   2.59 kB[22m
[2mdist/[22m[36massets/AccessView-C9dzkFu2.js                                         [39m[1m[2m 22.69 kB[22m[1m[22m[2m │ gzip:   6.50 kB[22m
[2mdist/[22m[36massets/KnowledgeView-DIcyya7_.js                                      [39m[1m[2m 29.27 kB[22m[1m[22m[2m │ gzip:   7.65 kB[22m
[2mdist/[22m[36massets/AiButton.vue_vue_type_script_setup_true_lang-BOLdNNhh.js       [39m[1m[2m 29.76 kB[22m[1m[22m[2m │ gzip:   9.69 kB[22m
[2mdist/[22m[36massets/AiTag.vue_vue_type_script_setup_true_lang-JLngYVJr.js          [39m[1m[2m 58.27 kB[22m[1m[22m[2m │ gzip:  20.38 kB[22m
[2mdist/[22m[36massets/ConfigView-D_NZ4fP4.js                                         [39m[1m[2m 77.11 kB[22m[1m[22m[2m │ gzip:  18.82 kB[22m
[2mdist/[22m[36massets/ChatView-hNb4uX2u.js                                           [39m[1m[2m139.43 kB[22m[1m[22m[2m │ gzip:  59.36 kB[22m
[2mdist/[22m[36massets/index-TunZt1cv.js                                              [39m[1m[2m210.73 kB[22m[1m[22m[2m │ gzip:  76.86 kB[22m
[2mdist/[22m[36massets/ChartWidget-8EXnnaRd.js                                        [39m[1m[2m547.15 kB[22m[1m[22m[2m │ gzip: 184.75 kB[22m
[32m✓ built in 12.43s[39m
```

