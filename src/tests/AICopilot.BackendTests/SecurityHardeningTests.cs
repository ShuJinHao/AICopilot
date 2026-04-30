using System.Reflection;
using System.Text.Json;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Dapper;
using AICopilot.Dapper.Security;
using AICopilot.HttpApi.Controllers;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void DeploymentConfig_ShouldNotCarryKnownWeakSecrets()
    {
        var solutionRoot = FindSolutionRoot();
        var httpDevelopmentSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Hosts",
            "AICopilot.HttpApi",
            "appsettings.Development.json"));
        var appHostSettings = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Hosts",
            "AICopilot.AppHost",
            "appsettings.json"));
        var envTemplate = File.ReadAllText(Path.Combine(solutionRoot, "artifacts", ".env"));
        var compose = File.ReadAllText(Path.Combine(solutionRoot, "artifacts", "docker-compose.yaml"));

        httpDevelopmentSettings.Should().NotContain("29ynIx63y0Uq5Yj6wZZYikBElPPW4rqpXKGq4voqmeMDefoJQEC8fQQzYPk95rNp");
        appHostSettings.Should().NotContain("\"pg-password\": \"123456\"");
        envTemplate.Should().Contain("EVENTBUS_PASSWORD=CHANGE_ME_EVENTBUS_PASSWORD");
        envTemplate.Should().Contain("PG_PASSWORD=CHANGE_ME_POSTGRES_PASSWORD");
        envTemplate.Should().Contain("QDRANT_KEY=CHANGE_ME_QDRANT_KEY");
        envTemplate.Should().Contain("AICOPILOT_API_KEY_ENCRYPTION_KEY=CHANGE_ME_32_BYTES_MINIMUM");
        compose.Should().Contain("AICopilotSecurity__ApiKeyEncryptionKey: \"${AICOPILOT_API_KEY_ENCRYPTION_KEY}\"");
    }

    [Fact]
    public void ManagementControllers_ShouldRequireHttpAuthentication()
    {
        typeof(AiGatewayController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
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
            "Services",
            "AICopilot.AiGatewayService",
            "Queries",
            "Sessions");
        var sessionCommandPath = Path.Combine(
            solutionRoot,
            "src",
            "Services",
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
            "Services",
            "AICopilot.AiGatewayService",
            "Agents",
            "ChatStreamRequest.cs"));
        chatStreamSource.Should().Contain("currentUser.Id != session.UserId");
        chatStreamSource.Should().Contain("sessionExecutionLock.AcquireAsync(request.SessionId");
        chatStreamSource.Should().Contain("finalAgentContextStore.GetAsync(request.SessionId");
        chatStreamSource.Should().Contain("AppProblemCodes.ApprovalPending");

        var controllerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Hosts",
            "AICopilot.HttpApi",
            "Controllers",
            "AiGatewayController.cs"));
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
            "Vues",
            "AICopilot.Web",
            "src",
            "stores",
            "chatStore.ts"));
        var chatServiceSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "services",
            "chatService.ts"));
        var approvalCardSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "components",
            "chat",
            "ApprovalCard.vue"));

        chatServiceSource.Should().Contain("/aigateway/approval/pending");
        chatStoreSource.Should().Contain("refreshPendingApprovals(sessionId)");
        chatStoreSource.Should().Contain("reconcilePendingApprovalCards");
        chatStoreSource.Should().Contain("sessionId !== currentSessionId.value");
        chatStoreSource.Should().Contain("errorSessionId.value !== currentSessionId.value");
        chatStoreSource.Should().Contain("approval_already_processed");
        chatStoreSource.Should().Contain("'expired'");
        approvalCardSource.Should().Contain("isSubmitting");
        approvalCardSource.Should().Contain("审批上下文已失效");
        approvalCardSource.Should().NotContain("isProcessing.value = true");
    }

    [Fact]
    public void FrontendConfig_ShouldExposeMcpManagementAndDataAnalysisSafetyHints()
    {
        var solutionRoot = FindSolutionRoot();
        var configViewSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "views",
            "ConfigView.vue"));
        var configStoreSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "stores",
            "configStore.ts"));
        var configServiceSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "services",
            "configService.ts"));
        var permissionsSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Vues",
            "AICopilot.Web",
            "src",
            "security",
            "permissions.ts"));

        permissionsSource.Should().Contain("Mcp.GetListServers");
        permissionsSource.Should().Contain("Mcp.CreateServer");
        configServiceSource.Should().Contain("/mcp/server/list");
        configServiceSource.Should().Contain("/mcp/server");
        configStoreSource.Should().Contain("mcpServers");
        configStoreSource.Should().Contain("currentMcpServer");
        configStoreSource.Should().Contain("normalizeToolNames");
        configStoreSource.Should().Contain("getProblemDetails");
        configViewSource.Should().Contain("MCP 配置由启动期 bootstrap 读取");
        configViewSource.Should().Contain("重启服务");
        configViewSource.Should().Contain("toolPolicySummaries");
        configViewSource.Should().Contain("留空表示保留已保存参数");
        configViewSource.Should().Contain("SQL 安全拒绝");
        configViewSource.Should().Contain("结果截断");
        configViewSource.Should().Contain("配置管理台保存时始终强制只读");
    }

    [Fact]
    public void FrontendKnowledgeManagement_ShouldExposeRagRouteAndUseMultipartUpload()
    {
        var solutionRoot = FindSolutionRoot();
        var vueRoot = Path.Combine(solutionRoot, "src", "Vues", "AICopilot.Web", "src");
        var permissionsSource = File.ReadAllText(Path.Combine(vueRoot, "security", "permissions.ts"));
        var authStoreSource = File.ReadAllText(Path.Combine(vueRoot, "stores", "authStore.ts"));
        var routerSource = File.ReadAllText(Path.Combine(vueRoot, "router", "index.ts"));
        var appShellSource = File.ReadAllText(Path.Combine(vueRoot, "components", "layout", "AppShell.vue"));
        var apiClientSource = File.ReadAllText(Path.Combine(vueRoot, "services", "apiClient.ts"));
        var ragServiceSource = File.ReadAllText(Path.Combine(vueRoot, "services", "ragService.ts"));
        var ragStoreSource = File.ReadAllText(Path.Combine(vueRoot, "stores", "ragStore.ts"));
        var knowledgeViewSource = File.ReadAllText(Path.Combine(vueRoot, "views", "KnowledgeView.vue"));
        var permissionCatalogSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.IdentityService",
            "Authorization",
            "PermissionCatalog.cs"));
        var embeddingManagementSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.RagService",
            "EmbeddingModels",
            "EmbeddingModelManagement.cs"));

        permissionsSource.Should().Contain("KNOWLEDGE_READ_PERMISSIONS");
        permissionsSource.Should().Contain("Rag.SearchKnowledgeBase");
        authStoreSource.Should().Contain("canManageKnowledge");
        routerSource.Should().Contain("path: '/knowledge'");
        routerSource.Should().Contain("ability: 'knowledge'");
        appShellSource.Should().Contain("canManageKnowledge");
        appShellSource.Should().Contain("知识库");
        apiClientSource.Should().Contain("postForm");
        apiClientSource.Should().Contain("isFormDataBody");
        ragServiceSource.Should().Contain("/rag/embedding-model/list");
        ragServiceSource.Should().Contain("/rag/knowledge-base/list");
        ragServiceSource.Should().Contain("/rag/document");
        ragServiceSource.Should().Contain("postForm<UploadDocumentResponse>");
        ragStoreSource.Should().Contain("apiKeyAction");
        ragStoreSource.Should().Contain("uploadDocument(file: File)");
        knowledgeViewSource.Should().Contain("Pending");
        knowledgeViewSource.Should().Contain("Embedding");
        knowledgeViewSource.Should().Contain("Indexed");
        knowledgeViewSource.Should().Contain("Failed");
        knowledgeViewSource.Should().Contain("KNOWLEDGE_WRITE_PERMISSIONS.search");
        permissionCatalogSource.Should().Contain("Rag.SearchKnowledgeBase");
        embeddingManagementSource.Should().Contain("request.ApiKey ?? entity.ApiKey");
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
    public void ApiControllerBase_ShouldUseConstructorInjectedSender()
    {
        var solutionRoot = FindSolutionRoot();
        var baseControllerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Hosts",
            "AICopilot.HttpApi",
            "Infrastructure",
            "ApiControllerBase.cs"));
        var controllerFiles = new[]
        {
            "AiGatewayController.cs",
            "DataAnalysisController.cs",
            "IdentityController.cs",
            "McpController.cs",
            "RagController.cs"
        };

        baseControllerSource.Should().Contain("ApiControllerBase(ISender sender)");
        baseControllerSource.Should().Contain("protected ISender Sender");
        baseControllerSource.Should().NotContain("RequestServices");
        baseControllerSource.Should().NotContain("GetRequiredService<ISender>");

        foreach (var controllerFile in controllerFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                solutionRoot,
                "src",
                "Hosts",
                "AICopilot.HttpApi",
                "Controllers",
                controllerFile));

            source.Should().Contain("(ISender sender) : ApiControllerBase(sender)", controllerFile);
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
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Hosts",
            "AICopilot.HttpApi",
            "DependencyInjection.cs"));

        dependencyInjectionSource.Should().Contain("options.AddPolicy(\"login\"");
        dependencyInjectionSource.Should().NotContain("AddTokenBucketLimiter(\"login\"");
        dependencyInjectionSource.Should().Contain("GetLoginPolicyPartitionKey");
        dependencyInjectionSource.Should().Contain("TryReadLoginUsername");
        dependencyInjectionSource.Should().Contain("RemoteIpAddress");
        dependencyInjectionSource.Should().Contain("JsonDocument.Parse");
        dependencyInjectionSource.Should().NotContain("X-Login-Username");
        dependencyInjectionSource.Should().NotContain("Request.Query.TryGetValue(\"username\"");
    }

    [Fact]
    public void UpdateUserRole_ShouldRefreshSecurityStamp()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
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
            "Infrastructure",
            "AICopilot.Infrastructure",
            "Storage",
            "LocalFileStorageService.cs"));

        source.Should().Contain("FileStorage:RootPath");
        source.Should().Contain("SpecialFolder.LocalApplicationData");
        source.Should().NotContain("D:\\\\");
    }

    [Fact]
    public void ChatStreamRuntime_ShouldNotExposeGenericExceptionMessages()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.AiGatewayService",
            "Agents",
            "ChatStreamRuntime.cs"));

        var exceptionParameterIndex = source.IndexOf("Exception exception,", StringComparison.Ordinal);
        exceptionParameterIndex.Should().BeGreaterThanOrEqualTo(0);
        var methodStart = source.LastIndexOf("public static ChatChunk CreateErrorChunk(", exceptionParameterIndex, StringComparison.Ordinal);
        methodStart.Should().BeGreaterThanOrEqualTo(0);
        var methodEnd = source.IndexOf("public static ChatChunk CreateErrorChunk(", methodStart + 1, StringComparison.Ordinal);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        method.Should().Contain("exception is ChatWorkflowException");
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
            "Infrastructure",
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
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditLogWriter.cs"));
        var querySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditLogQueryService.cs"));
        var auditContextSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs",
            "AuditDbContext.cs"));
        var efRepositorySource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "EfRepository.cs"));
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));

        writerSource.Should().Contain("AuditDbContext");
        writerSource.Should().NotContain("AiCopilotDbContext");
        querySource.Should().Contain("AuditDbContext");
        querySource.Should().NotContain("AiCopilotDbContext");
        auditContextSource.Should().Contain("DbSet<AuditLogEntry>");
        auditContextSource.Should().Contain("AuditLogEntryConfiguration");
        auditContextSource.Should().NotContain("ExcludeFromMigrations");
        efRepositorySource.Should().Contain("auditDbContext.SaveChangesAsync");
        dependencyInjectionSource.Should().Contain("AddNpgsqlDbContext<AuditDbContext>");
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
                "Services",
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
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));
        var implementationSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
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
    public void OutboxRuntimeServices_ShouldUseDedicatedOutboxDbContext()
    {
        var solutionRoot = FindSolutionRoot();
        var dispatcherSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox",
            "OutboxDispatcher.cs"));
        var publisherSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox",
            "OutboxIntegrationEventPublisher.cs"));
        var outboxContextSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox",
            "OutboxDbContext.cs"));
        var dependencyInjectionSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));

        dispatcherSource.Should().Contain("GetRequiredService<OutboxDbContext>");
        dispatcherSource.Should().NotContain("AiCopilotDbContext");
        publisherSource.Should().Contain("OutboxDbContext");
        publisherSource.Should().NotContain("AiCopilotDbContext");
        outboxContextSource.Should().Contain("DbSet<OutboxMessage>");
        outboxContextSource.Should().Contain("OutboxMessageConfiguration");
        outboxContextSource.Should().NotContain("ExcludeFromMigrations");
        dependencyInjectionSource.Should().Contain("AddNpgsqlDbContext<OutboxDbContext>");
    }

    [Fact]
    public void OutboxDispatcher_ShouldUseSkipLockedAndNotRetryCancellation()
    {
        var solutionRoot = FindSolutionRoot();
        var dispatcherSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox",
            "OutboxDispatcher.cs"));

        dispatcherSource.Should().Contain("BeginTransactionAsync");
        dispatcherSource.Should().Contain("CreateExecutionStrategy");
        dispatcherSource.Should().Contain("FOR UPDATE SKIP LOCKED");
        dispatcherSource.Should().Contain("catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)");
        dispatcherSource.Should().Contain("without incrementing retry count");
        dispatcherSource.Should().Contain("catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
        dispatcherSource.Should().Contain("message.MarkFailed(ex.Message");
    }

    [Fact]
    public void ModuleDbContexts_ShouldExcludeOutboxFromMigrations()
    {
        var solutionRoot = FindSolutionRoot();
        var moduleContextFiles = new[]
        {
            Path.Combine("src", "Infrastructure", "AICopilot.EntityFrameworkCore", "DataAnalysisDbContext.cs"),
            Path.Combine("src", "Infrastructure", "AICopilot.EntityFrameworkCore", "McpServerDbContext.cs")
        };

        foreach (var moduleContextFile in moduleContextFiles)
        {
            var source = File.ReadAllText(Path.Combine(solutionRoot, moduleContextFile));

            source.Should().Contain("DbSet<OutboxMessage>", moduleContextFile);
            source.Should().Contain("ExcludeFromMigrations", moduleContextFile);
        }
    }

    [Fact]
    public void AiGatewayConfigCommands_ShouldSaveAuditWithBusinessChanges()
    {
        var solutionRoot = FindSolutionRoot();
        var commandFiles = new[]
        {
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "ApprovalPolicies", "ApprovalPolicyManagement.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "CreateConversationTemplate.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "UpdateConversationTemplate.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "ConversationTemplates", "DeleteConversationTemplate.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "CreateLanguageModel.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "UpdateLanguageModel.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "LanguageModels", "DeleteLanguageModel.cs"),
            Path.Combine("src", "Services", "AICopilot.AiGatewayService", "Commands", "Sessions", "UpdateSessionSafetyAttestation.cs")
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
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/DataAnalysisExecutor.cs",
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/FinalAgentRunExecutor.cs",
            "src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessDatabaseManagement.cs",
            "src/services/AICopilot.DataAnalysisService/Plugins/DataAnalysisPlugin.cs"
        };

        var locations = Directory
            .EnumerateFiles(Path.Combine(solutionRoot, "src", "Services"), "*.cs", SearchOption.AllDirectories)
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
            "explicit audit saves are only allowed for cross-DbContext DataAnalysis writes and workflow executors with no business save point");
        locations.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().Contain(file => allowedFiles.Contains(file), "the whitelist should stay tied to at least one documented explicit audit save");
    }

    [Fact]
    public void ReviewBaseline_ShouldDocumentAuditAndOutboxBoundaries()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(solutionRoot, "REVIEW_BASELINE.md"));

        source.Should().Contain("AiCopilotDbContext");
        source.Should().Contain("AuditDbContext");
        source.Should().Contain("DataAnalysisDbContext");
        source.Should().Contain("OutboxDbContext");
        source.Should().Contain("auditLogWriter.SaveChangesAsync");
        source.Should().Contain("Audit writer decision tree");
        source.Should().Contain("FOR UPDATE SKIP LOCKED");
        source.Should().Contain("service restart");
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
            "Infrastructure",
            "AICopilot.Dapper",
            "DapperDatabaseConnector.cs"));

        source.Should().Contain("sqlGuardrail.Validate");
        source.Should().Contain("BeginTransactionAsync");
        source.Should().Contain("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY");
        source.Should().Contain("SET TRANSACTION READ ONLY");
        source.Should().Contain("SET SESSION CHARACTERISTICS AS TRANSACTION READ WRITE");
        source.Should().Contain("ExecuteReaderAsync");
        source.Should().Contain("normalizedRows.Count >= maxRows");
        source.Should().NotContain("QueryAsync(command)).ToList");
        source.Should().NotContain("rawRows.Take");
    }

    [Fact]
    public void DataAnalysisAuditSummaries_ShouldStayReadable()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.DataAnalysisService",
            "BusinessDatabases",
            "BusinessDatabaseManagement.cs"));

        source.Should().Contain("Created business database:");
        source.Should().Contain("Deleted business database:");
        source.Should().NotContain("鍒");
        source.Should().NotContain("锛");
        source.Should().NotContain("擄");
    }

    [Fact]
    public void DataAnalysisServices_ShouldNotBypassGuardedDatabaseConnector()
    {
        var solutionRoot = FindSolutionRoot();
        var serviceRoot = Path.Combine(solutionRoot, "src", "Services", "AICopilot.DataAnalysisService");
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
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
            "AICopilot.Infrastructure",
            "Mcp",
            "McpServerBootstrap.cs"));

        source.Should().Contain("ParseCommandArguments");
        source.Should().Contain("StringBuilder");
        source.Should().Contain("ConnectionTimeout = SseConnectionTimeout");
        source.Should().Contain("TransportMode = HttpTransportMode.Sse");
        source.Should().NotContain("Split(' ', StringSplitOptions.RemoveEmptyEntries)");
    }

    [Fact]
    public void RagIndexing_ShouldAllowRecoveryAndDeleteOldVectorsBeforeUpsert()
    {
        var solutionRoot = FindSolutionRoot();
        var indexingSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.RagService",
            "Documents",
            "DocumentIndexingService.cs"));
        var indexingOptionsSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Services",
            "AICopilot.RagService",
            "Documents",
            "RagIndexingOptions.cs"));
        var writerSource = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "Infrastructure",
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

        var server = new McpServerInfo(
            " server ",
            " description ",
            McpTransportType.Stdio,
            " dotnet ",
            " server.dll ",
            ChatExposureMode.Advisory,
            [" Echo ", "echo", " "]);

        server.Name.Should().Be("server");
        server.Description.Should().Be("description");
        server.Command.Should().Be("dotnet");
        server.Arguments.Should().Be("server.dll");
        server.AllowedToolNames.Should().Equal("Echo");
    }

    [Fact]
    public void KnowledgeBaseDocumentAndChunks_ShouldRejectInvalidInput()
    {
        var embeddingModelId = Guid.NewGuid();

        var emptyName = () => new KnowledgeBase(" ", "description", embeddingModelId);
        emptyName.Should().Throw<ArgumentException>();

        var emptyEmbeddingModelId = () => new KnowledgeBase("kb", "description", Guid.Empty);
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

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the test output directory.");
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
}
