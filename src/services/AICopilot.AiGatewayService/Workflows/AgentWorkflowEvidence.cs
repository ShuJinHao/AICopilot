using System.Text;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.Core.AiGateway.Runtime.AgentExecution;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Workflows;

public enum AgentWorkflowEvidenceKind
{
    ToolCatalog = 0,
    DataQuery = 1,
    RagCitation = 2,
    PolicyDecision = 3,
    DerivedMetric = 4
}

public enum AgentWorkflowEvidenceTruthClass
{
    ObservedFact = 0,
    DerivedFact = 1,
    PolicyDecision = 2
}

public enum AgentNodeExecutionEventType
{
    Started = 0,
    Skipped = 1,
    Empty = 2,
    Succeeded = 3,
    Failed = 4
}

public sealed record AgentWorkflowEvidence(
    string SchemaVersion,
    string NodeId,
    string NodeKind,
    BranchType BranchType,
    AgentWorkflowEvidenceKind EvidenceKind,
    AgentWorkflowEvidenceTruthClass TruthClass,
    string ProducerId,
    string Provider,
    string SourceMode,
    bool? IsSimulation,
    string? SemanticIntent,
    IReadOnlyCollection<string> SanitizedScope,
    string PayloadCanonicalJson,
    string PayloadDigest,
    string? SafeContext,
    IReadOnlyCollection<string> AllowedConsumerScopes,
    string Digest);

public sealed record AgentNodeExecutionEvent(
    string SchemaVersion,
    AgentNodeExecutionEventType EventType,
    string Lifecycle,
    string NodeId,
    string NodeKind,
    string Branch,
    bool Required,
    string? EvidenceSetDigest,
    string? FailureCode,
    DateTimeOffset OccurredAtUtc)
{
    public const string CurrentSchemaVersion = "agent-node-event:v1";

    public string Stage => $"node_{EventType.ToString().ToLowerInvariant()}";

    public string? Code => FailureCode;

    public string Detail => EventType switch
    {
        AgentNodeExecutionEventType.Started => $"{Branch} 节点已开始。",
        AgentNodeExecutionEventType.Skipped => $"{Branch} 节点不适用于本轮请求，已跳过。",
        AgentNodeExecutionEventType.Empty => $"{Branch} 节点已完成，但没有返回可用证据。",
        AgentNodeExecutionEventType.Succeeded => $"{Branch} 节点已完成并产出受控证据。",
        AgentNodeExecutionEventType.Failed => $"{Branch} 节点未能完成。",
        _ => $"{Branch} 节点状态已更新。"
    };

    public bool Recoverable => EventType != AgentNodeExecutionEventType.Failed;

    public string? SuggestedAction => EventType == AgentNodeExecutionEventType.Failed
        ? "请检查安全错误摘要或稍后重试。"
        : null;

    public IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["schemaVersion"] = SchemaVersion,
        ["lifecycle"] = Lifecycle,
        ["nodeId"] = NodeId,
        ["nodeKind"] = NodeKind,
        ["branch"] = Branch,
        ["required"] = Required.ToString().ToLowerInvariant(),
        ["evidenceSetDigest"] = EvidenceSetDigest ?? string.Empty
    };

    public string ToJson() => CanonicalJson.Serialize(this);
}

internal sealed record AgentBranchEvidenceSeed(
    string NodeKind,
    AgentWorkflowEvidenceKind EvidenceKind,
    AgentWorkflowEvidenceTruthClass TruthClass,
    string ProducerId,
    string Provider,
    string SourceMode,
    bool? IsSimulation,
    string? SemanticIntent,
    IReadOnlyCollection<string> SanitizedScope,
    string SafeContext);

internal sealed record AgentAnalysisNodeResult(
    BranchExecutionStatus Status,
    AgentBranchEvidenceSeed? Evidence,
    string? FailureCode = null,
    string? SafeMessage = null)
{
    public static AgentAnalysisNodeResult Succeeded(AgentBranchEvidenceSeed evidence) =>
        new(BranchExecutionStatus.Succeeded, evidence);

    public static AgentAnalysisNodeResult Empty(AgentBranchEvidenceSeed evidence) =>
        new(BranchExecutionStatus.Empty, evidence);

    public static AgentAnalysisNodeResult Failed(string code, string safeMessage) =>
        new(BranchExecutionStatus.Failed, null, code, safeMessage);
}

internal static class AgentWorkflowEvidenceNormalizer
{
    public const string SchemaVersion = "chat-evidence-adapter:v1";

