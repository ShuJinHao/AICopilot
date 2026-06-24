using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Safety;
using AICopilot.AiGatewayService.Workflows;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

[Trait("Suite", "SessionPolicySemantics")]
public sealed class SessionPolicySemanticsTests
{
    private const string PluginName = nameof(AdvisoryPolicyTestPlugin);
    private const string ToolName = nameof(AdvisoryPolicyTestPlugin.Inspect);
    private static readonly string RuntimeToolName = AiToolIdentity.CreateRuntimeName(
        AiToolTargetType.Plugin,
        PluginName,
        ToolName);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ApprovalToolResolver_ShouldUseCurrentEnabledPolicies()
    {
        var policies = new List<ApprovalPolicy>();
        var resolver = CreateApprovalToolResolver(policies, out _);

        var initialTool = (await resolver.GetToolsForPluginsAsync([PluginName]))
            .Should().ContainSingle().Which;
        initialTool.Name.Should().Be(RuntimeToolName);
        initialTool.RequiresApproval.Should().BeFalse();

        var policy = CreatePluginPolicy(isEnabled: true, requiresOnsiteAttestation: false);
        policies.Add(policy);

        var enabledPolicyTool = (await resolver.GetToolsForPluginsAsync([PluginName]))
            .Should().ContainSingle().Which;
        enabledPolicyTool.RequiresApproval.Should().BeTrue();

        policy.Update(
            policy.Name,
            policy.Description,
            policy.TargetType,
            policy.TargetName,
            policy.ToolNames,
            isEnabled: false,
            policy.RequiresOnsiteAttestation);

        var disabledPolicyTool = (await resolver.GetToolsForPluginsAsync([PluginName]))
            .Should().ContainSingle().Which;
        disabledPolicyTool.RequiresApproval.Should().BeFalse();
    }

    [Fact]
    public async Task ApprovalDecisionValidator_ShouldApplyCurrentOnsitePolicyToPendingApproval()
    {
        var policies = new List<ApprovalPolicy>();
        var resolver = new ApprovalRequirementResolver(new InMemoryReadRepository<ApprovalPolicy>(policies));
        var storedApproval = CreateStoredApproval();

        policies.Add(CreatePluginPolicy(isEnabled: true, requiresOnsiteAttestation: true));

        var validation = await InvokeApprovalDecisionValidatorAsync(
            CreateApprovalDecisionRequest(storedApproval, onsiteConfirmed: false),
            storedApproval,
            CreateSessionSnapshot(hasValidOnsiteAttestation: false),
            resolver);

        GetProperty<bool>(validation, "IsValid").Should().BeFalse();
        GetProperty<ChatChunk?>(validation, "Error")!.Content.Should().Contain(AppProblemCodes.OnsitePresenceRequired);
    }

    [Fact]
    public async Task ApprovalDecisionValidator_ShouldAllowPendingApproval_WhenCurrentOnsitePolicyIsSatisfied()
    {
        var policies = new List<ApprovalPolicy>
        {
            CreatePluginPolicy(isEnabled: true, requiresOnsiteAttestation: true)
        };
        var resolver = new ApprovalRequirementResolver(new InMemoryReadRepository<ApprovalPolicy>(policies));
        var storedApproval = CreateStoredApproval();

        var validation = await InvokeApprovalDecisionValidatorAsync(
            CreateApprovalDecisionRequest(storedApproval, onsiteConfirmed: true),
            storedApproval,
            CreateSessionSnapshot(hasValidOnsiteAttestation: true),
            resolver);

        GetProperty<bool>(validation, "IsValid").Should().BeTrue();
        GetProperty<ApprovalRequirement>(validation, "Requirement")
            .RequiresOnsiteAttestation.Should().BeTrue();
    }

