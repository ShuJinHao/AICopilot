using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.LanguageModels;

[AuthorizeRequirement("AiGateway.UpdateLanguageModel")]
public record TestLanguageModelCommand(
    Guid? Id = null,
    string? Provider = null,
    string? ProtocolType = null,
    string? Name = null,
    string? BaseUrl = null,
    string? ApiKey = null,
    bool ClearApiKey = false,
    int? MaxTokens = null,
    int? ContextWindowTokens = null,
    int? MaxOutputTokens = null,
    IReadOnlyList<string>? Usages = null,
    float? Temperature = null,
    bool PersistResult = false) : ICommand<Result<LanguageModelTestResultDto>>;

public class TestLanguageModelCommandHandler(
    IRepository<LanguageModel> repository,
    ISecretProtector secretProtector,
    ILanguageModelConnectivityTester tester)
    : ICommandHandler<TestLanguageModelCommand, Result<LanguageModelTestResultDto>>
{
    public async Task<Result<LanguageModelTestResultDto>> Handle(
        TestLanguageModelCommand request,
        CancellationToken cancellationToken)
    {
        LanguageModel? persistedModel = null;
        if (request.Id.HasValue)
        {
            persistedModel = await repository.GetByIdAsync(new LanguageModelId(request.Id.Value), cancellationToken);
            if (persistedModel is null)
            {
                return Result.NotFound("Language model not found.");
            }
        }

        var modelResult = BuildTestModel(request, persistedModel);
        if (!modelResult.Success)
        {
            var invalidResult = ToDto(new LanguageModelConnectivityTestOutcome(
                false,
                modelResult.Error,
                0,
                DateTimeOffset.UtcNow));

            if (request.PersistResult && persistedModel is not null)
            {
                persistedModel.MarkConnectivityFailed(invalidResult.CheckedAt, invalidResult.Error ?? invalidResult.Message);
                repository.Update(persistedModel);
                await repository.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(invalidResult);
        }

        var outcome = await tester.TestAsync(modelResult.Model!, cancellationToken);
        if (request.PersistResult && persistedModel is not null)
        {
            if (outcome.Success)
            {
                persistedModel.MarkConnectivitySucceeded(outcome.CheckedAt);
            }
            else
            {
                persistedModel.MarkConnectivityFailed(outcome.CheckedAt, outcome.Error ?? "Language model connectivity test failed.");
            }

            repository.Update(persistedModel);
            await repository.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToDto(outcome));
    }

    private BuildTestModelResult BuildTestModel(TestLanguageModelCommand request, LanguageModel? persistedModel)
    {
        try
        {
            var contextWindowTokens = request.ContextWindowTokens
                                      ?? request.MaxTokens
                                      ?? persistedModel?.Parameters.MaxTokens
                                      ?? 2048;
            var maxOutputTokens = request.MaxOutputTokens
                                  ?? persistedModel?.Parameters.MaxOutputTokens
                                  ?? 1024;
            var temperature = request.Temperature
                              ?? persistedModel?.Parameters.Temperature
                              ?? 0.7f;
            var apiKey = request.ClearApiKey
                ? null
                : string.IsNullOrWhiteSpace(request.ApiKey)
                    ? persistedModel?.ApiKey
                    : ProtectApiKey(request.ApiKey);

            var model = new LanguageModel(
                ResolveRequired(request.Provider, persistedModel?.Provider),
                ResolveRequired(request.Name, persistedModel?.Name),
                ResolveRequired(request.BaseUrl, persistedModel?.BaseUrl),
                apiKey,
                new ModelParameters
                {
                    MaxTokens = contextWindowTokens,
                    MaxOutputTokens = maxOutputTokens,
                    Temperature = temperature
                },
                ResolveRequired(request.ProtocolType, persistedModel?.ProtocolType),
                LanguageModelCommandMapper.ParseUsages(request.Usages ?? MapUsages(persistedModel?.Usage)),
                true);

            return BuildTestModelResult.Ok(model);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            return BuildTestModelResult.Failed(
                "Language model configuration is invalid. Please check provider, protocol, base URL, token budget, temperature, and usage settings.");
        }
    }

    private string? ProtectApiKey(string? apiKey)
    {
        return string.IsNullOrWhiteSpace(apiKey)
            ? null
            : secretProtector.Protect(apiKey.Trim());
    }

    private static string ResolveRequired(string? requestValue, string? fallbackValue)
    {
        var value = string.IsNullOrWhiteSpace(requestValue) ? fallbackValue : requestValue;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static IReadOnlyList<string>? MapUsages(LanguageModelUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        var values = new List<string>();
        if (usage.Value.HasFlag(LanguageModelUsage.Chat))
        {
            values.Add(nameof(LanguageModelUsage.Chat));
        }

        if (usage.Value.HasFlag(LanguageModelUsage.Routing))
        {
            values.Add(nameof(LanguageModelUsage.Routing));
        }

        if (usage.Value.HasFlag(LanguageModelUsage.Planner))
        {
            values.Add(nameof(LanguageModelUsage.Planner));
        }

        if (usage.Value.HasFlag(LanguageModelUsage.Embedding))
        {
            values.Add(nameof(LanguageModelUsage.Embedding));
        }

        return values;
    }

    private static LanguageModelTestResultDto ToDto(LanguageModelConnectivityTestOutcome outcome)
    {
        return new LanguageModelTestResultDto
        {
            Success = outcome.Success,
            Status = outcome.Success
                ? LanguageModelConnectivityStatus.Succeeded.ToString()
                : LanguageModelConnectivityStatus.Failed.ToString(),
            Message = outcome.Success ? "配置完成" : "连接失败",
            Error = outcome.Error,
            ElapsedMilliseconds = outcome.ElapsedMilliseconds,
            CheckedAt = outcome.CheckedAt
        };
    }

    private sealed record BuildTestModelResult(LanguageModel? Model, string? Error)
    {
        public bool Success => Model is not null;

        public static BuildTestModelResult Ok(LanguageModel model)
        {
            return new BuildTestModelResult(model, null);
        }

        public static BuildTestModelResult Failed(string error)
        {
            return new BuildTestModelResult(null, error);
        }
    }
}
