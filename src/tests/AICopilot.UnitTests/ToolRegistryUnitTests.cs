using System.Linq.Expressions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AICopilot.AgentPlugin;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.AiGatewayService.Models;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.Core.AiGateway.Aggregates.AgentTasks;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.Approvals;
using AICopilot.Core.AiGateway.Aggregates.Artifacts;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Aggregates.Uploads;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AICopilot.AgentWorkflowTestKit;

namespace AICopilot.UnitTests;

public sealed class ToolRegistryUnitTests : ToolRegistryGovernanceTestBase
{
    [Fact]
    public void BuiltInToolCatalog_ShouldExposeChineseDisplayNames()
    {
        BuiltInToolRegistrations.CurrentCatalogVersion.Should().BeGreaterThanOrEqualTo(12);
        var tools = BuiltInToolRegistrations.AgentRuntimeTools
            .ToDictionary(tool => tool.ToolCode, StringComparer.Ordinal);

        tools["read_uploaded_file"].DisplayName.Should().Be("读取上传文件");
        tools["parse_csv_json"].DisplayName.Should().Be("解析 CSV/JSON");
        tools["query_business_database_readonly"].DisplayName.Should().Be("查询只读业务数据");
        tools["generate_business_chart"].DisplayName.Should().Be("生成业务图表");
        tools["finalize_artifacts"].DisplayName.Should().Be("最终产物确认");
        tools["finalize_artifacts"].TargetName.Should().Be("ArtifactWorkspaceLifecycleCoordinator");
        tools["finalize_artifacts"].Description.Should()
            .Contain("approved durable NodeRun atomically commits final files");
        tools["join_evidence"].DataBoundary.Should().Be(ToolDataBoundary.AuthorizedEvidenceOnly);
        tools["assess_cloud_health"].DataBoundary.Should().Be(ToolDataBoundary.AuthorizedEvidenceOnly);
        tools["agent_reasoning"].DataBoundary.Should().Be(ToolDataBoundary.AuthorizedEvidenceOnly);
        BuiltInToolRegistrations.IsLifecycleCheckpoint("finalize_artifacts").Should().BeTrue();
        var finalizationRegistration = CreateTool(
            "finalize_artifacts",
            ToolProviderType.Artifact,
            targetName: tools["finalize_artifacts"].TargetName,
            requiresApproval: true,
            riskLevel: AiToolRiskLevel.High);
        var dispatcherExecutor = new RuntimeBuiltInAgentToolExecutor(_ =>
            Task.FromResult<object>(new { mustNotExecute = true }));
        dispatcherExecutor.CanExecute(finalizationRegistration, null!).Should().BeFalse(
            "the durable finalization checkpoint must not dispatch through a provider executor");
        var displayNames = tools.Values.Select(tool => tool.DisplayName).ToArray();
        displayNames.Should().NotContain("Finalize artifacts");
        displayNames.Should().NotContain("Generate business chart");
        displayNames.Should().NotContain("Parse CSV/JSON");
    }

