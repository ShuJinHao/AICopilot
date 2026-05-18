using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.McpService.McpServers;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using Microsoft.Extensions.DependencyInjection;

namespace AICopilot.BackendTests;

[Trait("Suite", "Batch9McpToolGovernance")]
public sealed class McpToolGovernanceTests
{
    [Fact]
    public async Task GovernanceQuery_ShouldProjectAllowlistRegistryRuntimeAndOrphanStatuses()
    {
        var server = new McpServerInfo(
            "runtime-mcp",
            "runtime server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            ChatExposureMode.Advisory,
            [
                new McpAllowedTool("allow_only", ReadOnlyDeclared: true, McpReadOnlyHint: true),
                new McpAllowedTool("disabled"),
                new McpAllowedTool("ready"),
                new McpAllowedTool("unavailable"),
                new McpAllowedTool("blocked", RiskLevel: AiToolRiskLevel.Blocked)
            ],
            true);
        var registry = new FakeMcpToolRegistryReadService(
            Registration("runtime-mcp", "disabled", runtimeAvailable: true, isEnabled: false),
            Registration("runtime-mcp", "ready", runtimeAvailable: true, isEnabled: true),
            Registration("runtime-mcp", "unavailable", runtimeAvailable: false, isEnabled: true),
            Registration("runtime-mcp", "blocked", runtimeAvailable: true, isEnabled: false, riskLevel: "Blocked"),
            Registration("runtime-mcp", "old", runtimeAvailable: false, isEnabled: false),
            Registration("deleted-mcp", "ghost", runtimeAvailable: false, isEnabled: true));
        var handler = new GetMcpToolGovernanceQueryHandler(
            new InMemoryReadRepository<McpServerInfo>([server]),
            registry);

        var result = await handler.Handle(new GetMcpToolGovernanceQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Summary.Total.Should().Be(7);
        result.Value.Summary.AllowlistedOnly.Should().Be(1);
        result.Value.Summary.RegisteredDisabled.Should().Be(1);
        result.Value.Summary.Ready.Should().Be(1);
        result.Value.Summary.RuntimeUnavailable.Should().Be(1);
        result.Value.Summary.Blocked.Should().Be(1);
        result.Value.Summary.OrphanedRegistration.Should().Be(2);
        result.Value.Items.Should().Contain(item =>
            item.ToolName == "allow_only" &&
            item.Status == "AllowlistedOnly" &&
            item.Allowlisted &&
            !item.Registered &&
            item.ReadOnlyDeclared &&
            item.McpReadOnlyHint == true);
        result.Value.Items.Should().Contain(item => item.ToolName == "disabled" && item.Status == "RegisteredDisabled");
        result.Value.Items.Should().Contain(item => item.ToolName == "ready" && item.Status == "Ready");
        result.Value.Items.Should().Contain(item => item.ToolName == "unavailable" && item.Status == "RuntimeUnavailable");
        result.Value.Items.Should().Contain(item => item.ToolName == "blocked" && item.Status == "Blocked");
        result.Value.Items.Should().Contain(item => item.ToolName == "old" && item.Status == "OrphanedRegistration");
        result.Value.Items.Should().Contain(item =>
            item.ServerName == "deleted-mcp" &&
            item.ServerId == null &&
            item.Status == "OrphanedRegistration");
    }

    [Fact]
    public async Task GovernanceQuery_ShouldFilterByStatusServerAndOrphanFlag()
    {
        var server = new McpServerInfo(
            "runtime-mcp",
            "runtime server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            ChatExposureMode.Advisory,
            [new McpAllowedTool("ready")],
            true);
        var registry = new FakeMcpToolRegistryReadService(
            Registration("runtime-mcp", "ready", runtimeAvailable: true, isEnabled: true),
            Registration("runtime-mcp", "old", runtimeAvailable: false, isEnabled: false));
        var handler = new GetMcpToolGovernanceQueryHandler(
            new InMemoryReadRepository<McpServerInfo>([server]),
            registry);

        var ready = await handler.Handle(
            new GetMcpToolGovernanceQuery(ServerName: "runtime-mcp", Status: "ready"),
            CancellationToken.None);
        var noOrphans = await handler.Handle(
            new GetMcpToolGovernanceQuery(ServerId: server.Id.Value, IncludeOrphans: false),
            CancellationToken.None);
        var invalidStatus = await handler.Handle(
            new GetMcpToolGovernanceQuery(Status: "unknown"),
            CancellationToken.None);

        ready.IsSuccess.Should().BeTrue();
        ready.Value!.Items.Should().ContainSingle(item => item.Status == "Ready");
        noOrphans.IsSuccess.Should().BeTrue();
        noOrphans.Value!.Items.Should().ContainSingle(item => item.ToolName == "ready");
        invalidStatus.Status.Should().Be(AICopilot.SharedKernel.Result.ResultStatus.Invalid);
    }

    [Fact]
    public async Task McpToolRegistryReadService_ShouldProjectOnlyMcpRegistrationsAndRuntimeAvailability()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var loader = new AgentPluginLoader([], provider);
        var toolCode = AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, "runtime-mcp", "read_status");
        loader.RegisterAgentPlugin(new GenericBridgePlugin
        {
            Name = "runtime-mcp",
            Description = "runtime test bridge",
            ChatExposureMode = ChatExposureMode.Advisory,
            Tools =
            [
                new AiToolDefinition
                {
                    Name = toolCode,
                    ToolName = "read_status",
                    TargetType = AiToolTargetType.McpServer,
                    TargetName = "runtime-mcp"
                }
            ]
        });
        var mcpRegistration = CreateToolRegistration(
            toolCode,
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp",
            isEnabled: true);
        var builtInRegistration = CreateToolRegistration(
            "read_uploaded_file",
            ToolProviderType.BuiltIn,
            ToolRegistrationTargetType.AgentRuntime,
            "AgentTaskRuntime",
            isEnabled: true);
        var service = new McpToolRegistryReadService(
            new InMemoryReadRepository<ToolRegistration>([mcpRegistration, builtInRegistration]),
            loader);

