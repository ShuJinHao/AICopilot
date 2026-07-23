using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.DataAnalysisService.Semantics;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.UnitTests;

public sealed class DeviceLogFollowUpIntentRewriterTests
{
    private readonly ISemanticQueryPlanner _planner;

    public DeviceLogFollowUpIntentRewriterTests()
    {
        var definitions = new SemanticDefinitionCatalog();
        var intents = new SemanticQuerySchemaRegistry(definitions);
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
            new(AiChatRole.User, "替我查询 DEV-001 最近1天的日志并帮我分析错误信息"),
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
            filter.Field == "deviceCode" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "DEV-001");
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
            new(AiChatRole.User, "查询 DEV-001 最近24小时日志"),
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
            filter.Field == "deviceCode" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "DEV-001");
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
                Query = "那 DEV-002 的警告呢"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "替我查询 DEV-001 最近1天的错误日志"),
            new(AiChatRole.Assistant, "当前未找到 Error 级别日志。"),
            new(AiChatRole.User, "那 DEV-002 的警告呢")
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
            filter.Field == "deviceCode" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "DEV-002");
        planning.Plan.Filters.Should().NotContain(filter =>
            filter.Field == "deviceCode" &&
            string.Equals(filter.Value, "DEV-001", StringComparison.Ordinal));
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
                Query = "那 DEV-002 呢"
            }
        };
        var history = new List<AiChatMessage>
        {
            new(AiChatRole.User, "替我查询 DEV-001 最近1天的错误日志"),
            new(AiChatRole.Assistant, "当前未找到 Error 级别日志。"),
            new(AiChatRole.User, "那 DEV-002 呢")
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
            filter.Field == "deviceCode" &&
            filter.Operator == SemanticFilterOperator.Equal &&
            filter.Value == "DEV-002");
        planning.Plan.Filters.Should().NotContain(filter =>
            filter.Field == "deviceCode" &&
            string.Equals(filter.Value, "DEV-001", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("换成昨天的数据", true)]
    [InlineData("那 DEV-002 呢", true)]
    [InlineData("再查一下 WARN 级别日志", true)]
    [InlineData("把时间改为 2026-07-01 到 2026-07-02", true)]
    [InlineData("为什么昨天的设备结论是这样", false)]
    [InlineData("请解释这个结论的依据", false)]
    public void EvidenceReusePolicy_ShouldRequireFreshQueryOnlyForScopeChanges(
        string message,
        bool expected)
    {
        AgentTaskChatEvidenceReusePolicy.RequiresFreshReadOnlyQuery(message).Should().Be(expected);
    }
}
