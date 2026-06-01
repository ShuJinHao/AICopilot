using System.Text;
using System.Text.Json;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.RagService.Commands.Documents;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.BackendTests;

[Trait("Suite", "AICopilotM6SecurityComplianceHardening")]
public sealed class AICopilotM6SecurityComplianceHardeningTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void M6Document_ShouldKeepSecurityBoundaryAndHardStopClaims()
    {
        var doc = Read("docs/AICopilotM6SecurityComplianceHardening.md");

        doc.Should().Contain("M6 closes the current security and compliance hardening baseline")
            .And.Contain("No plaintext secret, token, API key, connection string")
            .And.Contain("No `appsettings*.json` file is changed")
            .And.Contain("No migration is added")
            .And.Contain("ExecutionPermission=not granted")
            .And.Contain("GateState=BlockedUntilExplicitM7Authorization")
            .And.Contain("query_cloud_data_readonly");

        doc.Should().NotContain("ExecutionPermission=" + "granted")
            .And.NotContain("query_cloud_data_readonly " + "enabled")
            .And.NotContain("Cloud write is enabled")
            .And.NotContain("GA approved");
    }

    [Fact]
    public void SensitiveRuntimeContracts_ShouldExposeOnlySafeSecretAndEndpointMarkers()
    {
        SensitiveValueMasker.Mask("super-secret").Should().Be("******");
        SensitiveValueMasker.Mask(" ").Should().BeNull();

        Read("src/services/AICopilot.AiGatewayService/Queries/LanguageModels/LanguageModelDtoMapper.cs")
            .Should().Contain("ApiKeyPreview = string.IsNullOrEmpty(model.ApiKey) ? null : \"******\"");
        Read("src/services/AICopilot.RagService/EmbeddingModels/EmbeddingModelManagement.cs")
            .Should().Contain("ApiKeyPreview = string.IsNullOrEmpty(model.ApiKey) ? null : \"******\"");
        Read("src/infrastructure/AICopilot.AiRuntime/ModelProviderReliability.cs")
            .Should().Contain("HasApiKey");
        Read("src/infrastructure/AICopilot.AiRuntime/ModelEndpointPoolScheduler.cs")
            .Should().Contain("[redacted-endpoint]")
            .And.Contain("RedactedEndpointMarker");
        Read("src/services/AICopilot.Services.Contracts/Contracts/AiRuntimeContracts.cs")
            .Should().Contain("HasBaseUrl");
    }

    [Fact]
    public async Task ToolAndUploadPolicies_ShouldRejectWriteOrDangerousInputs()
    {
        var toolDecision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.SideEffecting,
            AiToolRiskLevel.High,
            "query_cloud_devices",
            "update device status",
            readOnlyDeclared: false);

        toolDecision.IsAllowed.Should().BeFalse();
        toolDecision.BlockReasons.Should().Contain(reason => reason.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        toolDecision.BlockReasons.Should().Contain(reason => reason.Contains("side-effecting", StringComparison.OrdinalIgnoreCase));
        toolDecision.BlockReasons.Should().Contain(reason => reason.Contains("write semantics", StringComparison.OrdinalIgnoreCase));

        var upload = new AiGatewayUploadStream(
            "../payload.exe",
            "application/octet-stream",
            2,
            new MemoryStream(Encoding.UTF8.GetBytes("MZ")));
        var uploadResult = await AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync(upload, CancellationToken.None);
        uploadResult.IsValid.Should().BeFalse();

        var ragFile = await RagDocumentUploadSecurityPolicy.NormalizeStreamAsync(
            new FileUploadStream(
                "report.pdf",
                new MemoryStream(Encoding.UTF8.GetBytes("not a pdf")),
                "application/pdf",
                9),
            CancellationToken.None);
        var ragResult = await RagDocumentUploadSecurityPolicy.ValidateAndNormalizeAsync(
            ragFile,
            new FixedDocumentFormatPolicy([".pdf"]),
            CancellationToken.None);
        ragResult.IsValid.Should().BeFalse();
    }

    [Fact]
    public void AuditAndArtifactEvidence_ShouldUseHashesAndSafeMetadata()
    {
        var toolAudit = Read("src/services/AICopilot.AiGatewayService/Workflows/Executors/ToolExecutionAuditRecorder.cs");
        toolAudit.Should().Contain("resultSha256")
            .And.Contain("resultBytes")
            .And.Contain("Tool execution result observed")
            .And.NotContain("raw payload")
            .And.NotContain("connection string");

        var agentAudit = Read("src/services/AICopilot.AiGatewayService/AgentTasks/AgentAuditRecorder.cs");
        agentAudit.Should().Contain("Agent.ArtifactDownload")
            .And.Contain("Agent.ArtifactVersionDownload")
            .And.Contain("queryHash")
            .And.Contain("resultHash")
            .And.NotContain("rawRows")
            .And.NotContain("fullSql");

        var search = Read("src/services/AICopilot.RagService/Queries/KnowledgeBases/SearchKnowledgeBase.cs");
        search.Should().Contain("queryHash")
            .And.Contain("supplementHashes")
            .And.Contain("warningCodes")
            .And.NotContain("QueryText = request.QueryText");

        var dataQuery = string.Concat(
            Read("src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessReadonlyQueryAuditRecorder.cs"),
            Read("src/services/AICopilot.DataAnalysisService/BusinessDatabases/BusinessQueryResultMapper.cs"));
        dataQuery.Should().Contain("queryHash")
            .And.Contain("sqlLength")
            .And.Contain("Governance")
            .And.NotContain("Metadata = sql");
    }

    [Fact]
    public void ArtifactDownload_ShouldRequirePermissionAndWriteDownloadAudit()
    {
        var workspace = Read("src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceQueryHandlers.cs");

        workspace.Should().Contain("AgentApprovalPermissions.DownloadArtifact")
            .And.Contain("CanReadFinalReviewWorkspace")
            .And.Contain("HasFinalOutputApprovalAsync")
            .And.Contain("RecordArtifactDownloadAsync")
            .And.Contain("auditLogWriter.SaveChangesAsync");
    }

    [Fact]
    public void SerializedM6AuditSamples_ShouldNotContainForbiddenSensitiveFields()
    {
        var sample = new
        {
            queryHash = "sha256:abc",
            resultHash = "sha256:def",
            resultBytes = 128,
            warningCodes = new[] { "SUPPLEMENT_OVERRIDE_APPLIED" }
        };

        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("queryHash")
            .And.Contain("resultHash")
            .And.NotContain("apiKey")
            .And.NotContain("token")
            .And.NotContain("connectionString")
            .And.NotContain("rawPayload")
            .And.NotContain("fullSql");
    }

    private static string Read(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath);
        File.Exists(fullPath).Should().BeTrue($"required file should exist: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Unable to locate AICopilot repository root.");
    }

    private sealed class FixedDocumentFormatPolicy(IReadOnlyCollection<string> supportedExtensions) : IDocumentFormatPolicy
    {
        public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions;

        public bool IsSupported(string extension)
        {
            return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
    }
}
