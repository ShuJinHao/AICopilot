using AICopilot.SharedKernel.Specification;

namespace AICopilot.Core.McpServer.Specifications.McpServerInfo;

public sealed class McpServerInfoByIdSpec : Specification<Aggregates.McpServerInfo.McpServerInfo>
{
    public McpServerInfoByIdSpec(Guid id)
    {
        FilterCondition = server => server.Id == id;
    }
}

public sealed class McpServerInfosOrderedSpec : Specification<Aggregates.McpServerInfo.McpServerInfo>
{
    public McpServerInfosOrderedSpec()
    {
        SetOrderBy(server => server.Name);
    }
}

public sealed class EnabledMcpServerInfosSpec : Specification<Aggregates.McpServerInfo.McpServerInfo>
{
    public EnabledMcpServerInfosSpec()
    {
        FilterCondition = server => server.IsEnabled;
        SetOrderBy(server => server.Name);
    }
}
