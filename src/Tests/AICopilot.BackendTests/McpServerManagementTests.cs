using System.Linq.Expressions;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.McpService.McpServers;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class McpServerManagementTests
{
    [Fact]
    public async Task CreateServerCommand_ShouldNormalizeAllowlistAndKeepArgumentsHiddenInDto()
    {
        var repository = new MutableMcpServerRepository();
        var handler = new CreateMcpServerCommandHandler(repository);

        var result = await handler.Handle(
            new CreateMcpServerCommand(
                "stdio-mcp",
                "stdio server",
                McpTransportType.Stdio,
                "dotnet",
                "server.dll",
                ChatExposureMode.Advisory,
                [" Echo ", "echo", "Inspect", " "],
                true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var server = repository.Items.Should().ContainSingle().Subject;
        server.AllowedToolNames.Should().Equal("Echo", "Inspect");

        var queryHandler = new GetMcpServerQueryHandler(
            repository,
            new InMemoryReadRepository<ApprovalPolicy>());
        var dto = await queryHandler.Handle(new GetMcpServerQuery(server.Id), CancellationToken.None);

        dto.IsSuccess.Should().BeTrue();
        dto.Value!.HasArguments.Should().BeTrue();
        dto.Value.ArgumentsMasked.Should().Be("******");
    }

    [Fact]
    public async Task UpdateServerCommand_WithBlankArguments_ShouldPreserveExistingHiddenArguments()
    {
        var server = new McpServerInfo(
            "preserve-mcp",
            "preserve server",
            McpTransportType.Stdio,
            "dotnet",
            "original-server.dll",
            ChatExposureMode.Disabled,
            ["Echo"],
            true);
        var repository = new MutableMcpServerRepository(server);
        var handler = new UpdateMcpServerCommandHandler(repository);

        var result = await handler.Handle(
            new UpdateMcpServerCommand(
                server.Id,
                "preserve-mcp-updated",
                "updated",
                McpTransportType.Stdio,
                "dotnet",
                "",
                ChatExposureMode.Advisory,
                ["Inspect"],
                false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        server.Arguments.Should().Be("original-server.dll");
        server.Name.Should().Be("preserve-mcp-updated");
        server.IsEnabled.Should().BeFalse();
        server.ChatExposureMode.Should().Be(ChatExposureMode.Advisory);
        server.AllowedToolNames.Should().Equal("Inspect");
    }

    [Fact]
    public async Task GetServerQuery_ShouldIncludeToolPolicySummaries_ForAllowlistedTools()
    {
        var server = new McpServerInfo(
            "advisory-mcp",
            "advisory server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            ChatExposureMode.Advisory,
            ["Echo", "Inspect"],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([server]);
        var approvalPolicyRepository = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "echo-approval",
                "requires approval",
                ApprovalTargetType.McpServer,
                "advisory-mcp",
                ["Echo"],
                isEnabled: true,
                requiresOnsiteAttestation: true),
            new ApprovalPolicy(
                "other-target",
                "ignored",
                ApprovalTargetType.McpServer,
                "another-mcp",
                ["Inspect"],
                isEnabled: true,
                requiresOnsiteAttestation: true),
            new ApprovalPolicy(
                "disabled-policy",
                "ignored",
                ApprovalTargetType.McpServer,
                "advisory-mcp",
                ["Inspect"],
                isEnabled: false,
                requiresOnsiteAttestation: true)
        ]);

        var handler = new GetMcpServerQueryHandler(serverRepository, approvalPolicyRepository);

        var result = await handler.Handle(new GetMcpServerQuery(server.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.ToolPolicySummaries.Should().HaveCount(2);
        result.Value.ToolPolicySummaries.Should().Contain(item =>
            item.ToolName == "Echo"
            && item.RequiresApproval
            && item.RequiresOnsiteAttestation);
        result.Value.ToolPolicySummaries.Should().Contain(item =>
            item.ToolName == "Inspect"
            && !item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
    }

    [Fact]
    public async Task GetListQuery_ShouldProjectToolPolicySummaries_ForEachServer()
    {
        var alphaServer = new McpServerInfo(
            "alpha-mcp",
            "alpha server",
            McpTransportType.Stdio,
            "dotnet",
            "alpha.dll",
            ChatExposureMode.ObserveOnly,
            ["Echo"],
            true);

        var betaServer = new McpServerInfo(
            "beta-mcp",
            "beta server",
            McpTransportType.Stdio,
            "dotnet",
            "beta.dll",
            ChatExposureMode.Advisory,
            ["Inspect"],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([betaServer, alphaServer]);
        var approvalPolicyRepository = new InMemoryReadRepository<ApprovalPolicy>(
        [
            new ApprovalPolicy(
                "beta-approval",
                "requires approval",
                ApprovalTargetType.McpServer,
                "beta-mcp",
                ["Inspect"],
                isEnabled: true,
                requiresOnsiteAttestation: false)
        ]);

        var handler = new GetListMcpServersQueryHandler(serverRepository, approvalPolicyRepository);

        var result = await handler.Handle(new GetListMcpServersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Select(item => item.Name).Should().Equal("alpha-mcp", "beta-mcp");
        result.Value.Single(item => item.Name == "alpha-mcp").ToolPolicySummaries.Should().ContainSingle(item =>
            item.ToolName == "Echo"
            && !item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
        result.Value.Single(item => item.Name == "beta-mcp").ToolPolicySummaries.Should().ContainSingle(item =>
            item.ToolName == "Inspect"
            && item.RequiresApproval
            && !item.RequiresOnsiteAttestation);
    }

    private sealed class MutableMcpServerRepository(params McpServerInfo[] servers) : IRepository<McpServerInfo>
    {
        private readonly List<McpServerInfo> servers = [..servers];

        public IReadOnlyList<McpServerInfo> Items => servers;

        public McpServerInfo Add(McpServerInfo entity)
        {
            servers.Add(entity);
            return entity;
        }

        public void Update(McpServerInfo entity)
        {
        }

        public void Delete(McpServerInfo entity)
        {
            servers.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        [Obsolete("Only exists for IReadRepository compatibility in tests.")]
        public IQueryable<McpServerInfo> GetQueryable()
        {
            return servers.AsQueryable();
        }

        public Task<List<McpServerInfo>> ListAsync(
            ISpecification<McpServerInfo>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<McpServerInfo?> FirstOrDefaultAsync(
            ISpecification<McpServerInfo>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<McpServerInfo>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<McpServerInfo>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<McpServerInfo?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(servers.FirstOrDefault(server => Equals(server.Id, id)));
        }

        public Task<List<McpServerInfo>> GetListAsync(
            Expression<Func<McpServerInfo, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(servers.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<McpServerInfo, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(servers.AsQueryable().Count(expression));
        }

        public Task<McpServerInfo?> GetAsync(
            Expression<Func<McpServerInfo, bool>> expression,
            Expression<Func<McpServerInfo, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(servers.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<McpServerInfo>> GetListAsync(
            Expression<Func<McpServerInfo, bool>> expression,
            Expression<Func<McpServerInfo, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(servers.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<McpServerInfo> Apply(ISpecification<McpServerInfo>? specification)
        {
            var query = servers.AsQueryable();
            return specification?.FilterCondition is null
                ? query
                : query.Where(specification.FilterCondition);
        }
    }
}
