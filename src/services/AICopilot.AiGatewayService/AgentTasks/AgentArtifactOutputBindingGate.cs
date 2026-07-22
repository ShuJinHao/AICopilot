using System.Text.Json;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class AgentArtifactOutputBindingGate
{
    private const string InvalidBinding =
        "Artifact tool output is not bound to one exact draft artifact created by the current task step.";

    internal static ToolOutputValidationResult Validate(
        AgentTask task,
        ArtifactWorkspace workspace,
        AgentStep step,
        ToolRegistration registration,
        AgentToolOutputSnapshot contractOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(contractOutput.CanonicalJson);
            var root = document.RootElement;
            var declaresArtifact = Matches(root, "resultType", "artifact");
            if (registration.ProviderType != ToolProviderType.Artifact)
            {
                return declaresArtifact
                    ? Failure(contractOutput.Utf8ByteCount)
                    : ToolOutputValidationResult.Success(
                        contractOutput.CanonicalJson,
                        contractOutput.Utf8ByteCount);
            }

            if (!declaresArtifact ||
                !TryReadArtifactId(root, out var artifactId) ||
                !TryReadArtifactType(root, out var artifactType))
            {
                return Failure(contractOutput.Utf8ByteCount);
            }

            var stepArtifacts = workspace.Artifacts
                .Where(artifact => artifact.CreatedByStepId == step.Id)
                .ToArray();
            if (stepArtifacts.Length != 1)
            {
                return Failure(contractOutput.Utf8ByteCount);
            }

            var artifact = stepArtifacts[0];
            return artifact.Id == new ArtifactId(artifactId) &&
                   AgentArtifactOutputContractBinding.MatchesExact(root, artifact) &&
                   task.WorkspaceId == workspace.Id &&
                   artifact.WorkspaceId == workspace.Id &&
                   workspace.TaskId == task.Id &&
                   artifact.TaskId == task.Id &&
                   step.TaskId == task.Id &&
                   artifact.ArtifactType == artifactType &&
                   artifact.Status == ArtifactStatus.Draft &&
                   artifact.RelativePath.StartsWith("final/", StringComparison.OrdinalIgnoreCase) is false
                ? ToolOutputValidationResult.Success(
                    contractOutput.CanonicalJson,
                    contractOutput.Utf8ByteCount)
                : Failure(contractOutput.Utf8ByteCount);
        }
        catch (JsonException)
        {
            return Failure(contractOutput.Utf8ByteCount);
        }
    }

    private static bool TryReadArtifactId(JsonElement root, out Guid artifactId)
    {
        artifactId = Guid.Empty;
        return root.TryGetProperty("artifactId", out var value) &&
               value.ValueKind == JsonValueKind.String &&
               Guid.TryParseExact(value.GetString(), "D", out artifactId) &&
               artifactId != Guid.Empty;
    }

    private static bool TryReadArtifactType(JsonElement root, out ArtifactType artifactType)
    {
        artifactType = default;
        if (!root.TryGetProperty("artifactType", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        artifactType = value.GetString() switch
        {
            "chart" => ArtifactType.Chart,
            "markdown" => ArtifactType.Markdown,
            "html" => ArtifactType.Html,
            "pdf" => ArtifactType.Pdf,
            "pptx" => ArtifactType.Pptx,
            "xlsx" => ArtifactType.Xlsx,
            _ => default
        };
        return artifactType is ArtifactType.Chart or
            ArtifactType.Markdown or
            ArtifactType.Html or
            ArtifactType.Pdf or
            ArtifactType.Pptx or
            ArtifactType.Xlsx;
    }

    private static bool Matches(JsonElement root, string propertyName, string expected) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static ToolOutputValidationResult Failure(int utf8ByteCount) =>
        ToolOutputValidationResult.Failure(InvalidBinding, utf8ByteCount);
}

internal static class AgentArtifactOutputContractBinding
{
    private static readonly HashSet<string> RequiredProperties =
        new(StringComparer.Ordinal)
        {
            "status",
            "resultType",
            "artifactType",
            "artifactId"
        };

    internal static bool MatchesExact(string? outputJson, Artifact artifact)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(outputJson);
            return MatchesExact(document.RootElement, artifact);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool MatchesExact(JsonElement root, Artifact artifact)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != RequiredProperties.Count ||
            properties.Select(property => property.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != RequiredProperties.Count ||
            properties.Any(property => !RequiredProperties.Contains(property.Name)))
        {
            return false;
        }

        var expectedArtifactId = artifact.Id.Value.ToString("D");
        var expectedArtifactType = artifact.ArtifactType.ToString().ToLowerInvariant();
        return Matches(root, "status", "completed") &&
               Matches(root, "resultType", "artifact") &&
               Matches(root, "artifactType", expectedArtifactType) &&
               root.TryGetProperty("artifactId", out var artifactId) &&
               artifactId.ValueKind == JsonValueKind.String &&
               Guid.TryParseExact(artifactId.GetString(), "D", out var parsedArtifactId) &&
               parsedArtifactId == artifact.Id.Value &&
               string.Equals(artifactId.GetString(), expectedArtifactId, StringComparison.Ordinal);
    }

    private static bool Matches(JsonElement root, string propertyName, string expected) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}
