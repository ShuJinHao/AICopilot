using System.Text;
using System.Text.Json;
using AICopilot.AgentWorkflowTestKit;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Tools;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
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

        first.IsSuccess.Should().BeTrue(FailureSummary(first));
        second.IsSuccess.Should().BeTrue(FailureSummary(second));
        first.Value!.CanonicalJson.Should().Be(second.Value!.CanonicalJson);
        first.Value.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        first.Value.Document.PlanDigest.Should().Be(first.Value.Digest);
        canonicalizer.ValidatePersisted(first.Value.CanonicalJson).IsSuccess.Should().BeTrue();
        CanonicalJson.Canonicalize(first.Value.CanonicalJson).Should().Be(first.Value.CanonicalJson);

        var exactBoundaryPlan = CreatePlanWithCanonicalSize(AgentPlanContractVersions.MaxPlanCanonicalBytes);
        var exactPlaceholderJson = JsonSerializer.Serialize(
            exactBoundaryPlan with { PlanDigest = new string('0', 64) },
            CanonicalJson.SerializerOptions);
        var exactPlaceholderCount = CanonicalJson.MeasureCanonicalUtf8Bytes(
            exactPlaceholderJson,
            AgentPlanContractVersions.MaxPlanCanonicalBytes);
        var exactBoundary = canonicalizer.Seal(exactBoundaryPlan);

        exactPlaceholderCount.Should().Be(AgentPlanContractVersions.MaxPlanCanonicalBytes);
        exactBoundary.IsSuccess.Should().BeTrue(FailureSummary(exactBoundary));
        exactBoundary.Value!.CanonicalByteCount.Should().Be(AgentPlanContractVersions.MaxPlanCanonicalBytes);
        Encoding.UTF8.GetByteCount(exactBoundary.Value.CanonicalJson).Should().Be(exactPlaceholderCount);

        var digestCrossingPlan = GrowPlanByOneCanonicalByte(exactBoundaryPlan);
        var withoutDigestJson = JsonSerializer.Serialize(
            digestCrossingPlan with { PlanDigest = null },
            CanonicalJson.SerializerOptions);
        var overPlaceholderJson = JsonSerializer.Serialize(
            digestCrossingPlan with { PlanDigest = new string('0', 64) },
            CanonicalJson.SerializerOptions);
        var withoutDigestCount = CanonicalJson.MeasureCanonicalUtf8Bytes(
            withoutDigestJson,
            AgentPlanContractVersions.MaxPlanCanonicalBytes);
        var overPlaceholderCount = CanonicalJson.MeasureCanonicalUtf8Bytes(
            overPlaceholderJson,
            AgentPlanContractVersions.MaxPlanCanonicalBytes);
        Action sharedCanonicalPreflight = () => CanonicalJson.Canonicalize(overPlaceholderJson);
        var overBoundary = canonicalizer.Seal(digestCrossingPlan);

        withoutDigestCount.Should().BeLessThanOrEqualTo(AgentPlanContractVersions.MaxPlanCanonicalBytes);
        overPlaceholderCount.Should().Be(AgentPlanContractVersions.MaxPlanCanonicalBytes + 1);
        sharedCanonicalPreflight.Should().Throw<JsonException>();
        ProblemCode(overBoundary).Should().Be(AppProblemCodes.PlanPayloadTooLarge);
        ProblemDetail(overBoundary).Should()
            .Be($"Plan v2 canonical payload exceeds the maximum of {AgentPlanContractVersions.MaxPlanCanonicalBytes} UTF-8 bytes.");
        ProblemDetail(overBoundary).Should().NotContain(
            (AgentPlanContractVersions.MaxPlanCanonicalBytes + 1).ToString(),
            "the bounded max+1 sentinel is not the actual canonical payload length");
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("100")]
    [InlineData("100.0")]
    public void CanonicalNumber_ShouldNormalizeEquivalentIntegersToPlainDecimal(string number)
    {
        CanonicalJson.Canonicalize($"{{\"value\":{number}}}").Should().Be("{\"value\":100}");
    }

    [Theory]
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("0e100000")]
    public void CanonicalNumber_ShouldNormalizeEveryZeroToUnsignedZero(string number)
    {
        CanonicalJson.Canonicalize($"{{\"value\":{number}}}").Should().Be("{\"value\":0}");
    }

    [Fact]
    public void CanonicalNumber_ShouldPreserveStrictPlanIntegerDeserializationAfterExponentInput()
    {
        var canonical = new AgentPlanCanonicalizer().Seal(CreatePlan()).Value!.CanonicalJson;
        var exponentForm = canonical.Replace("\"planVersion\":1", "\"planVersion\":1e0", StringComparison.Ordinal);

        var renormalized = CanonicalJson.Canonicalize(exponentForm);
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(renormalized, CanonicalJson.SerializerOptions);

        renormalized.Should().Contain("\"planVersion\":1");
        renormalized.Should().NotContain("\"planVersion\":1e0");
        plan!.PlanVersion.Should().Be(1);
    }

    [Fact]
    public void CanonicalNumber_ShouldEnforceSignificantLexicalAndExponentDomainBeforeExpansion()
    {
        var significant256 = new string('9', AgentCanonicalNumberPolicyV1.MaxSignificantDigits);
        var significant257 = significant256 + "9";
        var lexical384 = "0." + new string('0', 126) + significant256;
        var lexical385 = "0." + new string('0', 127) + significant256;

        CanonicalJson.Canonicalize($"{{\"value\":{significant256}}}").Should().Contain(significant256);
        CanonicalJson.Canonicalize($"{{\"value\":{lexical384}}}").Should().Contain("e-");
        CanonicalJson.Canonicalize("{\"value\":1e100000}").Should().Be("{\"value\":1e100000}");

        Action significantOverflow = () => CanonicalJson.Canonicalize($"{{\"value\":{significant257}}}");
        Action lexicalOverflow = () => CanonicalJson.Canonicalize($"{{\"value\":{lexical385}}}");
        Action exponentOverflow = () => CanonicalJson.Canonicalize("{\"value\":1e100001}");
        Action exponentDigitOverflow = () => CanonicalJson.Canonicalize("{\"value\":1e0000001}");

        significantOverflow.Should().Throw<JsonException>();
        lexicalOverflow.Should().Throw<JsonException>();
        exponentOverflow.Should().Throw<JsonException>();
        exponentDigitOverflow.Should().Throw<JsonException>();
    }

    [Fact]
    public void CanonicalJson_ShouldRejectDuplicatePropertiesAndExcessiveDepth()
    {
        Action duplicate = () => CanonicalJson.Canonicalize("{\"value\":1,\"value\":2}");
        var tooDeep = string.Concat(Enumerable.Repeat("{\"v\":", AgentCanonicalJsonV1.MaxDepth + 1)) +
                      "0" +
                      new string('}', AgentCanonicalJsonV1.MaxDepth + 1);
        Action excessiveDepth = () => CanonicalJson.Canonicalize(tooDeep);

        duplicate.Should().Throw<JsonException>();
        excessiveDepth.Should().Throw<JsonException>();
    }

    [Fact]
    public void CanonicalJson_PreflightShouldRejectCollectionPropertyStringAndRawSizeAmplification()
    {
        var tooManyItems = "[" + string.Join(',', Enumerable.Repeat("0", AgentCanonicalJsonV1.MaxArrayItems + 1)) + "]";
        var tooManyProperties = "{" + string.Join(',', Enumerable.Range(0, AgentCanonicalJsonV1.MaxObjectProperties + 1)
            .Select(index => $"\"p{index}\":0")) + "}";
        var longProperty = $"{{\"{new string('p', AgentCanonicalJsonV1.MaxPropertyNameUtf8Bytes + 1)}\":0}}";
        var longString = $"\"{new string('x', AgentCanonicalJsonV1.MaxStringUtf8Bytes + 1)}\"";
        var whitespaceAmplified = "{}" + new string(' ', AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes - 1);
        using var oversizedElement = JsonDocument.Parse(tooManyItems);

        Action array = () => CanonicalJson.Canonicalize(tooManyItems);
        Action elementArray = () => CanonicalJson.Canonicalize(oversizedElement.RootElement);
        Action serializedArray = () => CanonicalJson.Serialize(
            Enumerable.Range(0, AgentCanonicalJsonV1.MaxArrayItems + 1).ToArray());
        Action properties = () => CanonicalJson.Canonicalize(tooManyProperties);
        Action propertyName = () => CanonicalJson.Canonicalize(longProperty);
        Action stringValue = () => CanonicalJson.Canonicalize(longString);
        Action caseCollision = () => CanonicalJson.Canonicalize("{\"field\":1,\"FIELD\":2}");
        Action unicodeCaseCollision = () => CanonicalJson.Canonicalize("{\"é\":1,\"É\":2}");
        Action measuredArray = () => CanonicalJson.MeasureCanonicalUtf8Bytes(
            tooManyItems,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        const string unorderedEscapedJson = "{ \"z\": \"<\", \"drop\": \"ignored\", \"n\": 1e2, \"a\": \"中\" }";
        var excludedRootProperties = new HashSet<string>(["drop"], StringComparer.Ordinal);
        var measuredCanonicalBytes = CanonicalJson.MeasureCanonicalUtf8Bytes(
            unorderedEscapedJson,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes,
            excludedRootProperties);
        var measuredCanonicalJson = CanonicalJson.Canonicalize(
            unorderedEscapedJson,
            excludedRootProperties);
        const string boundarySkeleton = "{\"a\":\"\",\"b\":\"\",\"c\":\"\",\"d\":\"\"}";
        var boundaryRemaining = AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes -
                                Encoding.UTF8.GetByteCount(boundarySkeleton);
        var boundaryStringLengths = new int[4];
        for (var index = 0; index < boundaryStringLengths.Length; index++)
        {
            boundaryStringLengths[index] = Math.Min(
                boundaryRemaining,
                AgentCanonicalJsonV1.MaxStringUtf8Bytes);
            boundaryRemaining -= boundaryStringLengths[index];
        }

        boundaryRemaining.Should().Be(0);
        var exactBoundaryJson = $$"""{"a":"{{new string('x', boundaryStringLengths[0])}}","b":"{{new string('x', boundaryStringLengths[1])}}","c":"{{new string('x', boundaryStringLengths[2])}}","d":"{{new string('x', boundaryStringLengths[3])}}"}""";
        var overBoundaryJson = exactBoundaryJson.Replace(
            "\"}",
            "x\"}",
            StringComparison.Ordinal);
        var farOverBoundaryJson = JsonSerializer.Serialize(
            Enumerable.Repeat(
                new string('x', AgentCanonicalJsonV1.MaxStringUtf8Bytes),
                5).ToArray());
        var exactBoundaryMeasure = CanonicalJson.MeasureCanonicalUtf8Bytes(
            exactBoundaryJson,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        var overBoundaryMeasure = CanonicalJson.MeasureCanonicalUtf8Bytes(
            overBoundaryJson,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        var farOverBoundaryMeasure = CanonicalJson.MeasureCanonicalUtf8Bytes(
            farOverBoundaryJson,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        var farOverBoundaryDiagnostics = AgentCanonicalJsonV1.MeasureCanonicalUtf8BytesForDiagnostics(
            farOverBoundaryJson,
            AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        Action overBoundaryCanonicalize = () => CanonicalJson.Canonicalize(overBoundaryJson);

        array.Should().Throw<JsonException>();
        elementArray.Should().Throw<JsonException>();
        serializedArray.Should().Throw<JsonException>();
        properties.Should().Throw<JsonException>();
        propertyName.Should().Throw<JsonException>();
        stringValue.Should().Throw<JsonException>();
        caseCollision.Should().Throw<JsonException>();
        unicodeCaseCollision.Should().Throw<JsonException>();
        measuredArray.Should().Throw<JsonException>();
        measuredCanonicalBytes.Should().Be(Encoding.UTF8.GetByteCount(measuredCanonicalJson));
        measuredCanonicalBytes.Should().NotBe(Encoding.UTF8.GetByteCount(unorderedEscapedJson));
        measuredCanonicalJson.Should().NotContain("drop");
        measuredCanonicalJson.Should().Contain("100");
        exactBoundaryMeasure.Should().Be(AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes);
        Encoding.UTF8.GetByteCount(CanonicalJson.Canonicalize(exactBoundaryJson))
            .Should().Be(exactBoundaryMeasure);
        overBoundaryMeasure.Should().Be(AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes + 1);
        farOverBoundaryMeasure.Should().Be(AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes + 1);
        farOverBoundaryDiagnostics.Utf8ByteCount
            .Should().Be(AgentCanonicalJsonV1.MaxCanonicalJsonUtf8Bytes + 1);
        farOverBoundaryDiagnostics.CanonicalWriteTraversalCompleted.Should().BeFalse();
        farOverBoundaryDiagnostics.CanonicalWriteTokenCount.Should().Be(5);
        farOverBoundaryDiagnostics.CanonicalTokenFlushAttemptCount
            .Should().Be(farOverBoundaryDiagnostics.CanonicalWriteTokenCount);
        farOverBoundaryDiagnostics.SuccessfulStreamWriteCount.Should().BeGreaterThan(1);
        overBoundaryCanonicalize.Should().Throw<JsonException>();
        AgentStructuredPayloadPolicyV1.NormalizeNodeToolInput(whitespaceAmplified).IsValid.Should().BeFalse();
        AgentStructuredPayloadPolicyV1.NormalizeNodeToolInput(whitespaceAmplified).Utf8ByteCount.Should().Be(8_001);
    }

    [Fact]
    public void IntentAdapter_ShouldRejectTypedQueryBeforeDomWhenRawUtf8ExceedsBoundary()
    {
        var query = "{}" + new string(' ', AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes - 1);

        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = "Analysis.DeviceLog.Latest", Confidence = 0.9, Query = query }],
            CreateAdapterContext());

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }

    [Fact]
    public void CloudAiReadSchema_ShouldBeSingleEightOperationFieldAuthorityForProviderAndAdapter()
    {
        var schemas = CloudAiReadSemanticSchemaRegistry.GetOperationSchemas()
            .ToDictionary(schema => schema.Operation);

        schemas.Should().HaveCount(8);
        schemas[CloudAiReadOperation.Process].Filters.Select(rule => rule.Field).Should()
            .BeEquivalentTo("processId", "keyword", "processCode", "processName");
        schemas[CloudAiReadOperation.ClientRelease].Filters.Select(rule => rule.Field).Should()
            .Contain("includeArchived");
        schemas[CloudAiReadOperation.CapacitySummary].Filters.Select(rule => rule.Field).Should()
            .BeEquivalentTo("deviceId", "plcName", "shiftDate");
        schemas[CloudAiReadOperation.CapacitySummary].Filters.Select(rule => rule.Field).Should()
            .NotContain("deviceCode");
        schemas[CloudAiReadOperation.ProductionRecord].Filters.Select(rule => rule.Field).Should()
            .BeEquivalentTo("typeKey", "processId", "deviceId", "preset", "barcode", "result", "fieldMode");
        schemas[CloudAiReadOperation.ProductionRecord].Filters.Select(rule => rule.Field).Should()
            .NotContain("deviceCode");
        var processDetailSchema = CloudAiReadSemanticSchemaRegistry.GetIntentSchemas()
            .Single(intent => intent.IntentCode == "Analysis.Process.Detail");
        processDetailSchema.RequiredAnyFilterFields.Should()
            .BeEquivalentTo("processId", "processCode", "processName");
        processDetailSchema.RequiredAnyFilterFields.Should().NotContain("keyword");

        foreach (var intent in CloudAiReadSemanticSchemaRegistry.GetIntentSchemas())
        {
            var expectedFields = schemas[intent.Operation].Filters
                .Select(rule => rule.Field)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var field in schemas.Values.SelectMany(schema => schema.Filters).Select(rule => rule.Field).Distinct())
            {
                IntentResultToCandidateAdapter.IsAllowedPredicateField(intent.IntentCode, field).Should()
                    .Be(expectedFields.Contains(field));
            }
        }

        var keywordOnlyDetail = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult
            {
                Intent = "Analysis.Process.Detail",
                Confidence = 1,
                Query = """{"filters":[{"field":"keyword","operator":"contains","value":"CUT"}],"limit":1}"""
            }],
            CreateAdapterContext());
        ProblemCode(keywordOnlyDetail).Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }

    [Theory]
    [InlineData("INFORMATION")]
    [InlineData("WARNING")]
    [InlineData("DEBUG")]
    [InlineData("TRACE")]
    public void IntentAdapter_ShouldRejectNonCanonicalOrUnsupportedLogLevels(string level)
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult
            {
                Intent = "Analysis.DeviceLog.ByLevel",
                Confidence = 1,
                Query = $$"""{"filters":[{"field":"deviceId","operator":"eq","value":"{{DeviceId:D}}"},{"field":"preset","operator":"eq","value":"last_24h"},{"field":"level","operator":"eq","value":"{{level}}"}]}"""
            }],
            CreateAdapterContext());

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }

    [Fact]
    public void ValidatePersisted_ShouldRejectDigestAffectingMutation()
    {
        var canonicalizer = new AgentPlanCanonicalizer();
        var sealedPlan = canonicalizer.Seal(CreatePlan()).Value!;
        var changed = sealedPlan.Document with { PlannerMode = "ChangedAfterSeal" };
        var changedJson = CanonicalJson.Serialize(changed);

        var result = canonicalizer.ValidatePersisted(changedJson);

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("digest");
    }

    [Fact]
    public void SealExecutable_ShouldFailClosedUntilTrustedP2CompilerExists()
    {
        var result = new AgentPlanDraftContractAuthority(
                new IntentResultToCandidateAdapter(),
                new AgentPlanCanonicalizer())
            .SealExecutable(CreatePlan());

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("PlanCompiler");
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
    public void Seal_ShouldRequireProducerNodeOnlyForRequiredAvailableCandidate()
    {
        var plan = CreatePlan();
        var processCandidate = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = "Analysis.Process.List", Confidence = 1, Query = "{}" }],
            CreateAdapterContext()).Value!.Single();
        var optionalGeneralCandidate = plan.IntentCandidates!.Single() with
        {
            Required = new AgentIntentRequiredDocument(false, AgentIntentRequiredSource.DerivedDependency, "optional-upper-bound:v1")
        };
        var requiredCandidates = new[] { processCandidate, optionalGeneralCandidate }
            .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .ToArray();
        var requiredPlan = plan with
        {
            IntentCandidates = requiredCandidates,
            RequestedCapabilityCodes = ["Analysis.Process.List", "General.Chat"],
            ExecutionSnapshot = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
                CreateCatalog("read_uploaded_file"),
                AgentPlanV2TestData.RoutingConfiguration,
                [],
                [],
                requiredCandidates)
        };
        var optionalProcessCandidate = processCandidate with
        {
            Required = new AgentIntentRequiredDocument(false, AgentIntentRequiredSource.DerivedDependency, "optional-upper-bound:v1")
        };
        var optionalCandidates = new[] { optionalProcessCandidate, optionalGeneralCandidate }
            .OrderBy(candidate => candidate.IntentCode, StringComparer.Ordinal)
            .ToArray();
        var optionalPlan = requiredPlan with
        {
            IntentCandidates = optionalCandidates,
            ExecutionSnapshot = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
                CreateCatalog("read_uploaded_file"),
                AgentPlanV2TestData.RoutingConfiguration,
                [],
                [],
                optionalCandidates)
        };

        ProblemDetail(new AgentPlanCanonicalizer().Seal(requiredPlan)).Should().Contain("no producer Node");
        new AgentPlanCanonicalizer().Seal(optionalPlan).IsSuccess.Should().BeTrue();
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

    [Theory]
    [InlineData(200, 1_000, true)]
    [InlineData(201, 1_000, false)]
    [InlineData(200, 1_001, false)]
    public void Seal_ShouldRejectStepTextBeforeDomainCanSilentlyTruncate(
        int titleCharacters,
        int descriptionCharacters,
        bool expectedSuccess)
    {
        var plan = CreatePlan();
        var step = plan.Steps.Single() with
        {
            Title = new string('中', titleCharacters),
            Description = new string('文', descriptionCharacters)
        };

        var result = new AgentPlanCanonicalizer().Seal(plan with { Steps = [step] });

        result.IsSuccess.Should().Be(expectedSuccess, FailureSummary(result));
        if (!expectedSuccess)
        {
            ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        }
    }

    [Fact]
    public void Seal_ShouldRequireExactlyOneFinalizeStepIffArtifactTargetsExist()
    {
        var plan = CreatePlan();
        var artifactPlan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(
                AgentPlanV2TestData.CreateSingleStep("generate_chart_data", executable: false),
                AgentRuntimeJson.Options)! with
        {
            PlanDigest = null
        };
        var generate = artifactPlan.Steps.Single(step => step.ToolCode == "generate_chart_data");
        var finalize = artifactPlan.Steps.Single(step => step.ToolCode == "finalize_artifacts");
        var canonicalizer = new AgentPlanCanonicalizer();

        var valid = canonicalizer.Seal(artifactPlan);
        valid.IsSuccess.Should().BeTrue(FailureSummary(valid));
        ProblemDetail(canonicalizer.Seal(artifactPlan with { Steps = [generate] }))
            .Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(artifactPlan with { Steps = [generate, finalize, finalize] }))
            .Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(plan with { Steps = [plan.Steps.Single(), finalize] }))
            .Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(artifactPlan with
        {
            Steps = [generate, finalize with { StepType = AgentStepType.ArtifactGeneration }]
        })).Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(artifactPlan with
        {
            Steps = [generate, finalize with { RequiresApproval = false }]
        })).Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(artifactPlan with { Steps = [finalize, generate] }))
            .Should().Contain("bidirectionally");
        ProblemDetail(canonicalizer.Seal(artifactPlan with
        {
            Steps = [generate with { StepType = AgentStepType.Finalize }, finalize]
        })).Should().Contain("bidirectionally");
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

        boundary.IsSuccess.Should().BeTrue(FailureSummary(boundary));
        Encoding.UTF8.GetByteCount(boundary.Value!.Document.Nodes!.Single().Input!.CanonicalInputJson!).Should()
            .Be(AgentPlanContractVersions.MaxNodeInputCanonicalBytes);
        ProblemCode(over).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(over).Should().Contain("8000");
    }

    [Fact]
    public void NodeAndToolInput_ShouldShareExactChineseUtf8BoundaryOwner()
    {
        const string prefix = "{\"value\":\"";
        const string suffix = "\"}";
        var structuralBytes = Encoding.UTF8.GetByteCount(prefix + suffix);
        var remainingCanonicalBytes = AgentStructuredPayloadPolicyV1.MaxNodeToolInputUtf8Bytes - structuralBytes;
        var value = new string('中', remainingCanonicalBytes / 6) + new string('x', remainingCanonicalBytes % 6);
        var exactRaw = prefix + value + suffix;
        var overRaw = prefix + value + "x" + suffix;
        var exactNormalization = AgentNodeToolInputContractV1.Normalize(exactRaw);

        Encoding.UTF8.GetByteCount(exactRaw).Should().BeLessThan(8_000);
        exactNormalization.IsValid.Should().BeTrue();
        exactNormalization.CanonicalUtf8ByteCount.Should().Be(8_000);
        const string valueSchema =
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""";
        ToolInputSchemaValidator.ValidateAndParse(exactRaw, valueSchema).IsValid.Should().BeTrue();
        new AgentPlanCanonicalizer().Seal(WithNodeInput(CreatePlan(), exactNormalization.CanonicalJson!))
            .IsSuccess.Should().BeTrue();

        Encoding.UTF8.GetByteCount(overRaw).Should().BeLessThan(8_000);
        AgentNodeToolInputContractV1.Normalize(overRaw).IsValid.Should().BeFalse();
        ToolInputSchemaValidator.ValidateAndParse(overRaw, valueSchema).IsValid.Should().BeFalse();
        ProblemCode(new AgentPlanCanonicalizer().Seal(WithNodeInput(CreatePlan(), exactNormalization.CanonicalJson! + " ")))
            .Should().Be(AppProblemCodes.AgentPlanInvalid);
    }

    [Theory]
    [InlineData("{}", "[]")]
    [InlineData("{}", "{\"type\":\"obejct\"}")]
    [InlineData("{\"limit\":20,\"extra\":1}", "{\"type\":\"object\",\"properties\":{\"limit\":{\"type\":\"integer\"}}}")]
    [InlineData("{\"field\":1,\"FIELD\":2}", "{\"type\":\"object\",\"properties\":{\"field\":{\"type\":\"integer\"}}}")]
    public void ToolInputSchema_ShouldFailClosedForUnsupportedSchemaExtraFieldsAndCaseCollision(
        string input,
        string schema)
    {
        ToolInputValidationResult? result = null;
        Action action = () => result = ToolInputSchemaValidator.ValidateAndParse(input, schema);

        action.Should().NotThrow();
        result!.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ExecutionSnapshot_ShouldContainRealRoutingAuthorityAndRejectEveryDrift()
    {
        var catalog = CreateCatalog("generate_chart_data");
        var authority = AgentPlanV2TestData.RoutingConfiguration;
        var snapshot = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(catalog, authority);

        snapshot.PromptTemplateId.Should().Be(authority.TemplateCode);
        snapshot.PromptVersion.Should().Be(authority.TemplateVersion);
        snapshot.PromptHash.Should().Be(authority.PromptHash);
        snapshot.ModelId.Should().Be(authority.ModelId);
        snapshot.ModelProvider.Should().Be(authority.ModelProvider);
        snapshot.ModelProtocol.Should().Be(authority.ModelProtocol);
        snapshot.ModelParametersHash.Should().Be(authority.ModelParametersHash);
        snapshot.ModelContextWindowTokens.Should().Be(authority.ContextWindowTokens);
        snapshot.PlanContractVersion.Should().Be(AgentPlanContractVersions.PlanV2);
        snapshot.PlanContractDigest.Should().Be(AgentPlanContractSchemaAuthority.PlanContractDigest);
        snapshot.NodeContractVersion.Should().Be(AgentPlanContractVersions.NodeV1);
        snapshot.NodeContractDigest.Should().Be(AgentPlanContractSchemaAuthority.NodeContractDigest);
        snapshot.ConcurrencyPolicyVersion.Should().Be(AgentPlanContractVersions.ConcurrencyPolicyV1);
        snapshot.PluginCatalogDigest.Should().Be(AgentPlanCatalogSnapshotAuthority.CanonicalEmptyInventoryDigest);
        snapshot.McpCatalogDigest.Should().Be(AgentPlanCatalogSnapshotAuthority.CanonicalEmptyInventoryDigest);
        AgentPlanCatalogSnapshotAuthority.Matches(snapshot, catalog, authority).Should().BeTrue();

        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot,
            catalog,
            authority with { PromptHash = new string('c', 64) }).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot,
            catalog,
            authority with { ModelProvider = "ChangedProvider" }).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot,
            catalog,
            authority with { ModelParametersHash = new string('d', 64) }).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot,
            catalog,
            authority with { ContextWindowTokens = authority.ContextWindowTokens + 1 }).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot with { PlanContractDigest = new string('e', 64) },
            catalog,
            authority).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot with { NodeContractDigest = new string('e', 64) },
            catalog,
            authority).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot with { ConcurrencyPolicyVersion = "changed" },
            catalog,
            authority).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot with { PluginCatalogDigest = Hash("injected-plugin") },
            catalog,
            authority).Should().BeFalse();
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshot with { McpCatalogDigest = Hash("injected-mcp") },
            catalog,
            authority).Should().BeFalse();
    }

    [Fact]
    public void ExecutionSnapshot_ShouldHashFullToolSchemaBeyondDisplayTruncation()
    {
        var commonPrefix = $"{{\"type\":\"object\",\"description\":\"{new string('x', 4_100)}";
        var schemaA = commonPrefix + "A\"}";
        var schemaB = commonPrefix + "B\"}";
        var displaySchema = schemaA[..4_000];
        var baseline = CreateCatalog("generate_chart_data");
        var tool = baseline.Tools.Single();
        var catalogA = baseline with
        {
            Tools = [tool with { InputSchemaJson = displaySchema, InputSchemaHash = Hash(schemaA) }]
        };
        var catalogB = baseline with
        {
            Tools = [tool with { InputSchemaJson = displaySchema, InputSchemaHash = Hash(schemaB) }]
        };

        var snapshotA = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
            catalogA,
            AgentPlanV2TestData.RoutingConfiguration);
        var snapshotB = AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
            catalogB,
            AgentPlanV2TestData.RoutingConfiguration);

        displaySchema.Should().HaveLength(4_000);
        snapshotA.ToolCatalogDigest.Should().NotBe(snapshotB.ToolCatalogDigest);
        AgentPlanCatalogSnapshotAuthority.Matches(
            snapshotA,
            catalogB,
            AgentPlanV2TestData.RoutingConfiguration).Should().BeFalse();
    }

    [Fact]
    public void Seal_ShouldRejectInjectedPluginMcpAndContractSnapshotAuthority()
    {
        var plan = CreatePlan();
        var snapshot = plan.ExecutionSnapshot!;
        var canonicalizer = new AgentPlanCanonicalizer();

        ProblemCode(canonicalizer.Seal(plan with
        {
            ExecutionSnapshot = snapshot with { PluginCatalogDigest = Hash("injected-plugin") }
        })).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemCode(canonicalizer.Seal(plan with
        {
            ExecutionSnapshot = snapshot with { McpCatalogDigest = Hash("injected-mcp") }
        })).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemCode(canonicalizer.Seal(plan with
        {
            ExecutionSnapshot = snapshot with { PlanContractDigest = Hash("changed-plan-contract") }
        })).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemCode(canonicalizer.Seal(plan with
        {
            ExecutionSnapshot = snapshot with { NodeContractDigest = Hash("changed-node-contract") }
        })).Should().Be(AppProblemCodes.AgentPlanInvalid);
    }

    [Fact]
    public void P0DraftAuthority_ShouldSealContractWithoutSelectingOrCompilingExecutionGraph()
    {
        var authority = new AgentPlanDraftContractAuthority(
            new IntentResultToCandidateAdapter(),
            new AgentPlanCanonicalizer());
        var current = CreatePlan();
        var result = authority.SealDraft(new AgentPlanDraftContractRequest(
            "raw user goal that must not be persisted in Plan JSON",
            current,
            [new IntentResult { Intent = "General.Chat", Confidence = 1 }],
            CreateAdapterContext(),
            new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []),
            AgentPluginSelectionMode.BuiltInOnly,
            [],
            AgentCapabilitySelectionMode.InferredFromGoal,
            []));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Document.PlanKind.Should().Be(AgentTaskPlanKinds.PlanDraft);
        result.Value.Document.IsExecutable.Should().BeFalse();
        result.Value.Document.Nodes.Should().BeEmpty(
            "P0 seals an already-selected draft and P2 exclusively owns step-to-node graph compilation");
        result.Value.Document.JoinPolicies.Should().BeEmpty();
        result.Value.Document.Steps.Should().BeEquivalentTo(current.Steps);
        result.Value.CanonicalJson.Should().NotContain("raw user goal that must not be persisted");
        new AgentPlanCanonicalizer().ValidatePersisted(result.Value.CanonicalJson).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void P0DraftAuthority_ShouldFailClosedForPluginGraphSelectionOwnedByLaterStage()
    {
        var result = new AgentPlanDraftContractAuthority(
                new IntentResultToCandidateAdapter(),
                new AgentPlanCanonicalizer())
            .SealDraft(new AgentPlanDraftContractRequest(
                "goal",
                CreatePlan(),
                [new IntentResult { Intent = "General.Chat", Confidence = 1 }],
                CreateAdapterContext(),
                new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, 0, []),
                AgentPluginSelectionMode.ExplicitAllowlist,
                [Guid.Parse("33333333-3333-3333-3333-333333333333")],
                AgentCapabilitySelectionMode.InferredFromGoal,
                []));

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanInvalid);
        ProblemDetail(result).Should().Contain("fail-closed");
    }

    [Fact]
    public void MetadataResolver_ShouldExposeOnlyCompletedLegacyV1AsReadOnlyHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var legacyJson = "{\"version\":1}";
        var active = new AgentTask(
            new AICopilot.Core.AiGateway.Ids.SessionId(Guid.NewGuid()),
            Guid.NewGuid(),
            "Legacy active",
            "Legacy active",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            legacyJson,
            now);
        AgentTaskPlanMetadataResolver.Resolve(active).IntegrityStatus.Should()
            .Be(AgentTaskPlanMetadataResolver.Invalid);

        active.ConfirmExecutablePlan(legacyJson, [], now);
        active.ApprovePlan(now);
        active.AttachWorkspace(AICopilot.Core.AiGateway.Ids.ArtifactWorkspaceId.New(), now);
        active.Start(now);
        active.MarkWorkspaceReady(now);
        active.WaitForFinalApproval(now);
        active.Complete("completed historical output", now);

        var metadata = AgentTaskPlanMetadataResolver.Resolve(active);
        metadata.IntegrityStatus.Should().Be(AgentTaskPlanMetadataResolver.LegacyCompletedReadOnly);
        metadata.SchemaVersion.Should().Be(AgentPlanContractVersions.LegacyV1);
        metadata.IsExecutable.Should().BeFalse();
        AgentTaskDtoMapper.Map(active).CanRun.Should().BeFalse();
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
        ProblemCode(oversize).Should().Be(AppProblemCodes.EvidencePayloadTooLarge);
        ProblemDetail(oversize).Should().Contain("65536");
    }

    [Fact]
    public void IntentAdapter_ShouldDropRouterReasoningAndKeepDeviceDirectedRequestAsGap()
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
                    Intent = "Analysis.Device.Status",
                    Confidence = 0.91,
                    Query = """{"filters":[{"field":"deviceCode","operator":"eq","value":"DEV-001"}]}""",
                    Reasoning = "SECRET MODEL REASONING",
                    Reason = "SECRET ROUTER REASON"
                }
            ],
            context);

        result.IsSuccess.Should().BeTrue();
        var candidate = result.Value!.Single();
        candidate.IntentClass.Should().Be(AgentIntentClass.CloudOnly);
        candidate.ProviderCode.Should().Be("CloudAiRead");
        candidate.RequestedResources.Devices.Should().BeEmpty();
        candidate.CapabilityGap!.Code.Should().Be(AgentPlanCapabilityGapCodes.ResourceResolutionRequired);
        var json = CanonicalJson.Serialize(candidate);
        json.Should().NotContain("SECRET MODEL REASONING");
        json.Should().NotContain("SECRET ROUTER REASON");
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

    [Theory]
    [InlineData("Skill.AuthorizedReport", "TransitionSkill", "TransitionSkillRoster")]
    [InlineData("Action.AuthorizedAnalytics", "PluginAction", "PluginActionRoster")]
    public void IntentAdapter_ShouldClassifyAuthorizedTransitionRostersAsStableNonExecutableGaps(
        string intentCode,
        string expectedClass,
        string expectedProvider)
    {
        var context = CreateAdapterContext() with
        {
            KnownSkillCodes = ["AuthorizedReport"],
            KnownActionIntentCodes = ["Action.AuthorizedAnalytics"]
        };

        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = intentCode, Confidence = 0.9 }],
            context);

        result.IsSuccess.Should().BeTrue(FailureSummary(result));
        var candidate = result.Value!.Single();
        candidate.IntentClass.Should().Be(Enum.Parse<AgentIntentClass>(expectedClass));
        candidate.ProviderCode.Should().Be(expectedProvider);
        candidate.Availability.Should().Be(AgentIntentAvailability.KnownButUnavailable);
        candidate.CapabilityGap!.Code.Should().Be(AgentPlanCapabilityGapCodes.KnownCapabilityUnavailable);
    }

    [Fact]
    public void IntentAdapter_ShouldKeepUtcBoundariesAndOriginalNamedTimeZone()
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult
            {
                Intent = "Analysis.DeviceLog.Range",
                Confidence = 0.9,
                Query = BuildDeviceScopedTimeRangeQuery(
                    """{"fromUtc":"2026-07-01T00:00:00Z","toUtc":"2026-07-02T00:00:00Z","timeZone":"Asia/Shanghai"}""")
            }],
            CreateAdapterContext());

        result.IsSuccess.Should().BeTrue(FailureSummary(result));
        var range = result.Value!.Single().Filters.TimeRange!;
        range.TimeZone.Should().Be("Asia/Shanghai");
        range.FromUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        range.ToUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void IntentAdapter_ShouldValidateBothSidesOfNamedTimeZoneDst()
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult
            {
                Intent = "Analysis.DeviceLog.Range",
                Confidence = 0.9,
                Query = BuildDeviceScopedTimeRangeQuery(
                    """{"start":"2026-01-15T08:00:00-05:00","end":"2026-07-15T08:00:00-04:00","timeZone":"America/New_York"}""")
            }],
            CreateAdapterContext());

        result.IsSuccess.Should().BeTrue(FailureSummary(result));
        var range = result.Value!.Single().Filters.TimeRange!;
        range.TimeZone.Should().Be("America/New_York");
        range.FromUtc.Should().Be(DateTimeOffset.Parse("2026-01-15T13:00:00Z"));
        range.ToUtc.Should().Be(DateTimeOffset.Parse("2026-07-15T12:00:00Z"));
    }

    [Theory]
    [InlineData("{\"timeRange\":{\"start\":\"2026-07-15T08:00:00-05:00\",\"timeZone\":\"America/New_York\"}}")]
    [InlineData("{\"timeRange\":{\"fromUtc\":\"2026-07-15T08:00:00+08:00\",\"timeZone\":\"Asia/Shanghai\"}}")]
    [InlineData("{\"timeRange\":{\"fromUtc\":\"2026-07-15T00:00:00Z\",\"timeZone\":\"Not/AZone\"}}")]
    public void IntentAdapter_ShouldRejectInvalidTimeZoneOrUtcBoundary(string query)
    {
        var scopedQuery = query.Replace(
            "{\"timeRange\"",
            $"{{\"filters\":[{{\"field\":\"deviceId\",\"operator\":\"eq\",\"value\":\"{DeviceId:D}\"}}],\"timeRange\"",
            StringComparison.Ordinal);
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult { Intent = "Analysis.DeviceLog.Range", Confidence = 0.9, Query = scopedQuery }],
            CreateAdapterContext());

        ProblemCode(result).Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);
    }

    [Fact]
    public void IntentAdapter_ShouldDefaultPureUtcRangeToUtcZone()
    {
        var result = new IntentResultToCandidateAdapter().Adapt(
            [new IntentResult
            {
                Intent = "Analysis.DeviceLog.Range",
                Confidence = 0.9,
                Query = BuildDeviceScopedTimeRangeQuery(
                    """{"fromUtc":"2026-07-15T00:00:00Z","toUtc":"2026-07-16T00:00:00Z"}""")
            }],
            CreateAdapterContext());

        result.Value!.Single().Filters.TimeRange!.TimeZone.Should().Be("UTC");
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
            .Be("3194f57f1b57f454d846d4549ac8350d6dfa7062596b6708d38a5d8513b9a830");
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
            SchemaVersion: AgentPlanContractVersions.NodeV1,
            NodeId: "node-001",
            NodeKind: "DeterministicComputeNode",
            DependsOn: [],
            Required: true,
            InputSchemaRef: "node-input:v1",
            OutputSchemaRef: "evidence:derived-metric:v1",
            RequestedToolCodes: ["read_uploaded_file"],
            RequestedCapabilityCodes: ["General.Chat"],
            DataScopes: [],
            KnowledgeScopes: [],
            EvidenceSelectors: [],
            Input: new AgentPlanNodeInputDocument(
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
            ModelPolicy: null,
            TimeoutPolicy: new AgentPlanTimeoutPolicyDocument("timeout-policy:v1", 120),
            RetryPolicy: new AgentPlanRetryPolicyDocument("retry-policy:v1", 1, "None"),
            Budget: new AgentPlanNodeBudgetDocument(0, 0, 0),
            ApprovalPolicy: new AgentPlanApprovalPolicyDocument(false, "None"),
            IdempotencyPolicy: new AgentPlanIdempotencyPolicyDocument("idempotency-policy:v1", "Deterministic"),
            SideEffectClass: "ArtifactDraftOnly",
            JoinPolicy: null);

        var catalog = CreateCatalog("read_uploaded_file");
        var riskSummary = new Dictionary<string, int>(StringComparer.Ordinal) { ["Low"] = 1 };

        return new AgentTaskPlanDocument(
            Version: 2,
            PlannerTemplateCode: "agent_planner",
            Goal: $"{AgentTaskType.ReportGeneration} task; goalSha256={CanonicalJson.ComputeSha256("safe task summary")}",
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
                    "read_uploaded_file",
                    false)
            ],
            RuntimeSettings: new AgentTaskPlanRuntimeSettingsDocument(0, 0),
            PlannerMode: "PlanDraft",
            PlannerFallbackReason: null,
            PlannerModelId: AgentPlanV2TestData.RoutingConfiguration.ModelId,
            PlannerToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            PlannerAvailableToolCount: 1,
            DataSourceIds: [],
            BusinessDomains: [],
            QueryMode: "TextToSql",
            RequiresDataApproval: false,
            ArtifactTypes: [],
            PlannerSafetySummary: new AgentTaskPlanSafetySummaryDocument(
                "PlanV2Contract",
                "PlanDraft",
                null,
                PlannerToolCatalog.CurrentVersion,
                1,
                false,
                false,
                riskSummary,
                false),
            ForcedStepCodes: [],
            ApprovalCheckpoints: [],
            DataSourceSummaries: [],
            ToolCatalogVersion: PlannerToolCatalog.CurrentVersion,
            VisibleToolCount: 1,
            ToolRiskSummary: riskSummary,
            MockMcpOnly: false,
            ToolApprovalCheckpoints: [],
            PlanKind: AgentTaskPlanKinds.PlanDraft,
            IsExecutable: false,
            LifecycleSealPadding: "0000",
            CapabilityGaps: [AgentPlanCapabilityGapCodes.PlanCompilerUnavailable],
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
            ExecutionSnapshot: AgentPlanCatalogSnapshotAuthority.CreateSnapshot(
                catalog,
                AgentPlanV2TestData.RoutingConfiguration,
                [],
                [],
                [candidate]),
            SecuritySummary: new AgentPlanSecuritySummaryDocument(
                true,
                false,
                false,
                false,
                false,
                false));
    }

    private static AgentTaskPlanDocument CreatePlanWithCanonicalSize(int targetCanonicalBytes)
    {
        var basePlan = CreatePlan();
        var baseNode = basePlan.Nodes!.Single();
        var minimalPlan = basePlan with
        {
            PlannerTemplateCode = "p",
            PlannerMode = "p",
            Nodes =
            [
                baseNode with
                {
                    OutputSchemaRef = "evidence::v1",
                    ApprovalPolicy = baseNode.ApprovalPolicy! with { PolicyCode = "p" }
                }
            ]
        };
        var minimalJson = JsonSerializer.Serialize(
            minimalPlan with { PlanDigest = new string('0', 64) },
            CanonicalJson.SerializerOptions);
        var remaining = targetCanonicalBytes - CanonicalJson.MeasureCanonicalUtf8Bytes(
            minimalJson,
            AgentPlanContractVersions.MaxPlanCanonicalBytes);
        if (remaining < 0)
        {
            throw new InvalidOperationException("Requested Plan boundary is smaller than the valid Plan v2 envelope.");
        }

        var plannerTemplatePadding = TakeCanonicalPadding(ref remaining, "p".Length);
        var plannerModePadding = TakeCanonicalPadding(ref remaining, "p".Length);
        var outputSchemaPadding = TakeCanonicalPadding(ref remaining, "evidence::v1".Length);
        var approvalPolicyPadding = TakeCanonicalPadding(ref remaining, "p".Length);
        if (remaining != 0)
        {
            throw new InvalidOperationException("Valid Plan v2 strings cannot represent the requested canonical boundary.");
        }

        return minimalPlan with
        {
            PlannerTemplateCode = "p" + new string('x', plannerTemplatePadding),
            PlannerMode = "p" + new string('x', plannerModePadding),
            Nodes =
            [
                baseNode with
                {
                    OutputSchemaRef = "evidence:" + new string('x', outputSchemaPadding) + ":v1",
                    ApprovalPolicy = baseNode.ApprovalPolicy! with
                    {
                        PolicyCode = "p" + new string('x', approvalPolicyPadding)
                    }
                }
            ]
        };
    }

    private static int TakeCanonicalPadding(ref int remaining, int currentUtf8Bytes)
    {
        var padding = Math.Min(
            remaining,
            AgentCanonicalJsonV1.MaxStringUtf8Bytes - currentUtf8Bytes);
        remaining -= padding;
        return padding;
    }

    private static AgentTaskPlanDocument GrowPlanByOneCanonicalByte(AgentTaskPlanDocument plan)
    {
        var node = plan.Nodes!.Single();
        return plan with
        {
            Nodes =
            [
                node with
                {
                    ApprovalPolicy = node.ApprovalPolicy! with
                    {
                        PolicyCode = node.ApprovalPolicy.PolicyCode + "x"
                    }
                }
            ]
        };
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
            new AgentEvidenceProducerDocument(
                "DeterministicComputeNode",
                "test",
                "derive_metric",
                Hash("derive-metric-schema:v1"),
                null,
                null,
                null),
            new AgentEvidenceSourceDocument(
                "Agent",
                "opaque:test",
                "Inline",
                false,
                null,
                null,
                null,
                [],
                null,
                null,
                null,
                []),
            new AgentEvidenceQualityDocument(1, false, "Current", 0, 1, []),
            new AgentEvidencePayloadDocument(
                AgentPlanContractVersions.InlineEvidencePolicyV1,
                "InlineCanonicalJson",
                null,
                "application/json",
                byteCount,
                Hash(canonicalPayload),
                true,
                canonicalPayload),
            new AgentEvidenceContentDocument("safe", new Dictionary<string, decimal>(), [], [], []),
            new AgentEvidenceLineageDocument(
                [Guid.Parse("88888888-8888-4888-8888-888888888888")],
                Hash("input"),
                Hash(canonicalPayload)),
            new AgentEvidenceGovernanceDocument(
                "Internal",
                "NotRequired",
                ["AgentRuntime"],
                "TaskLifetime"),
            null,
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            string.Empty);
    }

    private static PlannerToolCatalog CreateCatalog(params string[] toolCodes)
    {
        const string schema =
            """{"type":"object","properties":{},"additionalProperties":false}""";
        var schemaHash = Hash(CanonicalJson.Canonicalize(schema));
        var tools = toolCodes.Select(toolCode => new AgentPlannerToolSummary(
            toolCode,
            toolCode,
            toolCode,
            "Artifact",
            "AgentRuntime",
            "AgentRuntime",
            schema,
            false,
            "Low",
            ProviderKind: "Artifact",
            OutputSchemaJson: schema,
            SideEffectClass: "ArtifactWrite",
            InputSchemaHash: schemaHash,
            OutputSchemaHash: schemaHash)).ToArray();
        return new PlannerToolCatalog(PlannerToolCatalog.CurrentVersion, tools.Length, tools);
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

    private static string BuildDeviceScopedTimeRangeQuery(string timeRangeJson)
    {
        return "{\"filters\":[{\"field\":\"deviceId\",\"operator\":\"eq\",\"value\":\"" +
               DeviceId.ToString("D") +
               "\"}],\"timeRange\":" +
               timeRangeJson +
               "}";
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

    private static string FailureSummary(IResult result)
    {
        return string.Join(" | ", result.Errors?.Select(error => error?.ToString()) ?? []);
    }
}
