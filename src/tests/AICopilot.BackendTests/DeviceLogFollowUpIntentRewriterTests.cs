using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

public sealed class DeviceLogFollowUpIntentRewriterTests
{
    private readonly ISemanticQueryPlanner _planner;

    public DeviceLogFollowUpIntentRewriterTests()
    {
        var definitions = new SemanticDefinitionCatalog();
        var intents = new SemanticIntentCatalog(definitions);
        _planner = new SemanticQueryPlanner(intents, definitions);
    }

    [Fact]
    public void Rewrite_ShouldForceDeviceLogRequery_ForWarningFollowUp()
    {
        var intents = new List<IntentResult>
        {
            new()
            {
                Intent = "General.Chat",
                Confidence = 0.91,
                Query = "警告信息呢 也是没有吗"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "替我查询下模切设备最近1天的日志并帮我分析错误信息"),
            new(AiChatRole.Assistant, "当前未找到 Error 级别日志。"),
            new(AiChatRole.User, "警告信息呢 也是没有吗")
        };

        DeviceLogFollowUpIntentRewriter.Rewrite(intents, history);

        intents.Should().ContainSingle(intent => intent.Intent == "Analysis.DeviceLog.ByLevel");
        var analysisIntent = intents.Single(intent => intent.Intent == "Analysis.DeviceLog.ByLevel");
        analysisIntent.Confidence.Should().BeGreaterThan(0.9);

        var planning = _planner.Plan(analysisIntent.Intent, analysisIntent.Query);

        planning.IsSuccess.Should().BeTrue(planning.ErrorMessage);
        planning.Plan.Should().NotBeNull();
        planning.Plan!.Filters.Should().Contain(filter =>
            filter.Field == "level" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "WARN");
        planning.Plan.Filters.Should().Contain(filter =>
            filter.Field == "processName" &&
            filter.Operator == SemanticFilterOperator.Contains &&
            filter.Value == "模切");
        planning.Plan.TimeRange.Should().NotBeNull();
    }

    [Fact]
    public void Rewrite_ShouldForceInfoRequery_ForNormalInformationFollowUp()
    {
        var intents = new List<IntentResult>
        {
            new()
            {
                Intent = "General.Chat",
                Confidence = 0.9
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "查询涂布工序最近24小时日志"),
            new(AiChatRole.Assistant, "已返回 Warning 记录。"),
            new(AiChatRole.User, "那正常信息呢")
        };

        DeviceLogFollowUpIntentRewriter.Rewrite(intents, history);

        var analysisIntent = intents.Single(intent => intent.Intent == "Analysis.DeviceLog.ByLevel");
        var planning = _planner.Plan(analysisIntent.Intent, analysisIntent.Query);

        planning.IsSuccess.Should().BeTrue(planning.ErrorMessage);
        planning.Plan!.Filters.Should().Contain(filter =>
            filter.Field == "level" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "INFO");
        planning.Plan.Filters.Should().Contain(filter =>
            filter.Field == "processName" &&
            filter.Operator == SemanticFilterOperator.Contains &&
            filter.Value == "涂布");
        planning.Plan.TimeRange.Should().NotBeNull();
    }

    [Fact]
    public void Rewrite_ShouldPreferCurrentScope_WhenFollowUpChangesProcessAndLevel()
    {
        var intents = new List<IntentResult>
        {
            new()
            {
                Intent = "General.Chat",
                Confidence = 0.88,
                Query = "那涂布设备的警告呢"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "替我查询下模切设备最近1天的错误日志"),
            new(AiChatRole.Assistant, "当前未找到 Error 级别日志。"),
            new(AiChatRole.User, "那涂布设备的警告呢")
        };

        DeviceLogFollowUpIntentRewriter.Rewrite(intents, history);

        var analysisIntent = intents.Single(intent => intent.Intent == "Analysis.DeviceLog.ByLevel");
        var planning = _planner.Plan(analysisIntent.Intent, analysisIntent.Query);

        planning.IsSuccess.Should().BeTrue(planning.ErrorMessage);
        planning.Plan!.Filters.Should().Contain(filter =>
            filter.Field == "level" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "WARN");
        planning.Plan.Filters.Should().Contain(filter =>
            filter.Field == "processName" &&
            filter.Operator == SemanticFilterOperator.Contains &&
            filter.Value == "涂布");
        planning.Plan.Filters.Should().NotContain(filter =>
            filter.Field == "processName" &&
            string.Equals(filter.Value, "模切", StringComparison.Ordinal));
        planning.Plan.TimeRange.Should().NotBeNull();
    }

    [Fact]
    public void Rewrite_ShouldTreatScopeOnlyFollowUpAsDeviceLogRequery()
    {
        var intents = new List<IntentResult>
        {
            new()
            {
                Intent = "General.Chat",
                Confidence = 0.86,
                Query = "那涂布设备呢"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "替我查询下模切设备最近1天的错误日志"),
            new(AiChatRole.Assistant, "当前未找到 Error 级别日志。"),
            new(AiChatRole.User, "那涂布设备呢")
        };

        DeviceLogFollowUpIntentRewriter.Rewrite(intents, history);

        var analysisIntent = intents.Single(intent => intent.Intent == "Analysis.DeviceLog.ByLevel");
        var planning = _planner.Plan(analysisIntent.Intent, analysisIntent.Query);

        planning.IsSuccess.Should().BeTrue(planning.ErrorMessage);
        planning.Plan!.Filters.Should().Contain(filter =>
            filter.Field == "level" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "ERROR");
        planning.Plan.Filters.Should().Contain(filter =>
            filter.Field == "processName" &&
            filter.Operator == SemanticFilterOperator.Contains &&
            filter.Value == "涂布");
        planning.Plan.Filters.Should().NotContain(filter =>
            filter.Field == "processName" &&
            string.Equals(filter.Value, "模切", StringComparison.Ordinal));
        planning.Plan.TimeRange.Should().NotBeNull();
    }

    [Fact]
    public void Rewrite_ShouldNotRewriteScopeOnlyQuestionWithoutDeviceLogHistory()
    {
        var intents = new List<IntentResult>
        {
            new()
            {
                Intent = "General.Chat",
                Confidence = 0.87,
                Query = "那涂布设备呢"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "你好"),
            new(AiChatRole.Assistant, "你好，有什么可以帮你？"),
            new(AiChatRole.User, "那涂布设备呢")
        };

        DeviceLogFollowUpIntentRewriter.Rewrite(intents, history);

        intents.Should().ContainSingle();
        intents[0].Intent.Should().Be("General.Chat");
        intents[0].Query.Should().Be("那涂布设备呢");
    }
}
