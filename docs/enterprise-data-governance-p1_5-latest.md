# AICopilot Enterprise Data Governance P1.5 Acceptance

- GeneratedAt: 2026-05-20 10:07:03
- Repository: C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot
- Boundary: AICopilot only; Cloud/Edge unchanged; Real CloudReadonly disabled
- Test Mode: fake/mock model endpoints and SimulationBusiness data source; real API keys are not required
- Build Output: C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5

## Summary

- Enterprise Data Governance Scope Guard: PASSED
- Build EntityFrameworkCore: PASSED
- Build HttpApi: PASSED
- Temporary PostgreSQL Migration Smoke: FAILED
- Run P0 P1 P1_5 Focused Backend Tests: FAILED

## P1.5 Stabilization Evidence

- Migrations: DataAnalysis, RAG, and AiGateway migration designer/snapshot metadata are verified by focused backend tests; when Docker/PostgreSQL is available, the acceptance entry also runs an isolated migration and rollback smoke.
- Scope Guard: git warning noise is suppressed while Cloud/Edge, Real CloudReadonly, shell, dangerous SQL, arbitrary path write, and plaintext secret checks remain active.
- SimulationBusiness: Small remains the CI/quick profile and Medium remains the local acceptance profile; seed SQL is idempotent and readonly-boundary oriented.
- Text-to-SQL and Agent: focused tests continue to require SimulationBusiness markers, query hash metadata, readonly guardrails, and deterministic fake/mock behavior.
- Prompt Policy/RAG/Model Pool: active policy version, hash-only audit metadata, CriticalOverride supplement windows, mock endpoint load, fallback, sticky streaming, and circuit statistics remain covered.
- Secrets: API keys, connection strings, tokens, and passwords are not printed in acceptance evidence.

## Details

### Enterprise Data Governance Scope Guard

```text
Enterprise Data Governance scope guard passed. Checked 137 candidate file(s).
```

### Build EntityFrameworkCore

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\efcore\AICopilot.EntityFrameworkCore.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:00:21.18
```

### Build HttpApi

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.SharedKernel.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Core.McpServer.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Core.Rag.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Services.Contracts.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Embedding.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.EntityFrameworkCore.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.IdentityService.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.McpService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.ServiceDefaults.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\httpapi\AICopilot.HttpApi.dll

已成功生成。
    0 个警告
    0 个错误

已用时间 00:01:04.02
```

### Temporary PostgreSQL Migration Smoke

```text
Expected P1.5 migration tables were not all present. Count=0
At C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\scripts\Run-EnterpriseDataGovernanceP1_5Acceptance.ps1:116 char:13
+             throw "Expected P1.5 migration tables were not all presen ...
+             ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : OperationStopped: (Expected P1.5 m...resent. Count=0:String) [], RuntimeException
    + FullyQualifiedErrorId : Expected P1.5 migration tables were not all present. Count=0
```

### Run P0 P1 P1_5 Focused Backend Tests

