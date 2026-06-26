using AICopilot.Core.AiGateway.Aggregates.Tools;

namespace AICopilot.AiGatewayService.Tools;

internal static class ToolRegistryUpdatePolicy
{
    public static IReadOnlyCollection<string> BuildChangedFields(
        ToolRegistration tool,
        UpdateToolRegistrationCommand request,
        ToolAuditLevel nextAuditLevel,
        ToolDataBoundary? nextDataBoundary)
    {
        var fields = new List<string>();
        AddIfChanged(request.DisplayName, tool.DisplayName, "displayName");
        AddIfChanged(request.Description, tool.Description, "description");
        AddIfChanged(request.InputSchemaJson, tool.InputSchemaJson, "inputSchemaJson");
        AddIfChanged(request.OutputSchemaJson, tool.OutputSchemaJson, "outputSchemaJson");
        if (request.RiskLevel.HasValue && request.RiskLevel.Value != tool.RiskLevel)
        {
            fields.Add("riskLevel");
        }

        AddIfChanged(request.RequiredPermission, tool.RequiredPermission, "requiredPermission");
        if (request.RequiresApproval.HasValue && request.RequiresApproval.Value != tool.RequiresApproval)
        {
            fields.Add("requiresApproval");
        }

        if (request.IsEnabled.HasValue && request.IsEnabled.Value != tool.IsEnabled)
        {
            fields.Add("isEnabled");
        }

        if (request.TimeoutSeconds.HasValue && request.TimeoutSeconds.Value != tool.TimeoutSeconds)
        {
            fields.Add("timeoutSeconds");
        }

        if (request.AuditLevel is not null && nextAuditLevel != tool.AuditLevel)
        {
            fields.Add("auditLevel");
        }

        AddIfChanged(request.Category, tool.Category, "category");
        if (request.BusinessDomains is not null &&
            !request.BusinessDomains.SequenceEqual(tool.BusinessDomains, StringComparer.OrdinalIgnoreCase))
        {
            fields.Add("businessDomains");
        }

        if (nextDataBoundary.HasValue && nextDataBoundary.Value != tool.DataBoundary)
        {
            fields.Add("dataBoundary");
        }

        if (request.IsVisibleToPlanner.HasValue && request.IsVisibleToPlanner.Value != tool.IsVisibleToPlanner)
        {
            fields.Add("isVisibleToPlanner");
        }

        if (request.IsExecutableByAgent.HasValue && request.IsExecutableByAgent.Value != tool.IsExecutableByAgent)
        {
            fields.Add("isExecutableByAgent");
        }

        if (request.SchemaVersion.HasValue && request.SchemaVersion.Value != tool.SchemaVersion)
        {
            fields.Add("schemaVersion");
        }

        if (request.CatalogVersion.HasValue && request.CatalogVersion.Value != tool.CatalogVersion)
        {
            fields.Add("catalogVersion");
        }

        AddIfChanged(request.ApprovalPolicy, tool.ApprovalPolicy, "approvalPolicy");
        return fields;

        void AddIfChanged(string? next, string? current, string field)
        {
            if (next is not null && !string.Equals(next, current, StringComparison.Ordinal))
            {
                fields.Add(field);
            }
        }
    }

    public static bool TryParseDataBoundary(string? value, out ToolDataBoundary? dataBoundary)
    {
        dataBoundary = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Enum.TryParse<ToolDataBoundary>(value, ignoreCase: true, out var parsed))
        {
            return false;
        }

        dataBoundary = parsed;
        return true;
    }
}
