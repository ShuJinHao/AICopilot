using System.ComponentModel;
using System.Text.Json.Serialization;
using AICopilot.Visualization;
using AICopilot.Visualization.Widgets;

namespace AICopilot.Services.Contracts;

public static class DataAnalysisPluginNames
{
    public const string DataAnalysisPlugin = "DataAnalysisPlugin";
}

public interface ISqlDialectInstructionProvider
{
    string GetInstructions(DatabaseProviderType providerType);
}

public interface IDataAnalysisVisualizationContext
{
    void CaptureResult(IEnumerable<dynamic> resultSet, IEnumerable<SchemaColumn> schema);

    void CaptureOutput(AnalysisDto? analysis, VisualDecisionDto? decision);

    (IEnumerable<dynamic>? Data, IEnumerable<SchemaColumn>? Schema) GetLastResult();

    DataAnalysisOutputDto GetOutput();

    bool HasData { get; }
}

public enum SemanticQueryTarget
{
    Device = 1,
    DeviceLog = 2,
    Recipe = 3,
    Capacity = 4,
    ProductionData = 5
}

public enum SemanticQueryKind
{
    List = 1,
    Detail = 2,
    Status = 3,
    Latest = 4,
    Range = 5,
    ByLevel = 6,
    VersionHistory = 7,
    ByDevice = 8,
    ByProcess = 9
}

public enum SemanticFilterOperator
{
    Equal = 1,
    Contains = 2,
    GreaterOrEqual = 3,
    LessOrEqual = 4,
    In = 5
}

public enum SemanticSortDirection
{
    Asc = 1,
    Desc = 2
}

public sealed record SemanticProjection(
    IReadOnlyList<string> Fields);

public sealed record SemanticFilter(
    string Field,
    SemanticFilterOperator Operator,
    string Value);

public sealed record SemanticSort(
    string Field,
    SemanticSortDirection Direction);

public sealed record SemanticTimeRange(
    string Field,
    DateTimeOffset? Start,
    DateTimeOffset? End);

public sealed record SemanticQueryPlan(
    string Intent,
    SemanticQueryTarget Target,
    SemanticQueryKind Kind,
    string? QueryText,
    SemanticProjection Projection,
    IReadOnlyList<SemanticFilter> Filters,
    SemanticTimeRange? TimeRange,
    SemanticSort? Sort,
    int Limit);

public sealed record SemanticPlanningResult(
    bool IsSuccess,
    SemanticQueryPlan? Plan,
    string? ErrorMessage)
{
    public static SemanticPlanningResult Success(SemanticQueryPlan plan)
    {
        return new SemanticPlanningResult(true, plan, null);
    }

    public static SemanticPlanningResult Failure(string errorMessage)
    {
        return new SemanticPlanningResult(false, null, errorMessage);
    }
}

public sealed record SemanticIntentDescriptor(
    string Intent,
    SemanticQueryTarget Target,
    SemanticQueryKind Kind,
    string Description,
    IReadOnlyList<string> DefaultFields,
    string? DefaultSortField,
    SemanticSortDirection DefaultSortDirection,
    int DefaultLimit,
    IReadOnlyList<string>? RequiredAllFilterFields = null,
    IReadOnlyList<string>? RequiredAnyFilterFields = null,
    bool RequiresTimeRange = false);

public sealed record GeneratedSemanticSql(
    string SqlText,
    IReadOnlyDictionary<string, object?> Parameters);

public abstract class SemanticEntityDefinition
{
    private readonly HashSet<string> allowedProjectionFields;
    private readonly HashSet<string> allowedFilterFields;
    private readonly HashSet<string> allowedSortFields;
    private readonly Dictionary<SemanticQueryKind, SemanticProjection> defaultProjections;

    protected SemanticEntityDefinition(
        SemanticQueryTarget target,
        IReadOnlyList<string> synonyms,
        IEnumerable<string> allowedProjectionFields,
        IEnumerable<string> allowedFilterFields,
        IEnumerable<string> allowedSortFields,
        IReadOnlyDictionary<SemanticQueryKind, SemanticProjection> defaultProjections,
        int defaultLimit,
        int maxLimit)
    {
        Target = target;
        Synonyms = synonyms;
        this.allowedProjectionFields = new HashSet<string>(allowedProjectionFields, StringComparer.OrdinalIgnoreCase);
        this.allowedFilterFields = new HashSet<string>(allowedFilterFields, StringComparer.OrdinalIgnoreCase);
        this.allowedSortFields = new HashSet<string>(allowedSortFields, StringComparer.OrdinalIgnoreCase);
        this.defaultProjections = defaultProjections.ToDictionary(item => item.Key, item => item.Value);
        DefaultLimit = defaultLimit;
        MaxLimit = maxLimit;
    }

    public SemanticQueryTarget Target { get; }

    public IReadOnlyList<string> Synonyms { get; }

    public IReadOnlyCollection<string> AllowedProjectionFields => allowedProjectionFields;

    public IReadOnlyCollection<string> AllowedFilterFields => allowedFilterFields;

    public IReadOnlyCollection<string> AllowedSortFields => allowedSortFields;

    public int DefaultLimit { get; }

    public int MaxLimit { get; }

    public bool IsProjectionFieldAllowed(string field)
    {
        return allowedProjectionFields.Contains(field);
    }

