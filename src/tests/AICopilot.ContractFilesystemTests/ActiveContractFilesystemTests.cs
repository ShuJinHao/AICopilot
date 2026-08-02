using System.Xml.Linq;

namespace AICopilot.ContractFilesystemTests;

public sealed class ActiveContractFilesystemTests
{
    private static readonly string[] ActiveContractPaths =
    [
        "AGENTS.md",
        "docs/AICopilot业务规则.md",
        "docs/AICopilot安全部署契约.md",
        "docs/AI架构路线图.md",
        "docs/Agent工作流与异常契约.md",
        "docs/Cloud只读数据分析契约.md",
        "docs/DDD聚合根边界.md",
    ];

    [Fact]
    public void ActiveContracts_ShouldRemainAtStablePaths()
    {
        var root = FindRepositoryRoot();

        foreach (var relativePath in ActiveContractPaths)
        {
            File.Exists(Path.Combine(root, relativePath))
                .Should()
                .BeTrue($"active contract {relativePath} must remain discoverable");
        }
    }

    [Fact]
    public void AiAuthorityDocuments_ShouldKeepTheCurrentHarnessSourceTruthMarkers()
    {
        var root = FindRepositoryRoot();
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var roadmap = File.ReadAllText(
            Path.Combine(root, "docs", "AI架构路线图.md"));

        businessRules.Should().Contain("JIT 首次身份绑定并发");
        businessRules.Should().Contain("Harness 逐次批准");
        businessRules.Should().Contain("查询确认键是 `SessionId`");
        businessRules.Should().Contain("Cloud provider / AI consumer 跨版本发布顺序");
        businessRules.Should().Contain("完工弹夹数");
        businessRules.Should().Contain("ModelContextProtocol 2.0.0");
        businessRules.Should().Contain("McpToolOutputSchemaContractV1");
        businessRules.Should().Contain("tool_execution_timeout");

        agentContract.Should().Contain("Microsoft Agent Framework Harness");
        agentContract.Should().Contain("Microsoft.Agents.AI` `1.16.0");
        agentContract.Should().Contain("MainChatToolGate");
        agentContract.Should().Contain("TrustedRenderChunkBuffer");
        agentContract.Should().Contain("KnowledgeQuery(question, knowledgeBaseNames)");
        agentContract.Should().Contain("/var/lib/aicopilot/data-protection-keys");
        agentContract.Should().Contain("SingleInstance");
        agentContract.Should().Contain("物理边界与禁止回潮");
        agentContract.Should().Contain("AICopilot.AiGatewayService/BusinessQueries");
        agentContract.Should().Contain("ModelContextProtocol 2.0.0");
        agentContract.Should().Contain("tool_execution_timeout");

        roadmap.Should().Contain(
            "| 能力 | 源码状态 | 候选验证退出门 | 生产状态 |");
        roadmap.Should().Contain("AI-01");
        roadmap.Should().Contain("AI-02");
        roadmap.Should().Contain("Harness 主聊天");
        roadmap.Should().Contain("ModelContextProtocol 2.0.0");
        roadmap.Should().Contain("MCP 2.0 受治理通道");
    }

