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
            ProtocolType = model.ProtocolType,
            Name = model.Name,
            BaseUrl = model.BaseUrl,
            MaxTokens = model.Parameters.MaxTokens,
            ContextWindowTokens = model.Parameters.MaxTokens,
            MaxOutputTokens = model.Parameters.MaxOutputTokens,
            Temperature = model.Parameters.Temperature,
            IsEnabled = model.IsEnabled,
            Usages = MapUsages(model.Usage),
            HasApiKey = !string.IsNullOrEmpty(model.ApiKey),
            ApiKeyPreview = string.IsNullOrEmpty(model.ApiKey) ? null : "******",
            ConnectivityStatus = model.ConnectivityStatus.ToString(),
            ConnectivityCheckedAt = model.ConnectivityCheckedAt,
            ConnectivityError = model.ConnectivityError
        };
    }

    private static string[] MapUsages(LanguageModelUsage usage)
    {
        var values = new List<string>();
        if (usage.HasFlag(LanguageModelUsage.Chat))
        {
            values.Add(nameof(LanguageModelUsage.Chat));
        }

        if (usage.HasFlag(LanguageModelUsage.Routing))
        {
            values.Add(nameof(LanguageModelUsage.Routing));
        }

        if (usage.HasFlag(LanguageModelUsage.Planner))
        {
            values.Add(nameof(LanguageModelUsage.Planner));
        }

        if (usage.HasFlag(LanguageModelUsage.Embedding))
        {
            values.Add(nameof(LanguageModelUsage.Embedding));
        }

        return values.ToArray();
    }
}
