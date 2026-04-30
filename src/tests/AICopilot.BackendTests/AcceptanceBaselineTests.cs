using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AICopilot.DataAnalysisService.Semantics;
using FluentAssertions;
using Npgsql;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase38Acceptance")]
[Trait("Runtime", "DockerRequired")]
public sealed class AcceptanceBaselineTests(AICopilotAppFixture fixture) : IClassFixture<AICopilotAppFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture = fixture;

    [Fact]
    public async Task ChineseStructuredAcceptanceChat_ShouldCoverAllBusinessDomains()
    {
        await AuthenticateAsAdminAsync();

        var semanticDatabase = await ProvisionSemanticBusinessDatabaseAsync();
        Guid businessDatabaseId = Guid.Empty;
        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            await DeleteBusinessDatabaseIfExistsAsync(ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName);

            businessDatabaseId = await CreateBusinessDatabaseAsync(
                ConfiguredSemanticPhysicalMappingProvider.DefaultDatabaseName,
                semanticDatabase.ConnectionString,
                "acceptance semantic readonly business database");

            languageModelId = await CreateLanguageModelAsync(
                $"acceptance-structured-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-acceptance-structured");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"AcceptanceStructuredAgent-{Guid.NewGuid():N}",
                languageModelId,
                "acceptance assistant",
                "You are a structured business acceptance assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var cases = new (string Message, string Intent, string[] ExpectedFragments)[]
            {
                ("列出 LINE-A 产线的设备", "Analysis.Device.List", ["DEV-001", "Cutter A", "LINE-A"]),
                ("查看设备 DEV-001 的详情", "Analysis.Device.Detail", ["DEV-001", "Cutter A", "Running"]),
                ("设备 DEV-001 现在是什么状态？", "Analysis.Device.Status", ["DEV-001", "Running"]),
                ("查看设备 DEV-001 最新日志", "Analysis.DeviceLog.Latest", ["DEV-001", "Temperature high", "Warn"]),
                ("查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-20T23:59:59Z 的日志", "Analysis.DeviceLog.Range", ["DEV-001", "Motor overload", "Start completed"]),
                ("查看设备 DEV-001 的错误日志", "Analysis.DeviceLog.ByLevel", ["DEV-001", "Error", "Motor overload"]),
                ("列出设备 DEV-001 的配方", "Analysis.Recipe.List", ["Recipe-Cut-01", "V2.0", "DEV-001"]),
                ("查看配方 Recipe-Cut-01 详情", "Analysis.Recipe.Detail", ["Recipe-Cut-01", "V2.0", "Cutting"]),
                ("查看配方 Recipe-Cut-01 的版本历史", "Analysis.Recipe.VersionHistory", ["Recipe-Cut-01", "V2.0", "V1.0"]),
                ("查看设备 DEV-001 在 2026-04-20T00:00:00Z 到 2026-04-21T23:59:59Z 的产能", "Analysis.Capacity.Range", ["DEV-001", "126", "123"]),
                ("查看设备 DEV-001 的产能", "Analysis.Capacity.ByDevice", ["DEV-001", "Cutting", "126"]),
                ("查看 Cutting 工序的产能", "Analysis.Capacity.ByProcess", ["Cutting", "DEV-001", "126"]),
                ("查看设备 DEV-001 最新生产记录", "Analysis.ProductionData.Latest", ["DEV-001", "CELL-0002", "Fail"]),
                ("查看 DEV-001 在 2026-04-21T00:00:00Z 到 2026-04-21T23:59:59Z 的生产记录", "Analysis.ProductionData.Range", ["DEV-001", "CELL-0001", "CELL-0002"]),
                ("查看设备 DEV-001 的生产记录", "Analysis.ProductionData.ByDevice", ["DEV-001", "CELL-0001", "Station-A"])
            };

            foreach (var testCase in cases)
            {
                await AssertSemanticChatAsync(sessionId, testCase.Message, testCase.Intent, testCase.ExpectedFragments);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }

            if (businessDatabaseId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/data-analysis/business-database", new { id = businessDatabaseId }, HttpStatusCode.NoContent);
            }
        }
    }

    [Fact]
    public async Task ChinesePolicyAcceptanceChat_ShouldCoverAllPolicyTopics()
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"acceptance-policy-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-acceptance-policy");

            generalTemplateId = await CreateConversationTemplateAsync(
                $"AcceptancePolicyAgent-{Guid.NewGuid():N}",
                languageModelId,
                "acceptance policy assistant",
                "You are a business policy acceptance assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "You must choose the best intent from the list and return a JSON array. {{$IntentList}}");

            sessionId = await CreateSessionAsync(generalTemplateId);

            var cases = new (string Message, string Intent, string[] ExpectedFragments)[]
            {
                ("员工修改机台参数需要什么权限？", "Policy.EmployeeAuthorization", ["业务结论", "双重", "禁止放宽"]),
                ("没有设备分配的操作员可以修改配方参数吗？", "Policy.EmployeeAuthorization", ["设备分配", "功能权限", "禁止放宽"]),
                ("谁可以注册新设备？", "Policy.DeviceRegistration", ["管理员", "业务结论", "禁止放宽"]),
                ("普通用户能注册新设备吗？", "Policy.DeviceRegistration", ["管理员", "不能", "禁止放宽"]),
                ("设备删除前要检查什么？", "Policy.DeviceLifecycle", ["历史依赖", "配方", "禁止放宽"]),
                ("删除设备前要检查什么？设备能硬删除吗？", "Policy.DeviceLifecycle", ["硬删除", "历史依赖", "禁止放宽"]),
                ("ClientCode 和 DeviceId 是什么关系？", "Policy.BootstrapIdentity", ["ClientCode", "DeviceId", "bootstrap"]),
                ("客户端可以跳过 bootstrap 直接用设备名称上传生产数据吗？", "Policy.BootstrapIdentity", ["不能", "DeviceId", "bootstrap"]),
                ("配方修改是覆盖还是新建版本？", "Policy.RecipeVersioning", ["版本化", "V1.0", "禁止放宽"]),
                ("当前生效配方修改后会覆盖旧版本吗？", "Policy.RecipeVersioning", ["版本化", "归档", "禁止放宽"])
            };

            foreach (var testCase in cases)
            {
                await AssertPolicyChatAsync(sessionId, testCase.Message, testCase.Intent, testCase.ExpectedFragments);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = intentRoutingTemplateId }, HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = generalTemplateId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }
        }
    }

    private async Task AuthenticateAsAdminAsync()
    {
        _fixture.ClearAuthToken();

        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = _fixture.BootstrapAdminUserName,
            password = _fixture.BootstrapAdminPassword
        });

        _fixture.SetAuthToken(result.Token);
    }

    private async Task<SemanticBusinessDatabaseContext> ProvisionSemanticBusinessDatabaseAsync()
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

    private async Task<Guid> CreateLanguageModelAsync(string name, string baseUrl, string apiKey)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl,
            apiKey,
            maxTokens = 4096,
            temperature = 0.2
        });

        return created.Id;
    }

    private async Task<Guid> CreateConversationTemplateAsync(string templateName, Guid modelId, string description, string prompt)
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

    private async Task<Guid> CreateBusinessDatabaseAsync(
        string name,
        string? connectionString = null,
        string description = "readonly db",
        int provider = 1,
        bool isEnabled = true,
        bool isReadOnly = true)
    {
        var created = await PostJsonAsync<CreatedBusinessDatabaseDto>("/api/data-analysis/business-database", new
        {
            name,
            description,
            connectionString = connectionString ?? "Host=localhost;Database=test;Username=test;Password=test;",
            provider,
            isEnabled,
            isReadOnly
        });

        return created.Id;
    }

    private async Task<Guid> CreateSessionAsync(Guid templateId)
    {
        var created = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new
        {
            templateId
        });

        return created.Id;
    }

    private async Task DeleteBusinessDatabaseIfExistsAsync(string name)
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

    private async Task DeleteConversationTemplateIfExistsAsync(string name)
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

    private async Task AssertSemanticChatAsync(Guid sessionId, string message, string expectedIntent, params string[] expectedTextFragments)
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
        text.Should().Contain("关键指标：");
        text.Should().Contain("关键记录：");
        text.Should().Contain("查询范围：");

        foreach (var expectedTextFragment in expectedTextFragments)
        {
            text.Should().Contain(expectedTextFragment);
        }

        text.Contains("SELECT", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_master_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("device_log_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("recipe_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("capacity_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("production_data_cloud_sim_view", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
        text.Contains("DeviceSemanticReadonly", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private async Task AssertPolicyChatAsync(Guid sessionId, string message, string expectedIntent, params string[] expectedTextFragments)
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

    private async Task<List<ChatChunkDto>> PostChatAsync(object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/aigateway/chat")
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

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
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

    private async Task SendJsonAsync(HttpMethod method, string uri, object? payload, HttpStatusCode expectedStatusCode)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(expectedStatusCode);
    }

    private string BuildFakeAiBaseUrl()
    {
        return _fixture.FakeAiBaseUri.ToString().TrimEnd('/');
    }

    private sealed record SemanticBusinessDatabaseContext(string Name, string ConnectionString);

    private sealed record LoginUserDto(string Token, string UserName);

    private sealed record CreatedLanguageModelDto(Guid Id, string Name);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record CreatedBusinessDatabaseDto(Guid Id, string Name);

    private sealed record CreatedSessionDto(Guid Id, string Title);

    private sealed record BusinessDatabaseDto(Guid Id, string Name);

    private sealed record ConversationTemplateDto(Guid Id, string Name);

    private sealed record ChatChunkDto(string Type, string Source, string Content);
}
