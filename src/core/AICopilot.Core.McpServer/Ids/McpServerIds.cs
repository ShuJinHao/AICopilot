using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.McpServer.Ids;

public readonly record struct McpServerId : IStronglyTypedGuidId
{
    public McpServerId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("MCP server id is required.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static McpServerId New() => new(Guid.NewGuid());

    public static implicit operator Guid(McpServerId id) => id.Value;

    public override string ToString() => Value.ToString();
}
