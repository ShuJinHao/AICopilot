using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Result;

namespace AICopilot.UnitTests;

public sealed class AgentPlanContractV2Tests
{
    private static readonly Guid PlanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Seal_ShouldProduceCanonicalRoundTripAndStableDigest()
    {
        var canonicalizer = new AgentPlanCanonicalizer();

        var first = canonicalizer.Seal(CreatePlan());
        var second = canonicalizer.Seal(CreatePlan());

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value!.CanonicalJson.Should().Be(second.Value!.CanonicalJson);
        first.Value.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        first.Value.Document.PlanDigest.Should().Be(first.Value.Digest);
        canonicalizer.ValidatePersisted(first.Value.CanonicalJson).IsSuccess.Should().BeTrue();
        CanonicalJson.Canonicalize(first.Value.CanonicalJson).Should().Be(first.Value.CanonicalJson);
    }

    [Fact]
    public void ValidatePersisted_ShouldRejectDigestAffectingMutation()
    {
        var canonicalizer = new AgentPlanCanonicalizer();
        var sealedPlan = canonicalizer.Seal(CreatePlan()).Value!;
        var changed = sealedPlan.Document with { Goal = "different safe summary" };
        var changedJson = CanonicalJson.Serialize(changed);

        var result = canonicalizer.ValidatePersisted(changedJson);

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("digest");
    }

