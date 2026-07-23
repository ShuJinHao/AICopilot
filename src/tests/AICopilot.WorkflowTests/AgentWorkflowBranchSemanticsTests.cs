using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.WorkflowTests;

public sealed class AgentWorkflowBranchSemanticsTests
{
    [Fact]
    public void BranchResult_ShouldDistinguishSkippedEmptySucceededAndFailed()
    {
        BranchResult.Skipped(BranchType.Knowledge).Status.Should().Be(BranchExecutionStatus.Skipped);
        BranchResult.FromKnowledge(string.Empty).Status.Should().Be(BranchExecutionStatus.Empty);
        BranchResult.FromDataAnalysis("safe context").Status.Should().Be(BranchExecutionStatus.Succeeded);

        var failed = BranchResult.Failed(
            BranchType.BusinessPolicy,
            AppProblemCodes.ChatStreamFailed,
            "safe failure");

        failed.Status.Should().Be(BranchExecutionStatus.Failed);
        failed.FailureCode.Should().Be(AppProblemCodes.ChatStreamFailed);
        failed.SafeMessage.Should().Be("safe failure");
        failed.BusinessPolicy.Should().BeNull();
    }

    [Fact]
    public async Task RunBranchSafelyAsync_ShouldMarkExceptionAsFailedWithoutUsingEmptyResult()
    {
        var result = await AgentWorkflowPipeline.RunBranchSafelyAsync(
            BranchType.DataAnalysis,
            isRequired: true,
            () => Task.FromException<BranchResult>(new InvalidOperationException("raw-secret-value")),
            NullLogger.Instance,
            CancellationToken.None);

        result.Type.Should().Be(BranchType.DataAnalysis);
        result.Status.Should().Be(BranchExecutionStatus.Failed);
        result.IsRequired.Should().BeTrue();
        result.FailureCode.Should().Be(AppProblemCodes.ChatStreamFailed);
        result.SafeMessage.Should().NotContain("raw-secret-value");
        result.DataAnalysis.Should().BeNull();
    }

    [Fact]
    public async Task RunBranchSafelyAsync_ShouldKeepLegitimateEmptyResult()
    {
        var result = await AgentWorkflowPipeline.RunBranchSafelyAsync(
            BranchType.Knowledge,
            isRequired: true,
            () => Task.FromResult(BranchResult.Empty(BranchType.Knowledge)),
            NullLogger.Instance,
            CancellationToken.None);

        result.Status.Should().Be(BranchExecutionStatus.Empty);
        result.IsRequired.Should().BeTrue();
        result.FailureCode.Should().BeNull();
    }

    [Fact]
    public void RequiredBranchFailure_ShouldCreateStableSanitizedErrorChunk()
    {
        var failureChunk = AgentWorkflowPipeline.CreateRequiredBranchFailureChunk(
        [
            BranchResult.Failed(
                    BranchType.DataAnalysis,
                    AppProblemCodes.ChatStreamFailed,
                    "Password=raw-secret-value")
                .WithRequirement(true)
        ]);

        failureChunk.Should().NotBeNull();
        failureChunk!.Type.Should().Be(ChunkType.Error);
        using var payload = JsonDocument.Parse(failureChunk.Content);
        payload.RootElement.GetProperty("code").GetString().Should().Be(AppProblemCodes.ChatStreamFailed);
        payload.RootElement.GetProperty("userFacingMessage").GetString().Should().Contain("停止生成最终回答");
        failureChunk.Content.Should().NotContain("raw-secret-value");
        failureChunk.Content.Should().NotContain("Password=");
    }

    [Fact]
    public void OptionalBranchFailure_ShouldNotBlockFinalSynthesis()
    {
        var failureChunk = AgentWorkflowPipeline.CreateRequiredBranchFailureChunk(
        [
            BranchResult.Failed(
                    BranchType.Knowledge,
                    AppProblemCodes.ChatStreamFailed,
                    "safe failure")
                .WithRequirement(false),
            BranchResult.FromDataAnalysis("safe context").WithRequirement(true)
        ]);

        failureChunk.Should().BeNull();
    }

    [Fact]
    public void ContextAggregator_ShouldUseOnlySucceededBranchPayloads()
    {
        var sessionId = Guid.NewGuid();
        var succeeded = AgentWorkflowEvidenceNormalizer.Normalize(
            BranchResult.FromDataAnalysis("verified rows"),
            sessionId);
        succeeded.IsSuccess.Should().BeTrue();
        var aggregator = new ContextAggregatorExecutor(NullLogger<ContextAggregatorExecutor>.Instance);
        var context = aggregator.Execute(
            new ChatStreamRequest(sessionId, "query"),
            ManufacturingSceneType.DeviceAnomalyDiagnosis,
            [
                succeeded.Value!,
                BranchResult.Empty(BranchType.Knowledge),
                BranchResult.Failed(
                    BranchType.BusinessPolicy,
                    AppProblemCodes.ChatStreamFailed,
                    "safe failure")
            ]);

        context.DataAnalysisContext.Should().Be("verified rows");
        context.KnowledgeContext.Should().BeEmpty();
        context.BusinessPolicyContext.Should().BeEmpty();
        context.Tools.Should().BeEmpty();
    }

    [Fact]
    public void RoutingResponseLogMetadata_ShouldExposeOnlyLengthHashTypeAndParseState()
    {
        const string rawResponse = "[{\"intent\":\"General.Chat\",\"reasoning\":\"raw-secret-value\"}]";
        var logger = new CapturingLogger<IntentRoutingExecutor>();

        IntentRoutingExecutor.LogResponseMetadata(logger, rawResponse, parsed: true);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawResponse)))
            .ToLowerInvariant();
        var message = logger.Entries.Should().ContainSingle().Subject.Message;

        message.Should().Contain($"ResponseLength={rawResponse.Length}");
        message.Should().Contain($"ResponseSha256={expectedHash}");
        message.Should().Contain("ResponseType=JsonArray");
        message.Should().Contain("Parsed=True");
        message.Should().NotContain("raw-secret-value");
        message.Should().NotContain("General.Chat");
    }

    [Fact]
    public void BranchRelevance_ShouldComeFromCurrentRoutingIntents()
    {
        var intents = new List<IntentResult>
        {
            new() { Intent = "Analysis.Device.List", Confidence = 0.9 },
            new() { Intent = "Knowledge.Operations", Confidence = 0.7 },
            new() { Intent = "Action.export_report", Confidence = 0.9 }
        };
        var registry = AgentIntentRegistryV1.CreateRoutingSnapshot(
        [
            new("Analysis.Device.List", "Device list"),
            new("Knowledge.Operations", "Operations knowledge"),
            new("Action.export_report", "Read-only report export")
        ]);

        DataAnalysisExecutor.IsRelevant(intents, registry).Should().BeTrue();
        KnowledgeRetrievalExecutor.IsRelevant(intents, registry).Should().BeTrue();

        var generalChat = new[] { new IntentResult { Intent = "General.Chat", Confidence = 1.0 } };
        DataAnalysisExecutor.IsRelevant(generalChat, registry).Should().BeFalse();
        KnowledgeRetrievalExecutor.IsRelevant(generalChat, registry).Should().BeFalse();
    }
}
