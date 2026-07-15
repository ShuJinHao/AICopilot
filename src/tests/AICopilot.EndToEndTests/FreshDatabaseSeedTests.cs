using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.Core.AiGateway.Aggregates.Skills;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.EntityFrameworkCore;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.EndToEndTests;

public sealed class FreshDatabaseSeedTests
{
    private const string AiCopilotFinalMigrationId = "20260429010506_DetachIdentityFromAiCopilotDbContext";
    private const string AiGatewayInitialMigrationId = "20260515030952_AiGatewayFreshBaseline";
    private const string RagFinalMigrationId = "20260515030526_AddKnowledgeBaseAccessScope";
    private const string DataAnalysisInitialMigrationId = "20260427000300_InitialDataAnalysisSchema";
    private const string McpServerInitialMigrationId = "20260427000100_InitialMcpServerSchema";
    private const string IdentityStoreBaselineMigrationId = "20260429021832_IdentityStoreMigrationBaseline";
    private const string PrivateMiniMaxProvider = "MiniMax Private";
    private const string PrivateMiniMaxModelName = "MiniMax-M3-AWQ-INT4";
    private const string PrivateMiniMaxBaseUrl = "http://model.internal.example:40034/v1";
    private const int PrivateMiniMaxContextWindowTokens = 65536;
    private const string PrivateMiniMaxRoutingConfigurationName = "MiniMax private routing model";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task FreshDb_ShouldSeedBootstrapAdminRolesPermissions_AndNoBusinessData()
    {
        await using var fixture = new CoreAICopilotAppFixture();
        await fixture.InitializeAsync();
        fixture.ClearAuthToken();

        using var response = await fixture.HttpClient.GetAsync("/api/identity/initialization-status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = (await response.Content.ReadFromJsonAsync<InitializationStatusDto>(JsonOptions))!;

        status.HasAdminRole.Should().BeTrue();
        status.HasUserRole.Should().BeTrue();
        status.BootstrapAdminConfigured.Should().BeTrue();
        status.HasEnabledAdminUser.Should().BeTrue();
        status.IsInitialized.Should().BeTrue();

        await using var dbContext = await CreateDbContextAsync(fixture);
        await using var identityDbContext = await CreateIdentityStoreDbContextAsync(fixture);

        var roles = await identityDbContext.Roles
            .OrderBy(role => role.Name)
            .ToListAsync();

        roles.Select(role => role.Name!).Should().Equal("Admin", "User");
        (await identityDbContext.Users.CountAsync()).Should().Be(1);

        await using var aiGatewayDbContext = await CreateAiGatewayDbContextAsync(fixture);
        (await aiGatewayDbContext.ChatRuntimeSettings.AnyAsync(settings => settings.Id == ChatRuntimeSettings.GlobalId))
            .Should().BeTrue();

        var seedModel = await aiGatewayDbContext.LanguageModels.SingleAsync();
        seedModel.Provider.Should().Be(PrivateMiniMaxProvider);
        seedModel.ProtocolType.Should().Be(LanguageModelProtocolTypes.OpenAICompatible);
        seedModel.Name.Should().Be(PrivateMiniMaxModelName);
        seedModel.BaseUrl.Should().Be(PrivateMiniMaxBaseUrl);
        seedModel.ApiKey.Should().BeNull("fresh databases must not seed a default model API key");
        seedModel.IsEnabled.Should().BeFalse("fresh databases must not enable a placeholder model before an administrator configures a real endpoint and key");
        seedModel.Usage.Should().Be(LanguageModelUsage.Chat | LanguageModelUsage.Routing | LanguageModelUsage.Planner);
        seedModel.Parameters.MaxTokens.Should().Be(PrivateMiniMaxContextWindowTokens);
        seedModel.Parameters.MaxOutputTokens.Should().Be(4096);
        seedModel.Parameters.Temperature.Should().BeApproximately(0.2f, 0.0001f);

        var templates = await aiGatewayDbContext.ConversationTemplates.ToListAsync();
        templates.Should().HaveCount(BuiltInConversationTemplates.All.Count);
        templates.Should().OnlyContain(template => template.IsBuiltIn && template.IsEnabled);
        templates.Select(template => template.Code)
            .Should()
            .BeEquivalentTo(BuiltInConversationTemplates.All.Select(template => template.Code));
        templates
            .Where(template => template.Code is "IntentRoutingAgent" or "agent_planner" or "agent_executor")
            .Should()
            .OnlyContain(template => template.ModelId == seedModel.Id);
        templates.Select(template => template.SystemPrompt)
            .Should()
            .NotContain(prompt => prompt.Contains("朝小夕", StringComparison.OrdinalIgnoreCase) ||
                                  prompt.Contains("朝夕", StringComparison.OrdinalIgnoreCase));

        var approvalPolicy = await aiGatewayDbContext.ApprovalPolicies.SingleAsync();
        approvalPolicy.Name.Should().Be("Default Agent Artifact Approval");
        approvalPolicy.TargetType.Should().Be(ApprovalTargetType.Plugin);
        approvalPolicy.TargetName.Should().Be("AgentTaskRuntime");
        approvalPolicy.IsEnabled.Should().BeTrue();
        approvalPolicy.RequiresOnsiteAttestation.Should().BeFalse();
        approvalPolicy.ToolNames.Should().Contain(["generate_pdf", "generate_pptx", "generate_xlsx", "finalize_artifacts"]);

        var tools = await aiGatewayDbContext.ToolRegistrations
            .OrderBy(tool => tool.ToolCode)
            .ToListAsync();
        tools.Should().HaveCount(BuiltInToolRegistrations.AgentRuntimeTools.Count);
        tools.Select(tool => tool.ToolCode)
            .Should()
            .BeEquivalentTo(BuiltInToolRegistrations.AgentRuntimeTools.Select(tool => tool.ToolCode));
        tools.Select(tool => tool.ToolCode)
            .Should()
            .NotIntersectWith(BuiltInToolRegistrations.ObsoleteAgentRuntimeToolCodes);
        tools.Should().OnlyContain(tool => tool.TargetType == ToolRegistrationTargetType.AgentRuntime);
        var toolTargets = BuiltInToolRegistrations.AgentRuntimeTools
            .ToDictionary(tool => tool.ToolCode, tool => tool.TargetName, StringComparer.OrdinalIgnoreCase);
        tools.Should().OnlyContain(tool => tool.TargetName == toolTargets[tool.ToolCode]);

        var cloudReadonlyTool = tools.Single(tool => tool.ToolCode == "query_cloud_data_readonly");
        cloudReadonlyTool.ProviderType.Should().Be(ToolProviderType.CloudReadonly);
        cloudReadonlyTool.IsEnabled.Should().BeFalse();
        cloudReadonlyTool.RequiresApproval.Should().BeTrue();
        cloudReadonlyTool.RiskLevel.Should().Be(AiToolRiskLevel.High);

        tools.Where(tool => tool.ToolCode is "generate_pdf" or "generate_pptx" or "generate_xlsx" or "finalize_artifacts")
            .Should()
            .OnlyContain(tool => tool.RequiresApproval &&
                                 (tool.RiskLevel == AiToolRiskLevel.RequiresApproval ||
                                  tool.RiskLevel == AiToolRiskLevel.High));
        tools.Should().NotContain(tool =>
            tool.ToolCode.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
            tool.ToolCode.Contains("cloud_write", StringComparison.OrdinalIgnoreCase) ||
            tool.Description.Contains("写入 Cloud", StringComparison.OrdinalIgnoreCase));

        var registeredToolCodes = tools
            .Select(tool => tool.ToolCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingSeedToolCodes = BuiltInSkillDefinitions.All
            .SelectMany(skill => skill.AllowedToolCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(toolCode => !registeredToolCodes.Contains(toolCode))
            .ToArray();
        missingSeedToolCodes.Should().BeEmpty("built-in skills may only narrow already governed tool registrations");

        var skills = await aiGatewayDbContext.SkillDefinitions
            .OrderBy(skill => skill.SkillCode)
            .ToListAsync();
        skills.Should().HaveCount(BuiltInSkillDefinitions.All.Count);
        skills.Should().OnlyContain(skill => skill.IsBuiltIn);
        skills.SelectMany(skill => skill.AllowedToolCodes)
            .Should()
            .OnlyContain(toolCode => registeredToolCodes.Contains(toolCode));
        skills.SelectMany(skill => skill.AllowedToolCodes)
            .Should()
            .NotIntersectWith(BuiltInToolRegistrations.ObsoleteAgentRuntimeToolCodes);

        var toolByCode = tools.ToDictionary(tool => tool.ToolCode, StringComparer.OrdinalIgnoreCase);
        var cloudReadonlySkill = skills.Single(skill => skill.SkillCode == "cloud_readonly");
        cloudReadonlySkill.AllowedToolCodes
            .Where(toolCode => toolByCode[toolCode].ProviderType == ToolProviderType.CloudReadonly)
            .Should()
            .BeEquivalentTo("query_cloud_data_readonly");
        cloudReadonlySkill.AllowedToolCodes
            .Should()
            .OnlyContain(toolCode =>
                toolByCode[toolCode].ProviderType != ToolProviderType.CloudReadonly ||
                !toolCode.Contains("write", StringComparison.OrdinalIgnoreCase),
                "Cloud write tools must never enter a built-in skill allowlist");

        var activeRoutingConfiguration = await aiGatewayDbContext.RoutingModelConfigurations.SingleAsync(configuration => configuration.IsActive);
        activeRoutingConfiguration.Name.Should().Be(PrivateMiniMaxRoutingConfigurationName);
        activeRoutingConfiguration.ModelId.Should().Be(seedModel.Id);

        await using var ragDbContext = await CreateRagDbContextAsync(fixture);
        (await ragDbContext.EmbeddingModels.CountAsync()).Should().Be(0);
        (await ragDbContext.KnowledgeBases.CountAsync()).Should().Be(0);

        await using var dataAnalysisDbContext = await CreateDataAnalysisDbContextAsync(fixture);
        (await dataAnalysisDbContext.BusinessDatabases.CountAsync()).Should().Be(0);

        var adminRole = roles.Single(role => role.Name == "Admin");
        var userRole = roles.Single(role => role.Name == "User");

        var adminPermissions = await identityDbContext.RoleClaims
            .Where(claim => claim.RoleId == adminRole.Id && claim.ClaimType == "permission")
            .Select(claim => claim.ClaimValue!)
            .OrderBy(value => value)
            .ToListAsync();

        var userPermissions = await identityDbContext.RoleClaims
            .Where(claim => claim.RoleId == userRole.Id && claim.ClaimType == "permission")
            .Select(claim => claim.ClaimValue!)
            .OrderBy(value => value)
            .ToListAsync();

        adminPermissions.Should().Contain("Identity.CreateRole");
        adminPermissions.Should().Contain("Identity.DisableUser");
        adminPermissions.Should().Contain("Identity.EnableUser");
        adminPermissions.Should().Contain("Identity.ResetUserPassword");
        adminPermissions.Should().Contain("Identity.DeleteRole");
        adminPermissions.Should().Contain("Identity.GetListAuditLogs");
        adminPermissions.Should().Contain("DataAnalysis.GetListBusinessDatabases");
        adminPermissions.Should().Contain("DataSource.TextToSql");
        adminPermissions.Should().Contain("DataSource.QueryGovernedSql");
        adminPermissions.Should().Contain("Rag.UploadDocument");
        adminPermissions.Should().Contain("AiGateway.ToolRegistry.Read");
        adminPermissions.Should().Contain("AiGateway.ToolRegistry.Manage");
        adminPermissions.Should().Contain("AiGateway.ApproveAgentToolCall");
        adminPermissions.Should().Contain("AiGateway.ApproveFinalOutput");

        userPermissions.Should().BeEquivalentTo(
            "AiGateway.CreateSession",
            "AiGateway.GetSession",
            "AiGateway.GetListSessions",
            "AiGateway.RenameSession",
            "AiGateway.DeleteSession",
            "AiGateway.GetAgentTask",
            "AiGateway.PlanAgentTask",
            "AiGateway.ApproveAgentTaskPlan",
            "AiGateway.RunAgentTask",
            "AiGateway.CancelAgentTask",
            "AiGateway.Upload",
            "AiGateway.GetUpload",
            "AiGateway.GetWorkspace",
            "AiGateway.DownloadArtifact",
            "AiGateway.EditArtifact",
            "AiGateway.SubmitFinalReview",
            "AiGateway.Chat",
            "AiGateway.ToolRegistry.Read",
            "Rag.GetKnowledgeBase",
            "Rag.GetListKnowledgeBases",
            "Rag.GetListDocuments",
            "Rag.UploadDocument",
            "Rag.DeleteDocument",
            "Rag.SearchKnowledgeBase");
        userPermissions.Should().NotContain("AiGateway.ApproveAgentToolCall");
        userPermissions.Should().NotContain("AiGateway.ApproveFinalOutput");
        userPermissions.Should().NotContain("AiGateway.FinalizeWorkspace");
        userPermissions.Should().NotContain("DataSource.TextToSql");
        userPermissions.Should().NotContain("DataSource.QueryGovernedSql");
        userPermissions.Should().NotContain("Rag.GetListEmbeddingModels");
        userPermissions.Should().NotContain("Rag.CreateKnowledgeBase");
        userPermissions.Should().NotContain("Rag.UpdateKnowledgeBase");
        userPermissions.Should().NotContain("Rag.DeleteKnowledgeBase");
        userPermissions.Should().NotContain("Rag.UpdateDocumentGovernance");

        await using var mcpDbContext = await CreateMcpDbContextAsync(fixture);
        (await mcpDbContext.McpServerInfos.CountAsync()).Should().Be(0);

        await AssertSplitModuleTablesAsync(dbContext);
        await AssertPerContextMigrationHistoryAsync(dbContext);
    }

    private static async Task<AiCopilotDbContext> CreateDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiCopilotDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.AiCopilot)
            .Options;

        return new AiCopilotDbContext(options);
    }

    private static async Task<IdentityStoreDbContext> CreateIdentityStoreDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IdentityStoreDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.IdentityStore)
            .Options;

