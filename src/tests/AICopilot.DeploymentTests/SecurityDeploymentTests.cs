using System.Text.RegularExpressions;
using AICopilot.FilesystemTestKit;

namespace AICopilot.DeploymentTests;

public sealed class SecurityDeploymentTests
{
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
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_ENABLED=false");
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_BASE_URL=http://model.internal.example:40034/v1");
        envTemplate.Should().Contain("AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS=65536");
        envTemplate.Should().NotContain("AICOPILOT_FILE_STORAGE_ROOT_PATH");
        envTemplate.Should().NotContain("AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH");
        envTemplate.Should().Contain("AICOPILOT_PERSISTENCE_MAINTENANCE_INTERVAL_SECONDS=300");
        envTemplate.Should().Contain("AICOPILOT_PERSISTENCE_RECONCILIATION_DELAY_MINUTES=10");
        envTemplate.Should().Contain("AICOPILOT_PERSISTENCE_MARKER_RETENTION_DAYS=30");
        envTemplate.Should().Contain("AICOPILOT_PERSISTENCE_MAINTENANCE_BATCH_SIZE=100");
        compose.Should().Contain("AICopilotSecurity__ApiKeyEncryptionKey: ${AICOPILOT_API_KEY_ENCRYPTION_KEY}");
        compose.Should().Contain("AICopilot__PrivateModel__BaseUrl: ${AICOPILOT_PRIVATE_MODEL_BASE_URL:-http://model.internal.example:40034/v1}");
        compose.Should().Contain("AICopilot__PrivateModel__ContextWindowTokens: ${AICOPILOT_PRIVATE_MODEL_CONTEXT_TOKENS:-65536}");
        compose.Should().Contain("CloudOidc__BootstrapAdminAutoBindEnabled: ${CLOUD_OIDC_BOOTSTRAP_ADMIN_AUTO_BIND_ENABLED:-false}");
        compose.Should().Contain("CloudOidc__BootstrapAdminUserName: ${AICOPILOT_BOOTSTRAP_ADMIN_USERNAME}");
        compose.Should().Contain("FileStorage__RootPath: /var/lib/aicopilot/storage");
        compose.Should().Contain("ArtifactWorkspace__RootPath: /var/lib/aicopilot/artifact-workspaces");
        compose.Should().NotContain("AICOPILOT_FILE_STORAGE_ROOT_PATH");
        compose.Should().NotContain("AICOPILOT_ARTIFACT_WORKSPACE_ROOT_PATH");
        compose.Should().Contain("enterprise-ai-aicopilot-data:/var/lib/aicopilot");
        compose.Should().Contain("PersistenceMaintenance__ReconciliationDelayMinutes: ${AICOPILOT_PERSISTENCE_RECONCILIATION_DELAY_MINUTES:-10}");
        compose.Should().Contain("PersistenceMaintenance__MarkerRetentionDays: ${AICOPILOT_PERSISTENCE_MARKER_RETENTION_DAYS:-30}");
        ExtractComposeService(compose, "aicopilot-httpapi")
            .Should().Contain("volumes: *aicopilot-data-volumes");
        ExtractComposeService(compose, "aicopilot-dataworker")
            .Should().Contain("volumes: *aicopilot-data-volumes");
        ExtractComposeService(compose, "aicopilot-ragworker")
            .Should().Contain("volumes: *aicopilot-data-volumes");
        ExtractComposeService(compose, "aicopilot-migration")
            .Should().NotContain("volumes: *aicopilot-data-volumes");
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
Current mitigation: Restricted runner access and scheduled secret rotation remain in effect.
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
    private static string FindSolutionRoot() => RepositoryTestSupport.Root;

    private static string ExtractComposeService(string compose, string serviceName)
    {
        var match = Regex.Match(
            compose,
            $@"(?ms)^  {Regex.Escape(serviceName)}:\r?\n(?<body>.*?)(?=^  [a-zA-Z0-9_-]+:|\z)");
        match.Success.Should().BeTrue($"compose service '{serviceName}' must exist");
        return match.Groups["body"].Value;
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
Environment secrets are restricted to AICopilot production and disaster workflows.
Workflow permissions stay least-privilege: contents: read only.
Production workflows use runs-on self-hosted iiot-linux-prod.
No production or secret-touching workflow uses GitHub hosted runners.

Credential strategy:
{credentialStrategyLine}
{credentialEvidence}

Sign-off:
{platformOwnerLine}
Release owner: Release Owner / 2026-07-06
""";
    }
}