```text
正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AICopilot.SharedKernel -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.SharedKernel.dll
  AICopilot.Core.Rag -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Core.Rag.dll
  AICopilot.Core.AiGateway -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Core.AiGateway.dll
  AICopilot.Core.DataAnalysis -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Core.DataAnalysis.dll
  AICopilot.Core.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Core.McpServer.dll
  AICopilot.Visualization -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Visualization.dll
  AICopilot.Services.Contracts -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Services.Contracts.dll
  AICopilot.EntityFrameworkCore -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.EntityFrameworkCore.dll
  AICopilot.AiRuntime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.AiRuntime.dll
  AICopilot.Dapper -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Dapper.dll
  AICopilot.Embedding -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Embedding.dll
  AICopilot.EventBus -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.EventBus.dll
  AICopilot.AgentPlugin -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.AgentPlugin.dll
  AICopilot.Infrastructure -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Infrastructure.dll
  AICopilot.AgentPlugin.Runtime -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.AgentPlugin.Runtime.dll
  AICopilot.Services.CrossCutting -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Services.CrossCutting.dll
  AICopilot.AiGatewayService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.AiGatewayService.dll
  AICopilot.DataAnalysisService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.DataAnalysisService.dll
  AICopilot.IdentityService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.IdentityService.dll
  AICopilot.RagService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.RagService.dll
  AICopilot.ServiceDefaults -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.ServiceDefaults.dll
  AICopilot.DataWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.DataWorker.dll
  AICopilot.McpService -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.McpService.dll
  AICopilot.HttpApi -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.HttpApi.dll
  AICopilot.MigrationWorkApp -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.MigrationWorkApp.dll
  AICopilot.RagWorker -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.RagWorker.dll
  AICopilot.AppHost -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.AppHost.dll
  AICopilot.Testing.McpServer -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.Testing.McpServer.dll
  AICopilot.BackendTests -> C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.BackendTests.dll
C:\Users\jinha\AppData\Local\Temp\aicopilot-enterprise-data-governance-p1_5\backendtests\AICopilot.BackendTests.dll (.NETCoreApp,Version=v10.0)的测试运行
总共 1 个测试文件与指定模式相匹配。
dotnet : [xUnit.net 00:00:03.35]     AICopilot.BackendTests.EnterpriseDataGovernanceP15Tests.EnterpriseGovernanceMigrat
ions_ShouldHaveDesignerAndSnapshotMarkers [FAIL]
At C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\scripts\Run-EnterpriseDataGovernanceP1_5Acceptance.ps1:158 char:5
+     dotnet test src/tests/AICopilot.BackendTests/AICopilot.BackendTes ...
+     ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : NotSpecified: ([xUnit.net 00:0...tMarkers [FAIL]:String) [], RemoteException
    + FullyQualifiedErrorId : NativeCommandError

  失败 AICopilot.BackendTests.EnterpriseDataGovernanceP15Tests.EnterpriseGovernanceMigrations_ShouldHaveDesignerAndSnapshotMarkers [28 ms]
  错误消息:
   Expected content "// <auto-generated />
using System;
using AICopilot.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AICopilot.EntityFrameworkCore.Migrations.AiGatewayDbContext
{
    [DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))]
    partial class AiGatewayDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("aigateway")
                .HasAnnotation("ProductVersion", "10.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentStep", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("description");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("error_message");

                    b.Property<DateTimeOffset?>("FinishedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("finished_at");

                    b.Property<string>("InputJson")
                        .HasColumnType("text")
                        .HasColumnName("input_json");

                    b.Property<string>("OutputJson")
                        .HasColumnType("text")
                        .HasColumnName("output_json");

                    b.Property<bool>("RequiresApproval")
                        .HasColumnType("boolean")
                        .HasColumnName("requires_approval");

                    b.Property<DateTimeOffset?>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("status");

                    b.Property<int>("StepIndex")
                        .HasColumnType("integer")
                        .HasColumnName("step_index");

                    b.Property<string>("StepType")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("step_type");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("title");

                    b.Property<string>("ToolCode")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("tool_code");

                    b.HasKey("Id");

                    b.HasIndex("TaskId", "StepIndex")
                        .IsUnique();

                    b.ToTable("agent_steps", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTask", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid?>("ActiveRunAttemptId")
                        .HasColumnType("uuid")
                        .HasColumnName("active_run_attempt_id");

                    b.Property<DateTimeOffset?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("FinalSummary")
                        .HasMaxLength(4000)
                        .HasColumnType("character varying(4000)")
                        .HasColumnName("final_summary");

                    b.Property<string>("Goal")
                        .IsRequired()
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("goal");

                    b.Property<Guid?>("ModelId")
                        .HasColumnType("uuid")
                        .HasColumnName("model_id");

                    b.Property<string>("PlanJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("plan_json");

                    b.Property<string>("RiskLevel")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("risk_level");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<int>("RunAttemptCount")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasDefaultValue(0)
                        .HasColumnName("run_attempt_count");

                    b.Property<DateTimeOffset?>("RunLeaseExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("run_lease_expires_at");

                    b.Property<Guid?>("RunLeaseId")
                        .HasColumnType("uuid")
                        .HasColumnName("run_lease_id");

                    b.Property<string>("RunLeaseOwner")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("run_lease_owner");

                    b.Property<Guid>("SessionId")
                        .HasColumnType("uuid")
                        .HasColumnName("session_id");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("status");

                    b.Property<string>("TaskCode")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("task_code");

                    b.Property<string>("TaskType")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("task_type");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("title");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.Property<Guid?>("WorkspaceId")
                        .HasColumnType("uuid")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("TaskCode")
                        .IsUnique();

                    b.HasIndex("UserId")
                        .HasDatabaseName("ix_agent_tasks_user_id");

                    b.ToTable("agent_tasks", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTaskRunAttempt", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<int>("AttemptNo")
                        .HasColumnType("integer")
                        .HasColumnName("attempt_no");

                    b.Property<DateTimeOffset?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.Property<string>("FailureCode")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("failure_code");

                    b.Property<DateTimeOffset?>("LeaseExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("lease_expires_at");

                    b.Property<Guid?>("LeaseId")
                        .HasColumnType("uuid")
                        .HasColumnName("lease_id");

                    b.Property<string>("LeaseOwner")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("lease_owner");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<string>("SafeMessage")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("safe_message");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<string>("TriggerType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("trigger_type");

                    b.HasKey("Id");

                    b.HasIndex("TaskId")
                        .HasDatabaseName("ix_agent_task_run_attempts_task_id");

                    b.HasIndex("TaskId", "AttemptNo")
                        .IsUnique()
                        .HasDatabaseName("ix_agent_task_run_attempts_task_attempt_no");

                    b.ToTable("agent_task_run_attempts", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTaskRunQueueItem", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("AvailableAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("available_at");

                    b.Property<DateTimeOffset?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("FailureCode")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("failure_code");

                    b.Property<DateTimeOffset?>("LeaseExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("lease_expires_at");

                    b.Property<Guid?>("LeaseId")
                        .HasColumnType("uuid")
                        .HasColumnName("lease_id");

                    b.Property<string>("LeaseOwner")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("lease_owner");

                    b.Property<Guid>("RequestedBy")
                        .HasColumnType("uuid")
                        .HasColumnName("requested_by");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<Guid?>("RunAttemptId")
                        .HasColumnType("uuid")
                        .HasColumnName("run_attempt_id");

                    b.Property<string>("SafeMessage")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("safe_message");

                    b.Property<DateTimeOffset?>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<string>("TriggerType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("trigger_type");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id");

                    b.HasIndex("LeaseExpiresAt")
                        .HasDatabaseName("ix_agent_task_run_queue_items_lease_expires_at");

                    b.HasIndex("RunAttemptId")
                        .HasDatabaseName("ix_agent_task_run_queue_items_run_attempt_id");

                    b.HasIndex("TaskId")
                        .IsUnique()
                        .HasDatabaseName("ux_agent_task_run_queue_items_active_task")
                        .HasFilter("status IN ('Queued', 'Leased')");

                    b.HasIndex("Status", "AvailableAt")
                        .HasDatabaseName("ix_agent_task_run_queue_items_status_available_at");

                    b.ToTable("agent_task_run_queue_items", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentWorkerHeartbeat", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid?>("ActiveQueueItemId")
                        .HasColumnType("uuid")
                        .HasColumnName("active_queue_item_id");

                    b.Property<Guid?>("ActiveTaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("active_task_id");

                    b.Property<DateTimeOffset>("LastSeenAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_seen_at");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Version")
                        .IsRequired()
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("version");

                    b.Property<string>("WorkerId")
                        .IsRequired()
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("worker_id");

                    b.Property<string>("WorkerName")
                        .IsRequired()
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("worker_name");

                    b.Property<string>("WorkspaceRootHash")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)")
                        .HasColumnName("workspace_root_hash");

                    b.HasKey("Id");

                    b.HasIndex("ActiveTaskId")
                        .HasDatabaseName("ix_agent_worker_heartbeats_active_task_id");

                    b.HasIndex("LastSeenAt")
                        .HasDatabaseName("ix_agent_worker_heartbeats_last_seen_at");

                    b.HasIndex("WorkerId")
                        .IsUnique()
                        .HasDatabaseName("ux_agent_worker_heartbeats_worker_id");

                    b.ToTable("agent_worker_heartbeats", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy.ApprovalPolicy", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Description")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("description");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean")
                        .HasColumnName("is_enabled");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<bool>("RequiresOnsiteAttestation")
                        .HasColumnType("boolean")
                        .HasColumnName("requires_onsite_attestation");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<int>("SchemaVersion")
                        .HasColumnType("integer")
                        .HasColumnName("schema_version");

                    b.Property<string>("TargetName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("target_name");

                    b.Property<string>("TargetType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("target_type");

                    b.PrimitiveCollection<string[]>("ToolNames")
                        .IsRequired()
                        .HasColumnType("text[]")
                        .HasColumnName("tool_names");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("approval_policies", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Approvals.ApprovalRequest", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("ApprovalComment")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("approval_comment");

                    b.Property<string>("ApprovalType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("approval_type");

                    b.Property<DateTimeOffset?>("ApprovedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("approved_at");

                    b.Property<Guid?>("ApprovedBy")
                        .HasColumnType("uuid")
                        .HasColumnName("approved_by");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid>("RequestedBy")
                        .HasColumnType("uuid")
                        .HasColumnName("requested_by");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<string>("TargetId")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("target_id");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.HasKey("Id");

                    b.HasIndex("TaskId")
                        .HasDatabaseName("ix_approval_requests_task_id");

                    b.ToTable("approval_requests", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Artifacts.Artifact", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("ArtifactType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("artifact_type");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid?>("CreatedByStepId")
                        .HasColumnType("uuid")
                        .HasColumnName("created_by_step_id");

                    b.Property<long>("FileSize")
                        .HasColumnType("bigint")
                        .HasColumnName("file_size");

                    b.Property<string>("MimeType")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("mime_type");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<string>("RelativePath")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("relative_path");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<int>("Version")
                        .HasColumnType("integer")
                        .HasColumnName("version");

                    b.Property<Guid>("WorkspaceId")
                        .HasColumnType("uuid")
                        .HasColumnName("workspace_id");

                    b.HasKey("Id");

                    b.HasIndex("WorkspaceId", "RelativePath");

                    b.ToTable("artifacts", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Artifacts.ArtifactWorkspace", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("RootPath")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("root_path");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.Property<string>("WorkspaceCode")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("workspace_code");

                    b.Property<string>("WorkspaceUrl")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("workspace_url");

                    b.HasKey("Id");

                    b.HasIndex("TaskId")
                        .IsUnique();

                    b.HasIndex("WorkspaceCode")
                        .IsUnique();

                    b.ToTable("artifact_workspaces", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<int>("BuiltInVersion")
                        .HasColumnType("integer")
                        .HasColumnName("built_in_version");

                    b.Property<string>("Code")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("code");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("description");

                    b.Property<bool>("IsBuiltIn")
                        .HasColumnType("boolean")
                        .HasColumnName("is_built_in");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean")
                        .HasColumnName("is_enabled");

                    b.Property<Guid>("ModelId")
                        .HasColumnType("uuid")
                        .HasColumnName("model_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<string>("Scope")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("scope");

                    b.Property<string>("SystemPrompt")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("system_prompt");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique()
                        .HasFilter("code IS NOT NULL");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("conversation_templates", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("ApiKey")
                        .HasMaxLength(2048)
                        .HasColumnType("character varying(2048)")
                        .HasColumnName("api_key");

                    b.Property<string>("BaseUrl")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("base_url");

                    b.Property<DateTimeOffset?>("ConnectivityCheckedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("connectivity_checked_at");

                    b.Property<string>("ConnectivityError")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("connectivity_error");

                    b.Property<int>("ConnectivityStatus")
                        .HasColumnType("integer")
                        .HasColumnName("connectivity_status");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean")
                        .HasColumnName("is_enabled");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("name");

                    b.Property<string>("ProtocolType")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character varying(64)")
                        .HasColumnName("protocol_type");

                    b.Property<string>("Provider")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("provider");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<int>("Usage")
                        .HasColumnType("integer")
                        .HasColumnName("usage");

                    b.HasKey("Id");

                    b.HasIndex("Provider", "Name")
                        .IsUnique();

                    b.ToTable("language_models", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.RoutingModel.RoutingModelConfiguration", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<bool>("IsActive")
                        .HasColumnType("boolean")
                        .HasColumnName("is_active");

                    b.Property<Guid>("ModelId")
                        .HasColumnType("uuid")
                        .HasColumnName("model_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.HasKey("Id");

                    b.HasIndex("IsActive")
                        .IsUnique()
                        .HasFilter("is_active");

                    b.HasIndex("ModelId");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("routing_model_configurations", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.RuntimeSettings.ChatRuntimeSettings", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<int>("AgentPlanningHistoryCount")
                        .HasColumnType("integer")
                        .HasColumnName("agent_planning_history_count");

                    b.Property<int>("AnswerHistoryCount")
                        .HasColumnType("integer")
                        .HasColumnName("answer_history_count");

                    b.Property<int>("ContextTokenLimit")
                        .HasColumnType("integer")
                        .HasColumnName("context_token_limit");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<int>("RagRewriteHistoryCount")
                        .HasColumnType("integer")
                        .HasColumnName("rag_rewrite_history_count");

                    b.Property<int>("RoutingHistoryCount")
                        .HasColumnType("integer")
                        .HasColumnName("routing_history_count");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<int>("SummaryThresholdMessages")
                        .HasColumnType("integer")
                        .HasColumnName("summary_threshold_messages");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id");

                    b.ToTable("chat_runtime_settings", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Sessions.Message", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer")
                        .HasColumnName("id");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("content");

                    b.Property<int?>("ContextWindowTokens")
                        .HasColumnType("integer")
                        .HasColumnName("context_window_tokens");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<Guid?>("FinalModelId")
                        .HasColumnType("uuid")
                        .HasColumnName("final_model_id");

                    b.Property<string>("FinalModelName")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("final_model_name");

                    b.Property<int?>("MaxOutputTokens")
                        .HasColumnType("integer")
                        .HasColumnName("max_output_tokens");

                    b.Property<Guid?>("RoutingModelId")
                        .HasColumnType("uuid")
                        .HasColumnName("routing_model_id");

                    b.Property<string>("RoutingModelName")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("routing_model_name");

                    b.Property<Guid>("SessionId")
                        .HasColumnType("uuid")
                        .HasColumnName("session_id");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("type");

                    b.HasKey("Id");

                    b.HasIndex("SessionId");

                    b.ToTable("messages", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Sessions.Session", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTime?>("LastMessageAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_message_at");

                    b.Property<string>("LastMessageSummary")
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("last_message_summary");

                    b.Property<int>("MessageCount")
                        .HasColumnType("integer")
                        .HasColumnName("message_count");

                    b.Property<DateTimeOffset?>("OnsiteConfirmationExpiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("onsite_confirmation_expires_at");

                    b.Property<DateTimeOffset?>("OnsiteConfirmedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("onsite_confirmed_at");

                    b.Property<string>("OnsiteConfirmedBy")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)")
                        .HasColumnName("onsite_confirmed_by");

                    b.Property<Guid>("TemplateId")
                        .HasColumnType("uuid")
                        .HasColumnName("template_id");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(48)
                        .HasColumnType("character varying(48)")
                        .HasColumnName("title");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("UserId")
                        .HasDatabaseName("ix_sessions_user_id");

                    b.ToTable("sessions", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Tools.ToolExecutionRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("ArtifactId")
                        .HasMaxLength(80)
                        .HasColumnType("character varying(80)")
                        .HasColumnName("artifact_id");

                    b.Property<string>("AuditMetadata")
                        .HasMaxLength(4000)
                        .HasColumnType("character varying(4000)")
                        .HasColumnName("audit_metadata");

                    b.Property<DateTimeOffset?>("CompletedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("completed_at");

                    b.Property<long?>("DurationMs")
                        .HasColumnType("bigint")
                        .HasColumnName("duration_ms");

                    b.Property<string>("ErrorCode")
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("error_code");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("error_message");

                    b.Property<string>("InputSummary")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)")
                        .HasColumnName("input_summary");

                    b.Property<string>("OutputSummary")
                        .HasMaxLength(4000)
                        .HasColumnType("character varying(4000)")
                        .HasColumnName("output_summary");

                    b.Property<Guid?>("RunAttemptId")
                        .HasColumnType("uuid")
                        .HasColumnName("run_attempt_id");

                    b.Property<DateTimeOffset>("StartedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("started_at");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<Guid>("StepId")
                        .HasColumnType("uuid")
                        .HasColumnName("step_id");

                    b.Property<Guid>("TaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("task_id");

                    b.Property<string>("ToolCode")
                        .IsRequired()
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("tool_code");

                    b.HasKey("Id");

                    b.HasIndex("RunAttemptId")
                        .HasDatabaseName("ix_tool_execution_records_run_attempt_id");

                    b.HasIndex("TaskId")
                        .HasDatabaseName("ix_tool_execution_records_task_id");

                    b.HasIndex("ToolCode")
                        .HasDatabaseName("ix_tool_execution_records_tool_code");

                    b.HasIndex("TaskId", "StepId")
                        .HasDatabaseName("ix_tool_execution_records_task_step");

                    b.ToTable("tool_execution_records", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Tools.ToolRegistration", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("AuditLevel")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("audit_level");

                    b.Property<string>("ApprovalPolicy")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("approval_policy");

                    b.Property<string[]>("BusinessDomains")
                        .IsRequired()
                        .HasColumnType("text[]")
                        .HasColumnName("business_domains");

                    b.Property<int>("CatalogVersion")
                        .HasColumnType("integer")
                        .HasColumnName("catalog_version");

                    b.Property<string>("Category")
                        .IsRequired()
                        .HasMaxLength(120)
                        .HasColumnType("character varying(120)")
                        .HasColumnName("category");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("DataBoundary")
                        .IsRequired()
                        .HasMaxLength(60)
                        .HasColumnType("character varying(60)")
                        .HasColumnName("data_boundary");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("description");

                    b.Property<string>("DisplayName")
                        .IsRequired()
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("display_name");

                    b.Property<string>("InputSchemaJson")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("input_schema_json");

                    b.Property<bool>("IsEnabled")
                        .HasColumnType("boolean")
                        .HasColumnName("is_enabled");

                    b.Property<bool>("IsExecutableByAgent")
                        .HasColumnType("boolean")
                        .HasColumnName("is_executable_by_agent");

                    b.Property<bool>("IsVisibleToPlanner")
                        .HasColumnType("boolean")
                        .HasColumnName("is_visible_to_planner");

                    b.Property<string>("OutputSchemaJson")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("output_schema_json");

                    b.Property<string>("ProviderType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("provider_type");

                    b.Property<string>("RequiredPermission")
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("required_permission");

                    b.Property<bool>("RequiresApproval")
                        .HasColumnType("boolean")
                        .HasColumnName("requires_approval");

                    b.Property<string>("RiskLevel")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("risk_level");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<int>("SchemaVersion")
                        .HasColumnType("integer")
                        .HasColumnName("schema_version");

                    b.Property<string>("TargetName")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("target_name");

                    b.Property<string>("TargetType")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("target_type");

                    b.Property<int>("TimeoutSeconds")
                        .HasColumnType("integer")
                        .HasColumnName("timeout_seconds");

                    b.Property<string>("ToolCode")
                        .IsRequired()
                        .HasMaxLength(160)
                        .HasColumnType("character varying(160)")
                        .HasColumnName("tool_code");

                    b.Property<DateTimeOffset>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("Id");

                    b.HasIndex("ToolCode")
                        .IsUnique();

                    b.ToTable("tool_registrations", "aigateway");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Uploads.UploadRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<Guid?>("AgentTaskId")
                        .HasColumnType("uuid")
                        .HasColumnName("agent_task_id");

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("content_type");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)")
                        .HasColumnName("file_name");

                    b.Property<long>("FileSize")
                        .HasColumnType("bigint")
                        .HasColumnName("file_size");

                    b.Property<Guid?>("KnowledgeBaseId")
                        .HasColumnType("uuid")
                        .HasColumnName("knowledge_base_id");

                    b.Property<int?>("RagDocumentId")
                        .HasColumnType("integer")
                        .HasColumnName("rag_document_id");

                    b.Property<uint>("RowVersion")
                        .IsConcurrencyToken()
                        .ValueGeneratedOnAddOrUpdate()
                        .HasColumnType("xid")
                        .HasColumnName("xmin");

                    b.Property<string>("Scope")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("scope");

                    b.Property<Guid?>("SessionId")
                        .HasColumnType("uuid")
                        .HasColumnName("session_id");

                    b.Property<string>("Sha256")
                        .IsRequired()
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)")
                        .HasColumnName("sha256");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(40)
                        .HasColumnType("character varying(40)")
                        .HasColumnName("status");

                    b.Property<string>("StoragePath")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)")
                        .HasColumnName("storage_path");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid")
                        .HasColumnName("user_id");

                    b.HasKey("Id");

                    b.HasIndex("KnowledgeBaseId")
                        .HasDatabaseName("ix_upload_records_knowledge_base_id");

                    b.HasIndex("UserId", "AgentTaskId")
                        .HasDatabaseName("ix_upload_records_user_agent_task");

                    b.HasIndex("UserId", "SessionId")
                        .HasDatabaseName("ix_upload_records_user_session");

                    b.ToTable("upload_records", "aigateway");
                });

            modelBuilder.Entity("AICopilot.EntityFrameworkCore.Outbox.OutboxMessage", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTime?>("DeadLetteredOnUtc")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("dead_lettered_on_utc");

                    b.Property<string>("Error")
                        .HasMaxLength(4000)
                        .HasColumnType("character varying(4000)")
                        .HasColumnName("error");

                    b.Property<string>("EventType")
                        .IsRequired()
                        .HasMaxLength(1024)
                        .HasColumnType("character varying(1024)")
                        .HasColumnName("event_type");

                    b.Property<string>("EventTypeName")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("character varying(512)")
                        .HasColumnName("event_type_name");

                    b.Property<DateTime?>("NextAttemptUtc")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("next_attempt_utc");

                    b.Property<DateTime>("OccurredOnUtc")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("occurred_on_utc");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("jsonb")
                        .HasColumnName("payload");

                    b.Property<DateTime?>("ProcessedOnUtc")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("processed_on_utc");

                    b.Property<int>("RetryCount")
                        .HasColumnType("integer")
                        .HasColumnName("retry_count");

                    b.HasKey("Id");

                    b.HasIndex("ProcessedOnUtc", "DeadLetteredOnUtc", "NextAttemptUtc");

                    b.ToTable("outbox_messages", "outbox", t =>
                        {
                            t.ExcludeFromMigrations();
                        });
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentStep", b =>
                {
                    b.HasOne("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTask", null)
                        .WithMany("Steps")
                        .HasForeignKey("TaskId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Artifacts.Artifact", b =>
                {
                    b.HasOne("AICopilot.Core.AiGateway.Aggregates.Artifacts.ArtifactWorkspace", null)
                        .WithMany("Artifacts")
                        .HasForeignKey("WorkspaceId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate", b =>
                {
                    b.OwnsOne("AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.TemplateSpecification", "Specification", b1 =>
                        {
                            b1.Property<Guid>("ConversationTemplateId")
                                .HasColumnType("uuid");

                            b1.Property<int?>("MaxTokens")
                                .HasColumnType("integer")
                                .HasColumnName("max_tokens");

                            b1.Property<float?>("Temperature")
                                .HasColumnType("real")
                                .HasColumnName("temperature");

                            b1.HasKey("ConversationTemplateId");

                            b1.ToTable("conversation_templates", "aigateway");

                            b1.WithOwner()
                                .HasForeignKey("ConversationTemplateId");
                        });

                    b.Navigation("Specification")
                        .IsRequired();
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel", b =>
                {
                    b.OwnsOne("AICopilot.Core.AiGateway.Aggregates.LanguageModel.ModelParameters", "Parameters", b1 =>
                        {
                            b1.Property<Guid>("LanguageModelId")
                                .HasColumnType("uuid");

                            b1.Property<int>("MaxOutputTokens")
                                .HasColumnType("integer")
                                .HasColumnName("max_output_tokens");

                            b1.Property<int>("MaxTokens")
                                .HasColumnType("integer")
                                .HasColumnName("max_tokens");

                            b1.Property<float>("Temperature")
                                .HasColumnType("real")
                                .HasColumnName("temperature");

                            b1.HasKey("LanguageModelId");

                            b1.ToTable("language_models", "aigateway");

                            b1.WithOwner()
                                .HasForeignKey("LanguageModelId");
                        });

                    b.Navigation("Parameters")
                        .IsRequired();
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Sessions.Message", b =>
                {
                    b.HasOne("AICopilot.Core.AiGateway.Aggregates.Sessions.Session", "Session")
                        .WithMany("Messages")
                        .HasForeignKey("SessionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_messages_sessions_session_id");

                    b.Navigation("Session");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTask", b =>
                {
                    b.Navigation("Steps");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Artifacts.ArtifactWorkspace", b =>
                {
                    b.Navigation("Artifacts");
                });

            modelBuilder.Entity("AICopilot.Core.AiGateway.Aggregates.Sessions.Session", b =>
                {
                    b.Navigation("Messages");
                });
#pragma warning restore 612, 618
        }
    }
}

" to contain "PromptPolicy" because AiGatewayDbContextModelSnapshot.cs must contain PromptPolicy.
  堆栈跟踪:
     at FluentAssertions.Execution.LateBoundTestFramework.Throw(String message)
   at FluentAssertions.Execution.DefaultAssertionStrategy.HandleFailure(String message)
   at FluentAssertions.Execution.AssertionScope.AddPreFormattedFailure(String formattedFailureMessage)
   at FluentAssertions.Execution.AssertionChain.FailWith(Func`1 getFailureReason)
   at FluentAssertions.Execution.AssertionChain.FailWith(Func`1 getFailureReason)
   at FluentAssertions.Execution.AssertionChain.FailWith(String message, Object[] args)
   at FluentAssertions.Primitives.StringAssertions`1.Contain(String expected, String because, Object[] becauseArgs)
   at AICopilot.BackendTests.EnterpriseDataGovernanceP15Tests.AssertFileContains(String path, String[] expectedSnippets) in C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.BackendTests\EnterpriseDataGovernanceP15Tests.cs:line 130
   at AICopilot.BackendTests.EnterpriseDataGovernanceP15Tests.EnterpriseGovernanceMigrations_ShouldHaveDesignerAndSnapshotMarkers() in C:\Users\jinha\Desktop\产线系统架构升级\1\AICopilot\src\tests\AICopilot.BackendTests\EnterpriseDataGovernanceP15Tests.cs:line 51
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

失败!  - 失败:     1，通过:    67，已跳过:     0，总计:    68，持续时间: 3 s - AICopilot.BackendTests.dll (net10.0)
```

## Remaining Risk

- P1.5 does not connect to a real Cloud database or require real model API keys.
- If Docker/PostgreSQL is unavailable, rerun the migration smoke against an explicitly approved temporary database before any production-like trial.

