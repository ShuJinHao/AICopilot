using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using AICopilot.Core.McpServer.Specifications.McpServerInfo;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.McpService.McpServers;

public sealed record McpToolGovernanceDto(
    Guid? ServerId,
    string ServerName,
    string ToolName,
    string ToolCode,
    string Status,
    bool Allowlisted,
    bool Registered,
    bool RuntimeAvailable,
    bool RegistryEnabled,
    string RiskLevel,
    bool RequiresApproval,
    string? RequiredPermission,
    bool ReadOnlyDeclared,
    bool? McpReadOnlyHint,
    bool? McpDestructiveHint,
    bool? McpIdempotentHint,
    DateTimeOffset? LastSyncedAt,
    string RecommendedAction);

public sealed record McpToolGovernanceSummaryDto(
    int Total,
    int AllowlistedOnly,
    int RegisteredDisabled,
    int Ready,
    int RuntimeUnavailable,
    int OrphanedRegistration,
    int Blocked);

public sealed record McpToolGovernancePageDto(
    McpToolGovernanceSummaryDto Summary,
    IReadOnlyCollection<McpToolGovernanceDto> Items);

[AuthorizeRequirement("Mcp.GetListServers")]
[AuthorizeRequirement("AiGateway.ToolRegistry.Read")]
public sealed record GetMcpToolGovernanceQuery(
    Guid? ServerId = null,
    string? ServerName = null,
    string? Status = null,
    bool IncludeOrphans = true) : IQuery<Result<McpToolGovernancePageDto>>;

