using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.BusinessQueries;
using AICopilot.AiGatewayService.Plugins;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiRuntime;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.UnitTests;

public sealed class MafNativeModeContractTests
{
    [Fact]
    public async Task UnfilteredHarnessAgent_ShouldExposeOfficialModeTools_AndPersistDefaultModes()
    {
        var chatClient = new ToolCatalogCapturingChatClient();
        using var services = new ServiceCollection().BuildServiceProvider();

#pragma warning disable MAAI001
        var agent = new HarnessAgent(
            chatClient,
            new HarnessAgentOptions
            {
                Name = "maf-native-mode-contract",
                MaximumIterationsPerRequest = 2,
                DisableToolAutoApproval = true,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = false,
                AgentModeProviderOptions = null,
                DisableAgentSkillsProvider = true,
                BackgroundAgents = Array.Empty<AIAgent>(),
                LoopEvaluators = Array.Empty<LoopEvaluator>(),
                DisableCompaction = true,
            },
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001

        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();

        var session = await agent.CreateSessionAsync();
        (await modeProvider!.GetModeAsync(session)).Should().Be("plan");

        _ = await agent.RunAsync("Inspect the current operating mode.", session);

        chatClient.LastToolNames.Should().Contain("mode_get");
        chatClient.LastToolNames.Should().Contain("mode_set");

        await modeProvider.SetModeAsync(session, "execute");
        _ = await agent.RunAsync("Continue with the same session.", session);

        (await modeProvider.GetModeAsync(session)).Should().Be("execute");
    }

    [Fact]
    public async Task ConfiguredMainHarness_ShouldLetModelSwitchMode_AndKeepOneGovernedToolCatalog()
    {
        var governedInvocationCount = 0;
        var governedTool = new AiToolDefinition
        {
            Name = "GovernedDiagnostic",
            Description = "Run a governed diagnostic.",
            InvokeAsync = (_, _) =>
            {
                Interlocked.Increment(ref governedInvocationCount);
                return ValueTask.FromResult<object?>("diagnostic-ok");
            }
        };
        var model = new LanguageModel(
            "OpenAI",
            "test-model",
            "https://example.test/v1",
            null,
            new ModelParameters { MaxTokens = 4096 });
        var template = new ConversationTemplate(
            "maf-native-runtime",
            "MAF native runtime contract",
            "Use the governed tools supplied by the server.",
            model.Id,
            new TemplateSpecification());
        var provider = new ModeSwitchingChatClient();
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = HarnessAgentRuntimeFactory.CreateOptions(
            new AgentRuntimeCreateRequest(
                model,
                template,
                new AiChatOptions { Tools = [governedTool] }),
            new CheckpointingChatHistoryProvider(null));

#pragma warning disable MAAI001
        var agent = new ToolInvocationGuardChatClient(provider).AsHarnessAgent(
            options,
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001
        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();
        var runtime = new HarnessRuntimeChatAgent(agent, modeProvider!);
        var session = await runtime.CreateSessionAsync();

        await DrainAsync(runtime.RunStreamingAsync(
            "Switch to execute mode.",
            session));

        (await runtime.GetModeAsync(session)).Should().Be(RuntimeAgentMode.Execute);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serialized = await runtime.SerializeSessionAsync(session, serializerOptions);
        var restored = await runtime.DeserializeSessionAsync(serialized, serializerOptions);
        (await runtime.GetModeAsync(restored)).Should().Be(RuntimeAgentMode.Execute);

        await DrainAsync(runtime.RunStreamingAsync(
            "Run the governed diagnostic.",
            restored));

        governedInvocationCount.Should().Be(1);
        provider.ToolCatalogs.Should().HaveCount(4);
        provider.ToolCatalogs.Should().AllSatisfy(catalog =>
        {
            catalog.Should().Contain("mode_get");
            catalog.Should().Contain("mode_set");
            catalog.Should().Contain("GovernedDiagnostic");
            catalog.Should().Equal(provider.ToolCatalogs[0]);
        });
        provider.AllowMultipleToolCalls.Should().OnlyContain(value => value == false);
    }

    [Fact]
    public async Task ConfiguredMainHarness_ShouldExposeRealGovernedCatalogEquallyAcrossModes()
    {
        var (catalog, rejectedToolNames) = CreateGovernedMainChatCatalog();
        var sessionSnapshot = new SessionRuntimeSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = Guid.Parse("11111111-1111-4111-8111-111111111111"),
            TemplateId = Guid.NewGuid(),
            Title = "native mode governed catalog"
        };
        var planTools = await catalog.BuildAsync(
            sessionSnapshot,
            "inspect the governed catalog",
            new TrustedRenderChunkBuffer(),
            CancellationToken.None);
        var executeTools = await catalog.BuildAsync(
            sessionSnapshot,
            "inspect the governed catalog",
            new TrustedRenderChunkBuffer(),
            CancellationToken.None);

        planTools.Select(tool => tool.Name).Should().Equal(
            executeTools.Select(tool => tool.Name));
        planTools.Select(tool => tool.Name).Should()
            .Contain(DiagnosticAdvisorPlugin.ToolCode)
            .And.Contain("BusinessQuery")
            .And.NotContain(rejectedToolNames);

        var model = new LanguageModel(
            "OpenAI",
            "test-model",
            "https://example.test/v1",
            null,
            new ModelParameters { MaxTokens = 4096 });
        var template = new ConversationTemplate(
            "maf-native-catalog",
            "MAF native governed catalog contract",
            "Use only the governed tools supplied by the server.",
            model.Id,
            new TemplateSpecification());
        var provider = new CatalogOnlyChatClient();
        using var services = new ServiceCollection().BuildServiceProvider();
        var options = HarnessAgentRuntimeFactory.CreateOptions(
            new AgentRuntimeCreateRequest(
                model,
                template,
                new AiChatOptions { Tools = planTools }),
            new CheckpointingChatHistoryProvider(null));

#pragma warning disable MAAI001
        var agent = new ToolInvocationGuardChatClient(provider).AsHarnessAgent(
            options,
            NullLoggerFactory.Instance,
            services);
#pragma warning restore MAAI001
        var modeProvider = agent.GetService<AgentModeProvider>();
        modeProvider.Should().NotBeNull();
        var runtime = new HarnessRuntimeChatAgent(agent, modeProvider!);
        var runtimeSession = await runtime.CreateSessionAsync();

        (await runtime.GetModeAsync(runtimeSession)).Should().Be(RuntimeAgentMode.Plan);
        await DrainAsync(runtime.RunStreamingAsync("Inspect in Plan.", runtimeSession));
        await runtime.SetModeAsync(runtimeSession, RuntimeAgentMode.Execute);
        await DrainAsync(runtime.RunStreamingAsync("Inspect in Execute.", runtimeSession));

        provider.ToolCatalogs.Should().HaveCount(2);
        provider.ToolCatalogs.Should().AllSatisfy(toolNames =>
        {
            toolNames.Should().Contain("mode_get");
            toolNames.Should().Contain("mode_set");
            toolNames.Should().Contain(DiagnosticAdvisorPlugin.ToolCode);
            toolNames.Should().Contain("BusinessQuery");
            toolNames.Should().NotContain(rejectedToolNames);
        });
        provider.ToolCatalogs[1].Should().Equal(provider.ToolCatalogs[0]);
    }

    private static (MainChatToolCatalog Catalog, string[] RejectedToolNames)
        CreateGovernedMainChatCatalog()
    {
        var diagnosticTool = new DiagnosticAdvisorPlugin().GetTools()!.Single();
        var dangerousMcp = CreateCandidate(
            AiToolTargetType.McpServer,
            "dangerous-cloud-mcp",
            "write_state",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.SideEffecting,
            "AiGateway.Chat",
            readOnlyDeclared: false,
            mcpReadOnlyHint: false,
            mcpDestructiveHint: true);
        var cloudWrite = CreateCandidate(
            AiToolTargetType.Plugin,
            "CloudWriter",
            "write_device",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.SideEffecting,
            "AiGateway.Chat",
            readOnlyDeclared: false);
        var unauthorized = CreateCandidate(
            AiToolTargetType.Plugin,
            "RestrictedDiagnostics",
            "inspect_secret",
            AiToolExternalSystemType.NonCloud,
            AiToolCapabilityKind.Diagnostics,
            "AiGateway.RestrictedDiagnostics",
            readOnlyDeclared: true);
        AiToolDefinition[] candidates =
        [
            diagnosticTool,
            dangerousMcp,
            cloudWrite,
            unauthorized,
        ];
        var registrations = candidates
            .Select(CreateRegistration)
            .ToArray();
        var access = new StubIdentityAccessService(["AiGateway.Chat"]);
        var gate = new MainChatToolGate(
            new ToolRegistryGuard(
                new InMemoryRepository<ToolRegistration>(registrations),
                access),
            access,
            new TestCurrentUser());
        var businessQueryExecutor = new BusinessQueryExecutor(
            null!,
            NullLogger<BusinessQueryExecutor>.Instance,
            null!,
            null!,
            new EmptyBusinessQueryContextStore());
        var pluginCatalog = new StaticPluginCatalog(
            new GenericBridgePlugin
            {
                Name = "native-mode-governed-candidates",
                Description = "Governed catalog candidates for the native MAF mode contract.",
                Tools = candidates,
                ChatExposureMode = ChatExposureMode.Advisory
            });

        return (
            new MainChatToolCatalog(
                pluginCatalog,
                gate,
                businessQueryExecutor,
                new EmptyKnowledgeBaseReadService(),
                new EmptyKnowledgeRetrievalService()),
            [dangerousMcp.Name, cloudWrite.Name, unauthorized.Name]);
    }

    private static AiToolDefinition CreateCandidate(
        AiToolTargetType targetType,
        string targetName,
        string toolName,
        AiToolExternalSystemType externalSystemType,
        AiToolCapabilityKind capabilityKind,
        string requiredPermission,
        bool readOnlyDeclared,
        bool? mcpReadOnlyHint = null,
        bool? mcpDestructiveHint = null)
    {
        return new AiToolDefinition
        {
            Name = AiToolIdentity.CreateRuntimeName(targetType, targetName, toolName),
            ToolName = toolName,
            Description = $"Governed candidate {toolName}.",
            Kind = targetType == AiToolTargetType.McpServer
                ? AiToolCallKind.Mcp
                : AiToolCallKind.Function,
            RequiresApproval = true,
            TargetType = targetType,
            TargetName = targetName,
            ServerName = targetType == AiToolTargetType.McpServer ? targetName : null,
            ExternalSystemType = externalSystemType,
            CapabilityKind = capabilityKind,
            RiskLevel = AiToolRiskLevel.RequiresApproval,
            RequiredPermission = requiredPermission,
            AuditLevel = nameof(ToolAuditLevel.Standard),
            DataBoundary = nameof(ToolDataBoundary.GovernedBusinessReadOnly),
            SchemaVersion = 1,
            TimeoutSeconds = 30,
            ReadOnlyDeclared = readOnlyDeclared,
            McpReadOnlyHint = mcpReadOnlyHint,
            McpDestructiveHint = mcpDestructiveHint,
            McpIdempotentHint = false,
            JsonSchema = ParseSchema(
                """{"type":"object","properties":{},"additionalProperties":false}"""),
            ReturnJsonSchema = ParseSchema(
                """{"type":"object","properties":{"status":{"type":"string"}},"required":["status"],"additionalProperties":false}""")
        };
    }

    private static ToolRegistration CreateRegistration(AiToolDefinition tool)
    {
        if (tool.Name == DiagnosticAdvisorPlugin.ToolCode)
        {
            var seed = BuiltInToolRegistrations.FindHarnessTool(tool.Name)!;
            return new ToolRegistration(
                seed.ToolCode,
                seed.DisplayName,
                seed.Description,
                seed.ProviderType,
                seed.TargetType,
                seed.TargetName,
                seed.InputSchemaJson,
                seed.OutputSchemaJson,
                seed.RiskLevel,
                seed.RequiredPermission,
                seed.RequiresApproval,
                seed.IsEnabled,
                seed.TimeoutSeconds,
                seed.AuditLevel,
                DateTimeOffset.UtcNow,
                seed.Category,
                seed.BusinessDomains,
                seed.DataBoundary,
                seed.IsExecutableByAgent,
                seed.SchemaVersion,
                seed.CatalogVersion);
        }

        return new ToolRegistration(
            tool.Name,
            tool.ToolName!,
            tool.Description!,
            tool.TargetType == AiToolTargetType.McpServer
                ? ToolProviderType.Mcp
                : ToolProviderType.BuiltIn,
            tool.TargetType == AiToolTargetType.McpServer
                ? ToolRegistrationTargetType.McpServer
                : ToolRegistrationTargetType.Plugin,
            tool.TargetName!,
            tool.JsonSchema!.Value.GetRawText(),
            tool.ReturnJsonSchema!.Value.GetRawText(),
            tool.RiskLevel,
            tool.RequiredPermission,
            tool.RequiresApproval,
            isEnabled: true,
            tool.TimeoutSeconds,
            ToolAuditLevel.Standard,
            DateTimeOffset.UtcNow,
            dataBoundary: ToolDataBoundary.GovernedBusinessReadOnly,
            isExecutableByAgent: true,
            schemaVersion: tool.SchemaVersion);
    }

    private static JsonElement ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task DrainAsync(IAsyncEnumerable<RuntimeAgentUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    private sealed class ToolCatalogCapturingChatClient : IChatClient
    {
        public IReadOnlyList<string> LastToolNames { get; private set; } = [];

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastToolNames = options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? [];

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
    }

    private sealed class ModeSwitchingChatClient : IChatClient
    {
        private readonly Queue<AIContent[]> responses = new(
        [
            [
                new FunctionCallContent(
                    "mode-call",
                    "mode_set",
                    new Dictionary<string, object?> { ["mode"] = "execute" })
            ],
            [new TextContent("Mode changed.")],
            [
                new FunctionCallContent(
                    "diagnostic-call",
                    "GovernedDiagnostic",
                    new Dictionary<string, object?>())
            ],
            [new TextContent("Diagnostic complete.")]
        ]);

        public List<IReadOnlyList<string>> ToolCatalogs { get; } = [];

        public List<bool?> AllowMultipleToolCalls { get; } = [];

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CaptureOptions(options);
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, NextResponse())));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            CaptureOptions(options);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, NextResponse());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        private AIContent[] NextResponse()
        {
            responses.Should().NotBeEmpty("the Harness should use the scripted provider calls");
            return responses.Dequeue();
        }

        private void CaptureOptions(ChatOptions? options)
        {
            AllowMultipleToolCalls.Add(options?.AllowMultipleToolCalls);
            ToolCatalogs.Add(options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? []);
        }
    }

    private sealed class CatalogOnlyChatClient : IChatClient
    {
        public List<IReadOnlyList<string>> ToolCatalogs { get; } = [];

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(options);
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Capture(options);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new TextContent("ok")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        private void Capture(ChatOptions? options)
        {
            ToolCatalogs.Add(options?.Tools?
                .Select(tool => tool is AIFunction function
                    ? function.Name
                    : tool.GetService<AIFunction>()?.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray() ?? []);
        }
    }

    private sealed class StaticPluginCatalog(IAgentPlugin plugin) : IAgentPluginCatalog
    {
        private readonly AiToolDefinition[] tools = plugin.GetTools()?.ToArray() ?? [];

        public AiToolDefinition[] GetTools(params string[] names) =>
            names.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase) ? tools : [];

        public AiToolDefinition[] GetPluginTools(string name) =>
            string.Equals(name, plugin.Name, StringComparison.OrdinalIgnoreCase) ? tools : [];

        public AiToolDefinition[] GetAllTools() => tools;

        public IAgentPlugin? GetPlugin(string name) =>
            string.Equals(name, plugin.Name, StringComparison.OrdinalIgnoreCase) ? plugin : null;

        public IAgentPlugin[] GetAllPlugin() => [plugin];
    }

    private sealed class EmptyBusinessQueryContextStore : IBusinessQueryContextStore
    {
        public BusinessQueryContext Resolve(BusinessQueryContext requested) => requested;

        public void Remember(BusinessQueryContext context)
        {
        }

        public BusinessQueryConfirmationChallenge BeginConfirmation(BusinessQueryContext requested) =>
            throw new InvalidOperationException("Confirmation is not expected in the catalog test.");

        public bool TryConfirmPending(
            Guid sessionId,
            string userMessage,
            out BusinessQueryContext confirmed)
        {
            confirmed = null!;
            return false;
        }
    }

    private sealed class EmptyKnowledgeBaseReadService : IKnowledgeBaseReadService
    {
        public Task<IReadOnlyList<KnowledgeBaseDescriptor>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeBaseDescriptor>>([]);

        public Task<IReadOnlyList<KnowledgeBaseDescriptor>> GetByNamesAsync(
            IReadOnlyCollection<string> names,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeBaseDescriptor>>([]);
    }

    private sealed class EmptyKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        public Task<IReadOnlyList<KnowledgeRetrievalResult>> SearchAsync(
            Guid knowledgeBaseId,
            string queryText,
            int topK,
            double minScore,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeRetrievalResult>>([]);
    }
}
