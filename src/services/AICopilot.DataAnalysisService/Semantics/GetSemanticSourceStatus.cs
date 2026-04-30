using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.DataAnalysisService.BusinessDatabases;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.DataAnalysisService.Semantics;

public record SemanticSourceStatusDto
{
    public required string Target { get; init; }
    public string? DatabaseName { get; init; }
    public string? SourceName { get; init; }
    public string? EffectiveSourceName { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsReadOnly { get; init; }
    public bool SourceExists { get; init; }
    public bool ProviderMatched { get; init; }
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = [];
    public required string Status { get; init; }
}

[AuthorizeRequirement("DataAnalysis.GetListBusinessDatabases")]
public record GetSemanticSourceStatusQuery : IQuery<Result<IList<SemanticSourceStatusDto>>>;

public class GetSemanticSourceStatusQueryHandler(
    ISemanticPhysicalMappingProvider mappingProvider,
    IReadRepository<BusinessDatabase> businessDatabaseRepository,
    ISemanticSourceInspector semanticSourceInspector)
    : IQueryHandler<GetSemanticSourceStatusQuery, Result<IList<SemanticSourceStatusDto>>>
{
    public async Task<Result<IList<SemanticSourceStatusDto>>> Handle(
        GetSemanticSourceStatusQuery request,
        CancellationToken cancellationToken)
    {
        var businessDatabases = await businessDatabaseRepository.ListAsync(cancellationToken: cancellationToken);
        var lookup = businessDatabases
            .GroupBy(database => database.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<SemanticSourceStatusDto>();
        foreach (var target in Enum.GetValues<SemanticQueryTarget>())
        {
            result.Add(await BuildStatusAsync(
                target,
                mappingProvider,
                lookup,
                semanticSourceInspector,
                cancellationToken));
        }

        return Result.Success<IList<SemanticSourceStatusDto>>(result);
    }

    private static async Task<SemanticSourceStatusDto> BuildStatusAsync(
        SemanticQueryTarget target,
        ISemanticPhysicalMappingProvider mappingProvider,
        IReadOnlyDictionary<string, BusinessDatabase> databases,
        ISemanticSourceInspector semanticSourceInspector,
        CancellationToken cancellationToken)
    {
        if (!mappingProvider.TryGetMapping(target, out var mapping))
        {
            return new SemanticSourceStatusDto
            {
                Target = target.ToString(),
                Status = SemanticSourceStatusValues.MissingMapping
            };
        }

        var effectiveSourceName = SemanticSourceInspector.ResolveEffectiveSourceName(mapping);

        if (string.IsNullOrWhiteSpace(mapping.DatabaseName) ||
            !databases.TryGetValue(mapping.DatabaseName, out var database))
        {
            return new SemanticSourceStatusDto
            {
                Target = target.ToString(),
                DatabaseName = mapping.DatabaseName,
                SourceName = mapping.SourceName,
                EffectiveSourceName = effectiveSourceName,
                Status = SemanticSourceStatusValues.DatabaseNotFound
            };
        }

        var providerMatched = BusinessDatabaseContractMapper.ToContractProvider(database.Provider) == mapping.Provider;
        var status = SemanticSourceStatusValues.Ready;
        var sourceExists = false;
        IReadOnlyList<string> missingRequiredFields = [];

        if (!database.IsEnabled)
        {
            status = SemanticSourceStatusValues.Disabled;
        }
        else if (!database.IsReadOnly)
        {
            status = SemanticSourceStatusValues.NotReadOnly;
        }
        else if (!providerMatched)
        {
            status = SemanticSourceStatusValues.ProviderMismatch;
        }
        else
        {
            var inspection = await semanticSourceInspector.InspectAsync(
                BusinessDatabaseContractMapper.ToConnectionInfo(database),
                mapping,
                cancellationToken);
            sourceExists = inspection.SourceExists;
            missingRequiredFields = inspection.MissingRequiredFields;
            status = !inspection.SourceExists
                ? SemanticSourceStatusValues.SourceNotFound
                : inspection.MissingRequiredFields.Count > 0
                    ? SemanticSourceStatusValues.FieldMismatch
                    : SemanticSourceStatusValues.Ready;
        }

        return new SemanticSourceStatusDto
        {
            Target = target.ToString(),
            DatabaseName = mapping.DatabaseName,
            SourceName = mapping.SourceName,
            EffectiveSourceName = effectiveSourceName,
            IsEnabled = database.IsEnabled,
            IsReadOnly = database.IsReadOnly,
            SourceExists = sourceExists,
            ProviderMatched = providerMatched,
            MissingRequiredFields = missingRequiredFields,
            Status = status
        };
    }
}