public sealed class GetMcpToolGovernanceQueryHandler(
    IReadRepository<McpServerInfo> serverRepository,
    IMcpToolRegistryReadService toolRegistryReadService)
    : IQueryHandler<GetMcpToolGovernanceQuery, Result<McpToolGovernancePageDto>>
{
    public async Task<Result<McpToolGovernancePageDto>> Handle(
        GetMcpToolGovernanceQuery request,
        CancellationToken cancellationToken)
    {
        var statusFilter = NormalizeStatusFilter(request.Status);
        if (statusFilter == string.Empty)
        {
            return Result.Invalid("MCP tool governance status is invalid.");
        }

        var allServers = await serverRepository.ListAsync(new McpServerInfosOrderedSpec(), cancellationToken);
        var servers = FilterServers(allServers, request);

        var registrations = await toolRegistryReadService.GetMcpToolRegistrationsAsync(cancellationToken);
        var registrationsByToolCode = registrations
            .GroupBy(registration => registration.ToolCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var expectedToolCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<McpToolGovernanceDto>();
        foreach (var server in servers)
        {
            foreach (var allowedTool in server.AllowedTools.OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase))
            {
                var toolCode = AiToolIdentity.CreateRuntimeName(
                    AiToolTargetType.McpServer,
                    server.Name,
                    allowedTool.ToolName);
                expectedToolCodes.Add(toolCode);

                registrationsByToolCode.TryGetValue(toolCode, out var registration);
                items.Add(BuildAllowlistedItem(server, allowedTool, toolCode, registration));
            }
        }

        if (request.IncludeOrphans)
        {
            items.AddRange(BuildOrphanedItems(registrations, servers, expectedToolCodes, request));
        }

        var filteredItems = items
            .Where(item => statusFilter is null ||
                           string.Equals(item.Status, statusFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result.Success(new McpToolGovernancePageDto(
            BuildSummary(filteredItems),
            filteredItems));
    }

    private static IReadOnlyCollection<McpServerInfo> FilterServers(
        IReadOnlyCollection<McpServerInfo> servers,
        GetMcpToolGovernanceQuery request)
    {
        var result = servers.AsEnumerable();
        if (request.ServerId.HasValue)
        {
            var serverId = new McpServerId(request.ServerId.Value);
            result = result.Where(server => server.Id == serverId);
        }

        if (!string.IsNullOrWhiteSpace(request.ServerName))
        {
            result = result.Where(server =>
                string.Equals(server.Name, request.ServerName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return result.ToArray();
    }

    private static McpToolGovernanceDto BuildAllowlistedItem(
        McpServerInfo server,
        McpAllowedTool allowedTool,
        string toolCode,
        McpToolRegistryReadModel? registration)
    {
        var effectiveRiskLevel = registration?.RiskLevel ?? allowedTool.EffectiveRiskLevel(server.RiskLevel).ToString();
        var status = ResolveStatus(registration);
        return new McpToolGovernanceDto(
            server.Id.Value,
            server.Name,
            allowedTool.ToolName,
            toolCode,
            status,
            Allowlisted: true,
            Registered: registration is not null,
            RuntimeAvailable: registration?.RuntimeAvailable ?? false,
            RegistryEnabled: registration?.IsEnabled ?? false,
            effectiveRiskLevel,
            registration?.RequiresApproval ?? effectiveRiskLevel == AiToolRiskLevel.RequiresApproval.ToString(),
            registration?.RequiredPermission,
            allowedTool.ReadOnlyDeclared,
            allowedTool.McpReadOnlyHint,
            allowedTool.McpDestructiveHint,
            allowedTool.McpIdempotentHint,
            registration?.UpdatedAt,
            RecommendedAction(status));
    }

    private static IReadOnlyCollection<McpToolGovernanceDto> BuildOrphanedItems(
        IReadOnlyCollection<McpToolRegistryReadModel> registrations,
        IReadOnlyCollection<McpServerInfo> filteredServers,
        HashSet<string> expectedToolCodes,
        GetMcpToolGovernanceQuery request)
    {
        var serversByName = filteredServers.ToDictionary(server => server.Name, StringComparer.OrdinalIgnoreCase);
        return registrations
            .Where(registration => !expectedToolCodes.Contains(registration.ToolCode))
            .Where(registration => string.IsNullOrWhiteSpace(request.ServerName) ||
                                   string.Equals(registration.ServerName, request.ServerName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(registration => !request.ServerId.HasValue || serversByName.ContainsKey(registration.ServerName))
            .Select(registration =>
            {
                serversByName.TryGetValue(registration.ServerName, out var server);
                return new McpToolGovernanceDto(
                    server?.Id.Value,
                    registration.ServerName,
                    registration.ToolName,
                    registration.ToolCode,
                    McpToolGovernanceStatuses.OrphanedRegistration,
                    Allowlisted: false,
                    Registered: true,
                    registration.RuntimeAvailable,
                    registration.IsEnabled,
                    registration.RiskLevel,
                    registration.RequiresApproval,
                    registration.RequiredPermission,
                    ReadOnlyDeclared: false,
                    McpReadOnlyHint: null,
                    McpDestructiveHint: null,
                    McpIdempotentHint: null,
                    registration.UpdatedAt,
                    RecommendedAction(McpToolGovernanceStatuses.OrphanedRegistration));
            })
            .ToArray();
    }

    private static string ResolveStatus(McpToolRegistryReadModel? registration)
    {
        if (registration is null)
        {
            return McpToolGovernanceStatuses.AllowlistedOnly;
        }

        if (string.Equals(registration.RiskLevel, AiToolRiskLevel.Blocked.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return McpToolGovernanceStatuses.Blocked;
        }

        if (!registration.IsEnabled)
        {
            return McpToolGovernanceStatuses.RegisteredDisabled;
        }

        return registration.RuntimeAvailable
            ? McpToolGovernanceStatuses.Ready
            : McpToolGovernanceStatuses.RuntimeUnavailable;
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim();
        return McpToolGovernanceStatuses.All.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? McpToolGovernanceStatuses.All.Single(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            : string.Empty;
    }

    private static McpToolGovernanceSummaryDto BuildSummary(IReadOnlyCollection<McpToolGovernanceDto> items)
    {
        return new McpToolGovernanceSummaryDto(
            items.Count,
            Count(McpToolGovernanceStatuses.AllowlistedOnly),
            Count(McpToolGovernanceStatuses.RegisteredDisabled),
            Count(McpToolGovernanceStatuses.Ready),
            Count(McpToolGovernanceStatuses.RuntimeUnavailable),
            Count(McpToolGovernanceStatuses.OrphanedRegistration),
            Count(McpToolGovernanceStatuses.Blocked));

        int Count(string status)
        {
            return items.Count(item => string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string RecommendedAction(string status)
    {
        return status switch
        {
            McpToolGovernanceStatuses.AllowlistedOnly => "Wait for runtime sync or check MCP server availability.",
            McpToolGovernanceStatuses.RegisteredDisabled => "Review and enable from Tool Registry if this tool should be available.",
            McpToolGovernanceStatuses.Ready => "No action required.",
            McpToolGovernanceStatuses.RuntimeUnavailable => "Check MCP runtime registration and server health before enabling Agent tasks.",
            McpToolGovernanceStatuses.OrphanedRegistration => "Disable the Tool Registry entry or add it back to the MCP server allowlist.",
            McpToolGovernanceStatuses.Blocked => "Review blocked risk classification; blocked tools stay unavailable.",
            _ => "Review MCP tool governance state."
        };
    }
}

internal static class McpToolGovernanceStatuses
{
    public const string AllowlistedOnly = "AllowlistedOnly";
    public const string RegisteredDisabled = "RegisteredDisabled";
    public const string Ready = "Ready";
    public const string RuntimeUnavailable = "RuntimeUnavailable";
    public const string OrphanedRegistration = "OrphanedRegistration";
    public const string Blocked = "Blocked";

    public static readonly IReadOnlyCollection<string> All =
    [
        AllowlistedOnly,
        RegisteredDisabled,
        Ready,
        RuntimeUnavailable,
        OrphanedRegistration,
        Blocked
    ];
}
