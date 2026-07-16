using System.Reflection;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.IdentityService.Authorization;
using AICopilot.Services.CrossCutting.Serialization;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.UnitTests;

public sealed class SecurityPolicyUnitTests
{
    [Fact]
    public void PermissionCatalog_ShouldNotExposeLegacyTrialPilotOrOperationsPermissions()
    {
        var catalog = new PermissionCatalog();
        var permissionCodes = catalog.GetAll()
            .Select(permission => permission.Code)
            .ToArray();
        var legacyPermissions = new[]
        {
            "AiGateway.TrialOperations.Read",
            "AiGateway.TrialOperations.Manage",
            "AiGateway.TrialOperations.AuditView",
            "AiGateway.RunQueue.Read",
            "AiGateway.RunQueue.Manage",
            "AiGateway.WorkerStatus.Read",
            "PilotAuthorization.Submit",
            "PilotAuthorization.View",
            "PilotAuthorization.Review",
            "PilotAuthorization.ApprovePlanning",
            "PilotAuthorization.Reject",
            "PilotAuthorization.Expire",
            "PilotAuthorization.Audit"
        };

        permissionCodes.Should().NotIntersectWith(legacyPermissions);
        catalog.GetDefaultPermissions(IdentityRoleNames.User)
            .Should()
            .NotIntersectWith(legacyPermissions);
    }
    [Fact]
    public void JsonHelper_ShouldUseHtmlSafeEscaping()
    {
        var json = new { Value = "<script>alert(1)</script>" }.ToJson();

        json.Should().Contain("\\u003Cscript\\u003E");
        json.Should().NotContain("<script>");
    }
    [Fact]
    public void LanguageModel_ShouldRejectInvalidInput()
    {
        var parameters = new ModelParameters { MaxTokens = 1024, Temperature = 0.2f };

        var emptyName = () => new LanguageModel("OpenAI", " ", "https://example.test", null, parameters);
        emptyName.Should().Throw<ArgumentException>();

        var invalidUrl = () => new LanguageModel("OpenAI", "model", "not-a-url", null, parameters);
        invalidUrl.Should().Throw<ArgumentException>();

        var invalidParameters = () => new LanguageModel(
            "OpenAI",
            "model",
            "https://example.test",
            null,
            new ModelParameters { MaxTokens = 0, Temperature = 0.2f });
        invalidParameters.Should().Throw<ArgumentOutOfRangeException>();

        typeof(LanguageModel).GetProperty(nameof(LanguageModel.Id))!
            .SetMethod!.IsPrivate.Should().BeTrue();
    }
    [Fact]
    public void BusinessDatabase_ShouldRejectInvalidInput()
    {
        var emptyName = () => new BusinessDatabase(
            " ",
            "description",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql);
        emptyName.Should().Throw<ArgumentException>();

        var emptyConnection = () => new BusinessDatabase(
            "database",
            "description",
            " ",
            DbProviderType.PostgreSql);
        emptyConnection.Should().Throw<ArgumentException>();

        var invalidProvider = () => new BusinessDatabase(
            "database",
            "description",
            "Host=localhost;Database=test",
            (DbProviderType)999);
        invalidProvider.Should().Throw<ArgumentOutOfRangeException>();

        var enabledCloudWithoutVerifiedCredential = () => new BusinessDatabase(
            "cloud-readonly",
            "cloud readonly source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false,
            isEnabled: true);
        enabledCloudWithoutVerifiedCredential.Should().Throw<InvalidOperationException>()
            .WithMessage("*verified read-only credential*");

        var disabledCloudDraft = new BusinessDatabase(
            "cloud-readonly-draft",
            "cloud readonly draft source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false,
            isEnabled: false);
        disabledCloudDraft.IsEnabled.Should().BeFalse();
        disabledCloudDraft.ReadOnlyCredentialVerified.Should().BeFalse();
        disabledCloudDraft.ExternalSystemType.Should().Be(BusinessDataExternalSystemType.CloudReadOnly);

        var cloudDatabase = new BusinessDatabase(
            "cloud-readonly",
            "cloud readonly source",
            "Host=localhost;Database=test",
            DbProviderType.PostgreSql,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: true);

        var enablingCloudWithoutVerifiedCredential = () => cloudDatabase.UpdateSettings(
            isEnabled: true,
            isReadOnly: true,
            BusinessDataExternalSystemType.CloudReadOnly,
            readOnlyCredentialVerified: false);
        enablingCloudWithoutVerifiedCredential.Should().Throw<InvalidOperationException>()
            .WithMessage("*verified read-only credential*");

        typeof(BusinessDatabase).GetProperty(nameof(BusinessDatabase.Id))!
            .SetMethod!.IsPrivate.Should().BeTrue();
    }
    [Fact]
    public void EmbeddingModel_ShouldRejectInvalidInput()
    {
        var emptyName = () => new EmbeddingModel(
            " ",
            "OpenAI",
            "https://example.test",
            "text-embedding-3-small",
            1536,
            8192);
        emptyName.Should().Throw<ArgumentException>();

        var invalidUrl = () => new EmbeddingModel(
            "embedding",
            "OpenAI",
            "not-a-url",
            "text-embedding-3-small",
            1536,
            8192);
        invalidUrl.Should().Throw<ArgumentException>();

        var invalidDimensions = () => new EmbeddingModel(
            "embedding",
            "OpenAI",
            "https://example.test",
            "text-embedding-3-small",
            0,
            8192);
        invalidDimensions.Should().Throw<ArgumentOutOfRangeException>();

        var model = new EmbeddingModel(
            " embedding ",
            " OpenAI ",
            " https://example.test ",
            " text-embedding-3-small ",
            1536,
            8192,
            " key ",
            false);

        model.Name.Should().Be("embedding");
        model.Provider.Should().Be("OpenAI");
        model.BaseUrl.Should().Be("https://example.test");
        model.ModelName.Should().Be("text-embedding-3-small");
        model.ApiKey.Should().Be("key");
        model.IsEnabled.Should().BeFalse();
    }
    [Fact]
    public void ApprovalPolicy_ShouldRejectInvalidInput()
    {
        var emptyName = () => new ApprovalPolicy(
            " ",
            null,
            ApprovalTargetType.Plugin,
            "tool",
            [],
            true,
            false);
        emptyName.Should().Throw<ArgumentException>();

        var invalidTargetType = () => new ApprovalPolicy(
            "policy",
            null,
            (ApprovalTargetType)999,
            "tool",
            [],
            true,
            false);
        invalidTargetType.Should().Throw<ArgumentOutOfRangeException>();

        var emptyTarget = () => new ApprovalPolicy(
            "policy",
            null,
            ApprovalTargetType.Plugin,
            " ",
            [],
            true,
            false);
        emptyTarget.Should().Throw<ArgumentException>();

        var policy = new ApprovalPolicy(
            " policy ",
            " description ",
            ApprovalTargetType.Plugin,
            " target ",
            [" Echo ", "echo", " "],
            true,
            false);

        policy.Name.Should().Be("policy");
        policy.Description.Should().Be("description");
        policy.TargetName.Should().Be("target");
        policy.ToolNames.Should().Equal("Echo");
    }
    [Fact]
    public void McpServerInfo_SecurityMetadata_ShouldBeRequiredBeforeOptionalParameters()
    {
        var constructorParameters = typeof(McpServerInfo)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();
        var updateParameters = typeof(McpServerInfo)
            .GetMethod(nameof(McpServerInfo.Update), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters();

        AssertRequiredSecurityMetadata(constructorParameters);
        AssertRequiredSecurityMetadata(updateParameters);
    }
    [Fact]
    public void McpServerInfo_ShouldRejectInvalidInput()
    {
        var emptyName = () => new McpServerInfo(
            " ",
            "description",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        emptyName.Should().Throw<ArgumentException>();

        var invalidTransport = () => new McpServerInfo(
            "server",
            "description",
            (McpTransportType)999,
            "dotnet",
            "server.dll",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        invalidTransport.Should().Throw<ArgumentOutOfRangeException>();

        var invalidSseUrl = () => new McpServerInfo(
            "server",
            "description",
            McpTransportType.Sse,
            null,
            "not-a-url",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        invalidSseUrl.Should().Throw<ArgumentException>();

        var unsafeSseUrl = () => new McpServerInfo(
            "server",
            "description",
            McpTransportType.Sse,
            null,
            "http://127.0.0.1/sse",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        unsafeSseUrl.Should().Throw<ArgumentException>();

        var cloudServerWithoutReadOnlyCapability = () => new McpServerInfo(
            "cloud-server",
            "description",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.Diagnostics);
        cloudServerWithoutReadOnlyCapability.Should().Throw<ArgumentException>();

        var cloudTargetWithWrongLabel = () => new McpServerInfo(
            "production-cloud",
            "Cloud production endpoint",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            externalSystemType: AiToolExternalSystemType.NonCloud,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        cloudTargetWithWrongLabel.Should().Throw<ArgumentException>();

        var cloudEndpointWithWrongLabel = () => new McpServerInfo(
            "remote-runtime",
            "Remote runtime",
            McpTransportType.Sse,
            null,
            "https://api.cloud.example/sse",
            externalSystemType: AiToolExternalSystemType.NonCloud,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        cloudEndpointWithWrongLabel.Should().Throw<ArgumentException>();

        var opaqueDynamicWriteTarget = () => new McpServerInfo(
            "gateway-a17",
            "Opaque remote runtime",
            McpTransportType.Sse,
            null,
            "https://relay.example.test/mcp",
            externalSystemType: AiToolExternalSystemType.NonCloud,
            capabilityKind: AiToolCapabilityKind.SideEffecting,
            chatExposureMode: ChatExposureMode.Advisory,
            allowedTools:
            [
                new McpAllowedTool(
                    "deleteDevice",
                    AiToolExternalSystemType.NonCloud,
                    AiToolCapabilityKind.SideEffecting)
            ],
            isEnabled: true);
        opaqueDynamicWriteTarget.Should().Throw<ArgumentException>()
            .WithMessage("*cannot establish a verified non-Cloud identity*");

        var cloudToolWithoutDeclaration = () => new McpServerInfo(
            "cloud-server",
            "description",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            allowedTools: [new McpAllowedTool("queryDeviceLogs")],
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        cloudToolWithoutDeclaration.Should().Throw<ArgumentException>();

        var cloudToolWithWrongOverride = () => new McpServerInfo(
            "server",
            "description",
            McpTransportType.Stdio,
            "dotnet",
            "server.dll",
            allowedTools:
            [
                new McpAllowedTool(
                    "queryDeviceLogs",
                    AiToolExternalSystemType.CloudReadOnly,
                    AiToolCapabilityKind.Diagnostics,
                    ReadOnlyDeclared: true)
            ],
            externalSystemType: AiToolExternalSystemType.CloudReadOnly,
            capabilityKind: AiToolCapabilityKind.ReadOnlyQuery);
        cloudToolWithWrongOverride.Should().Throw<ArgumentException>();

        var server = new McpServerInfo(
            " server ",
            " description ",
            McpTransportType.Stdio,
            " dotnet ",
            " server.dll ",
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            ChatExposureMode.Advisory,
            [
                new McpAllowedTool(" QueryStatus ", ReadOnlyDeclared: true),
                new McpAllowedTool("queryStatus", ReadOnlyDeclared: true),
                new McpAllowedTool(" ")
            ]);

        server.Name.Should().Be("server");
        server.Description.Should().Be("description");
        server.Command.Should().Be("dotnet");
        server.Arguments.Should().Be("server.dll");
        server.AllowedTools.Select(tool => tool.ToolName).Should().Equal("QueryStatus");
    }

    private static void AssertRequiredSecurityMetadata(IReadOnlyList<ParameterInfo> parameters)
    {
        var externalSystemType = parameters.Single(parameter => parameter.Name == "externalSystemType");
        var capabilityKind = parameters.Single(parameter => parameter.Name == "capabilityKind");
        var firstOptionalPosition = parameters.First(parameter => parameter.HasDefaultValue).Position;

        externalSystemType.ParameterType.Should().Be<AiToolExternalSystemType>();
        externalSystemType.HasDefaultValue.Should().BeFalse();
        externalSystemType.Position.Should().BeLessThan(firstOptionalPosition);
        capabilityKind.ParameterType.Should().Be<AiToolCapabilityKind>();
        capabilityKind.HasDefaultValue.Should().BeFalse();
        capabilityKind.Position.Should().BeLessThan(firstOptionalPosition);
    }

    [Fact]
    public void KnowledgeBaseDocumentAndChunks_ShouldRejectInvalidInput()
    {
        var embeddingModelId = EmbeddingModelId.New();

        var emptyName = () => new KnowledgeBase(" ", "description", embeddingModelId);
        emptyName.Should().Throw<ArgumentException>();

        var emptyEmbeddingModelId = () => new KnowledgeBase("kb", "description", new EmbeddingModelId(Guid.Empty));
        emptyEmbeddingModelId.Should().Throw<ArgumentException>();

        var knowledgeBase = new KnowledgeBase(" kb ", " description ", embeddingModelId);
        knowledgeBase.Name.Should().Be("kb");
        knowledgeBase.Description.Should().Be("description");

        var emptyDocumentName = () => knowledgeBase.AddDocument(
            new DocumentId(1),
            " ",
            "path.txt",
            ".txt",
            "hash");
        emptyDocumentName.Should().Throw<ArgumentException>();

        var document = knowledgeBase.AddDocument(
            new DocumentId(1),
            " doc ",
            " path.txt ",
            " .txt ",
            " hash ");
        document.Name.Should().Be("doc");
        document.FilePath.Should().Be("path.txt");
        document.Extension.Should().Be(".txt");
        document.FileHash.Should().Be("hash");

        document.StartParsing();
        document.CompleteParsing();

        var negativeChunkIndex = () => document.AddChunk(-1, "content");
        negativeChunkIndex.Should().Throw<ArgumentOutOfRangeException>();

        var emptyChunkContent = () => document.AddChunk(0, " ");
        emptyChunkContent.Should().Throw<ArgumentException>();

        document.AddChunk(0, " chunk content ");
        document.Chunks.Should().ContainSingle();
        document.Chunks.Single().Content.Should().Be("chunk content");

        var emptyVectorId = () => document.MarkChunkAsEmbedded(0, " ");
        emptyVectorId.Should().Throw<ArgumentException>();

        document.StartEmbedding();
        document.Status.Should().Be(DocumentStatus.Embedding);
        document.StartParsing();
        document.Status.Should().Be(DocumentStatus.Parsing);
        document.CompleteParsing();
        document.Status.Should().Be(DocumentStatus.Splitting);

        var emptyFailureMessage = () => document.MarkAsFailed(" ");
        emptyFailureMessage.Should().Throw<ArgumentException>();
    }
    [Fact]
    public void ConversationTemplate_ShouldAlwaysExposeNonNullSpecification()
    {
        var template = (ConversationTemplate)Activator.CreateInstance(
            typeof(ConversationTemplate),
            nonPublic: true)!;

        template.Specification.Should().NotBeNull();
    }
}