    [Fact]
    public void ValidatePersisted_ShouldAllowLifecycleSealWithoutChangingDigest()
    {
        var canonicalizer = new AgentPlanCanonicalizer();
        var draft = canonicalizer.Seal(CreatePlan()).Value!;
        var executable = draft.Document with
        {
            PlanKind = AgentTaskPlanKinds.ExecutablePlan,
            IsExecutable = true
        };
        var executableJson = CanonicalJson.Serialize(executable);

        var result = canonicalizer.ValidatePersisted(executableJson, requireExecutable: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.PlanDigest.Should().Be(draft.Digest);
    }

    [Theory]
    [InlineData("DagV1", "DagV1")]
    [InlineData("Implicit", "DagV1")]
    public void Seal_ShouldRejectNonLinearTopology(string topology, string expectedDetail)
    {
        var result = new AgentPlanCanonicalizer().Seal(CreatePlan() with { TopologyProfile = topology });

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain(expectedDetail);
    }

    [Theory]
    [InlineData("CloudWriteNode")]
    [InlineData("PlcControlNode")]
    public void Seal_ShouldRejectCloudWriteAndPlcNodes(string nodeKind)
    {
        var plan = CreatePlan();
        var node = plan.Nodes!.Single() with { NodeKind = nodeKind };

        var result = new AgentPlanCanonicalizer().Seal(plan with { Nodes = [node] });

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
    }

    [Fact]
    public void Seal_ShouldRejectExplicitEmptyCapabilityAllowlistWithNodes()
    {
        var plan = CreatePlan() with
        {
            CapabilitySelectionMode = AgentCapabilitySelectionMode.ExplicitAllowlist,
            RequestedCapabilityCodes = []
        };

        var result = new AgentPlanCanonicalizer().Seal(plan);

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("explicit empty capability allowlist");
    }

    [Fact]
    public void Seal_ShouldRejectBuiltInModeWithPluginIds()
    {
        var result = new AgentPlanCanonicalizer().Seal(CreatePlan() with
        {
            PluginSelectionMode = AgentPluginSelectionMode.BuiltInOnly,
            SelectedPluginIds = [Guid.Parse("33333333-3333-3333-3333-333333333333")]
        });

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("BuiltInOnly");
    }

    [Fact]
    public void Seal_ShouldEnforceCanonicalNodeInputUtf8BoundaryWithoutTruncation()
    {
        var canonicalizer = new AgentPlanCanonicalizer();
        var emptyInputBytes = Encoding.UTF8.GetByteCount("{\"value\":\"\"}");
        var boundaryInput = $"{{\"value\":\"{new string('x', AgentPlanContractVersions.MaxNodeInputCanonicalBytes - emptyInputBytes)}\"}}";
        var plan = WithNodeInput(CreatePlan(), boundaryInput);

        var boundary = canonicalizer.Seal(plan);
        var over = canonicalizer.Seal(WithNodeInput(CreatePlan(), boundaryInput.Replace("\"}", "x\"}", StringComparison.Ordinal)));

        boundary.IsSuccess.Should().BeTrue();
        Encoding.UTF8.GetByteCount(boundary.Value!.Document.Nodes!.Single().Input!.CanonicalInputJson!).Should()
            .Be(AgentPlanContractVersions.MaxNodeInputCanonicalBytes);
        ProblemCode(over).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(over).Should().Contain("8000");
    }

    [Fact]
    public void Seal_ShouldAcceptExactPlanBoundaryAndRejectOneByteOver()
    {
        var canonicalizer = new AgentPlanCanonicalizer();
        var baseline = canonicalizer.Seal(CreatePlan() with { PlannerFallbackReason = string.Empty }).Value!;
        var padding = new string('x', AgentPlanContractVersions.MaxPlanCanonicalBytes - baseline.CanonicalByteCount);

        var exact = canonicalizer.Seal(CreatePlan() with { PlannerFallbackReason = padding });
        var over = canonicalizer.Seal(CreatePlan() with { PlannerFallbackReason = padding + "x" });

        exact.IsSuccess.Should().BeTrue();
        exact.Value!.CanonicalByteCount.Should().Be(AgentPlanContractVersions.MaxPlanCanonicalBytes);
        exact.Value.CanonicalJson.Should().Contain(padding);
        ProblemCode(over).Should().Be(AppProblemCodes.PlanPayloadTooLarge);
    }

    [Fact]
    public void EvidenceV1_ShouldSealInlineCanonicalPayloadAndRejectOversize()
    {
        const string payload = "{\"count\":2,\"status\":\"ok\"}";
        var evidence = CreateEvidence(payload);

        var sealedEvidence = AgentEvidenceCanonicalizer.Seal(evidence);
        var oversizePayload = $"{{\"value\":\"{new string('x', AgentPlanContractVersions.MaxInlineEvidenceCanonicalBytes)}\"}}";
        var oversize = AgentEvidenceCanonicalizer.Seal(CreateEvidence(oversizePayload));

        sealedEvidence.IsSuccess.Should().BeTrue();
        sealedEvidence.Value!.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        sealedEvidence.Value.CanonicalJson.Should().Contain(AgentPlanContractVersions.EvidenceV1);
        ProblemCode(oversize).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(oversize).Should().Contain("65536");
    }

    [Fact]
    public void IntentAdapter_ShouldDropRawQueryAndReasoningAndKeepOnlyTypedCanonicalFields()
    {
        var adapter = new IntentResultToCandidateAdapter();
        var context = CreateAdapterContext() with
        {
            AuthorizedDeviceIdsByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DEV-001"] = DeviceId.ToString("D")
            }
        };
        var result = adapter.Adapt(
            [
                new IntentResult
                {
                    Intent = "Analysis.DeviceLog.Latest",
                    Confidence = 0.91,
                    Query = """{"queryText":"SECRET RAW PROMPT","filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}]}""",
                    Reasoning = "SECRET MODEL REASONING",
                    Reason = "SECRET ROUTER REASON"
                }
            ],
            context);

        result.IsSuccess.Should().BeTrue();
        var candidate = result.Value!.Single();
        candidate.IntentClass.Should().Be(AgentIntentClass.CloudOnly);
        candidate.ProviderCode.Should().Be("CloudAiRead");
        candidate.RequestedResources.Devices.Single().ResourceId.Should().Be(DeviceId.ToString("D"));
        var json = CanonicalJson.Serialize(candidate);
        json.Should().NotContain("SECRET RAW PROMPT");
        json.Should().NotContain("SECRET MODEL REASONING");
        json.Should().NotContain("SECRET ROUTER REASON");
        json.Should().NotContain("queryText");
    }