    [Fact]
    public async Task FinalAgentContextSerializer_ShouldRestoreStoredToolsFromCurrentRegistryAndApprovalPolicy()
    {
        var policies = new List<ApprovalPolicy>
        {
            CreatePluginPolicy(isEnabled: true, requiresOnsiteAttestation: false)
        };
        var toolResolver = CreateApprovalToolResolver(policies, out var pluginRegistry);
        var runtimeFactory = new FakeRuntimeAgentFactory();
        var model = FakeRuntimeAgentFactory.CreateModel();
        var template = FakeRuntimeAgentFactory.CreateTemplate(model);
        var session = new Session(Guid.NewGuid(), template.Id);
        var serializer = CreateFinalAgentContextSerializer(
            runtimeFactory,
            toolResolver,
            session,
            template,
            model);
        var storedContext = CreateStoredContext(session.Id, [RuntimeToolName]);

        await using (var restored = await serializer.RestoreAsync(storedContext))
        {
            var restoredTool = restored.RunOptions.Options.Tools.Should().ContainSingle().Which;
            restoredTool.Name.Should().Be(RuntimeToolName);
            restoredTool.RequiresApproval.Should().BeTrue();

            runtimeFactory.LastCreateRequest.Should().NotBeNull();
            runtimeFactory.LastCreateRequest!.Options.Tools.Should().ContainSingle(tool =>
                tool.Name == RuntimeToolName && tool.RequiresApproval);
        }

        pluginRegistry.UnregisterAgentPlugin(PluginName);

        await using var restoredAfterUnregister = await serializer.RestoreAsync(storedContext);
        restoredAfterUnregister.RunOptions.Options.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalAgentBuildExecutor_ShouldUseCurrentSessionTemplateAndModelConfiguration()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        var model = FakeRuntimeAgentFactory.CreateModel();
        model.UpdateInfo(FakeRuntimeAgentFactory.ProviderName, "current-model", "http://localhost/current-model");
        model.UpdateParameters(new ModelParameters { MaxTokens = 8192, Temperature = 0.7f });

        var template = new ConversationTemplate(
            "current-template",
            "current template",
            "Current system prompt.",
            model.Id,
            new TemplateSpecification { MaxTokens = 384, Temperature = 0.4f });
        var session = new Session(Guid.NewGuid(), template.Id);
        var tokenBudgetPolicy = new RecordingTokenBudgetPolicy();
        var executor = CreateFinalAgentBuildExecutor(
            runtimeFactory,
            tokenBudgetPolicy,
            session,
            [template],
            [model]);

        await using var context = await executor.ExecuteAsync(CreateGenerationContext(session.Id));

        tokenBudgetPolicy.LastModel.Should().BeSameAs(model);
        tokenBudgetPolicy.LastTemplate.Should().BeSameAs(template);
        runtimeFactory.LastCreateRequest.Should().NotBeNull();
        runtimeFactory.LastCreateRequest!.Model.Name.Should().Be("current-model");
        runtimeFactory.LastCreateRequest.Template.SystemPrompt.Should().Be("Current system prompt.");
        runtimeFactory.LastCreateRequest.Options.Instructions.Should().Be("Current system prompt.");
        context.RunOptions.Options.MaxOutputTokens.Should().Be(384);
    }

    [Fact]
    public async Task FinalAgentBuildExecutor_ShouldUseRequestedFinalModelAndRecordExecutionMetadata()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        var templateModel = FakeRuntimeAgentFactory.CreateModel();
        templateModel.UpdateInfo(FakeRuntimeAgentFactory.ProviderName, "template-model", "http://localhost/template-model");
        var selectedModel = FakeRuntimeAgentFactory.CreateModel();
        selectedModel.UpdateInfo(FakeRuntimeAgentFactory.ProviderName, "selected-model", "http://localhost/selected-model");
        selectedModel.UpdateParameters(new ModelParameters { MaxTokens = 1_000_000, MaxOutputTokens = 4096, Temperature = 0.2f });

        var template = new ConversationTemplate(
            "current-template",
            "current template",
            "Current system prompt.",
            templateModel.Id,
            new TemplateSpecification { MaxTokens = 384, Temperature = 0.4f });
        var session = new Session(Guid.NewGuid(), template.Id);
        var executor = CreateFinalAgentBuildExecutor(
            runtimeFactory,
            new RecordingTokenBudgetPolicy(),
            session,
            [template],
            [templateModel, selectedModel]);

        await using var context = await executor.ExecuteAsync(CreateGenerationContext(session.Id, selectedModel.Id));

