using System.Linq.Expressions;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.McpService.McpServers;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.ApplicationTests;

public sealed class McpServerManagementTests
{
    [Fact]
    public async Task CreateServerCommand_ShouldNormalizeAllowlistAndKeepArgumentsHiddenInDto()
    {
        var repository = new MutableMcpServerRepository();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new CreateMcpServerCommandHandler(repository, auditLogWriter);
        const string rawArguments = "server.dll --token secret-token";

        var result = await handler.Handle(
            new CreateMcpServerCommand(
                "stdio-mcp",
                "stdio server",
                McpTransportType.Stdio,
                "dotnet",
                rawArguments,
                ChatExposureMode.Advisory,
                [
                    new McpAllowedToolDto { ToolName = " Echo " },
                    new McpAllowedToolDto { ToolName = "echo" },
                    new McpAllowedToolDto { ToolName = "Inspect" },
                    new McpAllowedToolDto { ToolName = " " }
                ],
                true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var server = repository.Items.Should().ContainSingle().Subject;
        server.AllowedTools.Select(tool => tool.ToolName).Should().Equal("Echo", "Inspect");

        var queryHandler = new GetMcpServerQueryHandler(
            repository,
            new TestApprovalRequirementReadService());
        var dto = await queryHandler.Handle(new GetMcpServerQuery(server.Id), CancellationToken.None);

        dto.IsSuccess.Should().BeTrue();
        dto.Value!.HasArguments.Should().BeTrue();
        dto.Value.ArgumentsMasked.Should().Be("******");
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Mcp.CreateServer");
        audit.Summary.Should().NotContain(rawArguments);
        audit.Summary.Should().Contain("arguments=redacted");
    }

    [Fact]
    public async Task CreateServerCommand_ShouldRejectUnsafeSseEndpointBeforeSaveOrAudit()
    {
        var repository = new MutableMcpServerRepository();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new CreateMcpServerCommandHandler(repository, auditLogWriter);

        var act = async () => await handler.Handle(
            new CreateMcpServerCommand(
                "unsafe-sse",
                "unsafe server",
                McpTransportType.Sse,
                null,
                "http://169.254.169.254/latest/meta-data",
                ChatExposureMode.Advisory,
                [new McpAllowedToolDto { ToolName = "Echo" }],
                true),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        repository.Items.Should().BeEmpty();
        auditLogWriter.Requests.Should().BeEmpty();
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
            [new McpAllowedTool("Echo")],
            true);
        var repository = new MutableMcpServerRepository(server);
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UpdateMcpServerCommandHandler(repository, auditLogWriter);

        var result = await handler.Handle(
            new UpdateMcpServerCommand(
                server.Id,
                "preserve-mcp-updated",
                "updated",
                McpTransportType.Stdio,
                "dotnet",
                "",
                ChatExposureMode.Advisory,
                [new McpAllowedToolDto { ToolName = "Inspect" }],
                false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        server.Arguments.Should().Be("original-server.dll");
        server.Name.Should().Be("preserve-mcp-updated");
        server.IsEnabled.Should().BeFalse();
        server.ChatExposureMode.Should().Be(ChatExposureMode.Advisory);
        server.AllowedTools.Select(tool => tool.ToolName).Should().Equal("Inspect");
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Mcp.UpdateServer");
        audit.ChangedFields.Should().Contain(["name", "description", "chatExposureMode", "allowedTools", "isEnabled"]);
        audit.Summary.Should().NotContain("original-server.dll");
        audit.Summary.Should().Contain("arguments=unchanged");
    }

    [Fact]
    public async Task UpdateServerCommand_ShouldRejectUnsafeSseEndpointBeforeSaveOrAudit()
    {
        var server = new McpServerInfo(
            "existing-mcp",
            "existing server",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            ChatExposureMode.Disabled,
            [new McpAllowedTool("Echo")],
            true);
        var repository = new MutableMcpServerRepository(server);
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UpdateMcpServerCommandHandler(repository, auditLogWriter);

        var act = async () => await handler.Handle(
            new UpdateMcpServerCommand(
                server.Id,
                "updated-mcp",
                "updated server",
                McpTransportType.Sse,
                null,
                "http://10.0.0.1/sse",
                ChatExposureMode.Advisory,
                [new McpAllowedToolDto { ToolName = "Inspect" }],
                true),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        server.Name.Should().Be("existing-mcp");
        server.TransportType.Should().Be(McpTransportType.Stdio);
        server.Arguments.Should().Be("server.dll");
        server.AllowedTools.Select(tool => tool.ToolName).Should().Equal("Echo");
        auditLogWriter.Requests.Should().BeEmpty();
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
            [new McpAllowedTool("Echo"), new McpAllowedTool("Inspect")],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([server]);
        var approvalRequirementReadService = new TestApprovalRequirementReadService(
        [
            new ApprovalToolRequirementDto(
                AiToolTargetType.McpServer,
                "advisory-mcp",
                "Echo",
                RequiresApproval: true,
                RequiresOnsiteAttestation: true),
            new ApprovalToolRequirementDto(
                AiToolTargetType.McpServer,
                "another-mcp",
                "Inspect",
                RequiresApproval: true,
                RequiresOnsiteAttestation: true)
        ]);

        var handler = new GetMcpServerQueryHandler(serverRepository, approvalRequirementReadService);

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
            [new McpAllowedTool("Echo")],
            true);

        var betaServer = new McpServerInfo(
            "beta-mcp",
            "beta server",
            McpTransportType.Stdio,
            "dotnet",
            "beta.dll",
            ChatExposureMode.Advisory,
            [new McpAllowedTool("Inspect")],
            true);

        var serverRepository = new InMemoryReadRepository<McpServerInfo>([betaServer, alphaServer]);
        var approvalRequirementReadService = new TestApprovalRequirementReadService(
        [
            new ApprovalToolRequirementDto(
                AiToolTargetType.McpServer,
                "beta-mcp",
                "Inspect",
                RequiresApproval: true,
                RequiresOnsiteAttestation: false)
        ]);

        var handler = new GetListMcpServersQueryHandler(serverRepository, approvalRequirementReadService);

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

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }
}