    public static Result<BranchResult> Normalize(
        BranchResult result,
        Guid sessionId)
    {
        if (result.Status != BranchExecutionStatus.Succeeded)
        {
            return Result.Success(result with { Evidence = [] });
        }

        var seeds = result.EvidenceSeeds.Count == 0
            ? CreateDefaultSeeds(result)
            : result.EvidenceSeeds;
        if (seeds.Count == 0)
        {
            return Result.Failure(new ApiProblemDescriptor(
                AppProblemCodes.ChatStreamFailed,
                $"Succeeded workflow branch '{result.Type}' produced no typed Evidence seed."));
        }

        var evidence = new List<AgentWorkflowEvidence>(seeds.Count);
        for (var index = 0; index < seeds.Count; index++)
        {
            var seed = seeds.ElementAt(index);
            var nodeId = $"chat-{result.Type.ToString().ToLowerInvariant()}-{index + 1:00}";
            var safeContext = string.IsNullOrWhiteSpace(seed.SafeContext)
                ? null
                : seed.SafeContext.Trim();
            var payload = CanonicalJson.Serialize(new
            {
                nodeId,
                nodeKind = seed.NodeKind,
                evidenceKind = seed.EvidenceKind.ToString(),
                truthClass = seed.TruthClass.ToString(),
                seed.Provider,
                seed.SourceMode,
                seed.IsSimulation,
                seed.SemanticIntent,
                sanitizedScope = seed.SanitizedScope
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                safeContext
            });
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);
            if (payloadBytes > AgentEvidenceRecord.MaxInlinePayloadUtf8Bytes)
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.EvidencePayloadTooLarge,
                    $"Chat Evidence payload for '{nodeId}' is {payloadBytes} UTF-8 bytes; maximum is {AgentEvidenceRecord.MaxInlinePayloadUtf8Bytes}."));
            }

            var payloadDigest = CanonicalJson.ComputeSha256(payload);
            var scopes = AgentEvidenceAccessChecker.BuildChatScopes(sessionId);
            var digest = CanonicalJson.ComputeSha256(CanonicalJson.Serialize(new
            {
                schemaVersion = SchemaVersion,
                nodeId,
                nodeKind = seed.NodeKind,
                branch = result.Type.ToString(),
                evidenceKind = seed.EvidenceKind.ToString(),
                truthClass = seed.TruthClass.ToString(),
                seed.ProducerId,
                seed.Provider,
                seed.SourceMode,
                seed.IsSimulation,
                seed.SemanticIntent,
                payloadDigest,
                scopes
            }));
            evidence.Add(new AgentWorkflowEvidence(
                SchemaVersion,
                nodeId,
                seed.NodeKind,
                result.Type,
                seed.EvidenceKind,
                seed.TruthClass,
                seed.ProducerId,
                seed.Provider,
                seed.SourceMode,
                seed.IsSimulation,
                seed.SemanticIntent,
                seed.SanitizedScope
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                payload,
                payloadDigest,
                safeContext,
                scopes,
                digest));
        }

        return Result.Success(result with { Evidence = evidence });
    }

    public static string ComputeEvidenceSetDigest(IEnumerable<AgentWorkflowEvidence> evidence)
    {
        var digests = evidence
            .Select(item => item.Digest)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return CanonicalJson.ComputeSha256(CanonicalJson.Serialize(digests));
    }

    private static IReadOnlyCollection<AgentBranchEvidenceSeed> CreateDefaultSeeds(BranchResult result)
    {
        return result.Type switch
        {
            BranchType.Tools when result.Tools is { Length: > 0 } =>
            [
                new AgentBranchEvidenceSeed(
                    "PolicyValidationNode",
                    AgentWorkflowEvidenceKind.ToolCatalog,
                    AgentWorkflowEvidenceTruthClass.PolicyDecision,
                    "tool-safety-policy:v1",
                    "ToolRegistry",
                    "FilteredReadOnlyCapabilityView",
                    IsSimulation: null,
                    SemanticIntent: null,
                    result.Tools.Select(tool => $"tool:{tool.Name}")
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    string.Empty)
            ],
            BranchType.Knowledge when !string.IsNullOrWhiteSpace(result.Knowledge) =>
            [
                new AgentBranchEvidenceSeed(
                    "KnowledgeRetrievalNode",
                    AgentWorkflowEvidenceKind.RagCitation,
                    AgentWorkflowEvidenceTruthClass.ObservedFact,
                    "knowledge-retrieval:v1",
                    "KnowledgeBase",
                    "AuthorizedRagRead",
                    IsSimulation: null,
                    SemanticIntent: "Knowledge.Retrieve",
                    [],
                    result.Knowledge)
            ],
            BranchType.DataAnalysis when !string.IsNullOrWhiteSpace(result.DataAnalysis) =>
            [
                new AgentBranchEvidenceSeed(
                    "DeterministicComputeNode",
                    AgentWorkflowEvidenceKind.DerivedMetric,
                    AgentWorkflowEvidenceTruthClass.DerivedFact,
                    "data-analysis-compat-adapter:v1",
                    "DataAnalysis",
                    "CompatibilityAdapter",
                    IsSimulation: null,
                    SemanticIntent: null,
                    [],
                    result.DataAnalysis)
            ],
            BranchType.BusinessPolicy when !string.IsNullOrWhiteSpace(result.BusinessPolicy) =>
            [
                new AgentBranchEvidenceSeed(
                    "PolicyValidationNode",
                    AgentWorkflowEvidenceKind.PolicyDecision,
                    AgentWorkflowEvidenceTruthClass.PolicyDecision,
                    "business-policy:v1",
                    "BusinessPolicy",
                    "ServerOwnedPolicyCatalog",
                    IsSimulation: false,
                    SemanticIntent: null,
                    [],
                    result.BusinessPolicy)
            ],
            _ => []
        };
    }
}