        return new IdentityStoreDbContext(options);
    }

    private static async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.AiGateway)
            .Options;

        return new AiGatewayDbContext(options);
    }

    private static async Task<McpServerDbContext> CreateMcpDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<McpServerDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.McpServer)
            .Options;

        return new McpServerDbContext(options);
    }

    private static async Task<DataAnalysisDbContext> CreateDataAnalysisDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.DataAnalysis)
            .Options;

        return new DataAnalysisDbContext(options);
    }

    private static async Task<RagDbContext> CreateRagDbContextAsync(AICopilotAppFixture fixture)
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.Rag)
            .Options;

        return new RagDbContext(options);
    }

    private static async Task AssertSplitModuleTablesAsync(AiCopilotDbContext dbContext)
    {
        var expectedModuleTables = new[]
        {
            "identity.\"AspNetRoles\"",
            "identity.\"AspNetUsers\"",
            "identity.\"AspNetRoleClaims\"",
            "identity.\"AspNetUserClaims\"",
            "identity.\"AspNetUserLogins\"",
            "identity.\"AspNetUserRoles\"",
            "identity.\"AspNetUserTokens\"",
            "aigateway.language_models",
            "aigateway.conversation_templates",
            "aigateway.approval_policies",
            "aigateway.tool_registrations",
            "aigateway.tool_execution_records",
            "aigateway.sessions",
            "aigateway.messages",
            "rag.embedding_models",
            "rag.knowledge_bases",
            "rag.documents",
            "rag.document_chunks",
            "dataanalysis.business_databases",
            "mcp.mcp_server_info"
        };

        var forbiddenPublicTables = new[]
        {
            "public.\"AspNetRoles\"",
            "public.\"AspNetUsers\"",
            "public.\"AspNetRoleClaims\"",
            "public.\"AspNetUserClaims\"",
            "public.\"AspNetUserLogins\"",
            "public.\"AspNetUserRoles\"",
            "public.\"AspNetUserTokens\"",
            "public.language_models",
            "public.conversation_templates",
            "public.approval_policies",
            "public.tool_registrations",
            "public.tool_execution_records",
            "public.sessions",
            "public.messages",
            "public.embedding_models",
            "public.knowledge_bases",
            "public.documents",
            "public.document_chunks",
            "public.business_databases",
            "public.mcp_server_info"
        };

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            foreach (var tableName in expectedModuleTables)
            {
                (await ResolveTableAsync(dbContext, tableName)).Should().Be(
                    tableName,
                    $"{tableName} must be owned by its split module schema after fresh database migration");
            }

            foreach (var tableName in forbiddenPublicTables)
            {
                (await ResolveTableAsync(dbContext, tableName)).Should().BeNull(
                    $"{tableName} must not remain after split module migrations move ownership out of public");
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task AssertPerContextMigrationHistoryAsync(AiCopilotDbContext dbContext)
    {
        var expectedHistoryRows = new[]
        {
            (MigrationHistoryTables.AiCopilot, AiCopilotFinalMigrationId),
            (MigrationHistoryTables.IdentityStore, IdentityStoreBaselineMigrationId),
            (MigrationHistoryTables.AiGateway, AiGatewayInitialMigrationId),
            (MigrationHistoryTables.Rag, RagFinalMigrationId),
            (MigrationHistoryTables.DataAnalysis, DataAnalysisInitialMigrationId),
            (MigrationHistoryTables.McpServer, McpServerInitialMigrationId)
        };

        await dbContext.Database.OpenConnectionAsync();
        try
        {
            foreach (var (historyTable, migrationId) in expectedHistoryRows)
            {
                var resolvedHistoryTable = await ResolveTableAsync(dbContext, RegclassName(historyTable));
                resolvedHistoryTable.Should().NotBeNull(
                    $"{historyTable.ContextName} must write migrations to its split history table");

                var count = await CountMigrationHistoryRowsAsync(dbContext, historyTable, migrationId);
                count.Should().Be(1, $"{historyTable.ContextName} must have its expected migration history row");
            }

            (await ResolveTableAsync(dbContext, "public.\"__EFMigrationsHistory\""))
                .Should().BeNull("fresh databases must not create the legacy shared EF migrations history table");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<int> CountMigrationHistoryRowsAsync(
        DbContext dbContext,
        MigrationHistoryTable historyTable,
        string migrationId)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
             SELECT COUNT(*)
             FROM {HistoryTableSql(historyTable)}
             WHERE "MigrationId" = @migration_id
             """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "migration_id";
        parameter.Value = migrationId;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<string?> ResolveTableAsync(DbContext dbContext, string tableName)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass(@table_name)::text";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "table_name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : (string)result;
    }

    private static string HistoryTableSql(MigrationHistoryTable historyTable)
    {
        return $"{QuoteIdentifier(historyTable.Schema)}.{QuoteIdentifier(historyTable.TableName)}";
    }

    private static string RegclassName(MigrationHistoryTable historyTable)
    {
        return $"{historyTable.Schema}.{QuoteIdentifier(historyTable.TableName)}";
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record InitializationStatusDto(
        bool HasAdminRole,
        bool HasUserRole,
        bool HasEnabledAdminUser,
        bool BootstrapAdminConfigured,
        bool IsInitialized);
}