    public bool IsFilterFieldAllowed(string field)
    {
        return allowedFilterFields.Contains(field);
    }

    public bool IsSortFieldAllowed(string field)
    {
        return allowedSortFields.Contains(field);
    }

    public SemanticProjection GetDefaultProjection(SemanticQueryKind kind)
    {
        return defaultProjections.TryGetValue(kind, out var projection)
            ? projection
            : new SemanticProjection(allowedProjectionFields.ToArray());
    }
}

public interface ISemanticDefinitionCatalog
{
    IReadOnlyCollection<SemanticEntityDefinition> GetAll();

    SemanticEntityDefinition Get(SemanticQueryTarget target);
}

public interface ISemanticIntentCatalog
{
    IReadOnlyCollection<SemanticIntentDescriptor> GetAll();

    bool TryGet(string intent, out SemanticIntentDescriptor descriptor);
}

public sealed class SemanticPhysicalMapping
{
    private readonly HashSet<string> allowedProjectionFields;
    private readonly HashSet<string> allowedFilterFields;
    private readonly HashSet<string> allowedSortFields;

    public SemanticPhysicalMapping(
        SemanticQueryTarget target,
        DatabaseProviderType provider,
        string sourceName,
        IReadOnlyDictionary<string, string> fieldMappings,
        IEnumerable<string> allowedProjectionFields,
        IEnumerable<string> allowedFilterFields,
        IEnumerable<string> allowedSortFields,
        string? databaseName = null,
        string? fromClause = null,
        SemanticSort? defaultSort = null,
        IEnumerable<SemanticFilter>? defaultFilters = null)
    {
        Target = target;
        Provider = provider;
        SourceName = sourceName;
        DatabaseName = databaseName;
        FromClause = fromClause;
        FieldMappings = fieldMappings;
        this.allowedProjectionFields = new HashSet<string>(allowedProjectionFields, StringComparer.OrdinalIgnoreCase);
        this.allowedFilterFields = new HashSet<string>(allowedFilterFields, StringComparer.OrdinalIgnoreCase);
        this.allowedSortFields = new HashSet<string>(allowedSortFields, StringComparer.OrdinalIgnoreCase);
        DefaultSort = defaultSort;
        DefaultFilters = defaultFilters?.ToArray() ?? [];
    }

    public SemanticQueryTarget Target { get; }

    public DatabaseProviderType Provider { get; }

    public string SourceName { get; }

    public string? DatabaseName { get; }

    public string? FromClause { get; }

    public IReadOnlyDictionary<string, string> FieldMappings { get; }

    public SemanticSort? DefaultSort { get; }

    public IReadOnlyList<SemanticFilter> DefaultFilters { get; }

    public bool IsProjectionFieldAllowed(string field)
    {
        return allowedProjectionFields.Contains(field);
    }

    public bool IsFilterFieldAllowed(string field)
    {
        return allowedFilterFields.Contains(field);
    }

    public bool IsSortFieldAllowed(string field)
    {
        return allowedSortFields.Contains(field);
    }
}

public interface ISemanticPhysicalMappingProvider
{
    bool TryGetMapping(SemanticQueryTarget target, out SemanticPhysicalMapping mapping);
}

public interface ISemanticQueryPlanner
{
    SemanticPlanningResult Plan(string intent, string? query);
}

public interface ISemanticSqlGenerator
{
    GeneratedSemanticSql Generate(SemanticQueryPlan plan, SemanticPhysicalMapping mapping);
}

public record DataAnalysisOutputDto
{
    [JsonPropertyName("analysis")]
    public AnalysisDto? Analysis { get; set; }

    [JsonPropertyName("visual_decision")]
    public VisualDecisionDto? Decision { get; set; }
}

public record AnalysisDto
{
    [Description("业务化只读数据源标签，不包含内部数据库名、物理表名或连接信息")]
    [JsonPropertyName("source_label")]
    public string SourceLabel { get; set; } = string.Empty;

    [Description("根据用户问题生成的简短数据说明")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [Description("查询结果中每个字段的业务定义")]
    [JsonPropertyName("metadata")]
    public List<MetadataItemDto> Metadata { get; set; } = [];
}

public record MetadataItemDto
{
    [Description("字段名")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Description("从表结构或语义映射中获取的字段注释/说明")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public record VisualDecisionDto
{
    [Description("图表类型，可选值：Chart、DataTable、StatsCard")]
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WidgetType Type { get; set; }

    [Description("图表标题")]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [Description("根据用户问题生成的简短图表说明")]
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [Description("可选字段，Chart 类型图表专有字段")]
    [JsonPropertyName("chart_config")]
    public ChartConfig? ChartConfig { get; set; }

    [Description("可选字段，StatsCard 类型图表专有字段")]
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }
}

public record ChartConfig
{
    [Description("图表类型，可选值：Line、Bar、Pie")]
    [JsonPropertyName("category")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChartCategory Category { get; set; }

    [Description("作为 X 轴的字段名，例如 order_date")]
    [JsonPropertyName("x")]
    public string X { get; set; } = string.Empty;

    [Description("作为 Y 轴的数值字段名，例如 total_amount")]
    [JsonPropertyName("y")]
    public string Y { get; set; } = string.Empty;

    [Description("可选，用于分组的字段名，例如 product_category")]
    [JsonPropertyName("series")]
    public string? Series { get; set; }
}

