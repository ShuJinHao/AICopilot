using System.Text.Json.Serialization;

namespace AICopilot.AiGatewayService.Models;

public sealed record ChatModelMetadataPayload(
    [property: JsonPropertyName("finalModelId")] Guid? FinalModelId,
    [property: JsonPropertyName("finalModelName")] string? FinalModelName,
    [property: JsonPropertyName("contextWindowTokens")] int? ContextWindowTokens,
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens);