    [Fact]
    public void AgentPlannerToolSummary_DefaultOutputSchema_ShouldBeFrozenStrictEmptyObject()
    {
        var summary = new AgentPlannerToolSummary(
            "manual_summary",
            "Manual summary",
            "Manual summary fixture.",
            "BuiltIn",
            "AgentRuntime",
            "AgentTaskRuntime",
            PlannerToolCatalog.StrictEmptyOutputSchemaJson,
            RequiresApproval: false,
            "Low");

        summary.OutputSchemaJson.Should().Be(PlannerToolCatalog.StrictEmptyOutputSchemaJson);
        var contract = ToolOutputSchemaContractV1.Validate(summary.OutputSchemaJson);
        contract.IsValid.Should().BeTrue(contract.Error);
        contract.CanonicalJson.Should().Be(summary.OutputSchemaJson);
        using var schema = JsonDocument.Parse(summary.OutputSchemaJson);
        schema.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void BuiltInToolCatalog_ShouldOwnExactStrictInputSchemasForEveryRuntimeTool()
    {
        BuiltInToolRegistrations.CurrentCatalogVersion.Should().Be(19);
        BuiltInToolRegistrations.CurrentSchemaVersion.Should().Be(3);

        foreach (var tool in BuiltInToolRegistrations.AgentRuntimeTools)
        {
            var contract = ToolInputSchemaContractV1.Validate(tool.InputSchemaJson);
            contract.IsValid.Should().BeTrue($"{tool.ToolCode}: {contract.Error}");
            tool.SchemaVersion.Should().Be(BuiltInToolRegistrations.CurrentSchemaVersion);
            tool.CatalogVersion.Should().Be(BuiltInToolRegistrations.CurrentCatalogVersion);

            if (tool.ProviderType != ToolProviderType.MockMcp)
            {
                ToolInputSchemaValidator.ValidateAndParse("{}", tool.InputSchemaJson).IsValid
                    .Should().BeTrue(tool.ToolCode);
                ToolInputSchemaValidator.ValidateAndParse(
                        """{"unexpected":"must-not-be-ignored"}""",
                        tool.InputSchemaJson)
                    .IsValid.Should().BeFalse(tool.ToolCode);
            }
        }

        var schemas = BuiltInToolRegistrations.AgentRuntimeTools
            .ToDictionary(tool => tool.ToolCode, tool => tool.InputSchemaJson, StringComparer.Ordinal);
        ToolInputSchemaValidator.ValidateAndParse(
                "{}",
                schemas["mock_mcp_health_check"])
            .IsValid.Should().BeTrue();
        ToolInputSchemaValidator.ValidateAndParse(
                """{"domain":"Production"}""",
                schemas["mock_mcp_kpi_formula_lookup"])
            .IsValid.Should().BeTrue();
        ToolInputSchemaValidator.ValidateAndParse(
                """{"artifactType":"markdown","contentPreview":"SimulationBusiness queryHash"}""",
                schemas["mock_mcp_artifact_quality_check"])
            .IsValid.Should().BeTrue();
        ToolInputSchemaValidator.ValidateAndParse(
                """{"title":"preview","summary":"safe"}""",
                schemas["mock_mcp_external_ticket_preview"])
            .IsValid.Should().BeTrue();
        ToolInputSchemaValidator.ValidateAndParse(
                """{"domain":1}""",
                schemas["mock_mcp_kpi_formula_lookup"])
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ToolRegistryGuard_ShouldExposeInvalidSchemaAsTypedUnavailable()
    {
        var tool = CreateTool("invalid_schema_tool");
        typeof(ToolRegistration).GetProperty(nameof(ToolRegistration.InputSchemaJson))!
            .SetValue(tool, "{}");

        var decision = await CreateGuard(tool).ValidateAsync(
            tool.ToolCode,
            UserId,
            CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Problem!.Code.Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);

        var invalidOutput = CreateTool("invalid_output_schema_tool");
        typeof(ToolRegistration).GetProperty(nameof(ToolRegistration.OutputSchemaJson))!
            .SetValue(invalidOutput, "{}");
        var outputDecision = await CreateGuard(invalidOutput).ValidateAsync(
            invalidOutput.ToolCode,
            UserId,
            CancellationToken.None);
        outputDecision.IsAllowed.Should().BeFalse();
        outputDecision.Problem!.Code.Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);
    }

    [Fact]
    public void ToolRegistrationWritePolicy_ShouldRejectBuiltInSchemaOrVersionDrift()
    {
        var tool = BuiltInToolRegistrations.AgentRuntimeTools.Single(
            item => item.ToolCode == "query_business_database_readonly");

        ToolRegistrationInputContractPolicy.Validate(
                tool.ToolCode,
                tool.InputSchemaJson,
                tool.SchemaVersion,
                tool.CatalogVersion)
            .IsValid.Should().BeTrue();
        ToolRegistrationInputContractPolicy.Validate(
                tool.ToolCode,
                """{"type":"object","properties":{"ignored":{"type":"string"}},"additionalProperties":false}""",
                tool.SchemaVersion,
                tool.CatalogVersion)
            .IsValid.Should().BeFalse();
        ToolRegistrationInputContractPolicy.Validate(
                tool.ToolCode,
                tool.InputSchemaJson,
                tool.SchemaVersion + 1,
                tool.CatalogVersion)
            .IsValid.Should().BeFalse();

        ToolRegistrationOutputContractPolicy.Validate(
                tool.ToolCode,
                tool.OutputSchemaJson,
                tool.SchemaVersion,
                tool.CatalogVersion)
            .IsValid.Should().BeTrue();
        ToolRegistrationOutputContractPolicy.Validate(
                tool.ToolCode,
                """{"type":"object","properties":{},"additionalProperties":false}""",
                tool.SchemaVersion,
                tool.CatalogVersion)
            .IsValid.Should().BeFalse();
        ToolRegistrationOutputContractPolicy.Validate(
                tool.ToolCode,
                tool.OutputSchemaJson,
                tool.SchemaVersion + 1,
                tool.CatalogVersion)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ToolRegistryWriteHandlers_ShouldRejectInvalidOutputSchemaWithoutMutation()
    {
        var tool = CreateTool("custom_output_write_guard");
        var originalOutputSchema = tool.OutputSchemaJson;
        var repository = new InMemoryRepository<ToolRegistration>(tool);
        var pluginCatalog = new StubAgentPluginCatalog();
        var audit = new CapturingAuditLogWriter();
        var updateHandler = new UpdateToolRegistrationCommandHandler(repository, pluginCatalog, audit);

        var update = await updateHandler.Handle(
            new UpdateToolRegistrationCommand(tool.ToolCode, OutputSchemaJson: "{}"),
            CancellationToken.None);

        update.IsSuccess.Should().BeFalse();
        tool.OutputSchemaJson.Should().Be(originalOutputSchema);
        audit.Requests.Should().BeEmpty();

        var upsertHandler = new UpsertToolDefinitionCommandHandler(repository, pluginCatalog, audit);
        var upsert = await upsertHandler.Handle(
            new UpsertToolDefinitionCommand(
                "invalid_output_upsert",
                "invalid output upsert",
                "invalid output upsert",
                ToolProviderType.Mcp,
                ToolRegistrationTargetType.McpServer,
                "runtime-mcp",
                """{"type":"object","properties":{},"additionalProperties":false}""",
                "{}",
                AiToolRiskLevel.Low),
            CancellationToken.None);

        upsert.IsSuccess.Should().BeFalse();
        repository.Items.Should().NotContain(item => item.ToolCode == "invalid_output_upsert");
        audit.Requests.Should().BeEmpty();
    }

    [Fact]
    public void ToolRegistrationAggregate_ShouldCanonicalizeBothSchemasAndRejectInvalidUpdatesAtomically()
    {
        var tool = CreateTool(
            "aggregate_schema_authority",
            inputSchemaJson:
            """
            { "type": "object", "properties": {}, "additionalProperties": false }
            """,
            outputSchemaJson:
            """
            { "type": "object", "properties": { "ok": { "type": "boolean" } }, "required": ["ok"], "additionalProperties": false }
            """);
        var originalDisplayName = tool.DisplayName;
        var originalOutputSchema = tool.OutputSchemaJson;

        tool.InputSchemaJson.Should().Be(CanonicalJson.Canonicalize(tool.InputSchemaJson));
        tool.OutputSchemaJson.Should().Be(CanonicalJson.Canonicalize(tool.OutputSchemaJson));
        Action invalidUpdate = () => tool.Update(
            "mutated before invalid schema",
            tool.Description,
            tool.ProviderType,
            tool.TargetType,
            tool.TargetName,
            tool.InputSchemaJson,
            "{}",
            tool.RiskLevel,
            tool.RequiredPermission,
            tool.RequiresApproval,
            tool.IsEnabled,
            tool.TimeoutSeconds,
            tool.AuditLevel,
            DateTimeOffset.UtcNow);

        invalidUpdate.Should().Throw<ArgumentException>();
        tool.DisplayName.Should().Be(originalDisplayName);
        tool.OutputSchemaJson.Should().Be(originalOutputSchema);
    }

    [Fact]
    public void BuiltInToolCatalog_ShouldOwnClosedOutputSchemasThatMatchDeclaredResults()
    {
        foreach (var tool in BuiltInToolRegistrations.AgentRuntimeTools)
        {
            var schema = ToolOutputSchemaContractV1.Validate(tool.OutputSchemaJson);
            schema.IsValid.Should().BeTrue($"{tool.ToolCode}: {schema.Error}");
            schema.CanonicalJson.Should().NotBe(
                CanonicalJson.Canonicalize("""{"type":"object"}"""),
                $"{tool.ToolCode} must not retain the old empty output placeholder");

            var sample = BuildDeclaredOutputSample(tool.ToolCode);
            var validation = ToolOutputSchemaValidator.ValidateAndCanonicalize(
                sample,
                tool.OutputSchemaJson);
            validation.IsValid.Should().BeTrue($"{tool.ToolCode}: {validation.Error}");

            var withUnexpected = new Dictionary<string, object?>(
                JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    JsonSerializer.Serialize(sample))!,
                StringComparer.Ordinal)
            {
                ["unexpected"] = true
            };
            ToolOutputSchemaValidator.ValidateAndCanonicalize(withUnexpected, tool.OutputSchemaJson)
                .IsValid.Should().BeFalse($"{tool.ToolCode} output must remain closed");
        }
    }

    [Fact]
    public async Task PlannerToolCatalog_ShouldProjectCanonicalOutputSchemaAndFailClosedAtUtf8Boundary()
    {
        static string BuildSizedOutputSchema(int utf8ByteCount)
        {
            const string prefix = "{\"type\":\"object\",\"description\":\"";
            const string suffix = "\",\"properties\":{},\"additionalProperties\":false}";
            var fixedBytes = Encoding.UTF8.GetByteCount(prefix + suffix);
            var schema = prefix + new string('x', utf8ByteCount - fixedBytes) + suffix;
            Encoding.UTF8.GetByteCount(schema).Should().Be(utf8ByteCount);
            return schema;
        }

        foreach (var boundary in new[]
                 {
                     ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes - 1,
                     ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes
                 })
        {
            var rawSchema = BuildSizedOutputSchema(boundary);
            var contract = ToolOutputSchemaContractV1.Validate(rawSchema);
            contract.IsValid.Should().BeTrue(contract.Error);
            var guard = CreatePlanToolGuard(CreateGuard(CreateTool(
                $"output_schema_{boundary}",
                outputSchemaJson: rawSchema)));

            var result = await guard.GetAvailableToolCatalogAsync(UserId, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            var summary = result.Value!.Tools.Should().ContainSingle().Which;
            summary.OutputSchemaJson.Should().Be(contract.CanonicalJson);
            summary.OutputSchemaHash.Should().Be(CanonicalJson.ComputeSha256(contract.CanonicalJson!));
            summary.OutputSchema!.IsTruncated.Should().BeFalse();
        }

        var oversized = BuildSizedOutputSchema(ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes + 1);
        ToolOutputSchemaContractV1.Validate(oversized).IsValid.Should().BeFalse();
        Action createOversized = () => CreateTool("oversized_output_schema", outputSchemaJson: oversized);
        createOversized.Should().Throw<ArgumentException>();

        var baseSchema = """{"type":"object","properties":{},"additionalProperties":false}""";
        var whitespaceAmplified = new string(
            ' ',
            ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes - Encoding.UTF8.GetByteCount(baseSchema) + 1) + baseSchema;
        Encoding.UTF8.GetByteCount(whitespaceAmplified)
            .Should().Be(ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes + 1);
        ToolOutputSchemaContractV1.Validate(whitespaceAmplified).IsValid.Should().BeFalse();

        var unicodeDescription = string.Concat(Enumerable.Repeat("测🔧", 100));
        var unicodeSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            description = unicodeDescription,
            properties = new { },
            additionalProperties = false
        }, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Encoding.UTF8.GetByteCount(unicodeSchema).Should().BeLessThan(ToolOutputSchemaContractV1.MaxSchemaUtf8Bytes);
        ToolOutputSchemaContractV1.Validate(unicodeSchema).IsValid.Should().BeTrue();

        var sanitizerSensitiveSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            description = "token=provider-controlled-schema-value",
            properties = new { },
            additionalProperties = false
        });
        var sanitizerSensitive = CreatePlanToolGuard(CreateGuard(CreateTool(
            "sanitizer_sensitive_output_schema",
            outputSchemaJson: sanitizerSensitiveSchema)));