    [Fact]
    public void IntentAdapter_ShouldMergeDuplicatesDeterministically()
    {
        var adapter = new IntentResultToCandidateAdapter();
        var results = new[]
        {
            new IntentResult { Intent = "General.Chat", Confidence = 0.51, Reasoning = "first" },
            new IntentResult { Intent = "General.Chat", Confidence = 0.88, Query = "second" }
        };

        var first = adapter.Adapt(results, CreateAdapterContext());
        var second = adapter.Adapt(results.Reverse(), CreateAdapterContext());

        first.IsSuccess.Should().BeTrue();
        first.Value.Should().ContainSingle();
        first.Value!.Single().Confidence.Should().Be(0.88);
        CanonicalJson.Serialize(first.Value).Should().Be(CanonicalJson.Serialize(second.Value));
    }

    [Theory]
    [InlineData("Prediction.Device.FailureRisk", "known_capability_unavailable")]
    [InlineData("Prediction.Device.RemainingUsefulLife", "known_capability_unavailable")]
    [InlineData("Analysis.Unregistered.Source", "unknown_intent")]
    public void IntentAdapter_ShouldKeepUnavailableAndUnknownAsNonExecutableGaps(
        string intent,
        string expectedGap)
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = intent, Confidence = 0.9 }],
            CreateAdapterContext());

        result.IsSuccess.Should().BeTrue();
        var candidate = result.Value!.Single();
        candidate.Availability.Should().NotBe(AgentIntentAvailability.Available);
        candidate.CapabilityGap!.Code.Should().Be(expectedGap);
    }

    [Fact]
    public void IntentAdapter_ShouldRequireStableResourceResolution()
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [
                new IntentResult
                {
                    Intent = "Analysis.Device.Status",
                    Confidence = 0.9,
                    Query = """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-404"}]}"""
                }
            ],
            CreateAdapterContext());

        result.IsSuccess.Should().BeTrue();
        var candidate = result.Value!.Single();
        candidate.CapabilityGap!.Code.Should().Be("resource_resolution_required");
        candidate.Availability.Should().Be(AgentIntentAvailability.Unknown);
    }

    [Theory]
    [InlineData("Action.PlcWrite", null)]
    [InlineData("General.Chat", "please update cloud recipe")]
    public void IntentAdapter_ShouldRejectPlcAndCloudWriteSemantics(string intent, string? query)
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = intent, Confidence = 0.9, Query = query }],
            CreateAdapterContext());

        ProblemCode(result).Should().Be(AppProblemCodes.ControlActionBlocked);
    }

    [Fact]
    public void IntentCatalog_ShouldHaveFrozenDigest()
    {
        AgentIntentCatalogV1.CatalogVersion.Should().Be("intent-catalog:v1");
        AgentIntentCatalogV1.CatalogDigest.Should()
            .Be("6e8d2cc91b28e9952e202d6d782d5ceb582f36e0266591afe7bda0003aaa67c4");
    }

    private static AgentTaskPlanDocument CreatePlan()
    {
        var candidate = new AgentIntentCandidateDocument(
            AgentPlanContractVersions.IntentV1,
            "General.Chat",
            AgentIntentClass.General,
            AgentIntentAvailability.Available,
            "BuiltIn",
            1,
            new AgentIntentRequiredDocument(true, AgentIntentRequiredSource.ExplicitUserGoal, null),
            new AgentIntentRequestedResourcesDocument([], [], [], []),
            new AgentIntentFiltersDocument(null, []),
            [],
            new AgentIntentProvenanceDocument(
                "intent-router:v1",
                "intent-prompt:v1",
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest),
            null);
        var node = new AgentPlanNodeDocument(
            AgentPlanContractVersions.NodeV1,
            "node-001",
            "DeterministicTransformNode",
            [],
            true,
            "node-input:v1",
            "evidence:derived-metric:v1",
            [],
            ["General.Chat"],
            [],
            [],
            [],
            new AgentPlanNodeInputDocument(
                null,
                null,
                null,
                [],
                null,
                null,
                null,
                [],
                null,
                null,
                null),
            null,
            new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", 120),
            new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None"),
            new AgentPlanNodeBudgetDocument(0, 0, 0),
            new AgentPlanApprovalPolicyDocument(false, "None"),
            new AgentPlanIdempotencyPolicyDocument("idempotency-policy:v1", "Deterministic"),
            "ReadOnly",
            null);

        return new AgentTaskPlanDocument(
            Version: 2,
            PlannerTemplateCode: "agent_planner",
            Goal: "safe task summary",
            TaskType: AgentTaskType.ReportGeneration.ToString(),
            RiskLevel: AgentTaskRiskLevel.Low.ToString(),
            UploadIds: [],
            KnowledgeBaseIds: [],
            CloudReadonlyIntent: null,
            Steps:
            [
                new AgentTaskPlanStepDocument(
                    "Analyze",
                    "Deterministic analysis",
                    AgentStepType.Analysis,
                    null,
                    false)
            ],
            RuntimeSettings: new AgentTaskPlanRuntimeSettingsDocument(0, 0),
            PlannerMode: "PlanDraft",
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            CapabilityGaps: [],
            SchemaVersion: AgentPlanContractVersions.PlanV2,
            PlanId: PlanId,
            PlanVersion: 1,
            PlanDigest: null,
            TopologyProfile: "LinearV1",
            IntentCandidates: [candidate],
            CapabilitySelectionMode: AgentCapabilitySelectionMode.InferredFromGoal,
            RequestedCapabilityCodes: ["General.Chat"],
            PluginSelectionMode: AgentPluginSelectionMode.BuiltInOnly,
            SelectedPluginIds: [],
            ArtifactTargets: [],
            Nodes: [node],
            JoinPolicies: [],
            Budgets: new AgentPlanBudgetDocument(
                "budget-policy:v1",
                16,
                1800,
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            ApprovalSummary: new AgentPlanApprovalSummaryDocument(true, []),
            ExecutionSnapshot: new AgentExecutionSnapshotDocument(
                AgentPlanContractVersions.ExecutionSnapshotV1,
                AgentPlanContractVersions.PlanPolicyV1,
                PlannerToolCatalog.CurrentVersion,
                Hash("tools"),
                Hash("providers"),
                AgentIntentCatalogV1.CatalogVersion,
                AgentIntentCatalogV1.CatalogDigest,
                "agent_planner",
                "agent-planner:v1",
                Hash("prompt"),
                null,
                null,
                null,
                Hash("plugins"),
                Hash("mcp"),
                "data-contract:v1",
                "knowledge-contract:v1",
                "agent-policy:v1",
                "agent-guard:v1",
                "budget-policy:v1",
                AgentPlanContractVersions.MaxPlanCanonicalBytes),
            SecuritySummary: new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false));
    }

    private static AgentTaskPlanDocument WithNodeInput(AgentTaskPlanDocument plan, string inputJson)
    {
        var node = plan.Nodes!.Single();
        return plan with
        {
            Nodes = [node with { Input = node.Input! with { CanonicalInputJson = inputJson } }],
            Steps = [plan.Steps.Single() with { InputJson = inputJson }]
        };
    }

    private static AgentEvidenceEnvelopeDocument CreateEvidence(string canonicalPayload)
    {
        var byteCount = Encoding.UTF8.GetByteCount(canonicalPayload);
        return new AgentEvidenceEnvelopeDocument(
            AgentPlanContractVersions.EvidenceV1,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            null,
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            null,
            "node-001",
            "DerivedMetric",
            "DerivedFact",
            new AgentEvidenceProducerDocument("DeterministicTransformNode", "test", null, null, null, null, null),
            new AgentEvidenceSourceDocument("Agent", "opaque:test", "Inline", false, null, null, null, []),
            new AgentEvidenceQualityDocument(1, false, "Current", 0, 1, []),
            new AgentEvidencePayloadDocument(
                "InlineCanonicalJson",
                null,
                "application/json",
                byteCount,
                Hash(canonicalPayload),
                true,
                canonicalPayload),
            new AgentEvidenceContentDocument("safe", new Dictionary<string, decimal>(), [], [], []),
            [],
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            string.Empty);
    }

    private static AgentIntentAdapterContext CreateAdapterContext()
    {
        return new AgentIntentAdapterContext(
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string Hash(string value) => CanonicalJson.ComputeSha256(value);

    private static string ProblemCode(IResult result)
    {
        return result.Errors!.OfType<ApiProblemDescriptor>().Single().Code;
    }

    private static string ProblemDetail(IResult result)
    {
        return result.Errors!.OfType<ApiProblemDescriptor>().Single().Detail;
    }
}