    [Fact]
    public void AiAuthorityDocuments_ShouldKeepServerOwnedModeFallbackAndDurabilitySemantics()
    {
        var root = FindRepositoryRoot();
        var agentInstructions = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var persistenceContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));
        var roadmap = File.ReadAllText(
            Path.Combine(root, "docs", "AI架构路线图.md"));
        var activeContractText = string.Join(
            '\n',
            ActiveContractPaths.Select(path => File.ReadAllText(Path.Combine(root, path))));

        string[] modelOwnedFallbackMarkers =
        [
            "可由模型决定是否尝试同源 Text-to-SQL",
            "模型决定是否进入 Text-to-SQL",
            "模型决定 Text-to-SQL fallback",
        ];
        foreach (var marker in modelOwnedFallbackMarkers)
        {
            activeContractText.Should().NotContain(marker);
        }

        businessRules.Should().Contain(
            "`BusinessQueryFallbackPolicy` 是唯一 fallback 决策 owner");
        businessRules.Should().Contain("服务端才自动进入受控 Text-to-SQL");
        agentContract.Should().Contain(
            "`BusinessQueryFallbackPolicy` 是唯一 fallback 决策 owner");

        agentInstructions.Should().Contain("Plan 模式始终只读");
        businessRules.Should().Contain("`Plan` 始终只读");
        businessRules.Should().Contain("模型永远不能自行切换模式");
        businessRules.Should().Contain("`mode_set` 不得进入模型工具面");
        agentContract.Should().Contain("当前认证 owner 显式调用");
        agentContract.Should().Contain("`mode_set` 永不进入模型工具面");

        agentContract.Should().Contain("AgentSession checkpoint 只用于会话连续性");
        agentContract.Should().Contain("不是 durable Tool checkpoint");
        agentContract.Should().Contain("Interrupted 后不得恢复或重放");
        persistenceContract.Should().Contain(
            "数据库 durable commit marker 只用于事务提交结果验证");
        persistenceContract.Should().Contain(
            "不是 Agent durable 编排、Tool checkpoint");

        roadmap.Should().Contain("源码架构已收口");
        roadmap.Should().Contain("生产状态继续保持“未验收”");
        roadmap.Should().Contain("## 3. 候选验证退出门");
    }

    [Fact]
    public void FrontendChatTransport_ShouldUseOnlyCurrentHarnessRequests()
    {
        var root = FindRepositoryRoot();
        var frontendSourceRoot = Path.Combine(
            root,
            "src",
            "vues",
            "AICopilot.Web",
            "src");
        var chatService = File.ReadAllText(Path.Combine(
            frontendSourceRoot,
            "services",
            "chatService.ts"));
        var compactChatService = new string(chatService
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray());

        compactChatService.Should().Contain(
            "sendEventStream('/aigateway/chat',{sessionId,message},callbacks)");
        compactChatService.Should().Contain(
            "sendEventStream('/aigateway/approval/decision',{sessionId,callId,decision},callbacks");
        compactChatService.Should().Contain(
            "`/aigateway/session/${encodeURIComponent(sessionId)}/agent-mode`,{mode,expectedVersion}");

        var frontendSource = string.Join(
            '\n',
            Directory.EnumerateFiles(frontendSourceRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path) is ".ts" or ".vue")
                .Select(File.ReadAllText));
        string[] retiredTransportMarkers =
        [
            "referencedAgentTaskId",
            "/aigateway/agent/task",
            "/aigateway/artifact",
            "/aigateway/business-approval",
            "/aigateway/routing-model",
            "/aigateway/runtime-settings",
            "/aigateway/session/timeline",
            "/aigateway/agent-upload",
            "onsite"
        ];

        foreach (var marker in retiredTransportMarkers)
        {
            frontendSource.Should().NotContain(marker);
        }
    }

    [Fact]
    public void ProductionSolution_ShouldContainOnlyTheCompleteProductionProjectGraph()
    {
        var root = FindRepositoryRoot();
        var productionSolution = XDocument.Load(
            Path.Combine(root, "AICopilot.Production.slnx"));
        var selectedProjects = productionSolution
            .Descendants("Project")
            .Select(element => ((string?)element.Attribute("Path"))?.Replace('\\', '/'))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedProjects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path =>
                !path.StartsWith("src/tests/", StringComparison.Ordinal) &&
                !path.StartsWith("src/testing/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        selectedProjects.Should().NotBeEmpty();
        selectedProjects.Should().OnlyHaveUniqueItems();
        selectedProjects.Should().Equal(expectedProjects);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate AICopilot.slnx from the contract test output directory.");
    }
}
