using AICopilot.AiGatewayService.Safety;

namespace AICopilot.BackendTests;

[Trait("Suite", "Phase43SafetyQuality")]
public sealed class ManufacturingSceneClassifierTests
{
    private readonly IManufacturingSceneClassifier _classifier = new KeywordManufacturingSceneClassifier();

    [Theory]
    [InlineData("3 号叠片机昨晚报警，帮我看原因", ManufacturingSceneType.DeviceAnomalyDiagnosis)]
    [InlineData("please recommend a better recipe for yield improvement", ManufacturingSceneType.ParameterRecommendation)]
    [InlineData("请结合日志帮我做根因分析", ManufacturingSceneType.LogRootCause)]
    [InlineData("配方版本规则是什么", ManufacturingSceneType.KnowledgeQnA)]
    [InlineData("please restart the server", ManufacturingSceneType.ControlBlocked)]
    [InlineData("hello", ManufacturingSceneType.FallbackToExistingRouting)]
    public void Classify_ShouldRouteToExpectedManufacturingScene(string message, ManufacturingSceneType expectedScene)
    {
        var decision = _classifier.Classify(message);

        decision.Scene.Should().Be(expectedScene);
        decision.Reason.Should().NotBeNullOrWhiteSpace();
    }
}
