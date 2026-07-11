using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.HttpApi.Controllers;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Authorization;
using AICopilot.Infrastructure.Storage;
using AICopilot.RagService.Queries.KnowledgeBases;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Ai;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class SecurityHardeningTests
{
    private static readonly Regex LoggerFirstArgumentVariablePattern = new(
        @"Log(?:Critical|Error|Warning|Information|Debug|Trace)\([A-Za-z_][A-Za-z0-9_]*,",
        RegexOptions.Compiled);

    [Fact]
    public void PermissionCatalog_ShouldNotExposeLegacyTrialPilotOrOperationsPermissions()
    {
        var catalog = new PermissionCatalog();
        var permissionCodes = catalog.GetAll()
            .Select(permission => permission.Code)
            .ToArray();
        var legacyPermissions = new[]
        {
            "AiGateway.TrialOperations.Read",
            "AiGateway.TrialOperations.Manage",
            "AiGateway.TrialOperations.AuditView",
            "AiGateway.RunQueue.Read",
            "AiGateway.RunQueue.Manage",
            "AiGateway.WorkerStatus.Read",
            "PilotAuthorization.Submit",
            "PilotAuthorization.View",
            "PilotAuthorization.Review",
            "PilotAuthorization.ApprovePlanning",
            "PilotAuthorization.Reject",
            "PilotAuthorization.Expire",
            "PilotAuthorization.Audit"
        };

        permissionCodes.Should().NotIntersectWith(legacyPermissions);
        catalog.GetDefaultPermissions(IdentityRoleNames.User)
            .Should()
            .NotIntersectWith(legacyPermissions);
    }

    [Fact]
    public void BuiltInTools_ShouldNotExposeLegacyTrialPilotToolCodes()
    {
        BuiltInToolRegistrations.AgentRuntimeTools
            .Select(tool => tool.ToolCode)
            .Should()
            .NotIntersectWith(BuiltInToolRegistrations.ObsoleteAgentRuntimeToolCodes);
    }

    [Fact]
    public void DeploymentConfig_ShouldNotCarryKnownWeakSecrets()
    {
        var solutionRoot = FindSolutionRoot();
        var httpProductionSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi",
            "appsettings.json"));
        var httpDevelopmentSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi",
            "appsettings.Development.json"));
        var appHostSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.AppHost",
            "appsettings.json"));
        var envTemplate = File.ReadAllText(Path.Combine(solutionRoot, "deploy", "enterprise-ai", ".env.example"));
        var compose = File.ReadAllText(Path.Combine(solutionRoot, "deploy", "enterprise-ai", "docker-compose.yaml"));
        var migrationSeeder = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "MigrationWorkerAiGatewaySeeder.cs"));

        httpDevelopmentSettings.Should().NotContain("29ynIx63y0Uq5Yj6wZZYikBElPPW4rqpXKGq4voqmeMDefoJQEC8fQQzYPk95rNp");
        appHostSettings.Should().NotContain("\"pg-password\": \"123456\"");
        File.Exists(Path.Combine(solutionRoot, "artifacts", ".env")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "artifacts", "docker-compose.yaml")).Should().BeFalse();
        envTemplate.Should().Contain("POSTGRES_PASSWORD=");
        envTemplate.Should().Contain("RABBITMQ_PASSWORD=");
        envTemplate.Should().Contain("QDRANT_KEY=");
        envTemplate.Should().Contain("AICOPILOT_API_KEY_ENCRYPTION_KEY=");
        envTemplate.Should().Contain("AICOPILOT_JWT_SECRET_KEY=");
        envTemplate.Should().NotContain("CHANGE_ME");
        envTemplate.Should().NotContain("10.98.");
        envTemplate.Should().NotContain("dummy-key");
        migrationSeeder.Should().Contain("http://model.internal.example:40034/v1");
        migrationSeeder.Should().Contain("PrivateMiniMaxContextWindowTokens = 65536");
        migrationSeeder.Should().Contain("ProtectSeedApiKey(privateModelSeed.ApiKey)");
        migrationSeeder.Should().Contain("isEnabled: privateModelSeed.Enabled");
        migrationSeeder.Should().Contain("AICOPILOT_PRIVATE_MODEL_ENABLED");
        migrationSeeder.Should().NotContain("10.98.");
        migrationSeeder.Should().NotContain("dummy-key");
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_ENABLED=false");
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_BASE_URL=http://model.internal.example:40034/v1");
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS=65536");
        envTemplate.Should().Contain("AICOPILOT_FILE_STORAGE_ROOT_PATH=/var/lib/aicopilot/storage");
        envTemplate.Should().Contain("AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH=/var/lib/aicopilot/artifact-workspaces");
        compose.Should().Contain("AICopilotSecurity__ApiKeyEncryptionKey: ${AICOPILOT_API_KEY_ENCRYPTION_KEY}");
        compose.Should().Contain("AICopilot__PrivateModel__BaseUrl: ${AICOPILOT_PRIVATE_MODEL_BASE_URL:-http://model.internal.example:40034/v1}");
        compose.Should().Contain("AICopilot__PrivateModel__ContextWindowTokens: ${AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS:-65536}");
        compose.Should().Contain("CloudOidc__BootstrapAdminAutoBindEnabled: ${CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED:-false}");
        compose.Should().Contain("CloudOidc__BootstrapAdminUserName: ${AICOPILOT_BOOTSTRAP_ADMIN_USERNAME}");
        compose.Should().Contain("FileStorage__RootPath: ${AICOPILOT_FILE_STORAGE_ROOT_PATH:-/var/lib/aicopilot/storage}");
        compose.Should().Contain("ArtifactWorkspace__RootPath: ${AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH:-/var/lib/aicopilot/artifact-workspaces}");
        compose.Should().Contain("enterprise-ai-aicopilot-data:/var/lib/aicopilot");
        compose.Should().NotContain("10.98.90.154");
        compose.Should().NotContain("Mcp__Runtime__MockOnly");
        httpProductionSettings.Should().NotContain("\"MockOnly\": true");
        httpDevelopmentSettings.Should().Contain("\"MockOnly\": true");
    }

    [Fact]
    public void DeploymentWorkflows_ShouldUseIntranetRunnerAndHarborOnly()
    {
        var solutionRoot = FindSolutionRoot();
        var imageWorkflow = File.ReadAllText(Path.Combine(
            solutionRoot,
            ".github",
            "workflows",
            "aicopilot-image.yml"));
        var deployWorkflow = File.ReadAllText(Path.Combine(
            solutionRoot,
            ".github",
            "workflows",
            "aicopilot-deploy.yml"));
        var buildAndPush = File.ReadAllText(Path.Combine(
            solutionRoot,
            "deploy",
            "enterprise-ai",
            "build-and-push.sh"));
        var localRelease = File.ReadAllText(Path.Combine(
            solutionRoot,
            "deploy",
            "enterprise-ai",
            "local-release.sh"));

        buildAndPush.Should().Contain("IIOT_ROUTINE_BUILD_PROTOCOL=1");

        imageWorkflow.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        imageWorkflow.Should().Contain("Self-hosted runner must not run as root.");
        imageWorkflow.Should().Contain("workflow_dispatch:");
        imageWorkflow.Should().Contain("emergency_confirm:");
        imageWorkflow.Should().Contain("EMERGENCY_AICOPILOT_IMAGE_BUILD");
        imageWorkflow.Should().Contain("OCI_REGISTRY");
        imageWorkflow.Should().Contain("OCI_NAMESPACE");
        imageWorkflow.Should().Contain("dotnet publish");
        imageWorkflow.Should().Contain("docker buildx build");
        imageWorkflow.Should().Contain("--file deploy/enterprise-ai/Dockerfile.backend-runtime");
        imageWorkflow.Should().Contain("RUNTIME_BASE_IMAGE=\"${DOTNET_RUNTIME_BASE_IMAGE:-$image_prefix/base-dotnet-aspnet:10.0-noble}\"");
        imageWorkflow.Should().Contain("Prune old Harbor image tags");
        imageWorkflow.Should().Contain("HARBOR_KEEP_SHA_TAGS: 3");
        imageWorkflow.Should().Contain("bash deploy/enterprise-ai/harbor-retention.sh");
        imageWorkflow.Should().Contain("NODE_BASE_IMAGE=$image_prefix/base-node:22-alpine");
        imageWorkflow.Should().Contain("NGINX_BASE_IMAGE=$image_prefix/base-nginx:1.27-alpine");
        imageWorkflow.Should().NotContain("\n  push:");
        imageWorkflow.Should().NotContain("deploy/enterprise-ai/docker-compose");
        imageWorkflow.Should().NotContain("runs-on: ubuntu-latest");
        imageWorkflow.Should().NotContain("ghcr.io");
        imageWorkflow.Should().NotContain("docker/build-push-action");
        imageWorkflow.Should().NotContain("docker/setup-buildx-action");
        imageWorkflow.Should().NotContain("mcr.microsoft.com/dotnet/aspnet");
        imageWorkflow.Should().NotContain("10.98.90.154");

        deployWorkflow.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        deployWorkflow.Should().Contain("Emergency release tag from local Harbor build or disaster-recovery aicopilot-image (sha-*)");
        deployWorkflow.Should().Contain("emergency_confirm:");
        deployWorkflow.Should().Contain("EMERGENCY_AICOPILOT_DEPLOY");
        deployWorkflow.Should().Contain("DEPLOY_TARGET_DIR: ${{ secrets.DEPLOY_TARGET_DIR }}");
        deployWorkflow.Should().Contain("DEPLOY_ENV_FILE: ${{ secrets.DEPLOY_ENV_FILE }}");
        deployWorkflow.Should().Contain("Self-hosted runner must not run as root.");
        deployWorkflow.Should().Contain("rsync -a --delete");
        deployWorkflow.Should().Contain("printf '%s\\n' \"$DEPLOY_ENV_FILE\" > \"$DEPLOY_TARGET_DIR/.env\"");
        deployWorkflow.Should().Contain("find \"$DEPLOY_TARGET_DIR/scripts\" -maxdepth 1 -type f -name '*.sh' -exec chmod +x {} +");
        deployWorkflow.Should().Contain("services:");
        deployWorkflow.Should().Contain("DEPLOY_SERVICES: ${{ inputs.services }}");
        deployWorkflow.Should().Contain("deploy_args=(\"$RELEASE_TAG\")");
        deployWorkflow.Should().Contain("deploy_args+=(--services \"$DEPLOY_SERVICES\")");
        deployWorkflow.Should().Contain("./deploy-release.sh \"${deploy_args[@]}\"");
        deployWorkflow.Should().NotContain("runs-on: ubuntu-latest");
        deployWorkflow.Should().NotContain("appleboy/ssh-action");
        deployWorkflow.Should().NotContain("appleboy/scp-action");
        deployWorkflow.Should().NotContain("ghcr.io");
        deployWorkflow.Should().NotContain("10.98.90.154");

        buildAndPush.Should().Contain("AICopilot local image build requires explicit --services or --all.");
        buildAndPush.Should().Contain("REGISTRY=\"${REGISTRY:-}\"");
        buildAndPush.Should().Contain("REGISTRY is required");
        buildAndPush.Should().Contain("CLOUD_PLATFORM_URL is required");
        buildAndPush.Should().Contain("BUILD_TIMEOUT_SECONDS=\"${BUILD_TIMEOUT_SECONDS:-900}\"");
        buildAndPush.Should().Contain("HARBOR_TIMEOUT_SECONDS=\"${HARBOR_TIMEOUT_SECONDS:-120}\"");
        buildAndPush.Should().Contain("backend_runtime_selected=true");
        buildAndPush.Should().Contain("normalized=\"$normalized migration\"");
        buildAndPush.Should().Contain("local artifact_dir=\"$OUTPUT_DIR\"");
        buildAndPush.Should().Contain("--output-dir PATH");
        buildAndPush.Should().Contain("aicopilot-built-services.txt");
        buildAndPush.Should().Contain("AICOPILOT_HTTPAPI_IMAGE");
        buildAndPush.Should().NotContain("harbor-retention.sh");
        localRelease.Should().Contain("DEPLOY_SSH_TARGET");
        localRelease.Should().Contain("REGISTRY is required");
        localRelease.Should().Contain("CLOUD_PLATFORM_URL is required");
        localRelease.Should().Contain("ALLOW_ROOT_SSH_DEPLOY");
        localRelease.Should().Contain("Root SSH deploy is not the standard path");
        localRelease.Should().Contain("github-runner@<shared-host>");
        localRelease.Should().Contain("AICopilot remote preflight");
        localRelease.Should().Contain("SSH_TIMEOUT_SECONDS=\"${SSH_TIMEOUT_SECONDS:-1800}\"");
        localRelease.Should().Contain("git -C \"$REPO_ROOT\" fetch --quiet origin '+refs/heads/main:refs/remotes/origin/main'");
        localRelease.Should().Contain("Approved origin/main tip moved after the workspace plan");
        localRelease.Should().Contain("Formal AICopilot release requires HEAD to equal the fresh origin/main tip");
        localRelease.Should().Contain("DEPLOY_LOCK_TOKEN='$RUN_ID' EXPECTED_SUPPORT_DIGEST='$SUPPORT_DIGEST' ./deploy-release.sh");
    }

    [Fact]
    public void ProductionWorkflows_ShouldKeepLeastPrivilegeSelfHostedRunnerBoundary()
    {
        var workflowRoot = Path.Combine(FindSolutionRoot(), ".github", "workflows");
        var guardedWorkflows = Directory.GetFiles(workflowRoot, "aicopilot-*.yml")
            .Select(path => new
            {
                Path = path,
                FileName = Path.GetFileName(path),
                Source = File.ReadAllText(path)
            })
            .Where(workflow => workflow.Source.Contains("environment: production", StringComparison.Ordinal)
                || workflow.Source.Contains("secrets.", StringComparison.Ordinal))
            .OrderBy(workflow => workflow.FileName, StringComparer.Ordinal)
            .ToArray();

        guardedWorkflows
            .Select(workflow => workflow.FileName)
            .Should()
            .BeEquivalentTo(
                "aicopilot-ai-read-diagnostics.yml",
                "aicopilot-deploy.yml",
                "aicopilot-enable-direct-cloud-readonly-db.yml",
                "aicopilot-enable-real-cloud-ai-read.yml",
                "aicopilot-image.yml",
                "aicopilot-provision-cloud-readonly-db-role.yml",
                "aicopilot-routine-request.yml");

        foreach (var workflow in guardedWorkflows)
        {
            workflow.Source.Should().Contain("permissions:\n  contents: read", workflow.FileName);
            workflow.Source.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]", workflow.FileName);
            workflow.Source.Should().Contain("if [ \"$(id -u)\" -eq 0 ]; then", workflow.FileName);
            workflow.Source.Should().Contain("Self-hosted runner must not run as root.", workflow.FileName);
            workflow.Source.Should().Contain("check-runner-security-attestation.sh", workflow.FileName);
            workflow.Source.Should().NotContain("runs-on: ubuntu-latest", workflow.FileName);
            workflow.Source.Should().NotContain("runs-on: windows-latest", workflow.FileName);
            workflow.Source.Should().NotContain("id-token: write", workflow.FileName);
            workflow.Source.Should().NotContain("contents: write", workflow.FileName);
            workflow.Source.Should().NotContain("actions: write", workflow.FileName);
            workflow.Source.Should().NotContain("pull-requests: write", workflow.FileName);
            workflow.Source.Should().NotContain("permissions: write-all", workflow.FileName);
            workflow.Source.Should().NotContain("10.98.", workflow.FileName);
        }
    }

    [Fact]
    public void DeploymentScriptsAndDocs_ShouldKeepSingleStandardReleasePath()
    {
        var solutionRoot = FindSolutionRoot();
        var deployRoot = Path.Combine(solutionRoot, "deploy", "enterprise-ai");
        var deployGuide = File.ReadAllText(Path.Combine(solutionRoot, "AICopilot 项目部署与维护指南.md"));
        var deployReadme = File.ReadAllText(Path.Combine(deployRoot, "README.md"));
        var envTemplate = File.ReadAllText(Path.Combine(deployRoot, ".env.example"));
        var deployRelease = File.ReadAllText(Path.Combine(deployRoot, "deploy-release.sh"));
        var harborRetention = File.ReadAllText(Path.Combine(deployRoot, "harbor-retention.sh"));
        var mirrorBaseImages = File.ReadAllText(Path.Combine(deployRoot, "mirror-base-images.sh"));
        var buildAndPush = File.ReadAllText(Path.Combine(deployRoot, "build-and-push.sh"));
        var localRelease = File.ReadAllText(Path.Combine(deployRoot, "local-release.sh"));
        var releaseSecurityAttestation = File.ReadAllText(Path.Combine(
            deployRoot,
            "scripts",
            "check-release-security-attestation.sh"));
        var modelSecretMigrationCheck = File.ReadAllText(Path.Combine(
            deployRoot,
            "scripts",
            "check-model-secret-migration.sh"));
        var runnerSecurityAttestation = File.ReadAllText(Path.Combine(
            deployRoot,
            "scripts",
            "check-runner-security-attestation.sh"));
        var platformAttestationTemplate = File.ReadAllText(Path.Combine(
            deployRoot,
            "runner-platform-attestation.template.md"));
        var platformAttestationRecordCheck = File.ReadAllText(Path.Combine(
            deployRoot,
            "scripts",
            "check-platform-attestation-record.sh"));
        var backendDockerfile = File.ReadAllText(Path.Combine(deployRoot, "Dockerfile.backend-runtime"));
        var webDockerfile = File.ReadAllText(Path.Combine(solutionRoot, "src", "vues", "AICopilot.Web", "Dockerfile"));
        var webNginxTemplate = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "nginx.conf.template"));

        deployGuide.Should().Contain("工作区标准发布");
        deployGuide.Should().Contain("Docker Hub 不作为生产依赖源");
        deployGuide.Should().Contain("MCR 也不得作为生产构建的直接依赖源");
        deployGuide.Should().Contain("`aicopilot-image` / `aicopilot-deploy` 只保留带确认词的灾备入口");
        deployGuide.Should().Contain("单个镜像 build/push 默认 15 分钟超时");
        deployGuide.Should().Contain("mirror-base-images.sh");
        deployGuide.Should().Contain("deploy-release.sh");
        deployGuide.Should().Contain("日常生产发布不得等待这些 workflow");
        deployGuide.Should().Contain("deploy/enterprise-ai/README.md");
        deployGuide.Should().NotContain("docs/企业AI首次部署记录");
        File.Exists(Path.Combine(solutionRoot, "docs", "企业AI首次部署记录-2026-06-08.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理部署配置说明.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理已知问题清单.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理Simulation模式说明.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理前后端Integration分支说明.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理真实CloudReadonly准备说明.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理错误码与前端提示说明.md")).Should().BeFalse();
        File.Exists(Path.Combine(solutionRoot, "docs", "A助理权限矩阵.md")).Should().BeFalse();

        deployReadme.Should().Contain("AICopilot enterprise-ai deploy");
        deployReadme.Should().Contain("aicopilot-image");
        deployReadme.Should().Contain("aicopilot-deploy");
        deployReadme.Should().Contain("build-and-push.sh       # 统一入口内部调度的本机镜像构建实现");
        deployReadme.Should().Contain("local-release.sh        # 旧事务/基础设施维护的固定提交发布实现");
        deployReadme.Should().Contain("独立 detached worktree");
        deployReadme.Should().Contain("DEPLOY_ENV_FILE");
        deployReadme.Should().Contain("iiot-linux-prod");
        deployReadme.Should().Contain("非 root");
        deployReadme.Should().Contain("不通过 AICopilot 写 Cloud 业务数据");
        deployReadme.Should().Contain("应用镜像仓库只保留当前生产 `sha-*` tag");
        deployReadme.Should().Contain("./deploy-release.sh sha-<git-sha> --services migration,httpapi,web");
        deployReadme.Should().Contain("./deploy-release.sh --validate-only");
        deployReadme.Should().Contain("./scripts/check-release-security-attestation.sh");
        deployReadme.Should().Contain("./scripts/check-model-secret-migration.sh");
        deployReadme.Should().Contain("./scripts/check-runner-security-attestation.sh");
        deployReadme.Should().Contain("runner-platform-attestation.template.md");
        deployReadme.Should().Contain("scripts/check-platform-attestation-record.sh --record <filled-attestation.md>");
        deployReadme.Should().Contain("已批准的基础设施例外");
        deployReadme.Should().Contain("自动运行 `scripts/check-release-security-attestation.sh`");
        deployReadme.Should().Contain("summary 会包含 release security attestation 输出");
        deployGuide.Should().Contain("./scripts/check-release-security-attestation.sh");
        deployGuide.Should().Contain("./scripts/check-model-secret-migration.sh");
        deployGuide.Should().Contain("check-runner-security-attestation.sh");
        deployGuide.Should().Contain("runner-platform-attestation.template.md");
        deployGuide.Should().Contain("check-platform-attestation-record.sh --record <filled-attestation.md>");
        deployGuide.Should().Contain("release-security-attestation");
        deployGuide.Should().Contain("./deploy-release.sh --validate-only");

        envTemplate.Should().Contain("AICOPILOT_PUBLIC_URL=http://aicopilot.internal.example:82");
        envTemplate.Should().Contain("CLOUD_PLATFORM_URL=http://cloud.internal.example:81");
        envTemplate.Should().Contain("POSTGRES_IMAGE=harbor.internal.example:80/enterprise-ai/base-postgres:17.6");
        envTemplate.Should().Contain("RABBITMQ_IMAGE=harbor.internal.example:80/enterprise-ai/base-rabbitmq:4.2-management");
        envTemplate.Should().Contain("QDRANT_IMAGE=harbor.internal.example:80/enterprise-ai/base-qdrant:v1.15.5");
        envTemplate.Should().Contain("sha-replace-with-release-tag");
        envTemplate.Should().Contain("AICOPILOT_BOOTSTRAP_ADMIN_USERNAME=bootstrap-admin");
        envTemplate.Should().NotContain("AICOPILOT_BOOTSTRAP_ADMIN_USERNAME=101650");
        envTemplate.Should().Contain("CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=false");
        deployReadme.Should().Contain("生产模板和 compose fallback 均默认关闭");
        deployGuide.Should().Contain("生产模板和 compose fallback 默认使用 `CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED=false`");
        envTemplate.Should().Contain("AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=false");
        envTemplate.Should().NotContain("10.98.");
        envTemplate.Should().NotContain("CHANGE_ME");
        envTemplate.Should().NotContain("dummy-key");
        envTemplate.Should().NotContain("POSTGRES_IMAGE=postgres:");
        envTemplate.Should().NotContain("RABBITMQ_IMAGE=rabbitmq:");
        envTemplate.Should().NotContain("QDRANT_IMAGE=qdrant/");

        deployRelease.Should().Contain("APP_IMAGE_KEYS");
        deployRelease.Should().Contain("INFRA_IMAGE_KEYS");
        deployRelease.Should().Contain("Release tag must match sha-<hex>");
        deployRelease.Should().Contain("Image must be mirrored to Harbor");
        deployRelease.Should().Contain("normalize_services");
        deployRelease.Should().Contain("compose pull");
        deployRelease.Should().Contain("compose up -d --remove-orphans");
        deployRelease.Should().Contain("compose up -d \"${RUNTIME_SELECTED_SERVICES[@]}\"");
        deployRelease.Should().Contain("probe_web");
        deployRelease.Should().Contain("validate_deploy_environment");
        deployRelease.Should().Contain("VALIDATE_ONLY=false");
        deployRelease.Should().Contain("--validate-only");
        deployRelease.Should().Contain("AICopilot deploy environment validation passed");
        deployRelease.Should().Contain("ensure_no_template_placeholders");
        deployRelease.Should().Contain("ensure_http_only_environment");
        deployRelease.Should().Contain("require_intranet_http_oidc_issuer");
        deployRelease.Should().Contain("is_allowed_intranet_http_oidc_host");
        deployRelease.Should().Contain("*.internal.example|*.internal|*.lan|*.local");
        deployRelease.Should().Contain("HTTP-only Cloud OIDC issuer must be loopback, private IPv4, or a reserved intranet DNS suffix");
        deployRelease.Should().Contain("ensure_required_secrets");
        deployRelease.Should().Contain("AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY");
        deployRelease.Should().Contain("Deploy environment file must be owner-only readable/writable");
        deployRelease.Should().Contain("probe_web_security_headers");
        deployRelease.Should().Contain("require_response_header");
        deployRelease.Should().Contain("X-Content-Type-Options");
        deployRelease.Should().Contain("Content-Security-Policy");
        deployRelease.Should().Contain("Strict-Transport-Security until HTTPS is explicitly approved");
        deployRelease.Should().Contain("run_release_security_attestation");
        deployRelease.Should().Contain("scripts/check-release-security-attestation.sh");
        deployRelease.Should().Contain("scripts/check-model-secret-migration.sh");
        deployRelease.Should().Contain("BACKEND_RUNTIME_SELECTED");
        deployRelease.Should().Contain("backend runtime deploys must include migration");
        deployRelease.Should().Contain("Release Security Attestation");
        deployRelease.Should().Contain("exit \"$attestation_status\"");
        deployRelease.Should().Contain("check_model_secret_migration_preflight");
        deployRelease.Should().Contain("Run ./deploy-release.sh %s --services migration");
        var fullDeployMigrationThenPreflight = """
          compose up --no-deps --abort-on-container-exit --exit-code-from aicopilot-migration aicopilot-migration
          check_model_secret_migration_preflight
          compose up -d aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker aicopilot-webui
        """;
        var selectedDeployMigrationThenPreflight = """
            compose up --no-deps --abort-on-container-exit --exit-code-from aicopilot-migration aicopilot-migration
            check_model_secret_migration_preflight
        """;
        deployRelease.Should().Contain(fullDeployMigrationThenPreflight);
        deployRelease.Should().Contain(selectedDeployMigrationThenPreflight);
        deployRelease.LastIndexOf("check_model_secret_migration_preflight", StringComparison.Ordinal)
            .Should()
            .BeLessThan(deployRelease.IndexOf("compose up -d \"${RUNTIME_SELECTED_SERVICES[@]}\"", StringComparison.Ordinal));
        var webProbeCurl = "status_code=\"$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 \"$url\" || true)\"";
        deployRelease.Split(webProbeCurl, StringSplitOptions.None)
            .Should()
            .HaveCount(2, "the web probe should issue one HTTP request per retry attempt");
        deployRelease.Should().Contain("post-release-cleanup.sh");

        harborRetention.Should().Contain("HARBOR_KEEP_SHA_TAGS");
        harborRetention.Should().Contain("HARBOR_KEEP_SHA_TAG");
        harborRetention.Should().Contain("sha-[0-9a-f]");
        harborRetention.Should().Contain("Harbor GC must run");

        mirrorBaseImages.Should().Contain("postgres:17.6");
        mirrorBaseImages.Should().Contain("rabbitmq:4.2-management");
        mirrorBaseImages.Should().Contain("qdrant/qdrant:v1.15.5");
        mirrorBaseImages.Should().Contain("mcr.microsoft.com/dotnet/aspnet:10.0-noble");
        mirrorBaseImages.Should().Contain("base-dotnet-aspnet:10.0-noble");
        mirrorBaseImages.Should().Contain("libgssapi-krb5-2");
        mirrorBaseImages.Should().Contain("/var/lib/aicopilot/storage");
        mirrorBaseImages.Should().Contain("USER app");
        mirrorBaseImages.Should().Contain("node:22-alpine");
        mirrorBaseImages.Should().Contain("nginx:1.27-alpine");
        mirrorBaseImages.Should().Contain("docker buildx build");

        buildAndPush.Should().Contain("MIRROR_BASE_IMAGES");
        buildAndPush.Should().Contain("DOTNET_RUNTIME_BASE_IMAGE=\"${DOTNET_RUNTIME_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-dotnet-aspnet:10.0-noble}\"");
        buildAndPush.Should().Contain("NODE_BASE_IMAGE");
        buildAndPush.Should().Contain("NGINX_BASE_IMAGE");
        buildAndPush.Should().NotContain("DOTNET_RUNTIME_BASE_IMAGE=\"${DOTNET_RUNTIME_BASE_IMAGE:-mcr.microsoft.com/dotnet/aspnet:10.0-noble}\"");
        buildAndPush.Should().Contain("mirror-base-images.sh");
        buildAndPush.Should().NotContain("harbor-retention.sh");
        deployRelease.Should().Contain("scripts/check-release-security-attestation.sh");
        deployRelease.Should().Contain("scripts/check-model-secret-migration.sh");
        localRelease.Should().Contain("find scripts cloud-readonly");
        localRelease.Should().Contain("runner-platform-attestation.template.md");
        deployReadme.Should().Contain("scripts/check-runner-security-attestation.sh");
        deployReadme.Should().Contain("scripts/check-platform-attestation-record.sh");

        backendDockerfile.Should().Contain("ARG RUNTIME_BASE_IMAGE=harbor.internal.example:80/enterprise-ai/base-dotnet-aspnet:10.0-noble");
        backendDockerfile.Should().Contain("FROM ${RUNTIME_BASE_IMAGE} AS runtime");
        backendDockerfile.Should().Contain("USER app");
        backendDockerfile.Should().Contain("COPY --chown=app:app . .");
        backendDockerfile.Should().NotContain("mcr.microsoft.com/dotnet/aspnet");
        backendDockerfile.Should().NotContain("USER root");
        backendDockerfile.Should().NotContain("apt-get");

        webDockerfile.Should().Contain("ARG NODE_BASE_IMAGE=node:22-alpine");
        webDockerfile.Should().Contain("FROM ${NODE_BASE_IMAGE} AS build");
        webDockerfile.Should().Contain("ARG NGINX_BASE_IMAGE=nginx:1.27-alpine");
        webDockerfile.Should().Contain("FROM ${NGINX_BASE_IMAGE}");
        webDockerfile.Should().Contain("COPY --from=build --chown=nginx:nginx");
        webDockerfile.Should().Contain("chown -R nginx:nginx");
        webDockerfile.Should().Contain("USER nginx");
        webDockerfile.Should().NotContain("USER root");

        webNginxTemplate.Should().Contain("server_tokens off;");
        webNginxTemplate.Should().Contain("add_header X-Content-Type-Options \"nosniff\" always;");
        webNginxTemplate.Should().Contain("add_header X-Frame-Options \"DENY\" always;");
        webNginxTemplate.Should().Contain("add_header Referrer-Policy \"no-referrer\" always;");
        webNginxTemplate.Should().Contain("add_header Permissions-Policy");
        webNginxTemplate.Should().Contain("add_header Content-Security-Policy");
        webNginxTemplate.Should().Contain("frame-ancestors 'none'");
        webNginxTemplate.Should().NotContain("Strict-Transport-Security");
        webNginxTemplate.Should().NotContain("listen 443");
        webNginxTemplate.Should().NotContain("ssl_certificate");

        releaseSecurityAttestation.Should().Contain("check-release-security-attestation.sh");
        releaseSecurityAttestation.Should().Contain("HTTP-only");
        releaseSecurityAttestation.Should().Contain("must not be HTTPS");
        releaseSecurityAttestation.Should().Contain("Cloud OIDC status endpoint");
        releaseSecurityAttestation.Should().Contain("/api/identity/cloud-oidc/status");
        releaseSecurityAttestation.Should().Contain("check_cloud_oidc_status");
        releaseSecurityAttestation.Should().Contain("Strict-Transport-Security");
        releaseSecurityAttestation.Should().Contain("aicopilot-webui must not run as root.");
        releaseSecurityAttestation.Should().Contain("test -w /var/cache/nginx");
        releaseSecurityAttestation.Should().Contain("test -w /var/run");
        releaseSecurityAttestation.Should().Contain("check-model-secret-migration.sh");
        releaseSecurityAttestation.Should().NotContain("RETURN");
        modelSecretMigrationCheck.Should().Contain("check-model-secret-migration.sh");
        modelSecretMigrationCheck.Should().Contain("aigateway.language_models");
        modelSecretMigrationCheck.Should().Contain("rag.embedding_models");
        modelSecretMigrationCheck.Should().Contain("legacy_count=0 and unprotected_count=0");
        modelSecretMigrationCheck.Should().Contain("api_key LIKE 'encv1:%'");
        modelSecretMigrationCheck.Should().Contain("api_key NOT LIKE 'encv2:%'");
        modelSecretMigrationCheck.Should().Contain("MigrationWorker__CheckSecretsOnly=true");
        modelSecretMigrationCheck.Should().Contain("verify encv2 decryptability with the current key");
        modelSecretMigrationCheck.Should().Contain("failed while verifying encv2 decryptability");
        modelSecretMigrationCheck.Should().Contain("decryptability attestation passed with the current encryption key");
        modelSecretMigrationCheck.Should().Contain("failed while querying PostgreSQL");
        modelSecretMigrationCheck.Should().Contain("rerun a deploy including --services migration");
        modelSecretMigrationCheck.Should().NotContain("RETURN");
        runnerSecurityAttestation.Should().Contain("check-runner-security-attestation.sh");
        runnerSecurityAttestation.Should().Contain("AICopilot self-hosted runner must not run as root.");
        runnerSecurityAttestation.Should().Contain("/data/iiot-platform/runners/aicopilot");
        runnerSecurityAttestation.Should().Contain("/data/iiot-platform/runtime/docker");
        runnerSecurityAttestation.Should().Contain("/srv/enterprise-ai/deploy");
        runnerSecurityAttestation.Should().Contain("OIDC/Vault or equivalent short-lived");
        runnerSecurityAttestation.Should().Contain("runner-platform-attestation.template.md");
        runnerSecurityAttestation.Should().Contain("scripts/check-platform-attestation-record.sh");
        runnerSecurityAttestation.Should().Contain("Dry-run does not prove runner filesystem, Docker root, GitHub environment, or Vault/OIDC state.");

        platformAttestationTemplate.Should().Contain("AI-SEC-010");
        platformAttestationTemplate.Should().Contain("check-runner-security-attestation.sh");
        platformAttestationTemplate.Should().Contain("GitHub production environment");
        platformAttestationTemplate.Should().Contain("required reviewers");
        platformAttestationTemplate.Should().Contain("contents: read");
        platformAttestationTemplate.Should().Contain("self-hosted");
        platformAttestationTemplate.Should().Contain("iiot-linux-prod");
        platformAttestationTemplate.Should().Contain("OIDC/Vault or equivalent short-lived credentials");
        platformAttestationTemplate.Should().Contain("approved infrastructure exception");
        platformAttestationTemplate.Should().Contain("Ticket or change id:");
        platformAttestationTemplate.Should().Contain("Exception owner:");
        platformAttestationTemplate.Should().Contain("Due date:");
        platformAttestationTemplate.Should().Contain("Current mitigation:");
        platformAttestationTemplate.Should().Contain("Do not commit a filled production record");
        platformAttestationTemplate.Should().Contain("Platform owner:");
        platformAttestationTemplate.Should().Contain("Reviewer:");
        platformAttestationTemplate.Should().Contain("Release owner:");
        platformAttestationRecordCheck.Should().Contain("check-platform-attestation-record.sh");
        platformAttestationRecordCheck.Should().Contain("does not verify GitHub, Vault, OIDC, or runner infrastructure");
        platformAttestationRecordCheck.Should().Contain("Platform attestation record still contains template placeholders.");
        platformAttestationRecordCheck.Should().Contain("Platform attestation record contains unchecked checklist items.");
        platformAttestationRecordCheck.Should().Contain("Platform attestation record is missing required sign-off value");
        platformAttestationRecordCheck.Should().Contain("Platform attestation record has an invalid sign-off value");
        platformAttestationRecordCheck.Should().Contain("AI-SEC-010");
        platformAttestationRecordCheck.Should().Contain("check-runner-security-attestation\\.sh");
        platformAttestationRecordCheck.Should().Contain("production environment secret restriction evidence");
        platformAttestationRecordCheck.Should().Contain("no hosted runner evidence for production or secret-touching workflows");
        platformAttestationRecordCheck.Should().Contain("OIDC/Vault|short-lived credentials");
        platformAttestationRecordCheck.Should().Contain("approved exception ticket or change id");
        platformAttestationRecordCheck.Should().Contain("approved exception owner");
        platformAttestationRecordCheck.Should().Contain("approved exception due date");
        platformAttestationRecordCheck.Should().Contain("approved exception mitigation");
    }

    [Fact]
    public async Task PlatformAttestationRecordCheck_ShouldRejectIncompleteSignOffsAndWeakEvidenceWords()
    {
        var solutionRoot = FindSolutionRoot();
        var scriptPath = Path.Combine(
            solutionRoot,
            "deploy",
            "enterprise-ai",
            "scripts",
            "check-platform-attestation-record.sh");
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-platform-attestation-records",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);
        try
        {
            var validRecord = Path.Combine(tempDirectory, "valid.md");
            File.WriteAllText(
                validRecord,
                BuildPlatformAttestationRecord(
                    "Credential strategy: OIDC/Vault implemented for AICopilot production workflows with short-lived credentials.",
                    "Platform owner: Platform Owner / 2026-07-06"));

            var validResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", validRecord],
                solutionRoot);

            validResult.ExitCode.Should().Be(0, validResult.Output);
            validResult.Output.Should().Contain("AICopilot platform attestation record lint passed");

            var validExceptionRecord = Path.Combine(tempDirectory, "valid-exception.md");
            File.WriteAllText(
                validExceptionRecord,
                BuildPlatformAttestationRecord(
                    "Credential strategy: OIDC/Vault rollout is tracked as an approved infrastructure exception.",
                    "Platform owner: Platform Owner / 2026-07-06",
                    """
Ticket or change id: INFRA-123
Exception owner: Platform Owner
Due date: 2026-08-01
Current mitigation: GitHub production environment reviewers, restricted runner access, and scheduled secret rotation remain in effect.
"""));

            var validExceptionResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", validExceptionRecord],
                solutionRoot);

            validExceptionResult.ExitCode.Should().Be(0, validExceptionResult.Output);
            validExceptionResult.Output.Should().Contain("AICopilot platform attestation record lint passed");

            var vagueExceptionRecord = Path.Combine(tempDirectory, "vague-exception.md");
            File.WriteAllText(
                vagueExceptionRecord,
                BuildPlatformAttestationRecord(
                    "Credential strategy: OIDC/Vault rollout is tracked as an approved infrastructure exception.",
                    "Platform owner: Platform Owner / 2026-07-06"));

            var vagueExceptionResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", vagueExceptionRecord],
                solutionRoot);

            vagueExceptionResult.ExitCode.Should().NotBe(0, vagueExceptionResult.Output);
            vagueExceptionResult.Output.Should().Contain("approved exception ticket or change id");

            var emptySignOffRecord = Path.Combine(tempDirectory, "empty-sign-off.md");
            File.WriteAllText(
                emptySignOffRecord,
                BuildPlatformAttestationRecord(
                    "Credential strategy: OIDC/Vault implemented for AICopilot production workflows with short-lived credentials.",
                    "Platform owner:"));

            var emptySignOffResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", emptySignOffRecord],
                solutionRoot);

            emptySignOffResult.ExitCode.Should().NotBe(0, emptySignOffResult.Output);
            emptySignOffResult.Output.Should().Contain("missing required sign-off value: Platform owner");

            var weakEvidenceRecord = Path.Combine(tempDirectory, "weak-evidence.md");
            File.WriteAllText(
                weakEvidenceRecord,
                BuildPlatformAttestationRecord(
                    "Credential strategy: pending platform task.",
                    "Platform owner: Platform Owner / 2026-07-06"));

            var weakEvidenceResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", weakEvidenceRecord],
                solutionRoot);

            weakEvidenceResult.ExitCode.Should().NotBe(0, weakEvidenceResult.Output);
            weakEvidenceResult.Output.Should().Contain("unresolved placeholder wording");

            var missingSecretRestrictionRecord = Path.Combine(tempDirectory, "missing-secret-restriction.md");
            File.WriteAllText(
                missingSecretRestrictionRecord,
                BuildPlatformAttestationRecord(
                        "Credential strategy: OIDC/Vault implemented for AICopilot production workflows with short-lived credentials.",
                        "Platform owner: Platform Owner / 2026-07-06")
                    .Replace("Environment secrets are restricted to AICopilot production and disaster workflows.\n", string.Empty));

            var missingSecretRestrictionResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", missingSecretRestrictionRecord],
                solutionRoot);

            missingSecretRestrictionResult.ExitCode.Should().NotBe(0, missingSecretRestrictionResult.Output);
            missingSecretRestrictionResult.Output.Should().Contain("production environment secret restriction evidence");

            var missingHostedRunnerBoundaryRecord = Path.Combine(tempDirectory, "missing-hosted-runner-boundary.md");
            File.WriteAllText(
                missingHostedRunnerBoundaryRecord,
                BuildPlatformAttestationRecord(
                        "Credential strategy: OIDC/Vault implemented for AICopilot production workflows with short-lived credentials.",
                        "Platform owner: Platform Owner / 2026-07-06")
                    .Replace("No production or secret-touching workflow uses GitHub hosted runners.\n", string.Empty));

            var missingHostedRunnerBoundaryResult = await RepositoryTestSupport.RunAsync(
                "bash",
                [scriptPath, "--record", missingHostedRunnerBoundaryRecord],
                solutionRoot);

            missingHostedRunnerBoundaryResult.ExitCode.Should().NotBe(0, missingHostedRunnerBoundaryResult.Output);
            missingHostedRunnerBoundaryResult.Output.Should().Contain("no hosted runner evidence");
        }
        finally
        {
            RepositoryTestSupport.TryDeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void HttpApiCorsAndWebProxy_ShouldStaySameOriginByDefault()
    {
        var solutionRoot = FindSolutionRoot();
        var httpApiRoot = Path.Combine(solutionRoot, "src", "hosts", "AICopilot.HttpApi");
        var program = File.ReadAllText(Path.Combine(httpApiRoot, "Program.cs"));
        var dependencyInjection = File.ReadAllText(Path.Combine(httpApiRoot, "DependencyInjection.cs"));
        var corsConfiguration = File.ReadAllText(Path.Combine(httpApiRoot, "HttpApiCorsConfiguration.cs"));
        var appSettings = File.ReadAllText(Path.Combine(httpApiRoot, "appsettings.json"));
        var developmentSettings = File.ReadAllText(Path.Combine(httpApiRoot, "appsettings.Development.json"));
        var webRoot = Path.Combine(solutionRoot, "src", "vues", "AICopilot.Web");
        var webDockerfile = File.ReadAllText(Path.Combine(webRoot, "Dockerfile"));
        var webNginxTemplate = File.ReadAllText(Path.Combine(webRoot, "nginx.conf.template"));
        var appSettingSource = File.ReadAllText(Path.Combine(webRoot, "src", "appsetting.ts"));
        var apiClientSource = File.ReadAllText(Path.Combine(webRoot, "src", "services", "apiClient.ts"));
        var viteConfig = File.ReadAllText(Path.Combine(webRoot, "vite.config.ts"));

        dependencyInjection.Should().Contain("HttpApiCorsConfiguration.AddHttpApiCors");
        program.Should().Contain("app.UseCors(HttpApiCorsConfiguration.PolicyName);");
        program.IndexOf("app.UseCors(HttpApiCorsConfiguration.PolicyName);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal));

        corsConfiguration.Should().Contain("public const string SectionName = \"Cors\"");
        corsConfiguration.Should().Contain("public const string PolicyName = \"AICopilotExplicitOrigins\"");
        corsConfiguration.Should().Contain("Cors:AllowedOrigins must use explicit origins; wildcard origins are forbidden.");
        corsConfiguration.Should().Contain("Cors:AllowedOrigins values must be origins only, without path, query, or fragment.");
        corsConfiguration.Should().Contain("policy.SetIsOriginAllowed(_ => false);");
        corsConfiguration.Should().Contain("policy.WithOrigins(allowedOrigins);");
        corsConfiguration.Should().NotContain("AllowAnyOrigin");
        corsConfiguration.Should().NotContain("SetIsOriginAllowed(_ => true)");
        appSettings.Should().Contain("\"Cors\"");
        appSettings.Should().Contain("\"AllowedOrigins\": []");
        developmentSettings.Should().Contain("\"Cors\"");
        developmentSettings.Should().Contain("\"AllowedOrigins\": []");

        webDockerfile.Should().Contain("ARG VITE_API_BASE_URL=/api");
        webNginxTemplate.Should().Contain("location /api/");
        webNginxTemplate.Should().Contain("proxy_pass ${AICOPILOT_HTTPAPI_HTTP}/api/;");
        webNginxTemplate.Should().NotContain("Access-Control-Allow-Origin \"*\"");
        appSettingSource.Should().Contain("|| '/api'");
        viteConfig.Should().Contain("const apiBaseUrl = env.VITE_API_BASE_URL || '/api'");
        apiClientSource.Should().Contain("if (trimmed === '/api' || trimmed.startsWith('/api/'))");
        apiClientSource.Should().Contain("isTrustedEndpointOrigin");
        apiClientSource.Should().Contain("API endpoint origin is not trusted.");
    }

    [Fact]
    public void ManagementControllers_ShouldRequireHttpAuthentication()
    {
        var aiGatewayControllers = new[]
        {
            typeof(AiGatewayController),
            typeof(AiGatewayToolController),
            typeof(AiGatewaySessionController),
            typeof(AiGatewayAgentTaskController),
            typeof(AiGatewayWorkspaceArtifactController)
        };

        foreach (var controller in aiGatewayControllers)
        {
            controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull(controller.Name);
        }

        typeof(DataAnalysisController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(McpController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(RagController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void AiGatewaySessionAccess_ShouldBeScopedToCurrentUserAndPendingApproval()
    {
        var solutionRoot = FindSolutionRoot();
        var sessionQueryPath = Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Queries",
            "Sessions");
        var sessionCommandPath = Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Commands",
            "Sessions");

        File.ReadAllText(Path.Combine(sessionQueryPath, "GetListSessions.cs"))
            .Should().Contain("SessionsByUserOrderedSpec");
        File.ReadAllText(Path.Combine(sessionQueryPath, "GetSession.cs"))
            .Should().Contain("SessionByIdForUserSpec");
        File.ReadAllText(Path.Combine(sessionQueryPath, "GetListChatMessageHistory.cs"))
            .Should().Contain("SessionWithMessagesByIdForUserSpec");
        File.ReadAllText(Path.Combine(sessionQueryPath, "GetListChatMessages.cs"))
            .Should().Contain("SessionWithMessagesByIdForUserSpec");
        File.ReadAllText(Path.Combine(sessionQueryPath, "GetPendingApprovals.cs"))
            .Should().Contain("SessionByIdForUserSpec");
        File.ReadAllText(Path.Combine(sessionQueryPath, "GetPendingApprovals.cs"))
            .Should().Contain("IFinalAgentContextStore");
        File.ReadAllText(Path.Combine(sessionCommandPath, "DeleteSession.cs"))
            .Should().Contain("SessionByIdForUserSpec");
        File.ReadAllText(Path.Combine(sessionCommandPath, "DeleteSession.cs"))
            .Should().Contain("finalAgentContextStore.RemoveAsync");

        var chatStreamSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Agents",
            "ChatStreamHandler.cs"));
        chatStreamSource.Should().Contain("currentUser.Id != session.UserId");
        chatStreamSource.Should().Contain("sessionExecutionLock.AcquireAsync(request.SessionId");
        chatStreamSource.Should().Contain("finalAgentContextStore.GetAsync(request.SessionId");
        chatStreamSource.Should().Contain("AppProblemCodes.ApprovalPending");

        var controllerSource = ReadAiGatewayControllerSources(solutionRoot);
        controllerSource.Should().Contain("[HttpGet(\"approval/pending\")]");
        controllerSource.Should().Contain("GetPendingApprovalsQuery");
    }

    [Fact]
    public void FrontendChatApprovalUx_ShouldRecoverPendingApprovalAndScopeSessionState()
    {
        var solutionRoot = FindSolutionRoot();
        var chatStoreSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src",
            "stores",
            "chatStore.ts"));
        var approvalStoreSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src",
            "stores",
            "approvalStore.ts"));
        var chatErrorStoreSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src",
            "stores",
            "chatErrorStore.ts"));
        var chatServiceSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src",
            "services",
            "chatService.ts"));
        var approvalCardSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src",
            "components",
            "chat",
            "ApprovalCard.vue"));

        chatServiceSource.Should().Contain("/aigateway/approval/pending");
        chatStoreSource.Should().Contain("refreshPendingApprovals(sessionId)");
        approvalStoreSource.Should().Contain("reconcilePendingApprovalCards");
        approvalStoreSource.Should().Contain("sessionId !== sessionStore.currentSessionId");
        chatErrorStoreSource.Should().Contain("errorSessionId.value !== currentSessionId.value");
        chatErrorStoreSource.Should().Contain("approval_already_processed");
        approvalStoreSource.Should().Contain("'expired'");
        approvalCardSource.Should().Contain("isSubmitting");
        approvalCardSource.Should().Contain("已失效");
        approvalCardSource.Should().Contain("hasStrictIdentity");
        approvalCardSource.Should().NotContain("isProcessing.value = true");
    }

    [Fact]
    public void StreamHandlers_ShouldAuthorizeSessionBeforeLockAndPersistence()
    {
        var solutionRoot = FindSolutionRoot();
        var handlerFiles = new[]
        {
            Path.Combine(solutionRoot, "src", "services", "AICopilot.AiGatewayService", "Agents", "ChatStreamHandler.cs"),
            Path.Combine(solutionRoot, "src", "services", "AICopilot.AiGatewayService", "Agents", "ApprovalDecisionStreamHandler.cs"),
            Path.Combine(solutionRoot, "src", "services", "AICopilot.AiGatewayService", "AgentTasks", "PlanAgentTaskStreamHandler.cs")
        };

        foreach (var handlerFile in handlerFiles)
        {
            var source = File.ReadAllText(handlerFile);
            var loadSessionIndex = source.IndexOf("chatStreamRuntime.LoadSessionAsync", StringComparison.Ordinal);
            var userCheckIndex = source.IndexOf("currentUser.Id != session.UserId", StringComparison.Ordinal);
            var acquireLockIndex = source.IndexOf("sessionExecutionLock.AcquireAsync(request.SessionId", StringComparison.Ordinal);
            var appendIndex = source.IndexOf("messagePersistenceService.AppendBatchAsync(request.SessionId", StringComparison.Ordinal);

            loadSessionIndex.Should().BeGreaterThanOrEqualTo(0, handlerFile);
            userCheckIndex.Should().BeGreaterThan(loadSessionIndex, handlerFile);
            acquireLockIndex.Should().BeGreaterThan(userCheckIndex, handlerFile);
            appendIndex.Should().BeGreaterThan(acquireLockIndex, handlerFile);
            source.Should().Contain("if (pendingMessages.Count > 0)");
            source.Should().Contain("yield return earlyErrorChunk;");
        }
    }

    [Fact]
    public void FrontendConfig_ShouldKeepInternalConfigDomainsButExposeOnlyAgentSlots()
    {
        var solutionRoot = FindSolutionRoot();
        var frontendRoot = Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src");
        var configViewSource = File.ReadAllText(Path.Combine(
            frontendRoot,
            "views",
            "ConfigView.vue"));
        var configStoreSource = File.ReadAllText(Path.Combine(
            frontendRoot,
            "stores",
            "configStore.ts"));
        var configServiceSource = File.ReadAllText(Path.Combine(
            frontendRoot,
            "services",
            "configService.ts"));
        var permissionsSource = File.ReadAllText(Path.Combine(
            frontendRoot,
            "security",
            "permissions.ts"));

        File.Exists(Path.Combine(frontendRoot, "views", "config", "McpServerConfig.vue")).Should().BeFalse();
        File.Exists(Path.Combine(frontendRoot, "views", "config", "BusinessDatabaseConfig.vue")).Should().BeFalse();
        File.Exists(Path.Combine(frontendRoot, "views", "config", "ProviderReliabilityConfig.vue")).Should().BeFalse();
        File.Exists(Path.Combine(frontendRoot, "views", "configLabels.ts")).Should().BeFalse();
        File.Exists(Path.Combine(frontendRoot, "stores", "config", "mcpServerConfig.ts")).Should().BeFalse();
        File.Exists(Path.Combine(frontendRoot, "stores", "config", "businessDatabaseConfig.ts")).Should().BeFalse();

        permissionsSource.Should().NotContain("Mcp.GetListServers");
        permissionsSource.Should().NotContain("Mcp.CreateServer");
        permissionsSource.Should().NotContain("DataAnalysis.GetListBusinessDatabases");
        permissionsSource.Should().NotContain("AiGateway.GetProviderReliability");
        configServiceSource.Should().NotContain("/aigateway/provider-reliability");
        configServiceSource.Should().NotContain("/mcp/server/list");
        configServiceSource.Should().NotContain("/mcp/server");
        configServiceSource.Should().NotContain("/data-analysis/business-database");
        configStoreSource.Should().Contain("CONFIG_STORE_MESSAGES");
        configStoreSource.Should().Contain("useLanguageModelConfigDomain");
        configStoreSource.Should().Contain("useRoutingModelConfigDomain");
        configStoreSource.Should().Contain("useConversationTemplateConfigDomain");
        configStoreSource.Should().NotContain("useMcpServerConfigDomain");
        configStoreSource.Should().NotContain("useBusinessDatabaseConfigDomain");
        configStoreSource.Should().NotContain("useProviderReliabilityConfigDomain");
        configViewSource.Should().Contain("IntentRoutingAgent");
        configViewSource.Should().Contain("agent_planner");
        configViewSource.Should().Contain("agent_executor");
        configViewSource.Should().Contain("refreshAgentSlots");
        configViewSource.Should().NotContain("McpServerConfig");
        configViewSource.Should().NotContain("BusinessDatabaseConfig");
        configViewSource.Should().NotContain("ProviderReliabilityConfig");
    }

    [Fact]
    public void GetProviderReliabilityQuery_ShouldRequireDedicatedReadPermission()
    {
        var attribute = typeof(GetProviderReliabilityQuery)
            .GetCustomAttribute<AuthorizeRequirementAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permission.Should().Be("AiGateway.GetProviderReliability");
    }

    [Fact]
    public void FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload()
    {
        var solutionRoot = FindSolutionRoot();
        var vueRoot = Path.Combine(solutionRoot, "src", "vues", "AICopilot.Web", "src");
        var permissionsSource = File.ReadAllText(Path.Combine(vueRoot, "security", "permissions.ts"));
        var authStoreSource = File.ReadAllText(Path.Combine(vueRoot, "stores", "authStore.ts"));
        var routerSource = File.ReadAllText(Path.Combine(vueRoot, "router", "index.ts"));
        var appShellSource = File.ReadAllText(Path.Combine(vueRoot, "components", "layout", "AppShell.vue"));
        var i18nSource = File.ReadAllText(Path.Combine(vueRoot, "i18n", "index.ts"));
        var apiClientSource = File.ReadAllText(Path.Combine(vueRoot, "services", "apiClient.ts"));
        var ragServiceSource = File.ReadAllText(Path.Combine(vueRoot, "services", "ragService.ts"));
        var ragStoreSource = File.ReadAllText(Path.Combine(vueRoot, "stores", "ragStore.ts"));
        var embeddingModelStoreSource = File.ReadAllText(Path.Combine(
            vueRoot,
            "stores",
            "rag",
            "embeddingModelStore.ts"));
        var documentStoreSource = File.ReadAllText(Path.Combine(
            vueRoot,
            "stores",
            "rag",
            "documentStore.ts"));
        var documentGovernanceStoreSource = File.ReadAllText(Path.Combine(
            vueRoot,
            "stores",
            "rag",
            "documentGovernanceStore.ts"));
        var ragFormFactorySource = File.ReadAllText(Path.Combine(vueRoot, "stores", "ragFormFactories.ts"));
        var knowledgeViewSource = File.ReadAllText(Path.Combine(vueRoot, "views", "KnowledgeView.vue"));
        var knowledgeBaseManagementSource = File.ReadAllText(Path.Combine(vueRoot, "views", "knowledge", "KnowledgeBaseManagement.vue"));
        var knowledgeSearchPanelSource = File.ReadAllText(Path.Combine(vueRoot, "views", "knowledge", "KnowledgeSearchPanel.vue"));
        var knowledgeLabelsSource = File.ReadAllText(Path.Combine(vueRoot, "views", "knowledgeLabels.ts"));
        var permissionCatalogSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.IdentityService",
            "Authorization",
            "PermissionCatalog.cs"));
        var embeddingManagementSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.RagService",
            "EmbeddingModels",
            "EmbeddingModelManagement.cs"));

        permissionsSource.Should().Contain("KNOWLEDGE_READ_PERMISSIONS");
        permissionsSource.Should().Contain("Rag.SearchKnowledgeBase");
        authStoreSource.Should().Contain("canManageKnowledge");
        routerSource.Should().Contain("path: '/knowledge'");
        routerSource.Should().Contain("ability: 'knowledge'");
        appShellSource.Should().Contain("canManageKnowledge");
        appShellSource.Should().Contain("nav.knowledge");
        i18nSource.Should().Contain("知识库");
        apiClientSource.Should().Contain("postForm");
        apiClientSource.Should().Contain("isFormDataBody");
        ragServiceSource.Should().Contain("/rag/embedding-model/list");
        ragServiceSource.Should().Contain("/rag/knowledge-base/list");
        ragServiceSource.Should().Contain("/rag/document");
        ragServiceSource.Should().Contain("/rag/document/governance");
        ragServiceSource.Should().Contain("postForm<UploadDocumentResponse>");
        ragFormFactorySource.Should().Contain("apiKeyAction");
        ragStoreSource.Should().Contain("useEmbeddingModelDomain");
        embeddingModelStoreSource.Should().Contain("form.apiKey.trim()");
        documentStoreSource.Should().Contain("uploadDocument(file: File)");
        documentGovernanceStoreSource.Should().Contain("saveDocumentGovernance");
        knowledgeViewSource.Should().Contain("KnowledgeBaseManagement");
        knowledgeBaseManagementSource.Should().Contain("documentStatusLabel");
        knowledgeBaseManagementSource.Should().Contain("governanceType");
        knowledgeLabelsSource.Should().Contain("Pending");
        knowledgeLabelsSource.Should().Contain("Embedding");
        knowledgeLabelsSource.Should().Contain("Indexed");
        knowledgeLabelsSource.Should().Contain("Failed");
        knowledgeSearchPanelSource.Should().Contain("KNOWLEDGE_WRITE_PERMISSIONS.search");
        knowledgeBaseManagementSource.Should().Contain("KNOWLEDGE_WRITE_PERMISSIONS.document.governance");
        permissionCatalogSource.Should().Contain("Rag.SearchKnowledgeBase");
        permissionCatalogSource.Should().Contain("Rag.UpdateDocumentGovernance");
        embeddingManagementSource.Should().Contain("request.ApiKey is null");
        embeddingManagementSource.Should().Contain("ProtectApiKey(request.ApiKey)");
    }

    [Fact]
    public void SearchKnowledgeBaseQuery_ShouldRequireDedicatedRagSearchPermission()
    {
        var attribute = typeof(SearchKnowledgeBaseQuery)
            .GetCustomAttribute<AuthorizeRequirementAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permission.Should().Be("Rag.SearchKnowledgeBase");
    }

    [Fact]
    public void AgentPlanRuntimeAndUpload_ShouldRecheckRagKnowledgeBasePermissions()
    {
        var solutionRoot = FindSolutionRoot();
        var agentTaskPlanPreparationSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskPlanPreparationService.cs"));
        var agentRuntimeRagToolServiceSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "Runtime",
            "AgentRuntimeRagToolService.cs"));
        var uploadRecordsSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Uploads",
            "UploadRecords.cs"));
        var uploadRecordCoordinatorSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Uploads",
            "UploadRecordCoordinator.cs"));

        agentTaskPlanPreparationSource.Should().Contain("IKnowledgeBaseAccessChecker");
        agentTaskPlanPreparationSource.Should().Contain("CanReadAsync(");
        agentTaskPlanPreparationSource.Should().Contain("knowledgeBaseId");
        agentTaskPlanPreparationSource.Should().Contain("Result.NotFound");
        agentRuntimeRagToolServiceSource.Should().Contain("IKnowledgeBaseAccessChecker");
        agentRuntimeRagToolServiceSource.Should().Contain("CanReadAsync(");
        agentRuntimeRagToolServiceSource.Should().Contain("knowledgeBaseId");
        agentRuntimeRagToolServiceSource.Should().Contain("task.UserId");
        agentRuntimeRagToolServiceSource.Should().Contain("UnauthorizedAccessException");
        uploadRecordsSource.Should().Contain("UploadRecordCoordinator");
        uploadRecordsSource.Should().NotContain("CanWriteAsync(");
        uploadRecordCoordinatorSource.Should().Contain("IKnowledgeBaseAccessChecker");
        uploadRecordCoordinatorSource.Should().Contain("CanWriteAsync(");
        uploadRecordCoordinatorSource.Should().Contain("request.KnowledgeBaseId");
        uploadRecordCoordinatorSource.Should().Contain("Result.NotFound");
    }

    [Fact]
    public void ApiControllerBase_ShouldReturnProblemDetailsForErrorBranches()
    {
        var solutionRoot = FindSolutionRoot();
        var baseControllerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi",
            "Infrastructure",
            "ApiControllerBase.cs"));

        baseControllerSource.Should().Contain("case ResultStatus.Error:");
        baseControllerSource.Should().Contain("case ResultStatus.Invalid:");
        baseControllerSource.Should().Contain("case ResultStatus.NotFound:");
        baseControllerSource.Should().Contain("CreateProblemDetails(StatusCodes.Status400BadRequest, result.Errors)");
        baseControllerSource.Should().Contain("CreateProblemDetails(StatusCodes.Status404NotFound, result.Errors)");
        baseControllerSource.Should().NotContain("new { errors = result.Errors }");
    }

    [Fact]
    public void UseCaseExceptionHandler_ShouldReturnSanitizedProblemDetailsForCatchAll()
    {
        var solutionRoot = FindSolutionRoot();
        var exceptionHandlerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi",
            "Infrastructure",
            "UseCaseExceptionHandler.cs"));
        var problemCodesSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "shared",
            "AICopilot.SharedKernel",
            "Result",
            "ApiProblemDescriptor.cs"));

        problemCodesSource.Should().Contain("InternalServerError = \"internal_server_error\"");
        exceptionHandlerSource.Should().Contain("ILogger<UseCaseExceptionHandler>");
        exceptionHandlerSource.Should().Contain("logger.LogError");
        exceptionHandlerSource.Should().Contain("hidden_by_security_policy");
        exceptionHandlerSource.Should().NotContain("logger.LogError(\n            exception");
        exceptionHandlerSource.Should().Contain("AppProblemCodes.InternalServerError");
        exceptionHandlerSource.Should().Contain("StatusCodes.Status500InternalServerError");
        exceptionHandlerSource.Should().Contain("traceId");
        exceptionHandlerSource.Should().Contain("Request failed unexpectedly. Contact support with the trace id.");
        exceptionHandlerSource.Should().NotContain("return false;");
    }

    [Fact]
    public async Task UseCaseExceptionHandler_ShouldNotLogRawExceptionMessageForCatchAll()
    {
        var logger = new CapturingUseCaseExceptionLogger();
        var handler = new UseCaseExceptionHandler(logger);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-1"
        };
        context.Response.Body = new MemoryStream();
        var exception = new InvalidOperationException(
            "Provider endpoint http://model.internal.example failed with token=secret and SQL SELECT * FROM device_logs",
            new HttpRequestException("ConnectionString=Host=prod;Password=secret"));

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        var serializedLogs = string.Join('\n', logger.Messages);
        serializedLogs.Should().Contain("trace-1");
        serializedLogs.Should().Contain("InvalidOperationException");
        serializedLogs.Should().Contain("HttpRequestException");
        serializedLogs.Should().Contain("hidden_by_security_policy");
        serializedLogs.Should().NotContain("model.internal.example");
        serializedLogs.Should().NotContain("token=secret");
        serializedLogs.Should().NotContain("SELECT");
        serializedLogs.Should().NotContain("device_logs");
        serializedLogs.Should().NotContain("ConnectionString");
        serializedLogs.Should().NotContain("Password=secret");
        logger.Exceptions.Should().OnlyContain(item => item == null);
    }

    [Fact]
    public void FrontendSource_ShouldNotContainBareCatchBlocks()
    {
        var solutionRoot = FindSolutionRoot();
        var frontendRoot = Path.Combine(
            solutionRoot,
            "src",
            "vues",
            "AICopilot.Web",
            "src");
        var sourceFiles = Directory
            .EnumerateFiles(frontendRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".ts", StringComparison.Ordinal) ||
                           file.EndsWith(".vue", StringComparison.Ordinal))
            .ToArray();

        sourceFiles.Should().NotBeEmpty();
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            source.Should().NotContain("catch {", sourceFile);
        }
    }

    [Fact]
    public void IdentityManagementWrites_ShouldRequireAuthAndManagementRateLimit()
    {
        AssertIdentityManagementEndpoint(nameof(IdentityController.CreateRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.UpdateRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.DeleteRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.CreateUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.UpdateUserRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.DisableUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.EnableUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.ResetUserPassword));

        typeof(IdentityController)
            .GetMethod(nameof(IdentityController.Login))!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should().Be("login");

        typeof(IdentityController)
            .GetMethod(nameof(IdentityController.GetInitializationStatus))!
            .GetCustomAttribute<AuthorizeAttribute>()
            .Should().BeNull();
    }

    [Fact]
    public void LoginRateLimiter_ShouldPartitionByUsernameAndIp()
    {
        var solutionRoot = FindSolutionRoot();
        var httpApiRoot = Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi");
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            httpApiRoot,
            "DependencyInjection.cs"));
        var rateLimitingSource = File.ReadAllText(Path.Combine(
            httpApiRoot,
            "HttpApiRateLimitingConfiguration.cs"));
        var source = string.Concat(dependencyInjectionSource, Environment.NewLine, rateLimitingSource);

        source.Should().Contain("options.AddPolicy(\"login\"");
        source.Should().NotContain("AddTokenBucketLimiter(\"login\"");
        source.Should().Contain("GetLoginPolicyPartitionKey");
        source.Should().Contain("TryReadLoginUsername");
        source.Should().Contain("RemoteIpAddress");
        source.Should().Contain("JsonDocument.Parse");
        source.Should().NotContain("X-Login-Username");
        source.Should().NotContain("Request.Query.TryGetValue(\"username\"");
    }

    [Fact]
    public void UpdateUserRole_ShouldRefreshSecurityStamp()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.IdentityService",
            "Commands",
            "UpdateUserRole.cs"));

        var addRoleIndex = source.IndexOf("userManager.AddToRoleAsync", StringComparison.Ordinal);
        var refreshIndex = source.IndexOf("IdentityGovernanceHelper.RefreshSecurityStamp(user)", StringComparison.Ordinal);
        var updateIndex = source.IndexOf("userManager.UpdateAsync(user)", StringComparison.Ordinal);
        var auditIndex = source.IndexOf("auditLogWriter.WriteAsync", StringComparison.Ordinal);

        addRoleIndex.Should().BeGreaterThanOrEqualTo(0);
        refreshIndex.Should().BeGreaterThan(addRoleIndex);
        updateIndex.Should().BeGreaterThan(refreshIndex);
        auditIndex.Should().BeGreaterThan(updateIndex);
    }

    [Fact]
    public async Task LocalFileStorageService_ShouldConstrainAccessToConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "aicopilot-storage-tests", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = root
            })
            .Build();

        try
        {
            var storage = new LocalFileStorageService(configuration);
            await using var payload = new MemoryStream([1, 2, 3]);

            var relativePath = await storage.SaveAsync(payload, "../unsafe.txt");

            relativePath.Should().StartWith("uploads/");
            relativePath.Should().NotContain("..");
            relativePath.Should().EndWith("unsafe.txt");

            var stored = await storage.GetAsync(relativePath);
            stored.Should().NotBeNull();
            await using (stored!)
            {
                using var roundTrip = new MemoryStream();
                await stored.CopyToAsync(roundTrip);
                roundTrip.ToArray().Should().Equal(1, 2, 3);
            }

            await storage.DeleteAsync(relativePath);
            (await storage.GetAsync(relativePath)).Should().BeNull();

            Func<Task> traversalGet = async () => await storage.GetAsync("../escape.txt");
            Func<Task> nestedTraversalDelete = () => storage.DeleteAsync("uploads/../../escape.txt");
            Func<Task> absoluteGet = async () => await storage.GetAsync(
                Path.GetFullPath(Path.Combine(root, "..", "escape.txt")));

            await traversalGet.Should().ThrowAsync<InvalidOperationException>();
            await nestedTraversalDelete.Should().ThrowAsync<InvalidOperationException>();
            await absoluteGet.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LocalFileStorageService_ShouldNotUseHardcodedDriveRoot()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "Storage",
            "LocalFileStorageService.cs"));

        source.Should().Contain("FileStorage:RootPath");
        source.Should().Contain("SpecialFolder.LocalApplicationData");
        source.Should().NotContain("D:\\\\");
    }

    [Fact]
    public void AgentStreamRuntime_ShouldNotExposeGenericExceptionMessages()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Agents",
            "AgentStreamRuntime.cs"));

        var exceptionParameterIndex = source.IndexOf("Exception exception,", StringComparison.Ordinal);
        exceptionParameterIndex.Should().BeGreaterThanOrEqualTo(0);
        var methodStart = source.LastIndexOf("public static ChatChunk CreateErrorChunk(", exceptionParameterIndex, StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var methodEnd = source.IndexOf("public static ChatChunk CreateErrorChunk(", methodStart + 1, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("exception is AgentWorkflowException");
        method.Should().NotContain("exception.Message");
        method.Should().Contain("fallbackUserFacingMessage");
    }

    [Fact]
    public void AuditLogWriter_ShouldStageAuditBeforeExplicitSave()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditLogWriter.cs"));

        var writeStart = source.IndexOf("public Task WriteAsync", StringComparison.Ordinal);
        var saveStart = source.IndexOf("public Task<int> SaveChangesAsync", StringComparison.Ordinal);

        writeStart.Should().BeGreaterThanOrEqualTo(0);
        saveStart.Should().BeGreaterThan(writeStart);
        source[writeStart..saveStart].Should().NotContain("SaveChangesAsync(");
        source.Should().Contain("AuditDbContext");
        source.Should().NotContain("AiCopilotDbContext");
        source[saveStart..].Should().Contain("auditDbContext.SaveChangesAsync");
    }

    [Fact]
    public void AuditRuntimeServices_ShouldUseDedicatedAuditDbContext()
    {
        var solutionRoot = FindSolutionRoot();
        var writerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditLogWriter.cs"));
        var querySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditLogQueryService.cs"));
        var auditContextSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditDbContext.cs"));
        var efRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "EfRepository.cs"));
        var efRepositoryBaseSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "EfRepositoryBase.cs"));
        var efReadRepositoryBaseSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "EfReadRepositoryBase.cs"));
        var aiGatewayRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "AiGatewayRepository.cs"));
        var businessDatabaseRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "BusinessDatabaseRepository.cs"));
        var ragRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "RagRepository.cs"));
        var mcpServerRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "McpServerRepository.cs"));
        var transactionCoordinatorSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "AuditTransactionCoordinator.cs"));
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));

        writerSource.Should().Contain("AuditDbContext");
        writerSource.Should().NotContain("AiCopilotDbContext");
        querySource.Should().Contain("AuditDbContext");
        querySource.Should().NotContain("AiCopilotDbContext");
        auditContextSource.Should().Contain("DbSet<AuditLogEntry>");
        auditContextSource.Should().Contain("AuditLogEntryConfiguration");
        auditContextSource.Should().NotContain("ExcludeFromMigrations");
        efRepositorySource.Should().Contain("EfRepositoryBase<AiCopilotDbContext, T>");
        efRepositoryBaseSource.Should().Contain("AuditTransactionCoordinator");
        efRepositoryBaseSource.Should().Contain("transactionCoordinator.SaveChangesAsync");
        efReadRepositoryBaseSource.Should().Contain("SpecificationEvaluator.GetQuery");
        efReadRepositoryBaseSource.Should().Contain("ApplyIncludes");
        aiGatewayRepositorySource.Should().Contain("EfRepositoryBase<AiGatewayDbContext, T>");
        businessDatabaseRepositorySource.Should().Contain("EfRepositoryBase<DataAnalysisDbContext, BusinessDatabase>");
        ragRepositorySource.Should().Contain("EfRepositoryBase<RagDbContext, T>");
        mcpServerRepositorySource.Should().Contain("EfRepositoryBase<McpServerDbContext, McpServerInfo>");
        aiGatewayRepositorySource.Should().NotContain("ApplySpecification");
        businessDatabaseRepositorySource.Should().NotContain("ApplySpecification");
        ragRepositorySource.Should().NotContain("ApplySpecification");
        mcpServerRepositorySource.Should().NotContain("ApplySpecification");
        transactionCoordinatorSource.Should().Contain("CreateExecutionStrategy");
        transactionCoordinatorSource.Should().Contain("BeginTransactionAsync");
        transactionCoordinatorSource.Should().Contain("UseTransactionAsync");
        transactionCoordinatorSource.Should().Contain("new AuditDbContext");
        transactionCoordinatorSource.Should().Contain("transactionalAuditDbContext.SaveChangesAsync");
        transactionCoordinatorSource.Should().NotContain("SetDbConnection");
        dependencyInjectionSource.Should().Contain("AddNpgsqlDbContext<AuditDbContext>");
        dependencyInjectionSource.Should().Contain("AuditTransactionCoordinator");
    }

    [Fact]
    public void IdentityManagementCommands_ShouldUseTransactionalExecutionService()
    {
        var solutionRoot = FindSolutionRoot();
        var commandFiles = new[]
        {
            "CreateRole.cs",
            "UpdateRole.cs",
            "DeleteRole.cs",
            "CreatedUser.cs",
            "UpdateUserRole.cs",
            "DisableUser.cs",
            "EnableUser.cs",
            "ResetUserPassword.cs"
        };

        foreach (var commandFile in commandFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.IdentityService",
                "Commands",
                commandFile));

            source.Should().Contain("ITransactionalExecutionService", commandFile);
            source.Should().Contain("IIdentityAuditLogWriter", commandFile);
            source.Should().Contain("transactionalExecutionService.ExecuteAsync", commandFile);
            source.Should().NotContain("IAuditLogWriter", commandFile);
            source.Should().NotContain("auditLogWriter.SaveChangesAsync", commandFile);
        }
    }

    [Fact]
    public void EfCore_ShouldRegisterTransactionalExecutionService()
    {
        var solutionRoot = FindSolutionRoot();
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));
        var implementationSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "EfTransactionalExecutionService.cs"));

        dependencyInjectionSource.Should().Contain("ITransactionalExecutionService");
        dependencyInjectionSource.Should().Contain("EfTransactionalExecutionService");
        dependencyInjectionSource.Should().Contain("AddNpgsqlDbContext<IdentityStoreDbContext>");
        dependencyInjectionSource.Should().Contain("AddEntityFrameworkStores<IdentityStoreDbContext>");
        dependencyInjectionSource.Should().Contain("IIdentityAuditLogWriter");
        implementationSource.Should().Contain("CreateExecutionStrategy");
        implementationSource.Should().Contain("BeginTransactionAsync");
        implementationSource.Should().Contain("dbContext.SaveChangesAsync");
        implementationSource.Should().Contain("IdentityStoreDbContext");
        implementationSource.Should().NotContain("AiCopilotDbContext");
        implementationSource.Should().NotContain("AuditDbContext");
    }

    [Fact]
    public void IntegrationEventStager_ShouldExposeOnlyFactoryPath()
    {
        var stageMethod = typeof(IIntegrationEventStager).GetMethods().Should().ContainSingle().Subject;
        var stageParameter = stageMethod.GetParameters().Should().ContainSingle().Subject.ParameterType;
        stageMethod.Name.Should().Be(nameof(IIntegrationEventStager.Stage));
        stageMethod.IsGenericMethodDefinition.Should().BeTrue();
        stageParameter.IsGenericType.Should().BeTrue();
        stageParameter.GetGenericTypeDefinition().Should().Be(typeof(Func<>));

        typeof(RagIntegrationEventStager).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Should().ContainSingle(method => method.Name == nameof(IIntegrationEventStager.Stage));
        typeof(IIntegrationEventStager).Assembly
            .GetType("AICopilot.Services.Contracts.IIntegrationEventPublisher")
            .Should().BeNull();
        typeof(OutboxDbContext).Assembly
            .GetType("AICopilot.EntityFrameworkCore.Outbox.OutboxIntegrationEventPublisher")
            .Should().BeNull();
        typeof(AICopilot.EventBus.DependencyInjection).Assembly
            .GetType("AICopilot.EventBus.MassTransitIntegrationEventPublisher")
            .Should().BeNull();
    }

    [Fact]
    public void OutboxRuntimeContext_ShouldOwnOutboxModel()
    {
        using var outboxContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>()
                .UseNpgsql("Host=localhost;Database=aicopilot_security;Username=test;Password=test")
                .Options);
        var outboxEntity = outboxContext.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(OutboxMessage));
        outboxEntity.Should().NotBeNull();
        outboxEntity!.GetSchema().Should().Be("outbox");
        outboxEntity.GetTableName().Should().Be("outbox_messages");
        outboxEntity.IsTableExcludedFromMigrations().Should().BeFalse();
    }

    [Fact]
    public void OutboxDispatcher_ShouldUseSkipLockedAndNotRetryCancellation()
    {
        var solutionRoot = FindSolutionRoot();
        var dispatcherSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox",
            "OutboxDispatcher.cs"));

        dispatcherSource.Should().Contain("BeginTransactionAsync");
        dispatcherSource.Should().Contain("CreateExecutionStrategy");
        dispatcherSource.Should().Contain("FOR UPDATE SKIP LOCKED");
        dispatcherSource.Should().Contain("catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)");
        dispatcherSource.Should().Contain("without incrementing retry count");
        dispatcherSource.Should().Contain("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
        dispatcherSource.Should().Contain("message.MarkFailed(\"Outbox message publishing failed.");
        dispatcherSource.Should().NotContain("message.MarkFailed(ex.Message");
    }

    [Fact]
    public void ConfigCommands_ShouldSaveAuditWithBusinessChanges()
    {
        var solutionRoot = FindSolutionRoot();
        var commandFiles = new[]
        {
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "ApprovalPolicies", "ApprovalPolicyManagement.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "CreateConversationTemplate.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "UpdateConversationTemplate.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "DeleteConversationTemplate.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "CreateLanguageModel.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "UpdateLanguageModel.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "DeleteLanguageModel.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "Sessions", "UpdateSessionSafetyAttestation.cs"),
            Path.Combine("src", "services", "AICopilot.DataAnalysisService", "BusinessDatabases", "BusinessDatabaseCommandHandlers.cs"),
            Path.Combine("src", "services", "AICopilot.RagService", "Commands", "KnowledgeBases", "CreateKnowledgeBase.cs"),
            Path.Combine("src", "services", "AICopilot.RagService", "KnowledgeBases", "KnowledgeBaseManagement.cs"),
            Path.Combine("src", "services", "AICopilot.RagService", "EmbeddingModels", "EmbeddingModelManagement.cs"),
            Path.Combine("src", "services", "AICopilot.RagService", "Documents", "DocumentManagement.cs"),
            Path.Combine("src", "services", "AICopilot.McpService", "McpServers", "McpServerManagement.cs")
        };

        foreach (var commandFile in commandFiles)
        {
            var source = File.ReadAllText(Path.Combine(solutionRoot, commandFile));

            source.Should().Contain("auditLogWriter.WriteAsync", commandFile);
            source.Should().NotContain("auditLogWriter.SaveChangesAsync", commandFile);
        }
    }

    [Fact]
    public void ExplicitAuditSaves_ShouldStayInsideDocumentedWhitelist()
    {
        var solutionRoot = FindSolutionRoot();
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/DataAnalysisAuditRecorder.cs",
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/FinalAgentRunExecutor.cs",
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/ToolExecutionAuditRecorder.cs",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningCommandCoordinator.cs",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningQueryCoordinator.cs",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceLifecycleCoordinator.cs",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceQueryCoordinator.cs",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9Coordinator.cs",
            "src/services/AICopilot.AiGatewayService/Uploads/UploadRecordCoordinator.cs",
            "src/services/AICopilot.RagService/Commands/Documents/UploadDocument.cs",
            "src/services/AICopilot.RagService/Queries/KnowledgeBases/SearchKnowledgeBase.cs",
            "src/services/AICopilot.DataAnalysisService/Plugins/DataAnalysisSqlQueryRunner.cs",
            "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessReadonlyQueryAuditRecorder.cs",
            "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessTextToSql.cs"
        };

        var locations = Directory
            .EnumerateFiles(Path.Combine(solutionRoot, "src", "services"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = Path.GetRelativePath(solutionRoot, file).Replace('\\', '/'),
                    LineNumber = index + 1,
                    Line = line.Trim()
                }))
            .Where(item => item.Line.Contains("auditLogWriter.SaveChangesAsync", StringComparison.Ordinal))
            .ToArray();

        var violations = locations
            .Where(item => !allowedFiles.Contains(item.File))
            .Select(item => $"{item.File}:{item.LineNumber}: {item.Line}")
            .ToArray();

        violations.Should().BeEmpty(
            "explicit audit saves are only allowed for workflow/query executors with no business save point");
        locations.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().Contain(file => allowedFiles.Contains(file), "the whitelist should stay tied to at least one documented explicit audit save");
    }

    [Fact]
    public void BusinessRules_ShouldDocumentAuditAndOutboxBoundaries()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(solutionRoot, "资料", "AICopilot业务规则.md"));

        source.Should().Contain("AiCopilotDbContext");
        source.Should().Contain("AuditDbContext");
        source.Should().Contain("DataAnalysisDbContext");
        source.Should().Contain("OutboxDbContext");
        source.Should().Contain("auditLogWriter.SaveChangesAsync");
        source.Should().Contain("Audit writer decision tree");
        source.Should().Contain("FOR UPDATE SKIP LOCKED");
        source.Should().Contain("runtime registry refresh cycle");
        source.Should().Contain("security stamp");
        source.Should().Contain("__EFMigrationsHistory");
    }

    [Fact]
    public void GetListChatMessagesQuery_ShouldRequireSessionViewPermission()
    {
        var attribute = typeof(GetListChatMessagesQuery)
            .GetCustomAttribute<AuthorizeRequirementAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permission.Should().Be("AiGateway.GetSession");
    }

    [Fact]
    public async Task UploadDocument_ShouldRejectEmptyOrTooLargeFilesBeforeDispatch()
    {
        var controller = new RagController(new ThrowingSender());

        var empty = new FormFile(new MemoryStream(), 0, 0, "file", "empty.txt");
        var emptyResult = await controller.UploadDocument(Guid.NewGuid(), empty);
        emptyResult.Should().BeOfType<BadRequestObjectResult>();

        var large = new FormFile(
            new MemoryStream([1]),
            0,
            RagController.MaxDocumentUploadBytes + 1,
            "file",
            "large.txt");
        var largeResult = await controller.UploadDocument(Guid.NewGuid(), large);
        largeResult.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void UploadDocument_ShouldDeclareRequestSizeLimits()
    {
        var method = typeof(RagController).GetMethod(nameof(RagController.UploadDocument));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<RequestSizeLimitAttribute>()
            .Should().NotBeNull();
        method.GetCustomAttribute<RequestFormLimitsAttribute>()?.MultipartBodyLengthLimit
            .Should().Be(RagController.MaxDocumentUploadBytes);
    }

    [Fact]
    public void JsonHelper_ShouldUseHtmlSafeEscaping()
    {
        var json = new { Value = "<script>alert(1)</script>" }.ToJson();

        json.Should().Contain("\\u003Cscript\\u003E");
        json.Should().NotContain("<script>");
    }

    [Fact]
    public void DapperConnector_ShouldRejectEmptyConnectionString()
    {
        var connector = new DapperDatabaseConnector(
            new AstSqlGuardrail(),
            NullLogger<DapperDatabaseConnector>.Instance);

        var database = new BusinessDatabaseConnectionInfo(
            Guid.NewGuid(),
            "empty-connection",
            "empty connection test",
            " ",
            DatabaseProviderType.PostgreSql,
            IsEnabled: true,
            IsReadOnly: true);

        var action = () => connector.GetConnection(database);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Connection string is required*");
    }

    [Fact]
    public void DapperConnector_ShouldUseReadOnlyTransactionAndBoundedReader()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Dapper",
            "DapperDatabaseConnector.cs"));

        source.Should().Contain("sqlGuardrail.Validate");
        source.Should().Contain("BeginTransactionAsync");
        source.Should().Contain("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY");
        source.Should().Contain("SET TRANSACTION READ ONLY");
        source.Should().Contain("SET SESSION CHARACTERISTICS AS TRANSACTION READ WRITE");
        source.Should().Contain("ExecuteReaderAsync");
        source.Should().Contain("normalizedRows.Count >= maxRows");
        source.Should().Contain("BuildSqlLogMetadata");
        source.Should().Contain("SqlSha256");
        source.Should().Contain("SqlLength");
        source.Should().Contain("OriginalMessage=hidden_by_security_policy");
        source.Should().Contain("ClassifyGuardrailFailure");
        source.Should().NotContain("SQL: {Sql}");
        source.Should().NotContain("logger.LogError(\n                ex,");
        source.Should().NotContain("SQL security guard rejected query against {DatabaseName}: {Reason}");
        source.Should().NotContain("QueryAsync(command)).ToList");
        source.Should().NotContain("rawRows.Take");
    }

    [Fact]
    public void DataAnalysisPlugin_ShouldNotLogOrReturnRawSqlExceptionMessage()
    {
        var solutionRoot = FindSolutionRoot();
        var pluginSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.DataAnalysisService",
            "Plugins",
            "DataAnalysisPlugin.cs"));
        var formatterSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.DataAnalysisService",
            "Plugins",
            "DataAnalysisToolResultFormatter.cs"));

        pluginSource.Should().Contain("OriginalMessage=hidden_by_security_policy");
        pluginSource.Should().Contain("ClassifySqlRejection");
        pluginSource.Should().Contain("BuildSafeSqlRejectedMessage");
        pluginSource.Should().NotContain("SQL 执行被拦截: {Message}");
        pluginSource.Should().NotContain("原因: {ex.Message}");
        pluginSource.Should().NotContain("logger.LogError(ex, \"SQL 执行异常\")");
        pluginSource.Should().NotContain("logger.LogError(ex, \"获取表名失败");
        pluginSource.Should().NotContain("logger.LogError(ex, \"获取表结构失败");

        formatterSource.Should().Contain("ResolveSafeSqlRejectedReason");
        formatterSource.Should().Contain("ResolveSafeConfigurationMessage");
        formatterSource.Should().NotContain("ArgumentException or InvalidOperationException => $\"{prefix}: {ex.Message}\"");
    }

    [Fact]
    public void ProviderWorkflowAndWorkerLogs_ShouldNotAttachRawExceptions()
    {
        var solutionRoot = FindSolutionRoot();
        var sources = new Dictionary<string, string>
        {
            ["AgentRuntimeFactory"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "infrastructure",
                "AICopilot.AiRuntime",
                "AgentRuntimeFactory.cs")),
            ["AgentTaskRunQueueWorker"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "AgentTasks",
                "AgentTaskRunQueueWorker.cs")),
            ["AgentWorkflowPipeline"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "AgentWorkflowPipeline.cs")),
            ["AgentSkillRouterAutoSelector"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Skills",
                "AgentSkillRouterAutoSelector.cs")),
            ["IntentRoutingExecutor"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "Executors",
                "IntentRoutingExecutor.cs")),
            ["KnowledgeRetrievalExecutor"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "Executors",
                "KnowledgeRetrievalExecutor.cs")),
            ["ToolsPackExecutor"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "Executors",
                "ToolsPackExecutor.cs")),
            ["DataAnalysisWidgetEmitter"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "Executors",
                "DataAnalysisWidgetEmitter.cs")),
            ["FreeFormDbaAnalysisRunner"] = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "Workflows",
                "Executors",
                "FreeFormDbaAnalysisRunner.cs"))
        };

        foreach (var (name, source) in sources)
        {
            source.Should().Contain("OriginalMessage=hidden_by_security_policy", name);
            LoggerFirstArgumentVariablePattern.IsMatch(RemoveWhitespace(source))
                .Should()
                .BeFalse($"{name} must log ErrorType and fixed diagnostic codes instead of passing exception variables to logger overloads");
        }

        sources["AgentTaskRunQueueWorker"].Should().NotContain("ex.Message,");
    }

    [Fact]
    public void ProductionLogs_ShouldNotAttachRawExceptionObjects()
    {
        var solutionRoot = FindSolutionRoot();
        var productionRoots = new[]
        {
            Path.Combine(solutionRoot, "src", "hosts"),
            Path.Combine(solutionRoot, "src", "infrastructure"),
            Path.Combine(solutionRoot, "src", "services"),
            Path.Combine(solutionRoot, "src", "core"),
            Path.Combine(solutionRoot, "src", "shared")
        };
        var violations = productionRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => new
            {
                File = Path.GetRelativePath(solutionRoot, file),
                Source = RemoveWhitespace(File.ReadAllText(file))
            })
            .Where(item => LoggerFirstArgumentVariablePattern.IsMatch(item.Source))
            .Select(item => item.File)
            .ToArray();

        violations.Should().BeEmpty("production logs must record ErrorType and fixed diagnostic codes instead of passing exception variables to logger overloads");
    }

    [Fact]
    public void ErrorBoundaryMessages_ShouldNotReturnOrPersistRawExceptionMessages()
    {
        var solutionRoot = FindSolutionRoot();
        var rawMessageFreeFiles = new[]
        {
            Path.Combine("src", "services", "AICopilot.Services.CrossCutting", "Sql", "SqlAllowlistColumnInspector.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "Mcp", "McpServerBootstrap.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "CloudIdentity", "CloudIdentityStatusClient.cs"),
            Path.Combine("src", "services", "AICopilot.DataAnalysisService", "Semantics", "SemanticQueryPlanner.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "TestLanguageModel.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "Tools", "ToolInputSchemaValidator.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "AgentTasks", "AgentDynamicPlanner.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "AgentTasks", "PlannerToolCatalog.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "AgentTasks", "AgentDynamicPlannerResponseParser.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Dapper", "Security", "AstSqlGuardrail.cs"),
            Path.Combine("src", "services", "AICopilot.AiGatewayService", "AgentTasks", "AgentTaskRuntime.cs"),
            Path.Combine("src", "services", "AICopilot.RagService", "Documents", "DocumentIndexingService.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.EntityFrameworkCore", "Outbox", "OutboxDispatcher.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "CloudRead", "CloudAiReadHttpTransport.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "AiGateway", "LanguageModelConnectivityTester.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "AiGateway", "PostgreSqlSessionExecutionLock.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "Mcp", "McpRuntimeRegistrySynchronizer.cs"),
            Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "Mcp", "McpServerManager.cs")
        };

        foreach (var relativePath in rawMessageFreeFiles)
        {
            var source = File.ReadAllText(Path.Combine(solutionRoot, relativePath));
            source.Should().NotContain("ex.Message", relativePath);
            source.Should().NotContain("{ex.Message}", relativePath);
        }

        File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.AiGatewayService",
                "AgentTasks",
                "AgentTaskRuntime.cs"))
            .Should().Contain("BuildSafeExceptionSummary(ex)");
        File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "infrastructure",
                "AICopilot.Dapper",
                "Security",
                "AstSqlGuardrail.cs"))
            .Should().Contain("安全拦截：SQL 语句未通过安全语法解析。");
    }

    [Fact]
    public void DataAnalysisAuditSummaries_ShouldStayReadable()
    {
        var solutionRoot = FindSolutionRoot();
        var source = string.Concat(
            File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.DataAnalysisService",
                "BusinessDatabases",
                "BusinessDatabaseCommandHandlers.cs")),
            File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "services",
                "AICopilot.DataAnalysisService",
                "BusinessDatabases",
                "BusinessDatabaseDtoMapper.cs")));
        var queryRunnerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.DataAnalysisService",
            "Plugins",
            "DataAnalysisSqlQueryRunner.cs"));
        var semanticAuditSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "DataAnalysisAuditRecorder.cs"));

        source.Should().Contain("Created business database:");
        source.Should().Contain("Deleted business database:");
        queryRunnerSource.Should().Contain("RowsObserved=");
        queryRunnerSource.Should().Contain("已检测到至少");
        queryRunnerSource.Should().NotContain("共返回");
        semanticAuditSource.Should().Contain("RowsObserved=");
        semanticAuditSource.Should().NotContain("Rows={queryResult.ReturnedRowCount}");
        source.Should().NotContain("鍒");
        source.Should().NotContain("锛");
        source.Should().NotContain("擄");
    }

    [Fact]
    public void DataAnalysisServices_ShouldNotBypassGuardedDatabaseConnector()
    {
        var solutionRoot = FindSolutionRoot();
        var serviceRoot = Path.Combine(solutionRoot, "src", "services", "AICopilot.DataAnalysisService");
        var forbiddenPatterns = new[]
        {
            "new NpgsqlConnection",
            "new SqlConnection",
            "new MySqlConnection",
            ".QueryAsync(",
            ".ExecuteReaderAsync(",
            ".ExecuteNonQuery("
        };

        var violations = Directory
            .EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = Path.GetRelativePath(solutionRoot, file).Replace('\\', '/'),
                    LineNumber = index + 1,
                    Line = line.Trim()
                }))
            .Where(item => forbiddenPatterns.Any(pattern => item.Line.Contains(pattern, StringComparison.Ordinal)))
            .Select(item => $"{item.File}:{item.LineNumber}: {item.Line}")
            .ToArray();

        violations.Should().BeEmpty("DataAnalysis SQL execution must go through IDatabaseConnector and ISqlGuardrail");
    }

    [Fact]
    public void McpRuntime_ShouldUseQuotedArgumentParserAndSseConnectionTimeout()
    {
        var solutionRoot = FindSolutionRoot();
        var mcpRoot = Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "Mcp");
        var source = string.Concat(
            File.ReadAllText(Path.Combine(mcpRoot, "McpServerBootstrap.cs")),
            File.ReadAllText(Path.Combine(mcpRoot, "McpRuntimeClientFactory.cs")));

        source.Should().Contain("ParseCommandArguments");
        source.Should().Contain("StringBuilder");
        source.Should().Contain("McpRuntimeStdioCommandResolver.EnsureAvailable");
        source.Should().Contain("McpRuntimeStdioCommandUnavailableException");
        source.Should().Contain("McpSseEndpointValidator.TryValidate");
        source.Should().Contain("ConnectionTimeout = SseConnectionTimeout");
        source.Should().Contain("TransportMode = HttpTransportMode.Sse");
        source.Should().NotContain("new Uri(mcpServerInfo.Arguments)");
        source.Should().NotContain("Split(' ', StringSplitOptions.RemoveEmptyEntries)");
    }

    [Fact]
    public void RagIndexing_ShouldAllowRecoveryAndDeleteOldVectorsBeforeUpsert()
    {
        var solutionRoot = FindSolutionRoot();
        var indexingSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.RagService",
            "Documents",
            "DocumentIndexingService.cs"));
        var indexingOptionsSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.RagService",
            "Documents",
            "RagIndexingOptions.cs"));
        var writerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "Rag",
            "KnowledgeVectorIndexWriter.cs"));

        indexingSource.Should().Contain("CanStartOrRecoverIndexing");
        indexingSource.Should().Contain("KnowledgeBaseByDocumentIdWithDocumentChunksSpec");
        indexingSource.Should().Contain("DocumentStatus.Parsing");
        indexingSource.Should().Contain("DocumentStatus.Splitting");
        indexingSource.Should().Contain("DocumentStatus.Embedding");
        indexingSource.Should().Contain("previousChunkCount");
        indexingSource.Should().Contain("CreateLinkedTokenSource");
        indexingSource.Should().Contain("CancelAfter");
        indexingSource.Should().Contain("文档解析超时，请稍后重试。");
        indexingSource.Should().Contain("文档向量化超时，请稍后重试。");
        indexingSource.Should().Contain("RagIndexingTimeoutException");
        indexingOptionsSource.Should().Contain("Rag:Indexing");
        indexingOptionsSource.Should().Contain("ParsingTimeoutSeconds");
        indexingOptionsSource.Should().Contain("EmbeddingTimeoutSeconds");
        writerSource.Should().Contain("PreviousChunkCount");
        writerSource.Should().Contain("Math.Max(request.PreviousChunkCount, chunks.Count)");
        writerSource.Should().Contain("DeleteAsync(staleRecordKeys");
        writerSource.Should().Contain("UpsertAsync(records");
        writerSource.IndexOf("DeleteAsync(staleRecordKeys", StringComparison.Ordinal)
            .Should().BeLessThan(writerSource.IndexOf("UpsertAsync(records", StringComparison.Ordinal));
        writerSource.Should().Contain("BuildRecordKey");
    }

    [Fact]
    public void LanguageModel_ShouldRejectInvalidInput()
    {
        var parameters = new ModelParameters { MaxTokens = 1024, Temperature = 0.2f };

        var emptyName = () => new LanguageModel("OpenAI", " ", "https://example.test", null, parameters);
        emptyName.Should().Throw<ArgumentException>();

        var invalidUrl = () => new LanguageModel("OpenAI", "model", "not-a-url", null, parameters);
        invalidUrl.Should().Throw<ArgumentException>();

        var invalidParameters = () => new LanguageModel(
            "OpenAI",
            "model",
            "https://example.test",
            null,
            new ModelParameters { MaxTokens = 0, Temperature = 0.2f });
        invalidParameters.Should().Throw<ArgumentOutOfRangeException>();

        typeof(LanguageModel).GetProperty(nameof(LanguageModel.Id))!
            .SetMethod!.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void MigrationWorker_ShouldMigrateAllModelSecretFormatsBeforeRuntimeStarts()
    {
        var solutionRoot = FindSolutionRoot();
        var workerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "Worker.cs"));
        var migratorSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "MigrationWorkerSecretMigrator.cs"));

        workerSource.Should().Contain("MigrationWorkerSecretMigrator.MigrateAsync");
        workerSource.Should().Contain("MigrationWorker:CheckSecretsOnly");
        workerSource.Should().Contain("MigrationWorkerSecretMigrator.VerifyAsync");
        workerSource.IndexOf("MigrationWorkerSecretMigrator.VerifyAsync", StringComparison.Ordinal)
            .Should().BeLessThan(workerSource.IndexOf("MigrationWorkerSecretMigrator.MigrateAsync", StringComparison.Ordinal));
        workerSource.IndexOf("MigrationWorkerSecretMigrator.MigrateAsync", StringComparison.Ordinal)
            .Should().BeLessThan(workerSource.IndexOf("MigrationWorkerAiGatewaySeeder.SeedDefaultsAsync", StringComparison.Ordinal));
        migratorSource.Should().Contain("public static async Task VerifyAsync");
        migratorSource.Should().Contain("aiGatewayDbContext.LanguageModels");
        migratorSource.Should().Contain("ragDbContext.EmbeddingModels");
        migratorSource.Should().Contain("SecretStringEncryptor.ReEncryptLegacyCipher");
        migratorSource.Should().Contain("SecretStringEncryptor.Encrypt(storedValue.Trim())");
        migratorSource.Should().Contain("EnsureMigratedSecrets");
        migratorSource.Should().Contain("SecretStringEncryptor.Decrypt(storedValue)");
        migratorSource.Should().Contain("non-encv2 secret value");
        migratorSource.Should().Contain("unreadable encv2 secret value");
        migratorSource.Should().Contain("Database.BeginTransactionAsync");
        migratorSource.Should().Contain("new DbContextOptionsBuilder<RagDbContext>()");
        migratorSource.Should().Contain("UseNpgsql(aiGatewayDbContext.Database.GetDbConnection())");
        migratorSource.Should().Contain("UseTransactionAsync");
        migratorSource.Should().Contain("transaction.GetDbTransaction()");
        migratorSource.Should().Contain("aiGatewayDbContext.SaveChangesAsync");
        migratorSource.Should().Contain("ragDbContext.SaveChangesAsync");
        migratorSource.IndexOf("Database.BeginTransactionAsync", StringComparison.Ordinal)
            .Should().BeLessThan(migratorSource.IndexOf("UseTransactionAsync", StringComparison.Ordinal));
        migratorSource.IndexOf("UseTransactionAsync", StringComparison.Ordinal)
            .Should().BeLessThan(migratorSource.IndexOf("MigrateInCurrentTransactionAsync(", StringComparison.Ordinal));
        migratorSource.IndexOf("MigrateInCurrentTransactionAsync(", StringComparison.Ordinal)
            .Should().BeLessThan(migratorSource.IndexOf("transaction.CommitAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void BusinessDatabase_ShouldRejectInvalidInput()
    {
        var emptyName = () => new BusinessDatabase(
            " ",
            "description",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql);
        emptyName.Should().Throw<ArgumentException>();

        var emptyConnection = () => new BusinessDatabase(
            "database",
            "description",
            " ",
            DbProviderType.PostgreSql);
        emptyConnection.Should().Throw<ArgumentException>();

        var invalidProvider = () => new BusinessDatabase(
            "database",
            "description",
            "Host=localhost;Database=test",
            (DbProviderType)999);
        invalidProvider.Should().Throw<ArgumentOutOfRangeException>();

        var enabledCloudWithoutVerifiedCredential = () => new BusinessDatabase(
            "cloud-readonly",
            "cloud readonly source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false,
            isEnabled: true);
        enabledCloudWithoutVerifiedCredential.Should().Throw<InvalidOperationException>()
            .WithMessage("*verified read-only credential*");

        var disabledCloudDraft = new BusinessDatabase(
            "cloud-readonly-draft",
            "cloud readonly draft source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false,
            isEnabled: false);
        disabledCloudDraft.IsEnabled.Should().BeFalse();
        disabledCloudDraft.ReadOnlyCredentialVerified.Should().BeFalse();
        disabledCloudDraft.ExternalSystemType.Should().Be(BusinessDataExternalSystemType.CloudReadOnly);

        var cloudDatabase = new BusinessDatabase(
            "cloud-readonly",
            "cloud readonly source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);

        var enablingCloudWithoutVerifiedCredential = () => cloudDatabase.UpdateSettings(
            isEnabled: true,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false);
        enablingCloudWithoutVerifiedCredential.Should().Throw<InvalidOperationException>()
            .WithMessage("*verified read-only credential*");

        typeof(BusinessDatabase).GetProperty(nameof(BusinessDatabase.Id))!
            .SetMethod!.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public void EmbeddingModel_ShouldRejectInvalidInput()
    {
        var emptyName = () => new EmbeddingModel(
            " ",
            "OpenAI",
            "https://example.test",
            "text-embedding-3-small",
            1536,
            8192);
        emptyName.Should().Throw<ArgumentException>();

        var invalidUrl = () => new EmbeddingModel(
            "embedding",
            "OpenAI",
            "not-a-url",
            "text-embedding-3-small",
            1536,
            8192);
        invalidUrl.Should().Throw<ArgumentException>();

        var invalidDimensions = () => new EmbeddingModel(
            "embedding",
            "OpenAI",
            "https://example.test",
            "text-embedding-3-small",
            0,
            8192);
        invalidDimensions.Should().Throw<ArgumentOutOfRangeException>();

        var model = new EmbeddingModel(
            " embedding ",
            " OpenAI ",
            " https://example.test ",
            " text-embedding-3-small ",
            1536,
            8192,
            " key ",
            false);

        model.Name.Should().Be("embedding");
        model.Provider.Should().Be("OpenAI");
        model.BaseUrl.Should().Be("https://example.test");
        model.ModelName.Should().Be("text-embedding-3-small");
        model.ApiKey.Should().Be("key");
        model.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ApprovalPolicy_ShouldRejectInvalidInput()
    {
        var emptyName = () => new ApprovalPolicy(
            " ",
            null,
            ApprovalTargetType.Plugin,
            "tool",
            [],
            true,
            false);
        emptyName.Should().Throw<ArgumentException>();

        var invalidTargetType = () => new ApprovalPolicy(
            "policy",
            null,
            (ApprovalTargetType)999,
            "tool",
            [],
            true,
            false);
        invalidTargetType.Should().Throw<ArgumentOutOfRangeException>();

        var emptyTarget = () => new ApprovalPolicy(
            "policy",
            null,
            ApprovalTargetType.Plugin,
            " ",
            [],
            true,
            false);
        emptyTarget.Should().Throw<ArgumentException>();

        var policy = new ApprovalPolicy(
            " policy ",
            " description ",
            ApprovalTargetType.Plugin,
            " target ",
            [" Echo ", "echo", " "],
            true,
            false);

        policy.Name.Should().Be("policy");
        policy.Description.Should().Be("description");
        policy.TargetName.Should().Be("target");
        policy.ToolNames.Should().Equal("Echo");
    }

    [Fact]
    public void McpServerInfo_ShouldRejectInvalidInput()
    {
        var emptyName = () => new McpServerInfo(
            " ",
            "description",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll");
        emptyName.Should().Throw<ArgumentException>();

        var invalidTransport = () => new McpServerInfo(
            "server",
            "description",
            (McpTransportType)999,
            "dotnet",
            "server.dll");
        invalidTransport.Should().Throw<ArgumentOutOfRangeException>();

        var invalidSseUrl = () => new McpServerInfo(
            "server",
            "description",
            McpTransportType.Sse,
            null,
            "not-a-url");
        invalidSseUrl.Should().Throw<ArgumentException>();

        var unsafeSseUrl = () => new McpServerInfo(
            "server",
            "description",
            McpTransportType.Sse,
            null,
            "http://127.0.0.1/sse");
        unsafeSseUrl.Should().Throw<ArgumentException>();

        var server = new McpServerInfo(
            " server ",
            " description ",
            McpTransportType.Stdio,
            " dotnet ",
            " server.dll ",
            ChatExposureMode.Advisory,
            [new McpAllowedTool(" Echo "), new McpAllowedTool("echo"), new McpAllowedTool(" ")]);

        server.Name.Should().Be("server");
        server.Description.Should().Be("description");
        server.Command.Should().Be("dotnet");
        server.Arguments.Should().Be("server.dll");
        server.AllowedTools.Select(tool => tool.ToolName).Should().Equal("Echo");
    }

    [Theory]
    [InlineData("https://mcp.example.com/sse")]
    [InlineData("http://8.8.8.8/sse")]
    public void McpSseEndpointValidator_ShouldAllowPublicHttpEndpoints(string endpoint)
    {
        var isValid = McpSseEndpointValidator.TryValidate(endpoint, out var uri, out var errorMessage);

        isValid.Should().BeTrue(errorMessage);
        uri.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("file:///tmp/mcp.sock")]
    [InlineData("http://user:pass@mcp.example.com/sse")]
    [InlineData("https://mcp.example.com/sse#fragment")]
    [InlineData("http://localhost/sse")]
    [InlineData("http://dev.localhost/sse")]
    [InlineData("http://127.0.0.1/sse")]
    [InlineData("http://[::1]/sse")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/sse")]
    [InlineData("http://172.16.0.1/sse")]
    [InlineData("http://192.168.0.1/sse")]
    public void McpSseEndpointValidator_ShouldRejectUnsafeEndpoints(string endpoint)
    {
        var isValid = McpSseEndpointValidator.TryValidate(endpoint, out var uri, out var errorMessage);

        isValid.Should().BeFalse();
        uri.Should().BeNull();
        errorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void KnowledgeBaseDocumentAndChunks_ShouldRejectInvalidInput()
    {
        var embeddingModelId = EmbeddingModelId.New();

        var emptyName = () => new KnowledgeBase(" ", "description", embeddingModelId);
        emptyName.Should().Throw<ArgumentException>();

        var emptyEmbeddingModelId = () => new KnowledgeBase("kb", "description", new EmbeddingModelId(Guid.Empty));
        emptyEmbeddingModelId.Should().Throw<ArgumentException>();

        var knowledgeBase = new KnowledgeBase(" kb ", " description ", embeddingModelId);
        knowledgeBase.Name.Should().Be("kb");
        knowledgeBase.Description.Should().Be("description");

        var emptyDocumentName = () => knowledgeBase.AddDocument(" ", "path.txt", ".txt", "hash");
        emptyDocumentName.Should().Throw<ArgumentException>();

        var document = knowledgeBase.AddDocument(" doc ", " path.txt ", " .txt ", " hash ");
        document.Name.Should().Be("doc");
        document.FilePath.Should().Be("path.txt");
        document.Extension.Should().Be(".txt");
        document.FileHash.Should().Be("hash");

        document.StartParsing();
        document.CompleteParsing();

        var negativeChunkIndex = () => document.AddChunk(-1, "content");
        negativeChunkIndex.Should().Throw<ArgumentOutOfRangeException>();

        var emptyChunkContent = () => document.AddChunk(0, " ");
        emptyChunkContent.Should().Throw<ArgumentException>();

        document.AddChunk(0, " chunk content ");
        document.Chunks.Should().ContainSingle();
        document.Chunks.Single().Content.Should().Be("chunk content");

        var emptyVectorId = () => document.MarkChunkAsEmbedded(0, " ");
        emptyVectorId.Should().Throw<ArgumentException>();

        document.StartEmbedding();
        document.Status.Should().Be(DocumentStatus.Embedding);
        document.StartParsing();
        document.Status.Should().Be(DocumentStatus.Parsing);
        document.CompleteParsing();
        document.Status.Should().Be(DocumentStatus.Splitting);

        var emptyFailureMessage = () => document.MarkAsFailed(" ");
        emptyFailureMessage.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConversationTemplate_ShouldAlwaysExposeNonNullSpecification()
    {
        var template = (ConversationTemplate)Activator.CreateInstance(
            typeof(ConversationTemplate),
            nonPublic: true)!;

        template.Specification.Should().NotBeNull();
    }

    private static string FindSolutionRoot() => RepositoryTestSupport.Root;

    private static string ReadAiGatewayControllerSources(string solutionRoot)
    {
        var controllerPath = Path.Combine(solutionRoot, "src", "hosts", "AICopilot.HttpApi", "Controllers");
        return string.Join(
            "\n",
            Directory.GetFiles(controllerPath, "AiGateway*.cs")
                .OrderBy(file => file, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string RemoveWhitespace(string source)
    {
        return new string(source.Where(character => !char.IsWhiteSpace(character)).ToArray());
    }

    private static string BuildPlatformAttestationRecord(
        string credentialStrategyLine,
        string platformOwnerLine,
        string credentialEvidence = "")
    {
        return $"""
# AI-SEC-010 runner platform attestation

Environment: production
Repository: iiot/aicopilot
Release tag: sha-abcdef
Runner host: asset-001
Attestation date: 2026-07-06
Related infrastructure ticket: INFRA-123

Runner machine facts:
Runner service runs as a dedicated non-root account.
Runner labels include self-hosted and iiot-linux-prod.
Runner work root is /data/iiot-platform/runners/aicopilot.
Docker Root Dir is /data/iiot-platform/runtime/docker.
AICopilot deploy directory is /srv/enterprise-ai/deploy.
check-runner-security-attestation.sh completed successfully.

GitHub production environment:
required reviewers are configured for production deployments.
Environment secrets are restricted to AICopilot production and disaster workflows.
Workflow permissions stay least-privilege: contents: read only.
Production workflows use runs-on self-hosted iiot-linux-prod.
No production or secret-touching workflow uses GitHub hosted runners.

Credential strategy:
{credentialStrategyLine}
{credentialEvidence}

Sign-off:
{platformOwnerLine}
Reviewer: Security Reviewer / 2026-07-06
Release owner: Release Owner / 2026-07-06
""";
    }

    private static void AssertIdentityManagementEndpoint(string actionName)
    {
        var method = typeof(IdentityController).GetMethod(actionName);

        method.Should().NotBeNull();
        method!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            .Should().Be("identity-management");
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The sender should not be called by this test.");
        }
    }

    private sealed class CapturingUseCaseExceptionLogger : ILogger<UseCaseExceptionHandler>
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
