using System.Text.RegularExpressions;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;

namespace AICopilot.BackendTests;

public sealed class ClaudeFollowupClosureTests
{
    [Fact]
    public void AgentTaskStatusValues_ShouldPreserveHistoricalPersistenceValues()
    {
        ((int)AgentTaskStatus.WaitingPlanApproval).Should().Be(1);
        ((int)AgentTaskStatus.PlanApproved).Should().Be(2);
        ((int)AgentTaskStatus.Running).Should().Be(3);
        ((int)AgentTaskStatus.WaitingToolApproval).Should().Be(4);
        ((int)AgentTaskStatus.GeneratingArtifacts).Should().Be(5);
        ((int)AgentTaskStatus.WorkspaceReady).Should().Be(6);
        ((int)AgentTaskStatus.WaitingFinalApproval).Should().Be(7);
        ((int)AgentTaskStatus.Finalized).Should().Be(8);
        ((int)AgentTaskStatus.Completed).Should().Be(9);
        ((int)AgentTaskStatus.Rejected).Should().Be(10);
        ((int)AgentTaskStatus.Failed).Should().Be(11);
        ((int)AgentTaskStatus.Cancelled).Should().Be(12);
        ((int)AgentTaskStatus.Draft).Should().Be(100);
    }

    [Fact]
    public async Task AgentWorkflowSink_ShouldFlushWrittenChunksBeforeCompletion()
    {
        var sink = new AgentWorkflowSink();
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "first"), CancellationToken.None);
        await sink.WriteAsync(new ChatChunk("data-analysis", ChunkType.Text, "second"), CancellationToken.None);

        sink.Complete();

        var chunks = await ReadAllAsync(sink);

        chunks.Select(chunk => chunk.Content).Should().Equal("first", "second");
    }

    [Fact]
    public async Task AgentWorkflowSink_ShouldPropagateBranchFailureToReader()
    {
        var sink = new AgentWorkflowSink();
        var failure = new InvalidOperationException("branch failed");

        sink.Complete(failure);
        var read = async () => await ReadAllAsync(sink);

        await read.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("branch failed");
    }

    [Fact]
    public void AgentWorkflowPipeline_ShouldUseExplicitSinkCompletionFlow()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "AgentWorkflowPipeline.cs"));

        source.Should().NotContain(".ContinueWith(");
        source.Should().Contain("CompleteSinkWhenBranchesFinishAsync");
        source.Should().Contain("await branchTask.ConfigureAwait(false)");
        source.Should().Contain("sink.Complete(ex)");
    }

    [Fact]
    public void AgentWorkflowTopology_ShouldDeclareParallelFanOutBranches()
    {
        AgentWorkflowTopology.Stages
            .Where(stage => stage.Kind == AgentWorkflowStageKind.ParallelFanOut)
            .Select(stage => stage.Id)
            .Should()
            .ContainSingle()
            .Which
            .Should()
            .Be("ParallelFanOut");

        AgentWorkflowTopology.ParallelBranches
            .OrderBy(branch => branch.Order)
            .Select(branch => branch.BranchType)
            .Should()
            .Equal(
                BranchType.Tools,
                BranchType.Knowledge,
                BranchType.DataAnalysis,
                BranchType.BusinessPolicy);
    }

    [Fact]
    public void AgentWorkflowPipeline_ShouldUseTopologyWithoutFlatteningParallelBranches()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "AgentWorkflowPipeline.cs"));
        var method = ExtractMethodBody(source, "RunIntentWorkflowAsync");

        method.Should().Contain("AgentWorkflowTopology.ParallelBranches");
        method.Should().Contain("RunBranchSafelyAsync");
        method.Should().Contain("Task.WhenAll(branchTasks)");
        method.Should().Contain("AgentWorkflowSink");
        method.Should().NotContain("await ExecuteBranchAsync");
    }

    [Fact]
    public void AgentWorkflowPipeline_PlanDraft_ShouldUseCapabilityDiscoveryOnly()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "AgentWorkflowPipeline.cs"));
        var method = ExtractMethodBody(source, "RunPlanDraftWorkflowAsync");

        method.Should().Contain("toolsPack.DiscoverAsync");
        method.Should().NotContain("toolsPack.ExecuteAsync");
        method.Should().NotContain("dataAnalysis.ExecuteAsync");
        method.Should().NotContain("agentRun.ExecuteAsync");
        method.Should().NotContain("RunAgentTaskCommand");
    }

    [Fact]
    public void PlanAgentTaskStreamHandler_ShouldOnlyCreatePlanDraftAndEmitAgentTaskChunk()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "PlanAgentTaskStreamHandler.cs"));

        source.Should().Contain("new PlanAgentTaskCommand");
        source.Should().Contain("ChunkType.AgentTask");
        source.Should().Contain("ChunkType.AgentEvent");
        source.Should().Contain("\"plan_draft_started\"");
        source.Should().Contain("\"plan_draft_ready\"");
        source.Should().Contain("\"plan_draft_failed\"");
        source.Should().NotContain("RunAgentTaskCommand");
        source.Should().NotContain("RetryAgentTaskCommand");
        source.Should().NotContain("IAgentTaskRunQueue");
    }

    [Fact]
    public void PlanAgentTaskStreamEndpoint_ShouldUseServerSentEvents()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "hosts",
            "AICopilot.HttpApi",
            "Controllers",
            "AiGatewayAgentTaskController.cs"));

        source.Should().Contain("[HttpPost(\"agent/task/plan-stream\")]");
        source.Should().Contain("Sender.CreateStream(request)");
        source.Should().Contain("Results.ServerSentEvents(stream)");
        source.Should().NotContain("[HttpPost(\"agent/task/plan\")]");
    }

    [Fact]
    public void PlanAgentTaskCommand_ShouldCreatePlanDraftWithoutExecutableToolPreparation()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "PlanAgentTaskCoordinator.cs"));

        source.Should().Contain("workflowPipeline.RunPlanDraftWorkflowAsync");
        source.Should().Contain("PlanKind: AgentTaskPlanKinds.PlanDraft");
        source.Should().Contain("IsExecutable: false");
        source.Should().NotContain("planToolGuard.GetAvailableToolCatalogAsync");
        source.Should().NotContain("dynamicPlanner.CreatePlanAsync");
        source.Should().NotContain("cloudReadonlyPlanService.CreateIntentAsync");
        source.Should().NotContain("ResolvePlannerModelAsync");
        source.Should().NotContain("AppProblemCodes.PlannerToolCatalogEmpty");
        source.Should().NotContain("ValidateStepsAsync(");
    }

    [Fact]
    public void PlanAgentTaskCommand_ShouldNotCreateArtifactWorkspaceForPlanDraft()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "PlanAgentTaskCoordinator.cs"));

        source.Should().NotContain("IAgentArtifactWorkspaceService");
        source.Should().NotContain("CreateForTaskAsync(task");
        source.Should().NotContain("task.AttachWorkspace");
        source.Should().Contain("AgentTaskDtoMapper.Map(task, pendingApprovalCount: 1)");
    }

    [Fact]
    public void PlanDraftConfirmationService_ShouldOwnExecutablePlanToolValidation()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentPlanDraftConfirmationService.cs"));

        source.Should().Contain("planToolGuard.ValidateStepsAsync");
        source.Should().Contain("cloudReadonlyPlanService.CreateIntentAsync");
        source.Should().Contain("CloudReadonlyIntent = cloudReadonlyIntent");
        source.Should().Contain("PlanKind = AgentTaskPlanKinds.ExecutablePlan");
        source.Should().Contain("IsExecutable = true");
        source.Should().Contain("task.ConfirmExecutablePlan");
    }

    [Fact]
    public void ApprovePlanCommand_ShouldConfirmDraftBeforeApprovingAuditRecord()
    {
        var solutionRoot = FindSolutionRoot();
        var source = File.ReadAllText(Path.Combine(
            solutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskLifecycleCommandHandlers.cs"));

        var confirmationIndex = source.IndexOf("planDraftConfirmationService.ConfirmAsync", StringComparison.Ordinal);
        var taskApprovalIndex = source.IndexOf("task.ApprovePlan(now)", StringComparison.Ordinal);
        var approvalRecordIndex = source.IndexOf("approval.Approve(userId", StringComparison.Ordinal);

        confirmationIndex.Should().BeGreaterThanOrEqualTo(0);
        taskApprovalIndex.Should().BeGreaterThan(confirmationIndex);
        approvalRecordIndex.Should().BeGreaterThan(taskApprovalIndex);
    }

    [Fact]
    public void SharedAgentRuntimeTypes_ShouldUseAgentPrefix_NotChatPrefix()
    {
        var solutionRoot = FindSolutionRoot();
        var forbiddenSharedRuntimeNames = new[]
        {
            "ChatAgentFactory",
            "IChatExecutionMetadataAccessor",
            "ChatExecutionMetadataAccessor",
            "IChatStreamRuntime",
            "ChatStreamRuntime",
            "ChatWorkflowException",
            "IChatRuntimeSettingsProvider",
            "ChatRuntimeSettingsProvider"
        };

        var violations = new List<string>();
        foreach (var file in EnumerateProductionSources(solutionRoot))
        {
            var normalizedFile = NormalizePath(file);
            var source = File.ReadAllText(file);
            foreach (var name in forbiddenSharedRuntimeNames)
            {
                if (source.Contains(name, StringComparison.Ordinal))
                {
                    violations.Add($"{normalizedFile}: {name}");
                }
            }
        }

        violations.Should().BeEmpty(
            "shared agent runtime infrastructure must use Agent* naming so it is not mistaken for chat-only code.");
    }

    [Fact]
    public void Authorization_ShouldNotUseJwtRoleClaimAsAuthorizationSource()
    {
        var solutionRoot = FindSolutionRoot();
        var roleAuthorizationPatterns = new[]
        {
            new Regex(@"\[Authorize\s*\([^\)]*Roles\s*=", RegexOptions.CultureInvariant),
            new Regex(@"RequireRole\s*\(", RegexOptions.CultureInvariant),
            new Regex(@"new\s+AuthorizeAttribute\s*\{[^}]*Roles\s*=", RegexOptions.CultureInvariant)
        };
        var allowedClaimTypeRoleFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePath(Path.Combine(
                solutionRoot,
                "src",
                "hosts",
                "AICopilot.HttpApi",
                "Infrastructure",
                "CurrentUser.cs")),
            NormalizePath(Path.Combine(
                solutionRoot,
                "src",
                "infrastructure",
                "AICopilot.Infrastructure",
                "Authentication",
                "JwtTokenGenerator.cs"))
        };

        var roleAuthorizationViolations = new List<string>();
        var claimRoleViolations = new List<string>();
        foreach (var file in EnumerateProductionSources(solutionRoot))
        {
            var normalizedFile = NormalizePath(file);
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (roleAuthorizationPatterns.Any(pattern => pattern.IsMatch(line)))
                {
                    roleAuthorizationViolations.Add($"{normalizedFile}:{index + 1}: {line.Trim()}");
                }

                if (line.Contains("ClaimTypes.Role", StringComparison.Ordinal)
                    && !allowedClaimTypeRoleFiles.Contains(normalizedFile))
                {
                    claimRoleViolations.Add($"{normalizedFile}:{index + 1}: {line.Trim()}");
                }
            }
        }

        roleAuthorizationViolations.Should().BeEmpty(
            "AICopilot must not authorize with JWT role claims; permission attributes and SecurityStamp revocation are the authority.");
        claimRoleViolations.Should().BeEmpty(
            "ClaimTypes.Role is limited to token issuance and CurrentUser audit display, not authorization decisions.");
    }

    private static async Task<List<ChatChunk>> ReadAllAsync(AgentWorkflowSink sink)
    {
        var chunks = new List<ChatChunk>();
        await foreach (var chunk in sink.ReadAllAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThanOrEqualTo(0);
        var openBrace = source.IndexOf('{', methodIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[openBrace..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not extract method body for {methodName}.");
    }

    private static IEnumerable<string> EnumerateProductionSources(string solutionRoot)
    {
        var roots = new[]
        {
            Path.Combine(solutionRoot, "src", "core"),
            Path.Combine(solutionRoot, "src", "hosts"),
            Path.Combine(solutionRoot, "src", "infrastructure"),
            Path.Combine(solutionRoot, "src", "services"),
            Path.Combine(solutionRoot, "src", "shared")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the test output directory.");
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
