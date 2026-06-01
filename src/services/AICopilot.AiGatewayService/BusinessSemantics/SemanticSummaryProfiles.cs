using AICopilot.AiGatewayService.Models;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.AiGateway.Dtos;

namespace AICopilot.AiGatewayService.BusinessSemantics;

public interface ISemanticSummaryProfileCatalog
{
    IReadOnlyCollection<ISemanticSummaryProfile> GetAll();

    bool TryGet(SemanticQueryTarget target, out ISemanticSummaryProfile profile);
}

public interface ISemanticSummaryProfile
{
    SemanticQueryTarget Target { get; }

    IReadOnlyList<string> ExampleQuestions { get; }

    SemanticSummaryResponseContract ResponseContract { get; }

    string GetFieldLabel(string field);

    SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope);
}

public sealed record SemanticSummaryResponseContract(
    string ConclusionSection,
    string MetricSection,
    string HighlightSection,
    string ScopeSection)
{
    public static SemanticSummaryResponseContract Default { get; } = new(
        "结论",
        "关键指标",
        "关键记录",
        "查询范围");
}

public sealed class SemanticSummaryProfileCatalog : ISemanticSummaryProfileCatalog
{
    private readonly IReadOnlyDictionary<SemanticQueryTarget, ISemanticSummaryProfile> _profiles =
        new ISemanticSummaryProfile[]
        {
            new DeviceSummaryProfile(),
            new DeviceLogSummaryProfile(),
            new CapacitySummaryProfile(),
            new ProductionDataSummaryProfile()
        }.ToDictionary(item => item.Target);

    public IReadOnlyCollection<ISemanticSummaryProfile> GetAll()
    {
        return _profiles.Values.OrderBy(item => item.Target).ToArray();
    }

    public bool TryGet(SemanticQueryTarget target, out ISemanticSummaryProfile profile)
    {
        return _profiles.TryGetValue(target, out profile!);
    }
}

public abstract class SemanticSummaryProfileBase : ISemanticSummaryProfile
{
    public abstract SemanticQueryTarget Target { get; }

    public abstract IReadOnlyList<string> ExampleQuestions { get; }

    public SemanticSummaryResponseContract ResponseContract { get; } = SemanticSummaryResponseContract.Default;

    protected abstract IReadOnlyDictionary<string, string> FieldLabels { get; }

    public string GetFieldLabel(string field)
    {
        return FieldLabels.TryGetValue(field, out var label)
            ? label
            : field;
    }

    public abstract SemanticSummaryDto Build(
        SemanticQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        string scope);

    protected static SemanticMetricItemDto Metric(string name, string label, string value)
    {
        return new SemanticMetricItemDto(name, label, value);
    }
}
