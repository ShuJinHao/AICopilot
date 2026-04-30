using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.Queries.LanguageModels;

internal static class LanguageModelDtoMapper
{
    public static LanguageModelDto Map(LanguageModel model)
    {
        return new LanguageModelDto
        {
            Id = model.Id,
            Provider = model.Provider,
            Name = model.Name,
            BaseUrl = model.BaseUrl,
            MaxTokens = model.Parameters.MaxTokens,
            Temperature = model.Parameters.Temperature,
            HasApiKey = !string.IsNullOrEmpty(model.ApiKey),
            ApiKeyMasked = string.IsNullOrEmpty(model.ApiKey) ? null : "******"
        };
    }
}