        runtimeFactory.LastCreateRequest.Should().NotBeNull();
        runtimeFactory.LastCreateRequest!.Model.Id.Should().Be(selectedModel.Id);
        context.ExecutionMetadata.FinalModelId.Should().Be(selectedModel.Id.Value);
        context.ExecutionMetadata.FinalModelName.Should().Be("selected-model");
        context.ExecutionMetadata.ContextWindowTokens.Should().Be(1_000_000);
        context.ExecutionMetadata.MaxOutputTokens.Should().Be(384);
    }

    [Fact]
    public void Session_ShouldKeepSeparateModelSnapshotsForAssistantMessages()
    {
        var session = new Session(Guid.NewGuid(), ConversationTemplateId.New());
        var firstFinalModelId = Guid.NewGuid();
        var secondFinalModelId = Guid.NewGuid();
        var firstRoutingModelId = Guid.NewGuid();
        var secondRoutingModelId = Guid.NewGuid();

        session.AddMessage(
            "answer from model a",
            MessageType.Assistant,
            new MessageModelSnapshot(firstFinalModelId, "model-a", firstRoutingModelId, "router-a", 64_000, 4096));
        session.AddMessage(
            "answer from model b",
            MessageType.Assistant,
            new MessageModelSnapshot(secondFinalModelId, "model-b", secondRoutingModelId, "router-b", 1_000_000, 8192));

        var assistantMessages = session.Messages
            .Where(message => message.Type == MessageType.Assistant)
            .ToArray();

        assistantMessages.Should().HaveCount(2);
        assistantMessages[0].FinalModelId.Should().Be(firstFinalModelId);
        assistantMessages[0].FinalModelName.Should().Be("model-a");
        assistantMessages[0].RoutingModelId.Should().Be(firstRoutingModelId);
        assistantMessages[0].RoutingModelName.Should().Be("router-a");
        assistantMessages[0].ContextWindowTokens.Should().Be(64_000);
        assistantMessages[0].MaxOutputTokens.Should().Be(4096);

        assistantMessages[1].FinalModelId.Should().Be(secondFinalModelId);
        assistantMessages[1].FinalModelName.Should().Be("model-b");
        assistantMessages[1].RoutingModelId.Should().Be(secondRoutingModelId);
        assistantMessages[1].RoutingModelName.Should().Be("router-b");
        assistantMessages[1].ContextWindowTokens.Should().Be(1_000_000);
        assistantMessages[1].MaxOutputTokens.Should().Be(8192);
    }

    [Fact]
    public async Task FinalAgentBuildExecutor_ShouldReturnConfigurationMissing_WhenCurrentTemplateIsMissing()
    {
        var runtimeFactory = new FakeRuntimeAgentFactory();
        var model = FakeRuntimeAgentFactory.CreateModel();
        var missingTemplate = FakeRuntimeAgentFactory.CreateTemplate(model);
        var session = new Session(Guid.NewGuid(), missingTemplate.Id);
        var executor = CreateFinalAgentBuildExecutor(
            runtimeFactory,
            new RecordingTokenBudgetPolicy(),
            session,
            [],
            [model]);

        Func<Task> act = async () =>
        {
            await using var _ = await executor.ExecuteAsync(CreateGenerationContext(session.Id));
        };

        var exception = await act.Should().ThrowAsync<AgentWorkflowException>();
        exception.Which.Code.Should().Be(AppProblemCodes.ChatConfigurationMissing);
    }

    private static ApprovalToolResolver CreateApprovalToolResolver(
        IReadOnlyCollection<ApprovalPolicy> policies,
        out IAgentPluginRegistry pluginRegistry)
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var pluginLoader = new AgentPluginLoader([], serviceProvider);
        pluginLoader.RegisterAgentPlugin(new AdvisoryPolicyTestPlugin());
        pluginRegistry = pluginLoader;

        return new ApprovalToolResolver(
            pluginLoader,
            new ApprovalRequirementResolver(new InMemoryReadRepository<ApprovalPolicy>(policies)));
    }

    private static FinalAgentContextSerializer CreateFinalAgentContextSerializer(
        FakeRuntimeAgentFactory runtimeFactory,
        ApprovalToolResolver toolResolver,
        Session session,
        ConversationTemplate template,
        LanguageModel model)
    {
        return new FinalAgentContextSerializer(
            new ConfiguredAgentRuntimeFactory(
                new InMemoryReadRepository<ConversationTemplate>([template]),
                new InMemoryReadRepository<LanguageModel>([model]),
                runtimeFactory),
            toolResolver,
            new InMemoryReadRepository<Session>([session]));
    }

    private static FinalAgentBuildExecutor CreateFinalAgentBuildExecutor(
        FakeRuntimeAgentFactory runtimeFactory,
        ITokenBudgetPolicy tokenBudgetPolicy,
        Session session,
        IReadOnlyCollection<ConversationTemplate> templates,
        IReadOnlyCollection<LanguageModel> models)
    {
        return new FinalAgentBuildExecutor(
            new ConfiguredAgentRuntimeFactory(
                new InMemoryReadRepository<ConversationTemplate>(templates),
                new InMemoryReadRepository<LanguageModel>(models),
                runtimeFactory),
            new InMemoryReadRepository<Session>([session]),
            new InMemoryReadRepository<ConversationTemplate>(templates),
            new InMemoryReadRepository<LanguageModel>(models),
            tokenBudgetPolicy,
            NullLogger<FinalAgentBuildExecutor>.Instance);
    }

    private static GenerationContext CreateGenerationContext(Guid sessionId, Guid? finalModelId = null)
    {
        return new GenerationContext
        {
            Request = new ChatStreamRequest(sessionId, "diagnose current session", finalModelId),
            Scene = ManufacturingSceneType.DeviceAnomalyDiagnosis
        };
    }

    private static ApprovalPolicy CreatePluginPolicy(
        bool isEnabled,
        bool requiresOnsiteAttestation)
    {
        return new ApprovalPolicy(
            "test-plugin-policy",
            "test policy",
            ApprovalTargetType.Plugin,
            PluginName,
            [ToolName],
            isEnabled,
            requiresOnsiteAttestation);
    }

    private static StoredToolApprovalRequest CreateStoredApproval()
    {
        return new StoredToolApprovalRequest(
            "request-1",
            "call-1",
            "Function",
            ToolName,
            null,
            [],
            AiToolTargetType.Plugin.ToString(),
            PluginName,
            RuntimeToolName);
    }

    private static ApprovalDecisionStreamRequest CreateApprovalDecisionRequest(
        StoredToolApprovalRequest storedApproval,
        bool onsiteConfirmed)
    {
        return new ApprovalDecisionStreamRequest(
            Guid.NewGuid(),
            storedApproval.CallId,
            "approved",
            onsiteConfirmed,
            storedApproval.TargetType,
            storedApproval.TargetName,
            storedApproval.ToolName);
    }

    private static SessionRuntimeSnapshot CreateSessionSnapshot(bool hasValidOnsiteAttestation)
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionRuntimeSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Title = "test session",
            OnsiteConfirmedAt = hasValidOnsiteAttestation ? now.AddMinutes(-1) : null,
            OnsiteConfirmedBy = hasValidOnsiteAttestation ? "operator" : null,
            OnsiteConfirmationExpiresAt = hasValidOnsiteAttestation ? now.AddMinutes(5) : null
        };
    }

    private static StoredFinalAgentContext CreateStoredContext(
        Guid sessionId,
        IReadOnlyList<string> toolNames)
    {
        return new StoredFinalAgentContext(
            sessionId,
            "continue after approval",
            100,
            30,
            new ChatTokenTelemetryContext(sessionId, "old-model", "old-template", 4096, 512),
            512,
            0.3f,
            toolNames,
            JsonSerializer.Serialize(Guid.NewGuid(), JsonOptions),
            []);
    }

    private static async Task<object> InvokeApprovalDecisionValidatorAsync(
        ApprovalDecisionStreamRequest request,
        StoredToolApprovalRequest storedApproval,
        SessionRuntimeSnapshot session,
        ApprovalRequirementResolver resolver)
    {
        var validatorType = typeof(ApprovalDecisionStreamHandler).Assembly.GetType(
            "AICopilot.AiGatewayService.Agents.ApprovalDecisionValidator");
        validatorType.Should().NotBeNull();

        var method = validatorType!.GetMethod(
            "ValidateAsync",
            BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(
            null,
            [request, storedApproval, session, resolver, new StringBuilder(), CancellationToken.None])!;
        await task;

        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        return (T)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;
    }

    private sealed class AdvisoryPolicyTestPlugin : AgentPluginBase
    {
        public override string Description => "Advisory test plugin.";

        [Description("Inspect current diagnostic state.")]
        public string Inspect(string target)
        {
            return $"inspected {target}";
        }
    }

    private sealed class RecordingTokenBudgetPolicy : ITokenBudgetPolicy
    {
        public LanguageModel? LastModel { get; private set; }

        public ConversationTemplate? LastTemplate { get; private set; }

        public int CountSystemPromptTokens(ConversationTemplate template)
        {
            return template.SystemPrompt.Length;
        }

        public TokenBudgetDecision Evaluate(
            LanguageModel model,
            ConversationTemplate template,
            string finalUserPrompt)
        {
            LastModel = model;
            LastTemplate = template;
            return new TokenBudgetDecision(
                true,
                CountSystemPromptTokens(template) + finalUserPrompt.Length,
                template.Specification.MaxTokens ?? 512,
                model.Parameters.MaxTokens);
        }
    }
}