        var sanitizerSensitiveResult = await sanitizerSensitive.GetAvailableToolCatalogAsync(
            UserId,
            CancellationToken.None);

        sanitizerSensitiveResult.IsSuccess.Should().BeFalse();
        sanitizerSensitiveResult.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);
    }

    [Fact]
    public void ToolOutputRuntimeBoundary_ShouldUseCompactCanonicalUtf8LimitForUnicodeAndEscapes()
    {
        const string schema =
            """{"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}""";
        const string unicodeAndEscapes = "测🔧\"\\";
        var seed = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            new { value = unicodeAndEscapes },
            schema);
        seed.IsValid.Should().BeTrue(seed.Error);
        var fillerLength = AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes - seed.Utf8ByteCount;
        fillerLength.Should().BePositive();

        var exact = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            new { value = unicodeAndEscapes + new string('a', fillerLength) },
            schema);
        var over = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            new { value = unicodeAndEscapes + new string('a', fillerLength + 1) },
            schema);

        exact.IsValid.Should().BeTrue(exact.Error);
        exact.Utf8ByteCount.Should().Be(AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes);
        exact.CanonicalJson.Should().Be(CanonicalJson.Serialize(new
        {
            value = unicodeAndEscapes + new string('a', fillerLength)
        }));
        over.IsValid.Should().BeFalse();
        over.IsPayloadTooLarge.Should().BeTrue();
        over.Utf8ByteCount.Should().Be(AgentStructuredPayloadPolicyV1.MaxInlineOutputUtf8Bytes + 1);
    }

    [Fact]
    public void ToolOutputValidation_ShouldNeverEchoProviderControlledUndeclaredPropertyName()
    {
        const string maliciousProperty =
            "Bearer abc123 C:\\private\\provider.sql SELECT * FROM payroll -----BEGIN PRIVATE KEY-----";
        const string schema =
            """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""";
        var output = new Dictionary<string, object?>
        {
            ["ok"] = true,
            [maliciousProperty] = "must-not-be-reflected"
        };

        var validation = ToolOutputSchemaValidator.ValidateAndCanonicalize(output, schema);

        validation.IsValid.Should().BeFalse();
        validation.Error.Should().Be("Tool output contains an undeclared field at schema path '$'.");
        validation.Error.Should().NotContain("Bearer");
        validation.Error.Should().NotContain("private");
        validation.Error.Should().NotContain("SELECT");
        validation.Error.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public void ProviderDurableOutput_ShouldBeVersionedAndNeverPersistProviderRawPayload()
    {
        var tool = CreateTool(
            "mcp_sensitive_output",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp");
        var sensitive = "token=very-secret; password=hunter2; C:\\private\\report.sql; SELECT * FROM payroll; ";
        var raw = CanonicalJson.Serialize(new
        {
            message = sensitive + "/var/private/provider.pem; Authorization: Bearer abc123; " +
                      "-----BEGIN PRIVATE KEY-----" + new string('测', 2_000)
        });

        var durable = AgentToolDurableOutputBuilder.BuildProviderEnvelope(tool, raw);
        var validation = AgentToolDurableOutputContractV1.Validate(durable);
        var json = validation.CanonicalJson!;

        validation.IsValid.Should().BeTrue(validation.Error);
        durable.ContractVersion.Should().Be(AgentToolDurableOutputContractV1.ContractVersion);
        AgentToolDurableOutputContractV1.ContractDigest.Should().HaveLength(64);
        durable.RawPayloadPersisted.Should().BeFalse();
        durable.SummaryTruncated.Should().BeFalse("the persisted summary is fixed text, not a raw-payload slice");
        durable.SafeSummary.Should().Be(AgentToolDurableOutputContractV1.SafeSummary);
        durable.PersistedSummaryUtf8ByteCount.Should().Be(Encoding.UTF8.GetByteCount(durable.SafeSummary));
        durable.RawUtf8ByteCount.Should().Be(Encoding.UTF8.GetByteCount(raw));
        json.Should().Contain("agent-tool-durable-output:v1");
        json.Should().Contain("\"rawPayloadPersisted\":false");
        json.Should().NotContain("very-secret");
        json.Should().NotContain("hunter2");
        json.Should().NotContain("C:\\\\private");
        json.Should().NotContain("SELECT * FROM payroll");
        json.Should().NotContain("/var/private/provider.pem");
        json.Should().NotContain("Bearer abc123");
        json.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public void AgentToolRuntimeOutputGate_ShouldRejectRawOrFalselyBoundProviderDurableOutput()
    {
        const string schema =
            """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"],"additionalProperties":false}""";
        const string rawSecret =
            "Bearer abc123 C:\\private\\provider.sql SELECT * FROM payroll -----BEGIN PRIVATE KEY-----";
        var tool = CreateTool(
            "mcp_runtime_gate_fixture",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp",
            outputSchemaJson: schema);
        var contract = ToolOutputSchemaValidator.ValidateAndCanonicalize(
            new { message = rawSecret },
            schema);
        contract.IsValid.Should().BeTrue(contract.Error);

        var rogueRawResult = AgentToolExecutionResult.From(new { message = rawSecret });
        var rogueRawDecision = AgentToolRuntimeOutputGate.Validate(tool, rogueRawResult);
        rogueRawDecision.IsValid.Should().BeFalse();

        var falselyBoundEnvelope = new
        {
            contractVersion = AgentToolDurableOutputContractV1.ContractVersion,
            providerType = tool.ProviderType.ToString(),
            toolCode = tool.ToolCode,
            targetType = tool.TargetType.ToString(),
            targetName = tool.TargetName,
            resultHash = new string('0', 64),
            rawUtf8ByteCount = contract.Utf8ByteCount,
            safeSummary = AgentToolDurableOutputContractV1.SafeSummary,
            persistedSummaryUtf8ByteCount = Encoding.UTF8.GetByteCount(AgentToolDurableOutputContractV1.SafeSummary),
            summaryTruncated = false,
            rawPayloadPersisted = false
        };
        var falseHashResult = AgentToolExecutionResult.FromValidatedProviderOutput(
            tool,
            contract,
            falselyBoundEnvelope);
        var falseHashDecision = AgentToolRuntimeOutputGate.Validate(tool, falseHashResult);
        falseHashDecision.IsValid.Should().BeFalse();

        var validResult = AgentToolExecutionResult.FromValidatedProviderOutput(
            tool,
            contract,
            AgentToolDurableOutputBuilder.BuildProviderEnvelope(tool, contract.CanonicalJson!));
        var validDecision = AgentToolRuntimeOutputGate.Validate(tool, validResult);
        validDecision.IsValid.Should().BeTrue(validDecision.Error);

        var failedSurfaces = string.Join('\n', rogueRawDecision.Error, falseHashDecision.Error);
        failedSurfaces.Should().Be(
            "Provider durable output envelope is invalid or is not bound to the validated output snapshot.\n" +
            "Provider durable output envelope is invalid or is not bound to the validated output snapshot.");
        failedSurfaces.Should().NotContain("Bearer");
        failedSurfaces.Should().NotContain("private");
        failedSurfaces.Should().NotContain("SELECT");
        failedSurfaces.Should().NotContain("PRIVATE KEY");
    }

    [Fact]
    public void AgentToolRuntimeOutputGate_ShouldRequireExactSameSnapshotForBuiltInOutput()
    {
        const string schema =
            """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"],"additionalProperties":false}""";
        var tool = CreateTool("builtin_runtime_gate_fixture", outputSchemaJson: schema);
        var valid = AgentToolExecutionResult.From(new { ok = true });
        var validDecision = AgentToolRuntimeOutputGate.Validate(tool, valid);

        var contract = ToolOutputSchemaValidator.ValidateAndCanonicalize(new { ok = true }, schema);
        var secretDurable = ToolOutputSchemaValidator.CanonicalizeForPersistence(new
        {
            ok = true,
            secret = "Bearer abc123 C:\\private\\provider.sql SELECT * FROM payroll"
        });
        var split = new AgentToolExecutionResult(
            AgentToolOutputSnapshot.FromValidated(contract),
            AgentToolOutputSnapshot.FromValidated(secretDurable));
        var splitDecision = AgentToolRuntimeOutputGate.Validate(tool, split);

        validDecision.IsValid.Should().BeTrue(validDecision.Error);
        splitDecision.IsValid.Should().BeFalse();
        splitDecision.Error.Should().Be(
            "Runtime durable output is invalid or is not bound to the validated output snapshot.");
        splitDecision.Error.Should().NotContain("Bearer");
        splitDecision.Error.Should().NotContain("private");
        splitDecision.Error.Should().NotContain("SELECT");
    }

    [Fact]
    public void ToolExecutionSanitizerUtf8Metadata_ShouldTruncateOnlyOnRuneBoundary()
    {
        var result = ToolExecutionRecordSanitizer.SanitizeUtf8WithMetadata("测🔧A", 7);

        result.Value.Should().Be("测🔧");
        Encoding.UTF8.GetByteCount(result.Value!).Should().Be(7);
        result.SanitizedUtf8ByteCount.Should().Be(8);
        result.IsTruncated.Should().BeTrue();
    }


    [Fact]
    public void ToolExecutionSanitizer_ShouldRemoveProductUnsafeRuntimeDetails()
    {
        const string unsafeText = "<think>private reasoning</think> Bearer abc.def https://internal.example DB1.DBX0.0\n   at Company.Runtime.Run() in /Users/operator/app.cs:line 42";

        var result = ToolExecutionRecordSanitizer.Sanitize(unsafeText, 2_000);

        foreach (var marker in new[]
                 {
                     "[redacted-model-reasoning]", "Bearer ******", "[redacted-endpoint]",
                     "[redacted-plc-address]", "[redacted-exception-frame]"
                 })
        {
            result.Should().Contain(marker);
        }

        foreach (var unsafeFragment in new[] { "private reasoning", "internal.example", "/Users/operator" })
        {
            result.Should().NotContain(unsafeFragment);
        }
    }
    [Fact]
    public void ToolInputSchemaValidator_ShouldValidateNestedObjectsAndArrayItems()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "filters": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "field": { "type": "string", "enum": ["deviceCode"] },
                      "value": { "type": "string" }
                    },
                    "required": ["field", "value"]
                  }
                }
              },
              "required": ["filters"]
            }
            """;

        ToolInputSchemaValidator.ValidateAndParse(
                """{"filters":[{"field":"deviceCode","value":"DEV-001"}]}""",
                schema)
            .IsValid.Should().BeTrue();

        ToolInputSchemaValidator.ValidateAndParse("""{"filters":[{"field":"deviceCode"}]}""", schema)
            .Should().BeEquivalentTo(ToolInputValidationResult.Failure("Tool input is missing required field 'filters[0].value'."));
        ToolInputSchemaValidator.ValidateAndParse("""{"filters":[{"field":"recipeId","value":"R-1"}]}""", schema)
            .Should().BeEquivalentTo(ToolInputValidationResult.Failure("Tool input field '$.filters[0].field' is not one of the allowed values."));
    }

    [Fact]
    public void ToolInputSchemaValidator_ShouldRejectOptionalNullAndUseExactNumericInvocationTypes()
    {
        const string schema =
            """{"type":"object","properties":{"count":{"type":"integer"},"name":{"type":"string"},"ratio":{"type":"number"}},"additionalProperties":false}""";

        ToolInputSchemaValidator.ValidateAndParse("""{"name":null}""", schema).IsValid.Should().BeFalse();
        ToolInputSchemaValidator.ValidateAndParse("""{"count":null}""", schema).IsValid.Should().BeFalse();
        ToolInputSchemaValidator.ValidateAndParse("""{"ratio":null}""", schema).IsValid.Should().BeFalse();
        ToolInputSchemaContractV1.Validate(
                """{"type":"object","properties":{"value":{"type":"null"}},"additionalProperties":false}""")
            .IsValid.Should().BeFalse("the strict P0 tool schema domain does not support nullable values");
        ToolOutputSchemaContractV1.Validate(
                """{"type":"object","properties":{"value":{"type":"null"}},"additionalProperties":false}""")
            .IsValid.Should().BeFalse("input and output share one non-null strict type engine");

        var valid = ToolInputSchemaValidator.ValidateAndParse(
            """{"count":9223372036854775807,"ratio":123.456}""",
            schema);
        valid.IsValid.Should().BeTrue(valid.Error);
        valid.Arguments["count"].Should().BeOfType<long>().Which.Should().Be(long.MaxValue);
        valid.Arguments["ratio"].Should().BeOfType<decimal>().Which.Should().Be(123.456m);

        ToolInputValidationResult? integerOverflow = null;
        ToolInputValidationResult? numberOverflow = null;
        ToolInputValidationResult? precisionLoss = null;
        Action validate = () =>
        {
            integerOverflow = ToolInputSchemaValidator.ValidateAndParse(
                """{"count":9223372036854775808}""",
                schema);
            numberOverflow = ToolInputSchemaValidator.ValidateAndParse(
                """{"ratio":1e100}""",
                schema);
            precisionLoss = ToolInputSchemaValidator.ValidateAndParse(
                """{"ratio":0.1234567890123456789012345678901}""",
                schema);
        };

        validate.Should().NotThrow();
        integerOverflow!.IsValid.Should().BeFalse();
        numberOverflow!.IsValid.Should().BeFalse();
        precisionLoss!.IsValid.Should().BeFalse();
        ToolInputSchemaContractV1.Validate(
                """{"type":"object","properties":{"ratio":{"type":"number","enum":[1e100]}}}""")
            .IsValid.Should().BeFalse("schema enums and runtime values share one exact numeric domain");
    }

    [Fact]
    public void ToolSchemaContract_ShouldRejectNamesOrEnumsThatPlannerCannotProjectExactly()
    {
        static string SchemaWithProperty(string name)
        {
            return JsonSerializer.Serialize(new
            {
                type = "object",
                properties = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [name] = new { type = "string" }
                },
                additionalProperties = false
            });
        }

        foreach (var invalidName in new[]
                 {
                     string.Empty,
                     "   ",
                     "contains space",
                     "control\u0001name",
                     new string('n', ToolInputSchemaContractV1.MaxPropertyNameCharacters + 1)
                 })
        {
            ToolInputSchemaContractV1.Validate(SchemaWithProperty(invalidName)).IsValid
                .Should().BeFalse($"property name '{JsonSerializer.Serialize(invalidName)}' must not be rewritten by the catalog");
        }

        ToolInputSchemaContractV1.Validate(SchemaWithProperty(
                new string('n', ToolInputSchemaContractV1.MaxPropertyNameCharacters)))
            .IsValid.Should().BeTrue();

        var exactEnum = new string('e', ToolInputSchemaContractV1.MaxEnumDisplayCharacters);
        var overEnum = exactEnum + "e";
        static string SchemaWithEnum(string value) => JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                mode = new
                {
                    type = "string",
                    @enum = new[] { value }
                }
            },
            additionalProperties = false
        });

        ToolInputSchemaContractV1.Validate(SchemaWithEnum(exactEnum)).IsValid.Should().BeTrue();
        ToolInputSchemaContractV1.Validate(SchemaWithEnum(overEnum)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PlannerToolCatalog_ShouldFailClosedInsteadOfTruncatingInputSchema()
    {
        var oversizedSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            description = new string('x', PlannerToolCatalog.MaxInputSchemaUtf8Bytes),
            properties = new { },
            additionalProperties = false
        });
        var oversized = CreatePlanToolGuard(CreateGuard(CreateTool(
            "oversized_schema_tool",
            inputSchemaJson: oversizedSchema)));

        var oversizedResult = await oversized.GetAvailableToolCatalogAsync(UserId, CancellationToken.None);

        oversizedResult.IsSuccess.Should().BeFalse();
        oversizedResult.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);

        var properties = Enumerable.Range(0, 25).ToDictionary(
            index => $"field{index:D2}",
            _ => (object)new { type = "string" },
            StringComparer.Ordinal);
        var unrepresentableSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties,
            additionalProperties = false
        });
        var unrepresentable = CreatePlanToolGuard(CreateGuard(CreateTool(
            "unrepresentable_schema_tool",
            inputSchemaJson: unrepresentableSchema)));

        var unrepresentableResult = await unrepresentable.GetAvailableToolCatalogAsync(
            UserId,
            CancellationToken.None);

        unrepresentableResult.IsSuccess.Should().BeFalse();
        unrepresentableResult.Errors!.OfType<ApiProblemDescriptor>().Single().Code
            .Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);
    }
    [Fact]
    public void WorkspaceConfigHealth_ShouldDetectMismatchWithoutExposingPath()
    {
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "worker health",
            "worker health",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var queueItem = new AgentTaskRunQueueItem(task.Id, AgentTaskRunTriggerType.Manual, task.UserId, now);
        var heartbeat = new AgentWorkerHeartbeat("worker-1", "data-worker", now, "worker-hash", "1.0.0");
        heartbeat.MarkSeen(now, "data-worker", "worker-hash", "1.0.0", queueItem.Id, queueItem.TaskId);

        var status = AgentWorkerStatusCalculator.Build([queueItem], [heartbeat], "api-hash", now);

        status.StatusCode.Should().Be(AppProblemCodes.AgentWorkerWorkspaceMismatch);
        status.WorkspaceConsistent.Should().BeFalse();
        status.HttpApiWorkspaceRootHash.Should().Be("api-hash");
        status.HttpApiWorkspaceRootHash.Should().NotContain(":\\");
        status.Workers.Should().ContainSingle()
            .Which.WorkspaceMatchesHttpApi.Should().BeFalse();
    }
    [Fact]
    public void AgentToolExecutorResolver_ShouldResolveBuiltInCloudReadonlyAndMcpExecutors()
    {
        var builtInExecutor = new TestAgentToolExecutor(tool => tool.ProviderType == ToolProviderType.BuiltIn);
        var mcpExecutor = new TestAgentToolExecutor(tool => tool.ProviderType == ToolProviderType.Mcp);
        var resolver = new AgentToolExecutorResolver([builtInExecutor, mcpExecutor]);
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "resolve executor",
            "resolve executor",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var step = task.AddStep(
            "read upload",
            "read upload",
            AgentStepType.FileRead,
            "read_uploaded_file",
            false,
            now);

        resolver.Resolve(CreateTool("read_uploaded_file"), step).Should().BeSameAs(builtInExecutor);
        resolver.Resolve(CreateTool(
            "mcp_runtime_mcp_read",
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp"), step).Should().BeSameAs(mcpExecutor);
    }
    [Fact]
    public async Task MockMcpAgentToolExecutor_ShouldReturnMockMarkers_AndResultHash()
    {
        var now = DateTimeOffset.UtcNow;
        var registration = BuiltInToolRegistrations.AgentRuntimeTools.Single(
            item => item.ToolCode == "mock_mcp_kpi_formula_lookup");
        var tool = CreateTool(
            "mock_mcp_kpi_formula_lookup",
            ToolProviderType.MockMcp,
            targetName: "MockMcpProvider",
            inputSchemaJson: registration.InputSchemaJson);
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "Mock MCP KPI",
            "Mock MCP KPI",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            "{}",
            now);
        var step = task.AddStep(
            "Lookup KPI formula",
            "Lookup mock KPI formula.",
            AgentStepType.Analysis,
            tool.ToolCode,
            false,
            now,
            """{"domain":"Production"}""");
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        var plan = new AgentTaskPlanDocument(
            1,
            "agent_planner",
            "Mock MCP KPI",
            AgentTaskType.ReportGeneration.ToString(),
            AgentTaskRiskLevel.Low.ToString(),
            [],
            [],
            null,
            [],
            new AgentTaskPlanRuntimeSettingsDocument(30, 12000),
            ToolCatalogVersion: BuiltInToolRegistrations.CurrentCatalogVersion,
            VisibleToolCount: 1,
            ToolRiskSummary: new Dictionary<string, int> { [AiToolRiskLevel.Low.ToString()] = 1 },
            MockMcpOnly: false);

        var executor = new MockMcpAgentToolExecutor();
        var result = await executor.ExecuteAsync(new AgentToolExecutionContext(
            task,
            workspace,
            plan,
            step,
            new AgentTaskRunState(),
            tool,
            CancellationToken.None));

        var json = result.ContractOutput.CanonicalJson;
        json.Should().Contain("\"isMock\":true");
        json.Should().Contain("\"providerKind\":\"MockMcp\"");
        json.Should().Contain("\"toolRunId\"");
        json.Should().Contain("\"toolCatalogVersion\"");
        json.Should().Contain("\"resultHash\"");
        json.Should().Contain("capacityUtilization");

        var invalidStep = task.AddStep(
            "Lookup KPI formula with extra input",
            "Reject undeclared Mock MCP input.",
            AgentStepType.Analysis,
            tool.ToolCode,
            false,
            now,
            """{"domain":"Production","unexpected":true}""");
        Func<Task> executeInvalid = async () => await executor.ExecuteAsync(new AgentToolExecutionContext(
            task,
            workspace,
            plan,
            invalidStep,
            new AgentTaskRunState(),
            tool,
            CancellationToken.None));

        var failure = await executeInvalid.Should().ThrowAsync<AgentToolExecutionException>();
        failure.Which.Code.Should().Be(AppProblemCodes.AgentPlanSchemaInvalid);

        var outputDriftTool = CreateTool(
            "mock_mcp_kpi_formula_lookup",
            ToolProviderType.MockMcp,
            targetName: "MockMcpProvider",
            inputSchemaJson: registration.InputSchemaJson,
            outputSchemaJson:
            """{"type":"object","properties":{},"additionalProperties":false}""");
        var outputDrift = await ((Func<Task>)(async () => await executor.ExecuteAsync(
                new AgentToolExecutionContext(
                    task,
                    workspace,
                    plan,
                    step,
                    new AgentTaskRunState(),
                    outputDriftTool,
                    CancellationToken.None))))
            .Should().ThrowAsync<AgentToolExecutionException>();
        outputDrift.Which.Code.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);
    }

    [Fact]
    public async Task McpAgentToolExecutor_ShouldFailClosedOnOutputIdentityOrValueDrift_AndWithholdRawOutput()
    {
        const string toolCode = "mcp__runtime_mcp__query_status";
        const string outputSchema =
            """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"],"additionalProperties":false}""";
        var registration = CreateTool(
            toolCode,
            ToolProviderType.Mcp,
            ToolRegistrationTargetType.McpServer,
            "runtime-mcp",
            outputSchemaJson: outputSchema);
        var now = DateTimeOffset.UtcNow;
        var task = new AgentTask(
            SessionId.New(),
            UserId,
            "MCP output contract fixture",
            "MCP output contract fixture",
            AgentTaskType.ReportGeneration,
            AgentTaskRiskLevel.Low,
            null,
            AgentPlanV2TestData.CreateSingleStep(toolCode, executable: false),
            now);
        task.AddStep(
            "Query MCP status",
            "Query MCP status.",
            AgentStepType.Analysis,
            toolCode,
            requiresApproval: false,
            now);
        var workspace = new ArtifactWorkspace(
            task.Id,
            $"ws_{Guid.NewGuid():N}",
            @"C:\aicopilot-workspaces\test",
            "/api/aigateway/workspaces/test",
            now);
        var plan = JsonSerializer.Deserialize<AgentTaskPlanDocument>(task.PlanJson, AgentRuntimeJson.Options)!;
        using var services = new ServiceCollection().BuildServiceProvider();

        AiToolDefinition BuildTool(
            string? runtimeOutputSchema,
            Func<object?> output,
            Action? onInvoke = null,
            string? runtimeInputSchema = null)
        {
            return new AiToolDefinition
            {
                Name = toolCode,
                ToolName = "query_status",
                Kind = AiToolCallKind.Mcp,
                TargetType = AiToolTargetType.McpServer,
                TargetName = "runtime-mcp",
                ServerName = "runtime-mcp",
                ExternalSystemType = AiToolExternalSystemType.CloudReadOnly,
                CapabilityKind = AiToolCapabilityKind.ReadOnlyQuery,
                RiskLevel = AiToolRiskLevel.Low,
                ReadOnlyDeclared = true,
                McpReadOnlyHint = true,
                McpDestructiveHint = false,
                JsonSchema = JsonDocument.Parse(runtimeInputSchema ?? registration.InputSchemaJson).RootElement.Clone(),
                ReturnJsonSchema = runtimeOutputSchema is null
                    ? null
                    : JsonDocument.Parse(runtimeOutputSchema).RootElement.Clone(),
                InvokeAsync = (_, _) =>
                {
                    onInvoke?.Invoke();
                    return ValueTask.FromResult(output());
                }
            };
        }

        AgentToolExecutionContext Context() => new(
            task,
            workspace,
            plan,
            task.Steps.Single(),
            new AgentTaskRunState(),
            registration,
            CancellationToken.None);

        var invokedOnInputDrift = false;
        var inputDriftExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool(
                outputSchema,
                () => new { message = "must not execute" },
                () => invokedOnInputDrift = true,
                """{"type":"object","properties":{"other":{"type":"string"}},"required":["other"],"additionalProperties":false}""")),
            services);
        var inputDrift = await ((Func<Task>)(async () => await inputDriftExecutor.ExecuteAsync(Context())))
            .Should().ThrowAsync<AgentToolExecutionException>();
        inputDrift.Which.Code.Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);
        invokedOnInputDrift.Should().BeFalse("runtime input identity is checked before provider invocation");

        var invokedOnDrift = false;
        var driftExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool(
                """{"type":"object","properties":{"other":{"type":"string"}},"required":["other"],"additionalProperties":false}""",
                () => new { other = "unexpected" },
                () => invokedOnDrift = true)),
            services);
        var drift = await ((Func<Task>)(async () => await driftExecutor.ExecuteAsync(Context())))
            .Should().ThrowAsync<AgentToolExecutionException>();
        drift.Which.Code.Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);
        invokedOnDrift.Should().BeFalse("runtime output identity is checked before provider invocation");

        var malformedExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool("{}", () => new { message = "unexpected" })),
            services);
        var malformed = await ((Func<Task>)(async () => await malformedExecutor.ExecuteAsync(Context())))
            .Should().ThrowAsync<AgentToolExecutionException>();
        malformed.Which.Code.Should().Be(AppProblemCodes.PlannerToolSchemaUnsupported);

        var mismatchExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool(outputSchema, () => new { wrong = true })),
            services);
        var mismatch = await ((Func<Task>)(async () => await mismatchExecutor.ExecuteAsync(Context())))
            .Should().ThrowAsync<AgentToolExecutionException>();
        mismatch.Which.Code.Should().Be(AppProblemCodes.ToolOutputSchemaInvalid);

        var validExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool(outputSchema, () => new
            {
                message = "token=raw-secret; C:\\private\\a.txt; SELECT * FROM payroll; Bearer abc123"
            })),
            services);
        var valid = await validExecutor.ExecuteAsync(Context());
        var contractJson = valid.ContractOutput.CanonicalJson;
        var durableJson = valid.DurableOutput.CanonicalJson;

        contractJson.Should().Contain("raw-secret");
        durableJson.Should().Contain(AgentToolDurableOutputContractV1.ContractVersion);
        durableJson.Should().Contain("\"rawPayloadPersisted\":false");
        durableJson.Should().NotContain("raw-secret");
        durableJson.Should().NotContain("C:\\\\private");
        durableJson.Should().NotContain("SELECT * FROM payroll");
        durableJson.Should().NotContain("Bearer abc123");

        var statefulOutput = new StatefulProviderOutput();
        var statefulExecutor = new McpAgentToolExecutor(
            new StubAgentPluginCatalog(BuildTool(outputSchema, () => statefulOutput)),
            services);
        var stateful = await statefulExecutor.ExecuteAsync(Context());
        var runtimeValidation = ToolOutputSchemaValidator.ValidateCanonicalJson(
            stateful.ContractOutput.CanonicalJson,
            outputSchema);
        var runtimeGate = AgentToolRuntimeOutputGate.Validate(registration, stateful);
        using var statefulDurableDocument = JsonDocument.Parse(stateful.DurableOutput.CanonicalJson);
        var persistedHash = statefulDurableDocument.RootElement
            .GetProperty("resultHash")
            .GetString();
        var expectedHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(stateful.ContractOutput.CanonicalJson)))
            .ToLowerInvariant();

        runtimeValidation.IsValid.Should().BeTrue(runtimeValidation.Error);
        runtimeGate.IsValid.Should().BeTrue(runtimeGate.Error);
        statefulOutput.ReadCount.Should().Be(
            1,
            "provider objects must be serialized once; runtime revalidates the immutable canonical snapshot");
        stateful.ContractOutput.CanonicalJson.Should().Contain("first-read");
        stateful.ContractOutput.CanonicalJson.Should().NotContain("second-read");
        persistedHash.Should().Be(expectedHash);
    }

    private static object BuildDeclaredOutputSample(string toolCode)
    {
        return toolCode switch
        {
            "read_uploaded_file" => new
            {
                status = "completed",
                resultType = "upload-summary",
                itemCount = 1
            },
            "parse_csv_json" or "parse_table_file" => new
            {
                status = "completed",
                resultType = "table-summary",
                itemCount = 1,
                rowCount = 2
            },
            "rag_search" => new
            {
                status = "completed",
                resultType = "rag-summary",
                itemCount = 1,
                lowConfidence = false
            },
            "query_business_database_readonly" or "summarize_business_query_result" => new
            {
                status = "completed",
                resultType = "business-query-summary",
                sourceMode = "SimulationBusiness",
                isSimulation = true,
                rowCount = 2,
                isTruncated = false,
                resultHash = "business-hash"
            },
            "join_evidence" => new
            {
                status = "completed",
                resultType = "evidence-join",
                joinPolicy = "AllRequired",
                requiredEvidenceCount = 1,
                optionalEvidenceCount = 0,
                missingOptionalCount = 0
            },
            "assess_cloud_health" => new
            {
                status = "completed",
                resultType = "cloud-health-assessment",
                algorithmVersion = "cloud-health-assessment:v1",
                assessmentType = "CurrentDeviceRuntimeHealth",
                truthClass = "DerivedFact",
                healthScore = 100,
                healthLevel = "Stable",
                safeSummary = "Current device runtime health is stable.",
                findings = Array.Empty<string>(),
                confidence = 1.0,
                missingRate = 0.0,
                inputEvidenceCount = 1,
                evidenceSetDigest = "evidence-set-hash",
                sourceAsOfUtc = "2026-07-23T00:00:00.0000000+00:00",
                sourceMode = "Simulation",
                isSimulation = true,
                rowCount = 1,
                isTruncated = false,
                typedMetrics = new
                {
                    futureHeartbeatCount = 0,
                    missingHeartbeatCount = 0,
                    reportedIssueStatusCount = 0,
                    staleHeartbeatCount = 0,
                    totalDeviceCount = 1,
                    unknownRuntimeStatusCount = 0
                }
            },
            "agent_reasoning" => new
            {
                status = "completed",
                resultType = "agent-reasoning",
                childRunId = "child-run-001",
                completionStatus = "Completed",
                truthClass = "LlmInference",
                safeSummary = "Evidence-only reasoning completed.",
                findings = Array.Empty<string>(),
                citationRefs = new[] { "evidence-001" },
                evidenceWarnings = Array.Empty<string>(),
                conflictStatus = "None",
                confidence = 1.0,
                noFurtherToolCalls = true,
                recoveryUsed = false,
                modelCalls = 1
            },
            "generate_business_chart" or "generate_chart_data" => BuildArtifactSample("chart"),
            "generate_markdown_report" => BuildArtifactSample("markdown"),
            "generate_html_report" => BuildArtifactSample("html"),
            "generate_pdf" => BuildArtifactSample("pdf"),
            "generate_pptx" => BuildArtifactSample("pptx"),
            "generate_xlsx" => BuildArtifactSample("xlsx"),
            "finalize_artifacts" => new
            {
                status = "finalized",
                resultType = "finalization-checkpoint"
            },
            "mock_mcp_health_check" => BuildMockSample(toolCode, new
            {
                health = "Healthy",
                mockOnly = true,
                externalEndpointEnabled = false,
                checkedAt = DateTimeOffset.UtcNow
            }),
            "mock_mcp_kpi_formula_lookup" => BuildMockSample(toolCode, new
            {
                domain = "Production",
                formula = "capacityUtilization = actualOutput / plannedCapacity",
                source = "Mock MCP KPI formula catalog",
                isSimulationSupport = true
            }),
            "mock_mcp_artifact_quality_check" => BuildMockSample(toolCode, new
            {
                artifactType = "markdown",
                passed = true,
                checks = new
                {
                    simulationMarker = true,
                    queryHash = true,
                    noRealExternalSideEffect = true
                }
            }),
            "mock_mcp_external_ticket_preview" => BuildMockSample(toolCode, new
            {
                title = "preview",
                summary = "safe",
                sideEffectExecuted = false,
                previewOnly = true,
                externalSystem = "mock-ticket-system"
            }),
            _ => throw new InvalidOperationException($"No declared output sample for {toolCode}.")
        };

        static object BuildArtifactSample(string artifactType) => new
        {
            status = "completed",
            resultType = "artifact",
            artifactType,
            artifactId = Guid.NewGuid()
        };

        static object BuildMockSample(string code, object payload) => new
        {
            isMock = true,
            providerKind = "MockMcp",
            toolCode = code,
            toolRunId = Guid.NewGuid(),
            toolCatalogVersion = BuiltInToolRegistrations.CurrentCatalogVersion,
            schemaVersion = BuiltInToolRegistrations.CurrentSchemaVersion,
            status = "Succeeded",
            durationMs = 1L,
            resultHash = "mock-hash",
            payload
        };
    }

    private sealed class StatefulProviderOutput
    {
        private int readCount;

        internal int ReadCount => readCount;

        public string Message => Interlocked.Increment(ref readCount) == 1
            ? "first-read"
            : "second-read";
    }
}