        var result = await service.GetMcpToolRegistrationsAsync(CancellationToken.None);

        var item = result.Should().ContainSingle().Subject;
        item.ToolCode.Should().Be(toolCode);
        item.ServerName.Should().Be("runtime-mcp");
        item.ToolName.Should().Be("read_status");
        item.RuntimeAvailable.Should().BeTrue();
        item.IsEnabled.Should().BeTrue();
    }

    private static McpToolRegistryReadModel Registration(
        string serverName,
        string toolName,
        bool runtimeAvailable,
        bool isEnabled,
        string riskLevel = "Low")
    {
        return new McpToolRegistryReadModel(
            AiToolIdentity.CreateRuntimeName(AiToolTargetType.McpServer, serverName, toolName),
            serverName,
            toolName,
            runtimeAvailable,
            isEnabled,
            riskLevel,
            RequiresApproval: riskLevel == "RequiresApproval",
            RequiredPermission: null,
            DateTimeOffset.UtcNow);
    }

    private static ToolRegistration CreateToolRegistration(
        string toolCode,
        ToolProviderType providerType,
        ToolRegistrationTargetType targetType,
        string targetName,
        bool isEnabled)
    {
        return new ToolRegistration(
            toolCode,
            toolCode,
            "test tool",
            providerType,
            targetType,
            targetName,
            """{"type":"object"}""",
            """{"type":"object"}""",
            AiToolRiskLevel.Low,
            null,
            requiresApproval: false,
            isEnabled,
            timeoutSeconds: 120,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeMcpToolRegistryReadService(params McpToolRegistryReadModel[] registrations)
        : IMcpToolRegistryReadService
    {
        public Task<IReadOnlyCollection<McpToolRegistryReadModel>> GetMcpToolRegistrationsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<McpToolRegistryReadModel>>(registrations);
        }
    }
}

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "Batch9McpToolGovernance")]
[Trait("Runtime", "DockerRequired")]
public sealed class McpToolGovernancePermissionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public McpToolGovernancePermissionTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ToolGovernanceEndpoint_ShouldRequireMcpAndToolRegistryReadPermissions()
    {
        await AuthenticateAsAdminAsync();
        using var adminResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var mcpOnlyRole = await CreateRoleAsync(
            $"Batch9McpOnly-{Guid.NewGuid():N}",
            ["Mcp.GetListServers"]);
        var mcpOnlyUser = await CreateUserAsync($"batch9-mcp-only-{Guid.NewGuid():N}", mcpOnlyRole.RoleName);
        await AuthenticateAsync(mcpOnlyUser.UserName, "Password123!");
        using var missingRegistryResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        missingRegistryResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var missingRegistryProblem = await ReadJsonAsync<ProblemDetailsDto>(missingRegistryResponse);
        missingRegistryProblem.MissingPermissions.Should().Contain("AiGateway.ToolRegistry.Read");

        await AuthenticateAsAdminAsync();
        var registryOnlyRole = await CreateRoleAsync(
            $"Batch9RegistryOnly-{Guid.NewGuid():N}",
            ["AiGateway.ToolRegistry.Read"]);
        var registryOnlyUser = await CreateUserAsync($"batch9-registry-only-{Guid.NewGuid():N}", registryOnlyRole.RoleName);
        await AuthenticateAsync(registryOnlyUser.UserName, "Password123!");
        using var missingMcpResponse = await _fixture.HttpClient.GetAsync("/api/mcp/tool-governance");
        missingMcpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var missingMcpProblem = await ReadJsonAsync<ProblemDetailsDto>(missingMcpResponse);
        missingMcpProblem.MissingPermissions.Should().Contain("Mcp.GetListServers");
    }

    private async Task<CreatedRoleDto> CreateRoleAsync(string roleName, IReadOnlyCollection<string> permissions)
    {
        return await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
        {
            roleName,
            permissions
        });
    }

    private async Task<CreatedUserDto> CreateUserAsync(string userName, string roleName)
    {
        return await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
        {
            userName,
            password = "Password123!",
            roleName
        });
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await AuthenticateAsync(_fixture.BootstrapAdminUserName, _fixture.BootstrapAdminPassword);
    }

    private async Task AuthenticateAsync(string userName, string password)
    {
        var result = await PostJsonAsync<LoginUserDto>("/api/identity/login", new
        {
            username = userName,
            password
        });
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue($"POST '{uri}' failed: {body}");
        return JsonSerializer.Deserialize<T>(body, JsonOptions)!;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CreatedRoleDto(
        string RoleId,
        string RoleName,
        IReadOnlyCollection<string> Permissions,
        bool IsSystemRole,
        int AssignedUserCount);

    private sealed record CreatedUserDto(
        string UserId,
        string UserName,
        string RoleName,
        bool IsEnabled,
        string Status);

    private sealed record ProblemDetailsDto(
        string? Title,
        string? Detail,
        int? Status,
        string? Code,
        IReadOnlyCollection<string> MissingPermissions);
}
