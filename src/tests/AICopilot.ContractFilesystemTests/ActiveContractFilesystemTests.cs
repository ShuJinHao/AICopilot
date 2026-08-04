using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        var cloudContract = File.ReadAllText(
            Path.Combine(root, "docs", "Cloud只读数据分析契约.md"));
        businessRules.Should().Contain("JIT 首次身份绑定并发");
        businessRules.Should().Contain("逐次工具批准产品边界");
        cloudContract.Should().Contain("查询确认键固定为 `SessionId`");
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

    }

    [Fact]
    public void ArchitectureRoadmap_ShouldRemainADynamicCurrentStatusRegistry()
    {
        var root = FindRepositoryRoot();
        var agentInstructions = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var roadmap = File.ReadAllText(
            Path.Combine(root, "docs", "AI架构路线图.md"));

        const string expectedRoutingParagraph =
            "本文档只登记当前能力状态和下一退出门，不承载实现正文、验证算法、部署操作或历史过程。MAF / Harness 细节见 [Agent 工作流与异常契约](./Agent工作流与异常契约.md)，Cloud 查询与数据安全见 [Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)，聚合与持久化见 [DDD 聚合根边界](./DDD聚合根边界.md)，候选与生产退出规则见 [AICopilot 安全部署契约](./AICopilot安全部署契约.md)。战略性“不做”边界只见 [AICopilot 业务规则](./AICopilot业务规则.md)。";
        const string tableHeader =
            "| 能力 | 源码状态 | 候选状态 | 生产状态 | 下一退出门 |";
        Regex.Matches(
                roadmap,
                Regex.Escape(tableHeader),
                RegexOptions.CultureInvariant)
            .Should()
            .HaveCount(1, "the roadmap must expose exactly one current status registry");
        AssertActiveFragment(
            roadmap,
            tableHeader,
            "the current status registry must remain active Markdown prose");

        var roadmapLines = roadmap
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var headerLineIndex = Array.FindIndex(
            roadmapLines,
            line => string.Equals(line.Trim(), tableHeader, StringComparison.Ordinal));
        headerLineIndex.Should().BeGreaterThanOrEqualTo(0);
        headerLineIndex.Should().BeLessThan(roadmapLines.Length - 2);
        roadmapLines
            .Take(headerLineIndex)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Should()
            .SatisfyRespectively(
                title => title.Trim().Should().Be("# AICopilot AI 架构路线图"),
                routing => routing.Trim().Should().Be(
                    expectedRoutingParagraph,
                    "the single routing paragraph is a canonical owner map, not free-form prose"));

        var headerCells = ParseMarkdownTableRow(roadmapLines[headerLineIndex]);
        headerCells.Should().Equal(
            "能力",
            "源码状态",
            "候选状态",
            "生产状态",
            "下一退出门");
        var separatorCells = ParseMarkdownTableRow(roadmapLines[headerLineIndex + 1]);
        separatorCells.Should().HaveCount(headerCells.Length);
        foreach (var separatorCell in separatorCells)
        {
            Regex.IsMatch(
                    separatorCell,
                    @"^:?-{3,}:?$",
                    RegexOptions.CultureInvariant)
                .Should()
                .BeTrue("each status table column must have a Markdown separator");
        }

        var statusRows = roadmapLines
            .Skip(headerLineIndex + 2)
            .TakeWhile(line => line.TrimStart().StartsWith('|'))
            .Select(ParseMarkdownTableRow)
            .ToArray();
        statusRows.Should().NotBeEmpty("the current registry cannot be an empty shell");
        roadmapLines
            .Skip(headerLineIndex + 2 + statusRows.Length)
            .Should()
            .OnlyContain(
                line => string.IsNullOrWhiteSpace(line),
                "the roadmap may contain only its title, routing paragraph and current status table");
        foreach (var statusRow in statusRows)
        {
            statusRow.Should().HaveCount(headerCells.Length);
            foreach (var cell in statusRow)
            {
                cell.Should().NotBeNullOrWhiteSpace(
                    "every current status field must be explicitly registered");
            }
        }

        statusRows
            .Select(row => row[0])
            .Should()
            .OnlyHaveUniqueItems("capability names are the dynamic row identity");
        foreach (var capabilityName in statusRows.Select(row => row[0]))
        {
            capabilityName.Length.Should().BeLessThanOrEqualTo(
                64,
                "capability names must remain labels rather than implementation prose");
            Regex.IsMatch(
                    capabilityName,
                    @"[。；;！!]",
                    RegexOptions.CultureInvariant)
                .Should()
                .BeFalse("capability names cannot carry sentence-like implementation prose");
            var bareHexTokens = Regex.Matches(
                    capabilityName,
                    @"(?i)(?<![A-Za-z0-9])[0-9a-f]{7,40}(?![A-Za-z0-9])",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(match => match.Value)
                .Where(token =>
                    token.Any(char.IsDigit) &&
                    token.Any(character => character is >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                .ToArray();
            bareHexTokens.Should().BeEmpty(
                "capability labels cannot hide bare abbreviated or full commit SHAs");
        }

        string[] controlledSourceStates =
        [
            "未开始",
            "设计中",
            "实现中",
            "已建立",
            "已收口",
        ];
        string[] controlledCandidateStates =
        [
            "待验证",
            "验证中",
            "已通过",
            "验证失败",
            "不适用",
        ];
        string[] controlledProductionStates =
        [
            "未验收",
            "验收中",
            "已验收",
            "验收暂停",
        ];
        string[] controlledExitGates =
        [
            "完成该能力源码实现",
            "取得该能力候选证据",
            "取得该能力产物证据",
            "完成该能力生产验收",
            "无后续退出门",
        ];
        foreach (var statusRow in statusRows)
        {
            controlledSourceStates.Should().Contain(
                statusRow[1],
                "source state must use the controlled vocabulary");
            controlledCandidateStates.Should().Contain(
                statusRow[2],
                "candidate state must use the controlled vocabulary");
            controlledProductionStates.Should().Contain(
                statusRow[3],
                "production state must use the controlled vocabulary");
            statusRow[3].Should().Be(
                "未验收",
                "production acceptance has not been authorized for any current capability");
            controlledExitGates.Should().Contain(
                statusRow[4],
                "next exits must remain state-transition labels rather than validation procedures");
        }

        (string Source, string Candidate, string Production, string Exit)[]
            allowedStatusTransitions =
            [
                ("未开始", "不适用", "未验收", "完成该能力源码实现"),
                ("设计中", "不适用", "未验收", "完成该能力源码实现"),
                ("实现中", "不适用", "未验收", "完成该能力源码实现"),
                ("已建立", "待验证", "未验收", "取得该能力候选证据"),
                ("已收口", "待验证", "未验收", "取得该能力候选证据"),
                ("已建立", "验证中", "未验收", "取得该能力候选证据"),
                ("已收口", "验证中", "未验收", "取得该能力候选证据"),
                ("已建立", "验证失败", "未验收", "取得该能力候选证据"),
                ("已收口", "验证失败", "未验收", "取得该能力候选证据"),
                ("已建立", "已通过", "未验收", "取得该能力产物证据"),
                ("已收口", "已通过", "未验收", "取得该能力产物证据"),
                ("已建立", "已通过", "验收中", "完成该能力生产验收"),
                ("已收口", "已通过", "验收中", "完成该能力生产验收"),
                ("已建立", "已通过", "验收暂停", "完成该能力生产验收"),
                ("已收口", "已通过", "验收暂停", "完成该能力生产验收"),
                ("已建立", "已通过", "已验收", "无后续退出门"),
                ("已收口", "已通过", "已验收", "无后续退出门"),
            ];
        foreach (var statusRow in statusRows)
        {
            allowedStatusTransitions.Should().Contain(
                (statusRow[1], statusRow[2], statusRow[3], statusRow[4]),
                "source, candidate, production and next-exit states must form a coherent transition");
        }

        AssertActiveFragment(
            roadmap,
            "[Agent 工作流与异常契约](./Agent工作流与异常契约.md)",
            "the roadmap must route MAF/Harness detail to its owner contract");
        AssertActiveFragment(
            roadmap,
            "[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)",
            "the roadmap must route Cloud detail to its owner contract");
        AssertActiveFragment(
            roadmap,
            "[DDD 聚合根边界](./DDD聚合根边界.md)",
            "the roadmap must route persistence detail to its owner contract");
        AssertActiveFragment(
            roadmap,
            "[AICopilot 安全部署契约](./AICopilot安全部署契约.md)",
            "the roadmap must route candidate and deployment detail to its owner contract");
        AssertActiveFragment(
            roadmap,
            "[AICopilot 业务规则](./AICopilot业务规则.md)",
            "the roadmap must route strategic exclusions to the business rule owner");
        AssertActiveFragment(
            agentInstructions,
            "路线图是状态与退出门入口，不承载实现、候选验证算法或部署操作正文",
            "AGENTS must keep the roadmap route narrow");
        AssertActiveFragment(
            businessRules,
            "[AI 架构路线图](./AI架构路线图.md) 只作为当前状态与下一退出门登记表",
            "business rules must route current status to the roadmap");

        string[] strategicExclusions =
        [
            "不建设任意用户上传 Agent 定义后直接执行的平台",
            "不允许模型扩大 Tool、MCP、知识库、数据源或证据权限",
            "不以通用 SQL、MCP 或 Direct DB 替代已覆盖的 Cloud typed GET",
            "不以 Simulation、LLM 推断或当前健康评分冒充生产事实或预测模型结果",
        ];
        foreach (var exclusion in strategicExclusions)
        {
            AssertActiveFragment(
                businessRules,
                exclusion,
                "strategic exclusions belong to the business rule owner");
            roadmap.Should().NotContain(
                exclusion,
                "the roadmap must link strategic exclusions instead of copying them");
        }

        string[] forbiddenImplementationMarkers =
        [
            "AgentModeProvider",
            "mode_get",
            "mode_set",
            "SetModeAsync",
            "ToolInvocationGuardChatClient",
            "HarnessAgentOptions",
            "ChatOptions.Tools",
            "BusinessQueryFallbackPolicy",
            "BusinessQueryExecutor",
            "McpToolOutputSchemaContractV1",
            "AiGatewayDbContext",
            "Validate-Candidate",
            "Prepare-Release",
            "Deploy-Changed",
        ];
        foreach (var marker in forbiddenImplementationMarkers)
        {
            roadmap.Should().NotContain(
                marker,
                "runtime and validation implementation prose belongs to owner contracts");
        }

        roadmap.Should().NotContain("固定测试数");
        Regex.IsMatch(
                roadmap,
                @"(?i)(?<![A-Za-z])(?:PR|Pull\s+Request)\s*#?\s*\d+(?![A-Za-z0-9])|/pull/\d+",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("pull request history cannot enter the current status registry");
        Regex.IsMatch(
                roadmap,
                @"(?i)(?<![A-Za-z0-9])B[-\s]?\d+(?![A-Za-z0-9])",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("batch identifiers cannot enter the current status registry");
        Regex.IsMatch(
                roadmap,
                @"第?\s*\d+\s*批(?:开发|实施|收口|规则|架构)",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("numbered development batches cannot enter the current status registry");
        Regex.IsMatch(
                roadmap,
                @"(?i)(?:\b(?:commit|sha|head)\b|main@)\s*[:=@]?\s*[0-9a-f]{7,40}\b|/commit/[0-9a-f]{7,40}\b",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("historical commit SHAs cannot enter the current status registry");
        Regex.IsMatch(
                roadmap,
                @"(?<!\d)\d+\s*(?:项|个|条)?\s*(?:测试|用例)",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("fixed test counts cannot enter the current status registry");
        Regex.IsMatch(
                roadmap,
                @"(?:测试|用例|通过)\s*(?:共|合计|总计|为|[:：])?\s*\d+\s*/\s*\d+|\d+\s*/\s*\d+\s*(?:项|个|条)?\s*(?:测试|用例|通过)",
                RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("fixed test result counts cannot enter the current status registry");
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
    public void CloudFallbackTechnicalContract_ShouldRemainTheSingleSourceOfTruth()
    {
        var root = FindRepositoryRoot();
        var agentInstructions = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var cloudContract = File.ReadAllText(
            Path.Combine(root, "docs", "Cloud只读数据分析契约.md"));
        var normalizedCloudContract = NormalizeContractText(cloudContract);

        normalizedCloudContract.Should().Contain(
            "typed-first、结构化结果矩阵、查询确认、受控 Text-to-SQL、Simulation 边界和 fallback 决策的唯一技术正文");
        string[] technicalOwnerMarkers =
        [
            "AICOPILOT_FALLBACK_POLICY_V1_BEGIN",
            "BusinessQueryFallbackPolicy",
            "BusinessQueryContext",
            "Success、Empty、NeedClarification、Unsupported、Unavailable 或 Unauthorized",
            "查询确认键固定为 SessionId",
            "Text-to-SQL 修复重试默认最多 3 次，硬上限 5 次",
            "PreviousSqlForRepair",
            "CloudReadOnlyGovernedSchema",
            "SimulationBusiness DataSourceId",
            "Simulation.Enabled=false",
            "mfg_processes.process_name",
            "DeviceLog 自然语言中的工序或设备范围",
            "schema create、superuser、createdb、createrole 或 replication",
            "可能原因必须明确标注为 AI 推断分析",
        ];
        foreach (var marker in technicalOwnerMarkers)
        {
            normalizedCloudContract.Should().Contain(
                marker,
                "the Cloud contract must own every fallback implementation marker");
        }

        var linkedAuthorityDocuments = ActiveContractPaths
            .Where(path => path != "docs/Cloud只读数据分析契约.md")
            .Select(path => (
                Name: path,
                Text: NormalizeContractText(File.ReadAllText(Path.Combine(root, path)))))
            .ToArray();
        foreach (var (name, text) in linkedAuthorityDocuments)
        {
            foreach (var marker in technicalOwnerMarkers)
            {
                text.Should().NotContain(
                    marker,
                    $"{name} must link the unique Cloud contract instead of copying {marker}");
            }
        }

        agentInstructions.Should().Contain(
            "[Cloud 只读数据分析契约](docs/Cloud只读数据分析契约.md)");
        businessRules.Should().Contain(
            "[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)");
        agentContract.Should().Contain(
            "[Cloud 只读数据分析契约](./Cloud只读数据分析契约.md)");
        agentInstructions.Should().Contain("模型只看到 `BusinessQuery`");
        businessRules.Should().Contain("模型只看到 `BusinessQuery`");
        agentContract.Should().Contain(
            "模型可见的业务查询工具只有 `BusinessQuery`");
        agentContract.Should().Contain(
            "Text-to-SQL 只作为工具内部能力，绝不以独立工具暴露给模型");

        cloudContract.Should().Contain(
            "`BusinessQueryFallbackPolicy` 是唯一 fallback 决策 owner");
        cloudContract.Should().Contain(
            "只有同一 Cloud 来源返回 `Unsupported` 或 `Unavailable`");
        cloudContract.Should().Contain(
            "该 policy 才允许 `BusinessQueryExecutor` 在服务端自动进入受控 Text-to-SQL");
        cloudContract.Should().Contain("模型不得决定、触发或绕过 fallback");
        cloudContract.Should().Contain(
            "权限或凭据失败、跨源、MCP 与 Simulation 均不得 fallback");

        const string policyStartMarker = "<!-- AICOPILOT_FALLBACK_POLICY_V1_BEGIN -->";
        const string policyEndMarker = "<!-- AICOPILOT_FALLBACK_POLICY_V1_END -->";
        var canonicalPolicy = ExtractUniqueDelimitedSection(
            cloudContract,
            policyStartMarker,
            policyEndMarker);
        ComputeSha256(NormalizeContractText(canonicalPolicy)).Should().Be(
            "c3998e3797727ce14924aa66bbae5087630b96ff4d4da5e4d49aefd8e7a638a4",
            "the reviewed fallback decision matrix is a closed contract block");

        cloudContract.Should().Contain(
            "同一 `SessionId` 内只按已确认 scope 复用上下文");
        var activeContractStatements = ActiveContractPaths
            .SelectMany(path => GetContractClauses(
                File.ReadAllText(Path.Combine(root, path))))
            .ToArray();
        string[] retiredFallbackAssertions =
        [
            "模型决定是否进入 Text-to-SQL fallback",
            "模型可以决定是否进入 Text-to-SQL fallback",
            "模型决定 Text-to-SQL fallback",
            "可由模型决定是否尝试同源 Text-to-SQL",
            "可由模型触发同源 Text-to-SQL",
            "模型可以绕过 fallback",
            "模型可以触发 fallback",
            "模型不得决定 fallback 但在服务端允许时可以触发 fallback",
            "LLM 选择 Text-to-SQL",
            "同任务后续沿用已确认上下文",
            "Simulation 可以 fallback",
            "Simulation fallback 不受限制",
            "权限失败后允许 fallback",
            "权限失败仍走 fallback",
            "凭据失败时可以进入 Text-to-SQL",
            "Unavailable 可跨源 fallback",
            "Unavailable 可改用 MES Text-to-SQL",
            "跨源 fallback 合法",
        ];
        (string Statement, string RetiredAssertion)[] unsafeRegressionExamples =
        {
            ("不得暴露 SQL，但模型决定 Text-to-SQL fallback", "模型决定 Text-to-SQL fallback"),
            ("服务端负责审计；允许 LLM 选择 Text-to-SQL", "LLM 选择 Text-to-SQL"),
            ("模型不得决定 fallback 但在服务端允许时可以触发 fallback", "模型不得决定 fallback 但在服务端允许时可以触发 fallback"),
            ("模型可以决定是否进入\nText-to-SQL fallback", "模型可以决定是否进入 Text-to-SQL fallback"),
            ("权限检查必须保留，但是权限失败后允许 fallback", "权限失败后允许 fallback"),
            ("禁止泄露 SQL，同时模型决定 Text-to-SQL fallback", "模型决定 Text-to-SQL fallback"),
            ("禁止泄露 SQL，同时模型决定 **Text-to-SQL** fallback", "模型决定 Text-to-SQL fallback"),
            ("禁止泄露 SQL，同时模型决定 [Text-to-SQL](https://example.invalid) fallback", "模型决定 Text-to-SQL fallback"),
            ("禁止泄露 SQL，同时模型决定 [Text-to-SQL][policy] fallback", "模型决定 Text-to-SQL fallback"),
            ("禁止泄露 SQL，同时模型决定 <em>Text-to-SQL</em> fallback", "模型决定 Text-to-SQL fallback"),
            ("禁止泄露 SQL，同时模型决定 <code>Text-to-SQL</code> fallback", "模型决定 Text-to-SQL fallback"),
        };
        foreach (var (statement, retiredAssertion) in unsafeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(
                    NormalizeContractStatement(statement),
                    NormalizeContractStatement(retiredAssertion))
                .Should()
                .BeTrue();
        }

        (string Statement, string RetiredAssertion)[] safeRegressionExamples =
        [
            ("模型不得决定是否进入 Text-to-SQL fallback", "模型决定是否进入 Text-to-SQL fallback"),
            ("权限失败后不得 fallback", "权限失败后允许 fallback"),
            ("禁止 LLM 选择 Text-to-SQL", "LLM 选择 Text-to-SQL"),
            ("这不是模型决定 Text-to-SQL fallback", "模型决定 Text-to-SQL fallback"),
            ("并非模型决定 Text-to-SQL fallback", "模型决定 Text-to-SQL fallback"),
            ("该规则不作为“模型决定 Text-to-SQL fallback”的依据", "模型决定 Text-to-SQL fallback"),
        ];
        foreach (var (statement, retiredAssertion) in safeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(
                    NormalizeContractStatement(statement),
                    NormalizeContractStatement(retiredAssertion))
                .Should()
                .BeFalse();
        }

        foreach (var activeStatement in activeContractStatements)
        {
            foreach (var retiredAssertion in retiredFallbackAssertions)
            {
                ContainsUnnegatedRetiredAssertion(
                        activeStatement,
                        NormalizeContractStatement(retiredAssertion))
                    .Should()
                    .BeFalse(
                        $"active contracts must reject retired fallback assertion '{retiredAssertion}'");
            }
        }

        var policyStartIndex = cloudContract.IndexOf(policyStartMarker, StringComparison.Ordinal);
        var policyEndIndex = cloudContract.IndexOf(policyEndMarker, StringComparison.Ordinal);
        IsStandaloneLineAtColumnZero(cloudContract, policyStartIndex, policyStartMarker)
            .Should()
            .BeTrue("the canonical policy start marker must be active at column zero");
        IsStandaloneLineAtColumnZero(cloudContract, policyEndIndex, policyEndMarker)
            .Should()
            .BeTrue("the canonical policy end marker must be active at column zero");
        IsInsideInactiveContractWrapper(cloudContract, policyStartIndex).Should().BeFalse(
            "the canonical fallback policy must remain active Markdown prose");
        string[] inactivePolicyFixtures =
        [
            $"```text\n{policyStartMarker}\n{policyEndMarker}\n```",
            $"````text\n```text\n{policyStartMarker}\n{policyEndMarker}\n```\n````",
            $"    {policyStartMarker}\n    {policyEndMarker}",
            $"<!--\n{policyStartMarker}\n{policyEndMarker}\n-->",
            $"<template>\n{policyStartMarker}\n{policyEndMarker}\n</template>",
            $"<script type=\"text/plain\">\n{policyStartMarker}\n{policyEndMarker}\n</script>",
            $"<pre>\n{policyStartMarker}\n{policyEndMarker}\n</pre>",
            $"<style>\n{policyStartMarker}\n{policyEndMarker}\n</style>",
            $"<textarea>\n{policyStartMarker}\n{policyEndMarker}\n</textarea>",
            $"<xmp>\n{policyStartMarker}\n{policyEndMarker}\n</xmp>",
            $"<iframe>\n{policyStartMarker}\n{policyEndMarker}\n</iframe>",
            $"<noembed>\n{policyStartMarker}\n{policyEndMarker}\n</noembed>",
            $"<noframes>\n{policyStartMarker}\n{policyEndMarker}\n</noframes>",
            $"<plaintext>\n{policyStartMarker}\n{policyEndMarker}",
            $"<div hidden>\n{policyStartMarker}\n{policyEndMarker}\n</div>",
            $"<?hidden\n{policyStartMarker}\n{policyEndMarker}\n?>",
            $"<![CDATA[\n{policyStartMarker}\n{policyEndMarker}\n]]>",
            $"<!DOCTYPE\n{policyStartMarker}\n{policyEndMarker}\n>",
        ];
        foreach (var fixture in inactivePolicyFixtures)
        {
            IsInsideInactiveContractWrapper(
                    fixture,
                    fixture.IndexOf(policyStartMarker, StringComparison.Ordinal))
                .Should()
                .BeTrue();
        }

        var activeAfterIndentedCodeFixture =
            $"    ```text\n{policyStartMarker}\n{policyEndMarker}";
        IsInsideInactiveContractWrapper(
                activeAfterIndentedCodeFixture,
                activeAfterIndentedCodeFixture.IndexOf(
                    policyStartMarker,
                    StringComparison.Ordinal))
            .Should()
            .BeFalse("a four-space indented code line is not a CommonMark fence");
        var activeAfterClosedCommentFixture =
            $"<!-- example <div> -->\n{policyStartMarker}\n{policyEndMarker}";
        IsInsideInactiveContractWrapper(
                activeAfterClosedCommentFixture,
                activeAfterClosedCommentFixture.IndexOf(
                    policyStartMarker,
                    StringComparison.Ordinal))
            .Should()
            .BeFalse("HTML element examples inside closed comments must not hide active prose");
    }

    [Fact]
    public void ActiveContracts_ShouldKeepDurabilitySemantics()
    {
        var root = FindRepositoryRoot();
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var persistenceContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));

        AssertActiveFragment(
            agentContract,
            "AgentSession checkpoint 只用于会话连续性，不是 durable Tool checkpoint、任务队列、lease/fencing 或工具恢复点，也不证明远端工具已经完成。取得锁后发现遗留 `Running` 时只允许转为 `Interrupted`；Interrupted 后不得恢复或重放模型、工具及旧批准。",
            "AgentSession checkpoints must remain active continuity-only prose");
        AssertActiveFragment(
            agentContract,
            "`persistence_commit_outcome_unknown` 表示写入可能已提交，调用方不得自动重试；只返回非敏感 commit id 供受控对账。数据库 commit marker、事务验证与文件持久化规则的唯一技术正文是 [DDD 聚合根边界](./DDD聚合根边界.md)，本契约不复制其实现。",
            "the no-auto-retry outcome and its DDD route must remain active prose");
        AssertActiveFragment(
            persistenceContract,
            "数据库 durable commit marker 只用于事务提交结果验证和 commit-ACK 丢失对账，不是 Agent durable 编排、Tool checkpoint、任务恢复点或工具重放依据。",
            "database commit markers must remain distinct from Agent durability");
    }

    [Fact]
    public void DddAndPersistenceTechnicalContract_ShouldRemainTheSingleSourceOfTruth()
    {
        var root = FindRepositoryRoot();
        var agentInstructions = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var businessRules = File.ReadAllText(
            Path.Combine(root, "docs", "AICopilot业务规则.md"));
        var agentContract = File.ReadAllText(
            Path.Combine(root, "docs", "Agent工作流与异常契约.md"));
        var cloudContract = File.ReadAllText(
            Path.Combine(root, "docs", "Cloud只读数据分析契约.md"));
        var roadmap = File.ReadAllText(
            Path.Combine(root, "docs", "AI架构路线图.md"));
        var deploymentGuide = File.ReadAllText(
            Path.Combine(root, "deploy", "enterprise-ai", "README.md"));
        var dddContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));
        var normalizedDddContract = NormalizeContractText(dddContract);
        var persistenceContractPaths = ActiveContractPaths
            .Append("deploy/enterprise-ai/README.md")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        normalizedDddContract.Should().Contain(
            "聚合、持久化集合分类、DbContext 与迁移所有权、审计、Outbox、事务提交和 RAG 文件持久化的唯一技术正文");
        string[] requiredDddMarkers =
        [
            "`Session`",
            "`LanguageModel`",
            "`ConversationTemplate`",
            "`ToolRegistration`",
            "`BusinessDatabase`",
            "`DataSourcePermissionGrant`",
            "`McpServerInfo`",
            "`KnowledgeBase`",
            "`EmbeddingModel`",
            "`KnowledgeCategory`",
            "`KnowledgeSupplement`",
            "`Message`",
            "`Document`",
            "`DocumentChunk`",
            "`ModelParameters`",
            "`TemplateSpecification`",
            "`AgentSessionState`",
            "`ModelQuotaReservation`",
            "`PersistenceCommitMarker`",
            "`AuditLogEntry`",
            "`OutboxMessage`",
            "`ApplicationUser`",
            "`ExternalIdentityBinding`",
            "`IdentityRoleClaim<>`",
            "`IdentityRole<>`",
            "`IdentityUserClaim<>`",
            "`IdentityUserLogin<>`",
            "`IdentityUserRole<>`",
            "`IdentityUserToken<>`",
            "| Aggregate |",
            "| AggregateChild |",
            "| OwnedValueObject |",
            "| RuntimeRecord |",
            "| Audit |",
            "| IdentityRecord |",
            "`AiCopilotDbContext`",
            "`IdentityStoreDbContext`",
            "`AiGatewayDbContext`",
            "`RagDbContext`",
            "`DataAnalysisDbContext`",
            "`McpServerDbContext`",
            "`AuditDbContext`",
            "`OutboxDbContext`",
            "`PersistenceCommitMarkerDbContext`",
            "`ExcludeFromMigrations`",
            "`__EFMigrationsHistory_*`",
            "`PostgresModelQuotaReservationStore`",
            "`AiGatewayTransactionRunner`",
            "Audit writer decision tree",
            "`OutboxDispatcher`",
            "`FOR UPDATE SKIP LOCKED`",
            "`PersistenceCommitEngine`",
            "`RepositoryPersistenceCommitter`",
            "`SaveChangesAsync(false)`",
            "`ITransactionalExecutionService`",
            "`ExecuteInTransactionAsync(... verifySucceeded ...)`",
            "`PersistenceMaintenanceWorker`",
            "`PersistenceFileMaintenanceService`",
            "`IModelQuotaReservationStore.ReclaimExpiredAsync`",
            "`PersistenceFileCommitProtocol`",
            "PostgreSQL advisory lease",
            "`AICOPILOT_PERSISTENCE_*`",
            "`FileStorage:RootPath`",
            "`LocalApplicationData/AICopilot/storage`",
            "`created_at_utc`",
            "commit-ACK 丢失、verification transient/persistent failure、caller cancellation 和数据库生成 identity 重放",
        ];
        foreach (var marker in requiredDddMarkers)
        {
            var markerIndex = dddContract.IndexOf(marker, StringComparison.Ordinal);
            markerIndex.Should().BeGreaterThanOrEqualTo(
                0,
                "the DDD contract must own the complete persistence implementation boundary");
            IsInsideInactiveContractWrapper(dddContract, markerIndex)
                .Should()
                .BeFalse($"required DDD marker '{marker}' must remain active Markdown prose");
        }

        string[] requiredDddRelationshipFragments =
        [
            "`DataSourcePermissionGrant` 是 DataAnalysis bounded context 的正式独立聚合根。独立 `DataSourcePermissionGrantId`、`RowVersion`、授权/撤销生命周期、repository、审计写入和 `(DataSourceId, TargetType, TargetValue)` 唯一目标约束共同构成其独立不变量边界。`DataSourceId` 的强类型是 `BusinessDatabaseId`。",
            "`DataSourcePermissionGrant` 与 `BusinessDatabase` 是两个聚合；跨聚合仅由 Grant 的 `DataSourceId` 引用 `BusinessDatabaseId`，`BusinessDatabase` 不持有 Grant 子实体集合，也不得通过 EF navigation 恢复父子归属。该归属是正式长期边界。",
            "| Aggregate | `Session`、`LanguageModel`、`ConversationTemplate`、`ToolRegistration`、`BusinessDatabase`、`DataSourcePermissionGrant`、`McpServerInfo`、`KnowledgeBase`、`EmbeddingModel`、`KnowledgeCategory`、`KnowledgeSupplement` |",
            "| AggregateChild | `Message`、`Document`、`DocumentChunk` |",
            "| OwnedValueObject | `ModelParameters`、`TemplateSpecification` |",
            "| RuntimeRecord | `AgentSessionState`、`ModelQuotaReservation`、`PersistenceCommitMarker` |",
            "| Audit | `AuditLogEntry`、`OutboxMessage` |",
            "| IdentityRecord | `ApplicationUser`、`ExternalIdentityBinding`、`IdentityRoleClaim<>`、`IdentityRole<>`、`IdentityUserClaim<>`、`IdentityUserLogin<>`、`IdentityUserRole<>`、`IdentityUserToken<>` |",
            "| `AiCopilotDbContext` | `AuditLogEntry`、`OutboxMessage`、`PersistenceCommitMarker` | 主基础设施 migration owner；唯一拥有 Outbox 与 persistence commit marker 迁移 |",
            "| `IdentityStoreDbContext` | Identity 记录、`ExternalIdentityBinding`；审计只作为事务参与者 | 拥有 Identity 迁移；审计映射使用 `ExcludeFromMigrations` |",
            "| `AiGatewayDbContext` | 上述七个 AiGateway 集合 | 拥有已投产的 append-only migration 历史和当前增量升级 |",
            "| `RagDbContext` | RAG 聚合、`Document`、`DocumentChunk` | 拥有 RAG 迁移 |",
            "| `DataAnalysisDbContext` | `BusinessDatabase`、`DataSourcePermissionGrant` | 拥有 DataAnalysis 迁移 |",
            "| `McpServerDbContext` | `McpServerInfo` | 拥有 MCP 迁移 |",
            "| `AuditDbContext` | 审计查询和运行时审计写入 | 无独立 migration |",
            "| `OutboxDbContext` | 事务内物化和短生命周期领取 Outbox | 无独立 migration |",
            "| `PersistenceCommitMarkerDbContext` | fresh verification 与 marker 维护 | 无独立 migration，映射使用 `ExcludeFromMigrations` |",
            "六个 migration owner 必须使用各自隔离的 `__EFMigrationsHistory_*`；不得让单一 Context 的迁移或回滚污染其它 Context。",
            "AiGateway 尚未首次投产且 `aigateway` schema 不存在，或该 schema 由当前 migration role 拥有、仅保留 owner 的默认 `CREATE/USAGE` ACL，并且其中不存在任何 namespace-owned 对象（包括 table/index/sequence/view/function/type/collation/operator/text-search/statistics 等）的真正空库，可以在投产前整理 fresh baseline；仅缺 migration history、schema owner/ACL 漂移或残留任一对象均不得归类 Fresh。一旦 migration ID 进入生产，历史文件名、ID 和字节全部 append-only，后续模型变化只能新增 migration，不得压缩、改写或用新 baseline 覆盖生产升级链。",
            "生产升级前必须对真实 `__EFMigrationsHistory_AiGateway` 行和包括该 history 表在内的 schema 元数据做冻结指纹核对；指纹必须绑定 schema 及对象 owner/有效 ACL、列级 ACL、migration role 的完整双向角色继承连通分量与每条 grant option、global/schema default ACL、所有未建模 namespace-owned 对象 inventory、relation inventory、所有与 `aigateway` 相连的表继承/分区父子边、detach 状态、partition bound 与 partition key、列及其 temporal precision、索引完整定义、约束、sequence 参数（包括 cache）及 `OWNED BY` 列依赖、view/materialized view、全部 schema function、独立 enum/domain/range/composite type、非内部 trigger 及其跨 schema 被调函数的定义/owner/安全属性/ACL、表 RLS 开关和 policy，并从指纹核对到 AiGateway migration 完成全程持同一 PostgreSQL advisory lock，未知 history/schema 不得猜测、补写虚假 history 或继续 migration。候选 migration 必须在停止旧 runtime、备份或写入 started 状态前，以真实连接角色在可回滚事务内完整获取同一 DDL 围栏；当前围栏覆盖 `aigateway` relation/sequence、数据库本地 system catalog，以及共享的 `pg_authid`/`pg_auth_members`，因此 migration role 必须具备对应的 elevated catalog-lock capability，只验证 schema owner 权限不足以放行。退役表只能来自显式 allowlist；任何可到达生产 migration 的发布入口都必须先停稳 schema-dependent runtime，再生成最终完整 PostgreSQL dump 与 SHA-256，其它表和业务记录必须保留。",
            "`PostgresModelQuotaReservationStore` 是模型配额唯一生产 store，只能经 `AiGatewayTransactionRunner` 写入 `AiGatewayDbContext`；模型调用预约、结算、回收的事务语义不得复制到其它 Context。",
            "没有真实事件生产者的 DbContext 不得复制 Outbox `DbSet`、映射或 `SaveChangesAsync` 领域事件扫描。DataAnalysis 与 MCP 不写 Outbox；AiGateway 只从 `Session` 领域事件物化 Outbox，RAG 只使用 delayed integration-event factory，业务 Context 不映射共享 Outbox。",
            "审计写入遵守唯一 Audit writer decision tree：有业务保存点的命令把业务变更和审计行放入同一事务；`auditLogWriter.SaveChangesAsync` 只允许用于没有业务保存点且已被白名单记录的路径。",
            "`OutboxDispatcher` 统一领取和发布，必须保留 PostgreSQL `FOR UPDATE SKIP LOCKED` 或等价互斥策略以及 dead-letter 上限，禁止多 worker 重复发布同一消息。",
            "业务行、Outbox、审计和数据库 durable commit marker 只能由唯一 `PersistenceCommitEngine` / `RepositoryPersistenceCommitter` 在同一数据库事务中提交。每个 execution-strategy attempt 对业务 Context 只执行一次 `SaveChangesAsync(false)`；事务确认后才 `AcceptAllChanges`、清领域事件或清 RAG factory buffer。",
            "Identity 通过 `ITransactionalExecutionService` / `IdentityTransactionalExecutionService` 复用同一 engine；非成功 `Result` 必须回滚 UserManager/RoleManager 已触发的中间保存，拒绝审计只能在回滚后另行提交。禁止恢复 `EfTransactionalExecutionService`、通用 Outbox 扫描或复制第二套 transaction/retry。",
            "EF execution strategy 必须使用官方 `ExecuteInTransactionAsync(... verifySucceeded ...)` 或等价官方入口，禁止手写业务重试循环。commit-unknown 不得通过 `SaveChanges(false)`、Outbox 或 audit 是否存在来推断成功。",
            "数据库 durable commit marker 只用于事务提交结果验证和 commit-ACK 丢失对账，不是 Agent durable 编排、Tool checkpoint、任务恢复点或工具重放依据。marker 必须与业务写入处于同一事务，并由 fresh context 在独立超时和 execution strategy 下验证。",
            "marker 写入后 caller cancellation 不得中断 commit/verification。无法确认时返回稳定 503 `persistence_commit_outcome_unknown` 和非敏感 commit id；调用方不得自动重放业务。",
            "`PersistenceMaintenanceWorker` 只通过 `PersistenceFileMaintenanceService` 对账 RAG journal、清理 commit marker，并通过 `IModelQuotaReservationStore.ReclaimExpiredAsync` 回收过期模型配额预约；它不领取或发布 Outbox。Outbox 由独立托管的 `OutboxDispatcher` 负责。commit marker 默认保留 30 天并按 `created_at_utc` 索引；保留期必须长于对账延迟，有待处理或不可读 journal 时不得删除 marker。",
            "知识库文件唯一写入口是 RAG Document API。RAG `UploadDocument` 必须先写 durable reconciliation journal，再写物理文件，并与 repository marker 共用同一 commit id。",
            "RAG 数据库绑定上传路径必须复用唯一 `PersistenceFileCommitProtocol`。repository 未消费预留 commit id 时确认必须 fail-closed，回滚未提交文件并保留失败信号；不得因为 callback 正常返回就清除 journal。",
            "请求与 DataWorker 通过 PostgreSQL advisory lease 互斥。提交结果未知时保留文件和 journal；后台看到同一 marker 才保留文件并清 journal，看不到 marker 才删除文件。journal 不可读时停止 marker 清理。",
            "默认每 300 秒扫描，只对至少 10 分钟前的 journal 对账，单轮最多 100 条；`AICOPILOT_PERSISTENCE_*` 只能调整这些部署参数，不得把对账延迟设为 0，也不得让 marker 保留期短于对账延迟。不可读 journal 必须 fail-closed，不得手工批量删除 `.persistence/file-reconciliation`。",
            "标准容器共享卷只允许受信任的 AICopilot 后端写入。当前路径边界拒绝既有 symlink/reparse traversal，但不把同 UID 恶意进程在检查与打开之间替换目录的 TOCTOU 视为已解决；扩大威胁模型前必须增加容器权限隔离或 dirfd/`openat` 原子路径操作。",
            "标准生产容器部署必须把 RAG 可写 `FileStorage:RootPath` 固定为共享卷 `/var/lib/aicopilot/storage`，在该部署中不得回退容器层、`/app`、`LocalApplicationData` 或共享卷外路径。本地 dev/test 未显式配置时可使用现有 `LocalApplicationData/AICopilot/storage` fallback，但不得把它当作生产容器持久化。durable local file/journal backend 只支持 Linux/macOS，生产固定 Linux；Windows 必须明确拒绝该 backend。",
            "HttpApi、DataWorker 与 RagWorker 必须共享 `/var/lib/aicopilot`。RagWorker 的文档删除 consumer 必须先按 storage path 查询 pending journal 并争用同一 commit lease；journal 不可读或 lease active 时让消息重试，禁止从容器或 cron 直接删文件。文件对账、marker 保留与清理只能由当前维护链执行，不得恢复会话文件、Artifact workspace 或第二套文件 checkpoint。",
        ];
        foreach (var fragment in requiredDddRelationshipFragments)
        {
            var fragmentIndex = dddContract.IndexOf(fragment, StringComparison.Ordinal);
            fragmentIndex.Should().BeGreaterThanOrEqualTo(
                0,
                "the DDD contract must keep each reviewed classification, ownership and persistence relationship intact");
            IsInsideInactiveContractWrapper(dddContract, fragmentIndex)
                .Should()
                .BeFalse("reviewed DDD relationship fragments must remain active Markdown prose");
        }

        string[] uniqueTechnicalOwnerMarkers =
        [
            "DataSourcePermissionGrant",
            "DataSourcePermissionGrantId",
            "AiCopilotDbContext",
            "IdentityStoreDbContext",
            "AiGatewayDbContext",
            "RagDbContext",
            "DataAnalysisDbContext",
            "McpServerDbContext",
            "AuditDbContext",
            "OutboxDbContext",
            "PersistenceCommitMarkerDbContext",
            "AggregateChild",
            "OwnedValueObject",
            "RuntimeRecord",
            "IdentityRecord",
            "ExcludeFromMigrations",
            "__EFMigrationsHistory_*",
            "PostgresModelQuotaReservationStore",
            "AiGatewayTransactionRunner",
            "Audit writer decision tree",
            "OutboxDispatcher",
            "FOR UPDATE SKIP LOCKED",
            "PersistenceCommitEngine",
            "RepositoryPersistenceCommitter",
            "SaveChangesAsync(false)",
            "ITransactionalExecutionService",
            "ExecuteInTransactionAsync(... verifySucceeded ...)",
            "PersistenceMaintenanceWorker",
            "PersistenceFileMaintenanceService",
            "IModelQuotaReservationStore.ReclaimExpiredAsync",
            "PersistenceFileCommitProtocol",
            "PostgreSQL advisory lease",
            "AICOPILOT_PERSISTENCE_*",
            "FileStorage:RootPath",
            "LocalApplicationData/AICopilot/storage",
            "created_at_utc",
        ];
        var linkedAuthorityDocuments = persistenceContractPaths
            .Where(path => path != "docs/DDD聚合根边界.md")
            .Select(path => (
                Name: path,
                Text: NormalizeContractText(File.ReadAllText(Path.Combine(root, path)))))
            .ToArray();
        foreach (var (name, text) in linkedAuthorityDocuments)
        {
            foreach (var marker in uniqueTechnicalOwnerMarkers)
            {
                text.Should().NotContain(
                    NormalizeContractText(marker),
                    $"{name} must link the unique DDD contract instead of copying {marker}");
            }
        }

        AssertActiveFragment(
            agentInstructions,
            "唯一技术正文 [DDD 聚合根边界](docs/DDD聚合根边界.md)",
            "AGENTS must actively route DDD and persistence work to the owner contract");
        AssertActiveFragment(
            businessRules,
            "唯一技术正文是 [DDD 聚合根边界](./DDD聚合根边界.md)",
            "business rules must actively link the DDD owner contract");
        AssertActiveFragment(
            agentContract,
            "唯一技术正文是 [DDD 聚合根边界](./DDD聚合根边界.md)",
            "the Agent contract must actively link the DDD owner contract");
        AssertActiveFragment(
            cloudContract,
            "只由 [DDD 聚合根边界](./DDD聚合根边界.md) 定义",
            "the Cloud contract must actively link the DDD owner contract");
        AssertActiveFragment(
            roadmap,
            "[DDD 聚合根边界](./DDD聚合根边界.md)",
            "the roadmap must actively link the DDD owner contract");
        AssertActiveFragment(
            deploymentGuide,
            "[DDD 聚合根边界](../../docs/DDD聚合根边界.md)",
            "the deployment guide must actively link the DDD owner contract");

        AssertActiveFragment(
            businessRules,
            "聚合必须按各自业务不变量和生命周期独立演进；数据源授权与业务数据源之间只通过稳定标识跨聚合引用，不能把独立授权生命周期重新下沉为父实体的可变子集合。正式聚合清单、持久化分类和不变量理由只见 DDD 唯一技术正文。",
            "the business aggregate principle must remain active prose");
        AssertActiveFragment(
            businessRules,
            "业务变更、审计、待发布事件和数据库提交结果保障必须保持原子；提交结果未知时不得自动重放业务。事务参与者、重试/验证算法、Outbox 领取和 commit marker 细节只见 DDD 唯一技术正文。",
            "the business atomicity and no-replay principle must remain active prose");
        AssertActiveFragment(
            businessRules,
            "知识库上传必须通过持久化对账保护数据库与文件一致性，且只能使用正式 RAG 文档入口。journal、lease、存储路径、后台对账与保留实现只见 DDD 唯一技术正文。",
            "the business RAG persistence principle must remain active prose");
        AssertActiveFragment(
            agentContract,
            "`persistence_commit_outcome_unknown` 表示写入可能已提交，调用方不得自动重试；只返回非敏感 commit id 供受控对账。数据库 commit marker、事务验证与文件持久化规则的唯一技术正文是 [DDD 聚合根边界](./DDD聚合根边界.md)，本契约不复制其实现。",
            "the Agent outcome contract must actively forbid automatic retries and route to DDD");
        AssertActiveFragment(
            agentContract,
            "AgentSession checkpoint 只用于会话连续性，不是 durable Tool checkpoint、任务队列、lease/fencing 或工具恢复点，也不证明远端工具已经完成。取得锁后发现遗留 `Running` 时只允许转为 `Interrupted`；Interrupted 后不得恢复或重放模型、工具及旧批准。",
            "AgentSession checkpoints must remain active continuity-only prose");

        var activeStatements = persistenceContractPaths
            .SelectMany(path => GetContractClauses(
                File.ReadAllText(Path.Combine(root, path))))
            .ToArray();
        string[] retiredPersistenceAssertions =
        [
            "DataSourcePermissionGrant 是 BusinessDatabase 子实体",
            "BusinessDatabase 持有 DataSourcePermissionGrant",
            "恢复 EfTransactionalExecutionService",
            "复制第二套 transaction/retry",
            "恢复通用 Outbox 扫描",
            "DataAnalysis 写 Outbox",
            "MCP 写 Outbox",
            "业务 Context 映射共享 Outbox",
        ];
        (string Statement, string RetiredAssertion)[] unsafeRegressionExamples =
        [
            ("DataSourcePermissionGrant 是 BusinessDatabase 子实体", "DataSourcePermissionGrant 是 BusinessDatabase 子实体"),
            ("允许恢复 EfTransactionalExecutionService", "恢复 EfTransactionalExecutionService"),
            ("事务可以复制第二套 transaction/retry", "复制第二套 transaction/retry"),
            ("DataAnalysis 写 Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计且 DataAnalysis 写 Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计并允许 DataAnalysis 写 Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计，同时 DataAnalysis **写** Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计，同时 DataAnalysis [写][verb] Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计，同时 DataAnalysis <strong>写</strong> Outbox", "DataAnalysis 写 Outbox"),
            ("不得删除审计，同时 DataAnalysis <code>写</code> Outbox", "DataAnalysis 写 Outbox"),
        ];
        foreach (var (statement, retiredAssertion) in unsafeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(
                    NormalizeContractStatement(statement),
                    NormalizeContractStatement(retiredAssertion))
                .Should()
                .BeTrue();
        }

        (string Statement, string RetiredAssertion)[] safeRegressionExamples =
        [
            ("这不是 DataAnalysis 写 Outbox", "DataAnalysis 写 Outbox"),
            ("并非 DataAnalysis 写 Outbox", "DataAnalysis 写 Outbox"),
            ("该规则不作为“DataAnalysis 写 Outbox”的依据", "DataAnalysis 写 Outbox"),
            ("不得通过旧路径而恢复通用 Outbox 扫描", "恢复通用 Outbox 扫描"),
        ];
        foreach (var (statement, retiredAssertion) in safeRegressionExamples)
        {
            ContainsUnnegatedRetiredAssertion(
                    NormalizeContractStatement(statement),
                    NormalizeContractStatement(retiredAssertion))
                .Should()
                .BeFalse();
        }

        foreach (var activeStatement in activeStatements)
        {
            foreach (var retiredAssertion in retiredPersistenceAssertions)
            {
                ContainsUnnegatedRetiredAssertion(
                        activeStatement,
                        NormalizeContractStatement(retiredAssertion))
                    .Should()
                    .BeFalse(
                        $"active contracts must reject retired persistence assertion '{retiredAssertion}'");
            }
        }
    }

    [Fact]
    public void DataSourcePermissionGrant_ShouldKeepFormalIndependentAggregateOwnership()
    {
        var root = FindRepositoryRoot();
        var aggregateContract = File.ReadAllText(
            Path.Combine(root, "docs", "DDD聚合根边界.md"));
        var nonOwnerContracts = ActiveContractPaths
            .Where(path => path != "docs/DDD聚合根边界.md")
            .Select(path => File.ReadAllText(Path.Combine(root, path)))
            .ToArray();
        foreach (var contract in nonOwnerContracts)
        {
            contract.Should().NotContain(
                "DataSourcePermissionGrant",
                "aggregate type ownership belongs only to the DDD technical contract");
        }

        aggregateContract.Should().Contain(
            "`DataSourcePermissionGrant` 是 DataAnalysis bounded context 的正式独立聚合根");
        aggregateContract.Should().Contain("独立 `DataSourcePermissionGrantId`");
        aggregateContract.Should().Contain("`RowVersion`");
        aggregateContract.Should().Contain("授权/撤销生命周期");
        aggregateContract.Should().Contain("repository");
        aggregateContract.Should().Contain("审计写入");
        aggregateContract.Should().Contain(
            "`(DataSourceId, TargetType, TargetValue)` 唯一目标约束");
        aggregateContract.Should().Contain(
            "`DataSourceId` 的强类型是 `BusinessDatabaseId`");
        aggregateContract.Should().Contain(
            "跨聚合仅由 Grant 的 `DataSourceId` 引用 `BusinessDatabaseId`");
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
        var ownershipStatements = ActiveContractPaths
            .SelectMany(path => GetContractClauses(
                File.ReadAllText(Path.Combine(root, path))))
            .Where(statement =>
                statement.Contains("DataSourcePermissionGrant", StringComparison.Ordinal) ||
                statement.Contains("数据源授权", StringComparison.Ordinal))
            .ToArray();
        foreach (var statement in ownershipStatements)
        {
            foreach (var marker in unresolvedOwnershipMarkers)
            {
                ContainsUnnegatedRetiredAssertion(statement, marker)
                    .Should()
                    .BeFalse(
                        $"aggregate ownership must not return to unresolved wording '{marker}'");
            }
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

    private static string[] ParseMarkdownTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '|' || trimmed[^1] != '|')
        {
            return [];
        }

        return trimmed[1..^1]
            .Split('|')
            .Select(cell => cell.Trim())
            .ToArray();
    }

    private static string NormalizeContractText(string text)
    {
        var visibleMarkdown = System.Net.WebUtility.HtmlDecode(text);
        visibleMarkdown = Regex.Replace(
            visibleMarkdown,
            @"</?[A-Za-z][A-Za-z0-9:-]*(?:\s[^<>]*?)?/?>",
            string.Empty,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        visibleMarkdown = Regex.Replace(
            visibleMarkdown,
            @"!?\[([^\]\r\n]+)\]\([^)\r\n]*\)",
            "$1",
            RegexOptions.CultureInvariant);
        visibleMarkdown = Regex.Replace(
            visibleMarkdown,
            @"!?\[([^\]\r\n]+)\]\[[^\]\r\n]*\]",
            "$1",
            RegexOptions.CultureInvariant);
        visibleMarkdown = visibleMarkdown
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("~~", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal);
        visibleMarkdown = Regex.Replace(
            visibleMarkdown,
            @"(?<![\p{L}\p{N}])_(?=\S)|(?<=\S)_(?![\p{L}\p{N}])",
            string.Empty,
            RegexOptions.CultureInvariant);
        return string.Join(
            ' ',
            visibleMarkdown.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeContractStatement(string text)
    {
        return NormalizeContractText(text)
            .TrimStart('-', '*', ' ')
            .TrimEnd('。', '；', ';', '.');
    }

    private static string ExtractUniqueDelimitedSection(
        string text,
        string startMarker,
        string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var lastStart = text.LastIndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, StringComparison.Ordinal);
        var lastEnd = text.LastIndexOf(endMarker, StringComparison.Ordinal);
        if (start < 0 || end < 0 || start != lastStart || end != lastEnd || end <= start)
        {
            throw new InvalidDataException(
                $"Expected exactly one ordered contract block: {startMarker} ... {endMarker}");
        }

        return text[(start + startMarker.Length)..end];
    }

    private static IEnumerable<string> GetContractClauses(string text)
    {
        return NormalizeContractText(text)
            .Split(
                new[] { '。', '；', ';', '！', '!', '？', '?' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeContractStatement)
            .Where(statement => !string.IsNullOrWhiteSpace(statement));
    }

    private static void AssertActiveFragment(
        string text,
        string fragment,
        string because)
    {
        var fragmentIndex = text.IndexOf(fragment, StringComparison.Ordinal);
        fragmentIndex.Should().BeGreaterThanOrEqualTo(0, because);
        IsInsideInactiveContractWrapper(text, fragmentIndex)
            .Should()
            .BeFalse(because);
    }

    private static bool IsInsideInactiveContractWrapper(string text, int markerIndex)
    {
        if (markerIndex < 0 || markerIndex > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(markerIndex));
        }

        var prefix = text[..markerIndex];
        var markdownPrefix = Regex.Replace(
            prefix,
            @"<!--.*?-->|<\?.*?\?>|<!\[CDATA\[.*?\]\]>|<![A-Z][^>]*>",
            PreserveLineBreaks,
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var markerLineStart = markerIndex == 0
            ? 0
            : text.LastIndexOf('\n', markerIndex - 1) + 1;
        var markerLinePrefix = text.AsSpan(markerLineStart, markerIndex - markerLineStart);
        if (markerLinePrefix.Length > 0)
        {
            if (markerLinePrefix[0] == '\t')
            {
                return true;
            }

            var leadingSpaces = 0;
            while (leadingSpaces < markerLinePrefix.Length &&
                   markerLinePrefix[leadingSpaces] == ' ')
            {
                leadingSpaces++;
            }

            if (leadingSpaces >= 4)
            {
                return true;
            }
        }

        char? openFenceCharacter = null;
        var openFenceLength = 0;
        var openHtmlElements = new List<string>();
        foreach (var rawLine in markdownPrefix.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var fenceIndent = 0;
            while (fenceIndent < line.Length && line[fenceIndent] == ' ')
            {
                fenceIndent++;
            }

            if (fenceIndent > 3 ||
                (fenceIndent < line.Length && line[fenceIndent] == '\t'))
            {
                continue;
            }

            var content = line[fenceIndent..];
            if (content.Length < 3 || content[0] is not ('`' or '~'))
            {
                if (openFenceCharacter is null)
                {
                    UpdateOpenHtmlElements(line, openHtmlElements);
                }

                continue;
            }

            var fenceCharacter = content[0];
            var fenceLength = 0;
            while (fenceLength < content.Length &&
                   content[fenceLength] == fenceCharacter)
            {
                fenceLength++;
            }

            if (fenceLength < 3)
            {
                if (openFenceCharacter is null)
                {
                    UpdateOpenHtmlElements(line, openHtmlElements);
                }

                continue;
            }

            if (openFenceCharacter is null)
            {
                openFenceCharacter = fenceCharacter;
                openFenceLength = fenceLength;
                continue;
            }

            if (openFenceCharacter == fenceCharacter &&
                fenceLength >= openFenceLength &&
                string.IsNullOrWhiteSpace(content[fenceLength..]))
            {
                openFenceCharacter = null;
                openFenceLength = 0;
            }
        }

        var htmlCommentOpen = prefix.LastIndexOf("<!--", StringComparison.Ordinal);
        var htmlCommentClose = prefix.LastIndexOf("-->", StringComparison.Ordinal);
        var processingInstructionOpen = prefix.LastIndexOf("<?", StringComparison.Ordinal);
        var processingInstructionClose = prefix.LastIndexOf("?>", StringComparison.Ordinal);
        var cdataOpen = prefix.LastIndexOf("<![CDATA[", StringComparison.Ordinal);
        var cdataClose = prefix.LastIndexOf("]]>", StringComparison.Ordinal);
        var declarationMatches = Regex.Matches(
            prefix,
            @"<![A-Z]",
            RegexOptions.CultureInvariant);
        var declarationOpen = declarationMatches.Count == 0
            ? -1
            : declarationMatches[declarationMatches.Count - 1].Index;
        var declarationClose = prefix.LastIndexOf('>');

        return openFenceCharacter is not null ||
               htmlCommentOpen > htmlCommentClose ||
               processingInstructionOpen > processingInstructionClose ||
               cdataOpen > cdataClose ||
               declarationOpen > declarationClose ||
               openHtmlElements.Count > 0;
    }

    private static string PreserveLineBreaks(Match match)
    {
        return new string(match.Value
            .Select(character => character is '\r' or '\n' ? character : ' ')
            .ToArray());
    }

    private static void UpdateOpenHtmlElements(
        string line,
        List<string> openHtmlElements)
    {
        var withoutInlineCode = Regex.Replace(
            line,
            @"`+[^`\r\n]*`+",
            string.Empty,
            RegexOptions.CultureInvariant);
        var tags = Regex.Matches(
            withoutInlineCode,
            @"<(?<closing>/)?(?<name>[A-Za-z][A-Za-z0-9:-]*)(?:\s[^<>]*?)?(?<self>/)?>",
            RegexOptions.CultureInvariant);
        string[] voidElements =
        [
            "area",
            "base",
            "br",
            "col",
            "embed",
            "hr",
            "img",
            "input",
            "link",
            "meta",
            "param",
            "source",
            "track",
            "wbr",
        ];
        foreach (Match tag in tags)
        {
            var name = tag.Groups["name"].Value.ToLowerInvariant();
            if (tag.Groups["closing"].Success)
            {
                for (var index = openHtmlElements.Count - 1; index >= 0; index--)
                {
                    if (!string.Equals(
                            openHtmlElements[index],
                            name,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    openHtmlElements.RemoveRange(
                        index,
                        openHtmlElements.Count - index);
                    break;
                }

                continue;
            }

            if (!tag.Groups["self"].Success &&
                !voidElements.Contains(name, StringComparer.Ordinal))
            {
                openHtmlElements.Add(name);
            }
        }
    }

    private static bool IsStandaloneLineAtColumnZero(
        string text,
        int markerIndex,
        string marker)
    {
        if (markerIndex < 0 || markerIndex + marker.Length > text.Length)
        {
            return false;
        }

        var lineStart = markerIndex == 0
            ? 0
            : text.LastIndexOf('\n', markerIndex - 1) + 1;
        var lineEnd = text.IndexOf('\n', markerIndex);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var line = text[lineStart..lineEnd].TrimEnd('\r');
        return markerIndex == lineStart &&
               string.Equals(line, marker, StringComparison.Ordinal);
    }

    private static string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
            "不是",
            "并非",
            "不作为",
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
            string[] polarityResetMarkers =
            [
                "但是",
                "然而",
                "并且",
                "并允许",
                "同时",
                "随后",
                "然后",
                "而且",
                "且",
                "但",
                "却",
            ];
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
