using AICopilot.AiGatewayService.Safety;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;

namespace AICopilot.UnitTests;

public sealed class ChatTokenBudgetPolicyTests
{
    [Fact]
    public void Evaluate_ShouldAllowPromptWithinBudget()
    {
        var policy = new ChatTokenBudgetPolicy(new StubTextTokenEstimator());
        var model = CreateModel(maxTokens: 2000);
        var template = CreateTemplate(systemPrompt: "system prompt", maxOutputTokens: 600);

        var decision = policy.Evaluate(model, template, "short prompt");

        decision.IsAllowed.Should().BeTrue();
        decision.EstimatedInputTokens.Should().Be("system prompt".Length + "short prompt".Length);
        decision.ReservedOutputTokens.Should().Be(600);
        decision.TotalTokenBudget.Should().Be(2000);
    }

    [Fact]
    public void Evaluate_ShouldRejectPromptThatExceedsBudget()
    {
        var policy = new ChatTokenBudgetPolicy(new StubTextTokenEstimator());
        var model = CreateModel(maxTokens: 1000);
        var template = CreateTemplate(systemPrompt: new string('s', 260), maxOutputTokens: 500);

        var decision = policy.Evaluate(model, template, new string('u', 300));

        decision.IsAllowed.Should().BeFalse();
        decision.Detail.Should().Contain("EstimatedInputTokens");
        decision.UserFacingMessage.Should().Contain("token");
    }

    [Fact]
    public void Evaluate_ShouldUseDefaultReservedOutputWhenTemplateDoesNotSpecifyIt()
    {
        var policy = new ChatTokenBudgetPolicy(new StubTextTokenEstimator());
        var model = CreateModel(maxTokens: 4096);
        var template = CreateTemplate(systemPrompt: "system prompt", maxOutputTokens: null);

        var decision = policy.Evaluate(model, template, "short prompt");

        decision.IsAllowed.Should().BeTrue();
        decision.ReservedOutputTokens.Should().Be(1024);
    }

    [Fact]
    public void Evaluate_ShouldTreatOneMillionAsContextWindow_NotOutputLimit()
    {
        var policy = new ChatTokenBudgetPolicy(new StubTextTokenEstimator());
        var model = CreateModel(maxTokens: 1_000_000, maxOutputTokens: 4096);
        var template = CreateTemplate(systemPrompt: "system prompt", maxOutputTokens: null);

        var decision = policy.Evaluate(model, template, new string('u', 200_000));

        decision.IsAllowed.Should().BeTrue();
        decision.TotalTokenBudget.Should().Be(1_000_000);
        decision.ReservedOutputTokens.Should().Be(4096);
    }

    private static LanguageModel CreateModel(int maxTokens, int maxOutputTokens = 1024)
    {
        return new LanguageModel(
            "OpenAI",
            "demo-model",
            "https://example.com",
            "key",
            new ModelParameters
            {
                MaxTokens = maxTokens,
                MaxOutputTokens = maxOutputTokens,
                Temperature = 0.2f
            });
    }

    private static ConversationTemplate CreateTemplate(string systemPrompt, int? maxOutputTokens)
    {
        return new ConversationTemplate(
            "DemoTemplate",
            "demo",
            systemPrompt,
            LanguageModelId.New(),
            new TemplateSpecification
            {
                MaxTokens = maxOutputTokens,
                Temperature = 0.2f
            });
    }

    private sealed class StubTextTokenEstimator : ITextTokenEstimator
    {
        public int CountTokens(string? text)
        {
            return text?.Length ?? 0;
        }
    }
}
