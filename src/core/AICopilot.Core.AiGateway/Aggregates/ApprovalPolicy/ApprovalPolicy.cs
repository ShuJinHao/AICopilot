using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;

public class ApprovalPolicy : IAggregateRoot<ApprovalPolicyId>
{
    private readonly List<string> _toolNames = [];

    protected ApprovalPolicy()
    {
    }

    public ApprovalPolicy(
        string name,
        string? description,
        ApprovalTargetType targetType,
        string targetName,
        IEnumerable<string> toolNames,
        bool isEnabled,
        bool requiresOnsiteAttestation)
    {
        Id = ApprovalPolicyId.New();
        Update(name, description, targetType, targetName, toolNames, isEnabled, requiresOnsiteAttestation);
    }

    public ApprovalPolicyId Id { get; private set; }

    public uint RowVersion { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public ApprovalTargetType TargetType { get; private set; }

    public string TargetName { get; private set; } = string.Empty;

    public IReadOnlyCollection<string> ToolNames => _toolNames.AsReadOnly();

    public bool IsEnabled { get; private set; }

    public bool RequiresOnsiteAttestation { get; private set; }

    public void Update(
        string name,
        string? description,
        ApprovalTargetType targetType,
        string targetName,
        IEnumerable<string> toolNames,
        bool isEnabled,
        bool requiresOnsiteAttestation)
    {
        Validate(name, targetType, targetName);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TargetType = targetType;
        TargetName = targetName.Trim();
        IsEnabled = isEnabled;
        RequiresOnsiteAttestation = requiresOnsiteAttestation;

        _toolNames.Clear();
        _toolNames.AddRange(
            (toolNames ?? [])
                .Where(toolName => !string.IsNullOrWhiteSpace(toolName))
                .Select(toolName => toolName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void Validate(string name, ApprovalTargetType targetType, string targetName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Approval policy name is required.", nameof(name));
        }

        if (!Enum.IsDefined(typeof(ApprovalTargetType), targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Approval policy target type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("Approval policy target name is required.", nameof(targetName));
        }
    }
}
