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
        "src/vues/AICopilot.Web/AGENTS.md",
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
        businessRules.Should().Contain("逐次工具批准产品边界");
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
        agentContract.Should().Contain("## 3. Harness tool approval");
        agentContract.Should().Contain("物理边界与禁止回潮");
        agentContract.Should().Contain("AICopilot.AiGatewayService/BusinessQueries");
        agentContract.Should().Contain("ModelContextProtocol 2.0.0");
        agentContract.Should().Contain("tool_execution_timeout");

        roadmap.Should().Contain(
            "| 能力 | 源码状态 | 候选验证退出门 | 生产状态 |");
        roadmap.Should().Contain("AI-01");
        roadmap.Should().Contain("AI-02");
        roadmap.Should().Contain("Harness 主聊天");
        roadmap.Should().Contain("MCP 2.0 受治理通道");
    }

    [Fact]
    public void MafTechnicalContract_ShouldRemainTheSingleSourceOfTruth()
    {
        var root = FindRepositoryRoot();
        var agentInstructions = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var roadmap = File.ReadAllText(
            Path.Combine(root, "docs", "AI架构路线图.md"));
        var normalizedAgentContract = NormalizeContractText(agentContract);

        normalizedAgentContract.Should().Contain("MAF / Harness 技术契约的唯一正文");
        string[] technicalOwnerMarkers =
        [
            "Microsoft.Agents.AI 1.16.0",
            "每轮最多 8 次模型调用",
            "FileMemory、WebSearch、AgentSkills、BackgroundAgents、LoopEvaluators",
            "FileAccessStore",
            "Microsoft.Agents.AI.Workflows",
            "AgentModeProvider",
            "mode_get",
            "mode_set",
            "SetModeAsync",
            "ToolInvocationGuardChatClient",
            "ChatOptions.Tools",
            "MainChatToolCatalog",
            "HarnessAgentOptions",
            "AgentModeProviderOptions",
            "ApprovalRequiredAIFunction",
            "ToolApprovalAgentOptions",
            "AutoApprovalRules",
            "AlwaysApproveToolApprovalResponseContent",
            "DisableToolAutoApproval",
            "AllowMultipleToolCalls",
            "agent_session_state SSE",
            "expectedVersion",
            "PUT /api/aigateway/session/{sessionId}/agent-mode",
            "POST /api/aigateway/approval/decision",
            "callId",
            "ScopedRuntimeAgent.ConfigurationSnapshot",
            "agent_session_states 一对一记录",
            "AgentSession 明文上限 2 MiB",
            "30 天滑动 TTL",
            "AICopilot.AgentSessions",
            "/var/lib/aicopilot/data-protection-keys",
            "当前部署拓扑只支持 SingleInstance",
            "AgentSession checkpoint 只用于会话连续性",
            "独立 BOM 批次",
            "核心包保持同版本",
        ];
        foreach (var marker in technicalOwnerMarkers)
        {
            normalizedAgentContract.Should().Contain(
                marker,
                "the Agent contract must own every MAF/Harness implementation marker");
        }

        agentInstructions.Should().Contain(
            "[Agent 工作流与异常契约](docs/Agent工作流与异常契约.md)");
        businessRules.Should().Contain(
            "[Agent 工作流与异常契约](./Agent工作流与异常契约.md)");
        roadmap.Should().Contain(
            "[Agent 工作流与异常契约](./Agent工作流与异常契约.md)");
        var frontendInstructions = File.ReadAllText(
            Path.Combine(root, "src", "vues", "AICopilot.Web", "AGENTS.md"));
        frontendInstructions.Should().Contain(
            "[Agent 工作流与异常契约](../../../docs/Agent工作流与异常契约.md)");

        var linkedAuthorityDocuments = ActiveContractPaths
            .Where(path => path != "docs/Agent工作流与异常契约.md")
            .Select(path => (
                Name: path,
                Text: File.ReadAllText(Path.Combine(root, path))))
            .ToArray();
        foreach (var (name, text) in linkedAuthorityDocuments)
        {
            foreach (var marker in technicalOwnerMarkers)
            {
                NormalizeContractText(text).Should().NotContain(
                    marker,
                    $"{name} must link the unique Agent contract instead of copying {marker}");
            }
        }

        agentInstructions.Should().Contain("只是行为状态，不是安全隔离或授权边界");
        agentInstructions.Should().Contain("模式与授权正交");
        businessRules.Should().Contain(
            "`Plan` 用于帮助用户交互式澄清、调查并形成 Todo");
        businessRules.Should().Contain(
            "`Execute` 用于自主、连续地完成 Todo");
        businessRules.Should().Contain("模式与授权正交");
        businessRules.Should().Contain(
            "切换模式不得扩大或缩小用户权限、可用工具、数据边界或批准策略");
        frontendInstructions.Should().Contain(
            "前端不得按模式推断、隐藏或放大工具权限");
        frontendInstructions.Should().Contain(
            "模式切换不得扩大 Cloud/MES/ERP 写权限");
        agentContract.Should().Contain(
            "批准身份只能来自受保护的服务端 `AgentSessionState` 绑定");
        normalizedAgentContract.Should().Contain(
            "POST /api/aigateway/approval/decision 请求固定为 { sessionId, callId, decision }");
        agentContract.Should().Contain(
            "客户端不得回传或覆盖 target、tool identity、schema version、参数摘要");
        agentContract.Should().Contain(
            "`Plan` 是交互式行为模式，用于澄清、调查、调用受治理工具和形成 Todo");
        agentContract.Should().Contain(
            "`AgentModeProvider` 必须继续向模型提供 `mode_get` 与 `mode_set`");
        agentContract.Should().Contain("该公开 API 不是唯一切换入口");
        agentContract.Should().Contain("模式不是业务工具目录的筛选输入");
        agentContract.Should().Contain("模式不是安全隔离或授权边界，模式与授权正交");
        agentContract.Should().Contain(
            "不得删除、重排或按 Plan / Execute 过滤 MAF 与应用传入的工具");
        roadmap.Should().Contain("MAF 原生模式运行时");
        roadmap.Should().Contain("源码架构已收口");
        roadmap.Should().Contain("生产状态继续保持“未验收”");
        roadmap.Should().Contain("## 3. 候选验证退出门");
    }

    [Fact]
    public void ActiveContracts_ShouldRejectRetiredMafModeAuthoritySemantics()
    {
        var root = FindRepositoryRoot();
        var activeContractStatements = ActiveContractPaths
            .SelectMany(path => File.ReadAllLines(Path.Combine(root, path)))
            .Select(NormalizeContractStatement)
            .Where(statement => !string.IsNullOrWhiteSpace(statement))
            .ToArray();

        string[] retiredModeAuthorityStatements =
        [
            "Plan 模式始终只读",
            "Plan 始终只读",
            "Plan 模式只允许 Todo",
            "Plan 模式只允许 Todo、mode_get",
            "Plan 仅允许 Todo/mode_get",
            "Plan 只做规划，不执行外部或业务工具",
            "模型永远不能自行切换模式",
            "模型不能自行切换模式",
            "mode_set 不得进入模型工具面",
            "mode_set 永不进入模型工具面",
            "模式切换唯一入口",
            "Execute-only 服务端工具",
            "Plan 模式绝不公开",
            "必须隐藏 mode_set",
            "需要隐藏 mode_set",
            "屏蔽 mode_set",
            "模型不可见 mode_set",
            "根据模式裁剪业务工具",
            "按模式筛除业务工具",
            "Plan / Execute 决定业务工具目录",
            "Plan / Execute 用于授予权限",
            "模式决定用户权限",
            "模式决定工具权限",
            "切换模式即可获得额外权限",
            "使用自研模式状态机",
            "维护私有模式状态机",
            "允许复制官方模式状态机",
            "通过复制官方模式状态机实现",
        ];
        (string Statement, string RetiredAssertion)[] unsafeRegressionExamples =
        [
            ("Plan 模式始终只读，仅允许 Todo", "Plan 模式始终只读"),
            ("主聊天必须隐藏 mode_set", "必须隐藏 mode_set"),
            ("在 Execute 下模式决定工具权限", "模式决定工具权限"),
            ("项目使用自研模式状态机", "使用自研模式状态机"),
            ("不得扩大普通用户权限，但模式决定工具权限", "模式决定工具权限"),
            ("不能隐藏错误，但主聊天必须隐藏 mode_set", "必须隐藏 mode_set"),
            ("禁止旧审批绕过但模式决定工具权限", "模式决定工具权限"),
        ];
        foreach (var (statement, retiredAssertion) in unsafeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(statement, retiredAssertion)
                .Should()
                .BeTrue();
        }

        (string Statement, string RetiredAssertion)[] safeRegressionExamples =
        [
            ("禁止屏蔽 mode_set", "屏蔽 mode_set"),
            ("禁止根据模式裁剪业务工具", "根据模式裁剪业务工具"),
            ("不得使用自研模式状态机", "使用自研模式状态机"),
            ("禁止模式决定用户权限", "模式决定用户权限"),
        ];
        foreach (var (statement, retiredAssertion) in safeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(statement, retiredAssertion)
                .Should()
                .BeFalse();
        }

        foreach (var activeStatement in activeContractStatements)
        {
            foreach (var retiredStatement in retiredModeAuthorityStatements)
            {
                ContainsUnnegatedRetiredAssertion(
                        activeStatement,
                        NormalizeContractStatement(retiredStatement))
                    .Should()
                    .BeFalse(
                        $"active contracts must reject retired MAF assertion '{retiredStatement}'");
            }
        }

        var activeContractText = NormalizeContractText(string.Join(
            '\n',
            ActiveContractPaths.Select(path => File.ReadAllText(Path.Combine(root, path)))));
        string[] retiredRuntimeIdentifiers =
        [
            "Never call mode_set",
            "ToolSurfaceGuardChatClient",
            "HarnessToolSurfacePolicy",
        ];
        foreach (var identifier in retiredRuntimeIdentifiers)
        {
            activeContractText.Should().NotContain(identifier);
        }
    }

    [Fact]
    public void ActiveContracts_ShouldKeepServerOwnedFallbackAndDurabilitySemantics()
    {
        var root = FindRepositoryRoot();
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var persistenceContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));
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

        agentContract.Should().Contain("AgentSession checkpoint 只用于会话连续性");
        agentContract.Should().Contain("不是 durable Tool checkpoint");
        agentContract.Should().Contain("Interrupted 后不得恢复或重放");
        persistenceContract.Should().Contain(
            "数据库 durable commit marker 只用于事务提交结果验证");
        persistenceContract.Should().Contain(
            "不是 Agent durable 编排、Tool checkpoint");
    }

    [Fact]
    public void DataSourcePermissionGrant_ShouldKeepFormalIndependentAggregateOwnership()
    {
        var root = FindRepositoryRoot();
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var aggregateContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));
        var ownershipLines = string.Join(
            '\n',
            new[] { businessRules, aggregateContract }
                .SelectMany(text => text.Split('\n'))
                .Where(line => line.Contains("DataSourcePermissionGrant", StringComparison.Ordinal)));

        businessRules.Should().Contain(
            "`DataSourcePermissionGrant` 正式冻结为 DataAnalysis bounded context 的独立聚合根");
        aggregateContract.Should().Contain(
            "`DataSourcePermissionGrant` 是 DataAnalysis bounded context 的正式独立聚合根");
        aggregateContract.Should().Contain("独立 `DataSourcePermissionGrantId`");
        aggregateContract.Should().Contain("`RowVersion`");
        aggregateContract.Should().Contain("授权/撤销生命周期");
        aggregateContract.Should().Contain("repository");
        aggregateContract.Should().Contain("审计写入");
        aggregateContract.Should().Contain(
            "`(BusinessDatabaseId, TargetType, TargetValue)` 唯一目标约束");
        aggregateContract.Should().Contain("跨聚合仅由 Grant 引用 `BusinessDatabaseId`");
        aggregateContract.Should().Contain("`BusinessDatabase` 不持有 Grant 子实体集合");

        string[] unresolvedOwnershipMarkers =
        [
            "AggregatePendingReview",
            "PendingReview",
            "归属未决",
            "归属待定",
            "归属待确认",
            "尚待决定",
            "尚未定稿",
            "暂未确定",
            "暂作为",
            "暂定",
            "待评估",
            "下沉",
        ];
        foreach (var marker in unresolvedOwnershipMarkers)
        {
            ownershipLines.Should().NotContain(marker);
        }
    }

    [Fact]
    public void MainHarnessSource_ShouldKeepNativeMafModeRuntimeAlignment()
    {
        var root = FindRepositoryRoot();
        var runtimeRoot = Path.Combine(
            root,
            "src",
            "infrastructure",
            "AICopilot.AiRuntime");
        var factory = File.ReadAllText(Path.Combine(
            runtimeRoot,
            "HarnessAgentRuntimeFactory.cs"));
        var runtimeAgent = File.ReadAllText(Path.Combine(
            runtimeRoot,
            "HarnessRuntimeChatAgent.cs"));
        var invocationGuardPath = Path.Combine(
            runtimeRoot,
            "ToolInvocationGuardChatClient.cs");
        var invocationGuard = File.ReadAllText(invocationGuardPath);
        var builtInPrompts = File.ReadAllText(Path.Combine(
            root,
            "src",
            "core",
            "AICopilot.Core.AiGateway",
            "Aggregates",
            "ConversationTemplate",
            "BuiltInConversationTemplates.cs"));

        File.Exists(Path.Combine(runtimeRoot, "ToolSurfaceGuardChatClient.cs"))
            .Should().BeFalse();
        factory.Should().Contain("AgentModeProviderOptions = null");
        factory.Should().Contain("new ToolInvocationGuardChatClient(modelClient)");
        factory.Should().NotContain("Never call mode_set");
        runtimeAgent.Should().NotContain("HarnessToolSurfacePolicy");
        runtimeAgent.Should().NotContain("SynchronizeToolSurface");
        invocationGuard.Should().NotContain("RuntimeAgentMode");
        invocationGuard.Should().NotContain("mode_set");
        invocationGuard.Should().NotContain(".Tools =");
        invocationGuard.Should().Contain("ResolveAllowedToolNames(guardedOptions)");
        builtInPrompts.Should().Contain("MAF 原生行为模式");
        builtInPrompts.Should().Contain("模型可使用官方 mode_get / mode_set");
        builtInPrompts.Should().Contain("模式与授权正交");
        builtInPrompts.Should().NotContain("Plan 只做规划，不执行外部或业务工具");
        builtInPrompts.Should().NotContain("Never call mode_set");
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
        var chatPresentation = File.ReadAllText(Path.Combine(
            frontendSourceRoot,
            "protocol",
            "chatPresentation.ts"));

        compactChatService.Should().Contain(
            "sendEventStream('/aigateway/chat',{sessionId,message},callbacks)");
        compactChatService.Should().Contain(
            "sendEventStream('/aigateway/approval/decision',{sessionId,callId,decision},callbacks");
        compactChatService.Should().Contain(
            "`/aigateway/session/${encodeURIComponent(sessionId)}/agent-mode`,{mode,expectedVersion}");
        chatPresentation.Should().Contain("交互式澄清、调查并形成待办");
        chatPresentation.Should().Contain("自主连续完成待办");
        chatPresentation.Should().NotContain("不查询外部数据");
        chatPresentation.Should().NotContain("只规划和整理待办");

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

    private static string NormalizeContractText(string text)
    {
        var withoutMarkdownCode = text.Replace("`", string.Empty, StringComparison.Ordinal);
        return string.Join(
            ' ',
            withoutMarkdownCode.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeContractStatement(string text)
    {
        return NormalizeContractText(text)
            .TrimStart('-', '*', ' ')
            .TrimEnd('。', '；', ';', '.');
    }

    private static bool ContainsUnnegatedRetiredAssertion(
        string statement,
        string retiredAssertion)
    {
        string[] safeNegationMarkers =
        [
            "禁止",
            "不得",
            "不应",
            "不能",
            "不可",
            "严禁",
            "不允许",
            "拒绝",
            "阻止",
            "防止",
            "避免",
        ];
        var searchStart = 0;
        while (searchStart < statement.Length)
        {
            var assertionIndex = statement.IndexOf(
                retiredAssertion,
                searchStart,
                StringComparison.Ordinal);
            if (assertionIndex < 0)
            {
                return false;
            }

            var prefix = statement[..assertionIndex];
            var clauseBoundary = prefix.LastIndexOfAny(
                new[] { '。', '；', ';', '！', '!', '？', '?', '，', ',', '：', ':' });
            string[] polarityResetMarkers = ["但是", "然而", "但", "却"];
            foreach (var resetMarker in polarityResetMarkers)
            {
                var resetIndex = prefix.LastIndexOf(
                    resetMarker,
                    StringComparison.Ordinal);
                if (resetIndex >= 0)
                {
                    clauseBoundary = Math.Max(
                        clauseBoundary,
                        resetIndex + resetMarker.Length - 1);
                }
            }
            var clausePrefix = prefix[(clauseBoundary + 1)..];
            if (!safeNegationMarkers.Any(marker =>
                    clausePrefix.Contains(marker, StringComparison.Ordinal)))
            {
                return true;
            }

            searchStart = assertionIndex + retiredAssertion.Length;
        }

        return false;
    }
}
