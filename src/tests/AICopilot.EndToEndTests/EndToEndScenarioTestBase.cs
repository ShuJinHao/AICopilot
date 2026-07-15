using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Security;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.Infrastructure.Mcp;
using AICopilot.McpService;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;


namespace AICopilot.EndToEndTests;

public abstract class EndToEndScenarioTestBase
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    protected static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(90);
    protected static readonly TimeSpan EventuallyInterval = TimeSpan.FromMilliseconds(750);

    protected readonly AICopilotAppFixture _fixture;

    protected EndToEndScenarioTestBase(AICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    protected async Task AuthenticateAsAdminAsync()
    {
        _fixture.ClearAuthToken();

        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = _fixture.BootstrapAdminUserName,
            password = _fixture.BootstrapAdminPassword
        });

        _fixture.SetAuthToken(result.Token);
    }

    protected async Task AuthenticateAsync(string userName, string password)
    {
        _fixture.ClearAuthToken();

        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = userName,
            password
        });

        _fixture.SetAuthToken(result.Token);
    }

    protected async Task<Guid> CreateUserAsync(string userName, string password, string roleName)
    {
        var created = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
        {
            userName,
            password,
            roleName
        });

        return created.UserId;
    }

    protected async Task<Guid> CreateLanguageModelAsync(
        string name,
        string baseUrl,
        string apiKey,
        int contextWindowTokens = 4096,
        int maxOutputTokens = 1024,
        IReadOnlyCollection<string>? usages = null)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl,
            apiKey,
            maxTokens = contextWindowTokens,
            contextWindowTokens,
            maxOutputTokens,
            usages,
            temperature = 0.2
        });

        created.Id.Should().NotBeEmpty();
        return created.Id;
    }

    protected async Task<Guid> CreateActiveRoutingModelAsync(Guid modelId)
    {
        var created = await PostJsonAsync<RoutingModelConfigurationDto>("/api/aigateway/routing-model", new
        {
            name = $"runtime-routing-{Guid.NewGuid():N}",
            modelId,
            isActive = true
        });

        return created.Id;
    }

    protected async Task<Guid> CreateConversationTemplateAsync(string templateName, Guid modelId, string description, string prompt)
    {
        var created = await PostJsonAsync<CreatedConversationTemplateDto>("/api/aigateway/conversation-template", new
        {
            name = templateName,
            description,
            systemPrompt = prompt,
            modelId,
            maxTokens = 512,
            temperature = 0.1
        });

        return created.Id;
    }

    protected async Task<Guid> CreateApprovalPolicyAsync(
        string name,
        ApprovalTargetType targetType,
        string targetName,
        IReadOnlyCollection<string> toolNames,
        bool isEnabled,
        bool requiresOnsiteAttestation = false)
    {
        var created = await PostJsonAsync<CreatedApprovalPolicyDto>("/api/aigateway/approval-policy", new
        {
            name,
            description = "integration-test",
            targetType,
            targetName,
            toolNames,
            isEnabled,
            requiresOnsiteAttestation
        });

        return created.Id;
    }

    protected async Task<Guid> CreateEmbeddingModelAsync(string name, string modelName, string baseUrl, string apiKey)
    {
        var created = await PostJsonAsync<CreatedEmbeddingModelDto>("/api/rag/embedding-model", new
        {
            name,
            provider = "OpenAI",
            baseUrl,
            apiKey,
            modelName,
            dimensions = 4,
            maxTokens = 256,
            isEnabled = true
        });

        return created.Id;
    }

    protected async Task<Guid> CreateKnowledgeBaseAsync(string name, string description, Guid embeddingModelId)
    {
        var created = await PostJsonAsync<CreatedKnowledgeBaseDto>("/api/rag/knowledge-base", new
        {
            name,
            description,
            embeddingModelId
        });

        return created.Id;
    }

    protected async Task<Guid> CreateBusinessDatabaseAsync(
        string name,
        string? connectionString = null,
        string description = "readonly db",
        int provider = 1,
        bool isEnabled = true,
        bool isReadOnly = true,
        bool readOnlyCredentialVerified = false)
    {
        var created = await PostJsonAsync<CreatedBusinessDatabaseDto>("/api/data-analysis/business-database", new
        {
            name,
            description,
            connectionString = connectionString ?? "Host=localhost;Database=test;Username=test;Password=test;",
            provider,
            isEnabled,
            isReadOnly,
            readOnlyCredentialVerified
        });

        return created.Id;
    }

    protected async Task<Guid> CreateMcpServerAsync(
        string name,
        bool isEnabled,
        string command,
        string arguments,
        ChatExposureMode chatExposureMode = ChatExposureMode.Disabled,
        IReadOnlyCollection<string>? allowedToolNames = null)
    {
        var created = await PostJsonAsync<CreatedMcpServerDto>("/api/mcp/server", new
        {
            name,
            description = "testing mcp server",
            transportType = 1,
            command,
            arguments,
            chatExposureMode,
            allowedTools = (allowedToolNames ?? Array.Empty<string>())
                .Select(toolName => new { toolName, readOnlyDeclared = false })
                .ToArray(),
            isEnabled
        });

        return created.Id;
    }

    protected async Task<Guid> CreateSessionAsync(Guid templateId)
    {
        var created = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new
        {
            templateId
        });

        return created.Id;
    }

    protected async Task<UploadDocumentDto> UploadDocumentAsync(Guid knowledgeBaseId, string fileName, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(knowledgeBaseId.ToString()), "knowledgeBaseId");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rag/document")
        {
            Content = form
        };

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UploadDocumentDto>(JsonOptions))!;
    }

    protected async Task DeleteBusinessDatabaseIfExistsAsync(string name)
    {
        var businessDatabases = await GetJsonAsync<List<BusinessDatabaseDto>>("/api/data-analysis/business-database/list");
        foreach (var businessDatabase in businessDatabases.Where(item => item.Name == name))
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/data-analysis/business-database",
                new { id = businessDatabase.Id },
                HttpStatusCode.NoContent);
        }
    }

    protected async Task DeleteConversationTemplateIfExistsAsync(string name)
    {
        var templates = await GetJsonAsync<List<ConversationTemplateDto>>("/api/aigateway/conversation-template/list");
        foreach (var template in templates.Where(item => item.Name == name))
        {
            await SendJsonAsync(
                HttpMethod.Delete,
                "/api/aigateway/conversation-template",
                new { id = template.Id },
                HttpStatusCode.NoContent);
        }
    }

    protected async Task<List<ChatChunkDto>> PostChatAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/chat", payload);
    }

    protected async Task<List<ChatChunkDto>> PostApprovalDecisionAsync(object payload)
    {
        return await PostEventStreamAsync("/api/aigateway/approval/decision", payload);
    }

    protected static ProblemChunkDto ReadSingleError(IReadOnlyCollection<ChatChunkDto> events)
    {
        var errorChunk = events.Single(item => item.Type == "Error");
        return JsonSerializer.Deserialize<ProblemChunkDto>(errorChunk.Content, JsonOptions)!;
    }

    protected async Task<List<ChatChunkDto>> PostEventStreamAsync(string uri, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _fixture.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var events = new List<ChatChunkDto>();
        var buffer = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                if (buffer.Length == 0)
                {
                    continue;
                }

                var data = buffer.ToString();
                buffer.Clear();

                if (data == "[DONE]")
                {
                    break;
                }

                events.Add(JsonSerializer.Deserialize<ChatChunkDto>(data, JsonOptions)!);
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                buffer.Append(line["data:".Length..].TrimStart());
            }
        }

        return events;
    }

    protected async Task AssertPolicyChatAsync(
        Guid sessionId,
        string message,
        string expectedIntent,
        params string[] expectedTextFragments)
    {
        var events = await PostChatAsync(new
        {
            sessionId,
            message
        });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("业务结论");
        text.Should().Contain("适用条件");
        text.Should().Contain("禁止放宽的边界");

        foreach (var expectedTextFragment in expectedTextFragments)
        {
            text.Should().Contain(expectedTextFragment);
        }

        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("capacity_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("production_data_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    protected async Task AssertSemanticChatAsync(
        Guid sessionId,
        string message,
        string expectedIntent,
        params string[] expectedTextFragments)
    {
        var events = await PostChatAsync(new
        {
            sessionId,
            message
        });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("结论：");
        text.Should().Contain("正式 Cloud AiRead 数据源不可用");
        text.Should().Contain("未回退 Direct DB、Text-to-SQL 或 Simulation");
        text.Should().NotContain("关键指标：");
        text.Should().NotContain("关键记录：");

        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_log_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_log_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("capacity_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("production_data_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    protected async Task AssertCloudOnlySourceUnavailableAsync(
        Guid sessionId,
        string message,
        string expectedIntent)
    {
        var events = await PostChatAsync(new { sessionId, message });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item =>
            item.Type == "Intent" &&
            item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(
            events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().Contain("正式 Cloud AiRead 数据源不可用");
        text.Should().Contain("未回退 Direct DB、Text-to-SQL 或 Simulation");
        text.Should().NotContain("Running");
        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    protected async Task AssertRecipeDataReadBlockedAsync(
        Guid sessionId,
        string message,
        string expectedIntent)
    {
        var events = await PostChatAsync(new
        {
            sessionId,
            message
        });

        events.Should().NotContain(item => item.Type == "Error");
        events.Should().Contain(item => item.Type == "Intent" && item.Content.Contains(expectedIntent, StringComparison.OrdinalIgnoreCase));

        var text = string.Concat(events.Where(item => item.Type == "Text").Select(item => item.Content));
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("当前 AI 不读取云端配方主数据或配方版本数据");
        text.Should().Contain("不能查询具体配方");
        text.Should().NotContain("Recipe-Cut-01");
        text.Should().NotContain("V2.0");
        text.Should().NotContain("V1.0");
        text.Should().NotContain("DEV-001");
        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("vw_recipe_readonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    protected async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    protected async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"POST '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    protected async Task SendJsonAsync(HttpMethod method, string uri, object payload, HttpStatusCode? expectedStatusCode = null)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _fixture.HttpClient.SendAsync(request);
        if (expectedStatusCode.HasValue)
        {
            response.StatusCode.Should().Be(expectedStatusCode.Value);
            return;
        }

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new Xunit.Sdk.XunitException(
            $"Request to '{uri}' failed with status {(int)response.StatusCode} ({response.StatusCode}). Response body: {body}");
    }

    protected async Task<AiGatewayDbContext> CreateAiGatewayDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<AiGatewayDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AiGatewayDbContext(options);
    }

    protected async Task<McpServerDbContext> CreateMcpDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<McpServerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new McpServerDbContext(options);
    }

    protected async Task<DataAnalysisDbContext> CreateDataAnalysisDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<DataAnalysisDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DataAnalysisDbContext(options);
    }

    protected async Task<SemanticBusinessDatabaseContext> ProvisionSemanticBusinessDatabaseAsync()
    {
        var businessConnectionString = await _fixture.GetConnectionStringAsync("cloud-device-semantic-sim");

        await using var businessConnection = new NpgsqlConnection(businessConnectionString);
        await businessConnection.OpenAsync();

        await using var validationCommand = businessConnection.CreateCommand();
        validationCommand.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM device_master_cloud_sim_view),
                (SELECT COUNT(*) FROM device_log_cloud_sim_view),
                (SELECT COUNT(*) FROM recipe_cloud_sim_view),
                (SELECT COUNT(*) FROM capacity_cloud_sim_view),
                (SELECT COUNT(*) FROM production_data_cloud_sim_view);
            """;

        await using var reader = await validationCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync()
            || reader.GetInt32(0) <= 0
            || reader.GetInt32(1) <= 0
            || reader.GetInt32(2) <= 0
            || reader.GetInt32(3) <= 0
            || reader.GetInt32(4) <= 0)
        {
            throw new InvalidOperationException("Cloud semantic simulation source is not ready.");
        }

        return new SemanticBusinessDatabaseContext("cloud-device-semantic-sim", businessConnectionString);
    }

    protected static IHost BuildMcpVerificationHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] = connectionString
        });

        builder.AddEfCore();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<AgentPluginLoader>();
        builder.Services.AddSingleton<IApprovalRequirementReadService>(new TestApprovalRequirementReadService());
        builder.Services.AddScoped<IMcpServerBootstrap, TestMcpServerBootstrap>();

        return builder.Build();
    }

    protected string BuildFakeAiBaseUrl()
    {
        return new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/');
    }

    protected static string GetTestingMcpExecutablePath()
    {
        var assemblyLocation = typeof(TestingMcpServerMarker).Assembly.Location;
        var executablePath = Path.ChangeExtension(assemblyLocation, ".exe");

        return File.Exists(executablePath)
            ? executablePath
            : assemblyLocation;
    }

    protected static string GetDiagnosticChecklistToolName()
    {
        var plugin = new DiagnosticAdvisorPlugin();
        var tool = plugin.GetTools()
            ?.First(function => function.Name.Contains("DiagnosticChecklist", StringComparison.OrdinalIgnoreCase));

        return tool?.Name ?? "GenerateDiagnosticChecklist";
    }

    protected static async Task<T> EventuallyAsync<T>(Func<Task<T>> action, Func<T, bool> predicate)
    {
        var deadline = DateTime.UtcNow + EventuallyTimeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await action();
                if (predicate(result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(EventuallyInterval);
        }

        if (lastException != null)
        {
            throw lastException;
        }

        throw new TimeoutException("EventuallyAsync timed out before the condition was satisfied.");
    }

    protected sealed record InitializationStatusDto(
        bool HasAdminRole,
        bool HasUserRole,
        bool BootstrapAdminConfigured,
        bool HasEnabledAdminUser,
        bool IsInitialized);

    protected sealed record LoginUserDto(string UserName, string Token);

    protected sealed record CreatedUserDto(Guid UserId, string UserName, string RoleName);

    protected sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    protected sealed record LanguageModelDto(
        Guid Id,
        string Provider,
        string ProtocolType,
        string Name,
        string BaseUrl,
        int MaxTokens,
        int ContextWindowTokens,
        int MaxOutputTokens,
        double Temperature,
        bool IsEnabled,
        IReadOnlyList<string> Usages,
        bool HasApiKey,
        string? ApiKeyPreview,
        string ConnectivityStatus,
        DateTimeOffset? ConnectivityCheckedAt,
        string? ConnectivityError);

    protected sealed record LanguageModelTestResultDto(
        bool Success,
        string Status,
        string Message,
        string? Error,
        long ElapsedMilliseconds,
        DateTimeOffset CheckedAt);

    protected sealed record SelectableChatModelDto(
        Guid Id,
        string Provider,
        string ProtocolType,
        string Name,
        int ContextWindowTokens,
        int MaxOutputTokens);

    protected sealed record RoutingModelConfigurationDto(
        Guid Id,
        string Name,
        Guid ModelId,
        string ModelName,
        string ModelProvider,
        bool IsActive);

    protected sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    protected sealed record ConversationTemplateDto(
        Guid Id,
        string Name,
        string Description,
        string SystemPrompt,
        Guid ModelId,
        int? MaxTokens,
        double? Temperature,
        bool IsEnabled);

    protected sealed record CreatedApprovalPolicyDto(Guid Id, string Name);

    protected sealed record ApprovalPolicyDto(
        Guid Id,
        string Name,
        string? Description,
        ApprovalTargetType TargetType,
        string TargetName,
        IReadOnlyCollection<string> ToolNames,
        bool IsEnabled,
        bool RequiresOnsiteAttestation);

    protected sealed record CreatedEmbeddingModelDto(Guid Id, string Name);

    protected sealed record EmbeddingModelDto(
        Guid Id,
        string Name,
        string Provider,
        string BaseUrl,
        string ModelName,
        int Dimensions,
        int MaxTokens,
        bool IsEnabled,
        bool HasApiKey,
        string? ApiKeyPreview);

    protected sealed record CreatedKnowledgeBaseDto(Guid Id, string Name);

    protected sealed record KnowledgeBaseDto(
        Guid Id,
        string Name,
        string Description,
        Guid EmbeddingModelId,
        int DocumentCount);

    protected sealed record CreatedBusinessDatabaseDto(Guid Id, string Name);

    protected sealed record BusinessDatabaseDto(
        Guid Id,
        string Name,
        string Description,
        int Provider,
        bool IsEnabled,
        bool IsReadOnly,
        DateTime CreatedAt,
        bool HasConnectionString,
        string? ConnectionStringMasked);

    protected sealed record CreatedMcpServerDto(Guid Id, string Name);

    protected sealed record McpServerDto(
        Guid Id,
        string Name,
        string Description,
        int TransportType,
        string? Command,
        bool HasArguments,
        string? ArgumentsMasked,
        ChatExposureMode ChatExposureMode,
        IReadOnlyCollection<McpAllowedToolDto> AllowedTools,
        bool IsEnabled);

    protected sealed record McpAllowedToolDto(
        string ToolName,
        int? ExternalSystemType,
        int? CapabilityKind,
        int? RiskLevel,
        bool ReadOnlyDeclared);

    protected sealed record CreatedSessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

    protected sealed record SessionDto(
        Guid Id,
        string Title,
        DateTimeOffset? OnsiteConfirmedAt,
        string? OnsiteConfirmedBy,
        DateTimeOffset? OnsiteConfirmationExpiresAt);

    protected sealed record PendingApprovalDto(
        string CallId,
        string Name,
        string? RuntimeName,
        string? TargetType,
        string? TargetName,
        string? ToolName,
        IReadOnlyDictionary<string, object?> Args,
        bool RequiresOnsiteAttestation,
        DateTimeOffset? AttestationExpiresAt);

    protected sealed record AuditLogListDto(
        IReadOnlyCollection<AuditLogSummaryDto> Items,
        int Page,
        int PageSize,
        int TotalCount);

    protected sealed record AuditLogSummaryDto(
        Guid Id,
        string ActionGroup,
        string ActionCode,
        string TargetType,
        string? TargetId,
        string? TargetName,
        string? OperatorUserName,
        string? OperatorRoleName,
        string Result,
        string Summary,
        IReadOnlyCollection<string> ChangedFields,
        DateTime CreatedAt);

    protected sealed record ChatHistoryMessageDto(
        int MessageId,
        int Sequence,
        Guid SessionId,
        string Role,
        string Content,
        DateTime CreatedAt,
        IReadOnlyCollection<ChatChunkDto> RenderChunks,
        Guid? FinalModelId,
        string? FinalModelName,
        Guid? RoutingModelId,
        string? RoutingModelName,
        int? ContextWindowTokens,
        int? MaxOutputTokens);

    protected sealed record ChatHistoryMessagePageDto(
        IReadOnlyList<ChatHistoryMessageDto> Items,
        int? BeforeSequence,
        int? AfterSequence,
        bool HasMore,
        bool HasMoreBefore,
        bool HasMoreAfter);

    protected sealed record ChatModelMetadataDto(
        Guid? FinalModelId,
        string? FinalModelName,
        Guid? RoutingModelId,
        string? RoutingModelName,
        int? ContextWindowTokens,
        int? MaxOutputTokens);

    protected sealed record SemanticSourceStatusDto(
        string Target,
        string? DatabaseName,
        string? SourceName,
        string? EffectiveSourceName,
        bool IsEnabled,
        bool IsReadOnly,
        bool SourceExists,
        bool ProviderMatched,
        IReadOnlyCollection<string> MissingRequiredFields,
        string Status);

    protected sealed record UploadDocumentDto(int Id, string Status);

    protected sealed record KnowledgeDocumentDto(
        int Id,
        Guid KnowledgeBaseId,
        string Name,
        string Extension,
        DocumentStatus Status,
        int ChunkCount,
        string? ErrorMessage,
        DateTime CreatedAt,
        DateTime? ProcessedAt);

    protected sealed record SearchKnowledgeBaseResult(string Text, double Score, int DocumentId, string? DocumentName);

    protected sealed record ChatChunkDto(string Source, string Type, string Content);

    protected sealed record ProblemChunkDto(string? Code, string? Detail, string? UserFacingMessage);

    protected sealed record SemanticBusinessDatabaseContext(string DatabaseName, string ConnectionString);
}
