using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.DataAnalysis.Ids;
using AICopilot.Core.McpServer.Aggregates.McpServerInfo;
using AICopilot.Core.McpServer.Ids;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.SharedKernel.Domain;

namespace AICopilot.ArchitectureTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    [Fact]
    public void CoreProjects_ShouldNotReferenceSiblingCoreProjects()
    {
        var coreAssemblies = new[]
        {
            typeof(Session).Assembly,
            typeof(BusinessDatabase).Assembly,
            typeof(McpServerInfo).Assembly,
            typeof(KnowledgeBase).Assembly
        };

        foreach (var assembly in coreAssemblies)
        {
            var siblingCoreReferences = assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Where(name => name!.StartsWith("AICopilot.Core.", StringComparison.Ordinal))
                .Where(name => name != assembly.GetName().Name)
                .ToArray();

            siblingCoreReferences.Should().BeEmpty(
                $"{assembly.GetName().Name} must not directly depend on another Core bounded context");
        }
    }

    [Fact]
    public void AiGatewayService_ShouldNotReferenceOtherCoreModules()
    {
        var forbidden = new Regex(@"AICopilot\.Core\.(Rag|DataAnalysis|McpServer)", RegexOptions.Compiled);
        var violations = ScanSource(Path.Combine("src", "services", "AICopilot.AiGatewayService"), forbidden);

        violations.Should().BeEmpty("AiGatewayService must call other modules through Services.Contracts");
    }

    [Fact]
    public void AiGatewayServiceProject_ShouldNotReferenceOtherCoreProjects()
    {
        var projectFile = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AICopilot.AiGatewayService.csproj");

        var document = XDocument.Load(projectFile);
        var forbiddenReferences = document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Where(include => include!.Contains(@"Core\AICopilot.Core.Rag\", StringComparison.OrdinalIgnoreCase)
                              || include.Contains(@"Core\AICopilot.Core.DataAnalysis\", StringComparison.OrdinalIgnoreCase)
                              || include.Contains(@"Core\AICopilot.Core.McpServer\", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        forbiddenReferences.Should().BeEmpty(
            "AiGatewayService may only reference its own Core module and cross-module contracts");
    }

    [Fact]
    public void AiGatewayRuntimeCoordination_ShouldUseDiAndDedicatedToolResultAudit()
    {
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services", "AICopilot.AiGatewayService");
        var runtimeSource = File.ReadAllText(Path.Combine(serviceRoot, "Agents", "AgentStreamRuntime.cs"));
        var executorSource = File.ReadAllText(Path.Combine(serviceRoot, "Workflows", "Executors", "FinalAgentRunExecutor.cs"));
        var auditSource = File.ReadAllText(Path.Combine(serviceRoot, "Workflows", "Executors", "ToolExecutionAuditRecorder.cs"));
        var dependencyInjection = File.ReadAllText(Path.Combine(serviceRoot, "DependencyInjection.cs"));

        runtimeSource.Should().Contain("public interface IAgentStreamRuntime");
        runtimeSource.Should().Contain("public sealed class AgentStreamRuntime");
        runtimeSource.Should().NotContain("static class AgentStreamRuntime");
        dependencyInjection.Should().Contain("AddScoped<IAgentStreamRuntime, AgentStreamRuntime>");

        executorSource.Should().Contain("ToolExecutionAuditRecorder toolExecutionAuditRecorder");
        executorSource.Should().Contain("RecordResultAsync");
        auditSource.Should().Contain("Tool.ExecuteResult");
        auditSource.Should().Contain("resultSha256");
        dependencyInjection.Should().Contain("AddScoped<ToolExecutionAuditRecorder>");
    }

    [Fact]
    public void AICopilotWeb_ShouldKeepAgentRunErrorsSessionScoped()
    {
        var webRoot = Path.Combine(SolutionRoot, "src", "vues", "AICopilot.Web");
        var webRules = File.ReadAllText(Path.Combine(webRoot, "AGENTS.md"));
        var chatStore = File.ReadAllText(Path.Combine(webRoot, "src", "stores", "chatStore.ts"));
        var agentTaskStore = File.ReadAllText(Path.Combine(webRoot, "src", "stores", "agentTaskStore.ts"));
        var artifactWorkspaceStore = File.ReadAllText(Path.Combine(webRoot, "src", "stores", "artifactWorkspaceStore.ts"));

        webRules.Should().Contain("Backend Errors Are Contract Data");
        webRules.Should().Contain("Session State Must Be Scoped");
        chatStore.Should().NotContain("agentErrorMessage");
        chatStore.Should().Contain("agentTaskStore.reset()");
        chatStore.Should().Contain("artifactWorkspaceStore.reset()");
        chatStore.Should().Contain("catalogStore.resetSelections()");
        agentTaskStore.Should().Contain("function reset()");
        agentTaskStore.Should().Contain("agentApprovals.value = []");
        agentTaskStore.Should().Contain("timelineEvents.value = []");
        artifactWorkspaceStore.Should().Contain("function reset()");
        artifactWorkspaceStore.Should().Contain("currentWorkspace.value = null");
        artifactWorkspaceStore.Should().Contain("currentArtifactPreview.value = null");
    }

    [Fact]
    public void McpService_ShouldNotReferenceAiGatewayCore()
    {
        var forbidden = new Regex(@"AICopilot\.Core\.AiGateway\.", RegexOptions.Compiled);
        var violations = ScanSource(Path.Combine("src", "services", "AICopilot.McpService"), forbidden);

        violations.Should().BeEmpty("McpService must read approval requirements through Services.Contracts");
    }

    [Fact]
    public void McpRuntime_ShouldNotReferenceApprovalPolicyCore()
    {
        var forbidden = new Regex(
            @"AICopilot\.Core\.AiGateway\.(Aggregates|Specifications)\.ApprovalPolicy|ApprovalPolicy|EnabledApprovalPolicies",
            RegexOptions.Compiled);
        var violations = ScanSource(Path.Combine("src", "infrastructure", "AICopilot.Infrastructure", "Mcp"), forbidden);

        violations.Should().BeEmpty("MCP runtime must not depend on AiGateway ApprovalPolicy internals");
    }

    [Fact]
    public void ServiceModules_ShouldOnlyReferenceTheirOwnedCoreModule()
    {
        var ownedCoreByService = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["AICopilot.AiGatewayService"] = "AICopilot.Core.AiGateway",
            ["AICopilot.DataAnalysisService"] = "AICopilot.Core.DataAnalysis",
            ["AICopilot.McpService"] = "AICopilot.Core.McpServer",
            ["AICopilot.RagService"] = "AICopilot.Core.Rag",
            ["AICopilot.IdentityService"] = null
        };
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services");
        var coreReference = new Regex(@"AICopilot\.Core\.[A-Za-z0-9_.]+", RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var (serviceProject, allowedCore) in ownedCoreByService)
        {
            var projectRoot = Path.Combine(serviceRoot, serviceProject);
            violations.AddRange(ScanSource(projectRoot, serviceProject, allowedCore, coreReference));
            violations.AddRange(ScanProjectReferences(projectRoot, serviceProject, allowedCore));
        }

        violations.Should().BeEmpty(
            "service modules must depend only on their owned Core bounded context; cross-module collaboration must go through Services.Contracts");
    }

    [Fact]
    public void ServiceRequests_ShouldDeclareAuthorizeRequirementOrBeExplicitlyPublic()
    {
        var publicRequests = new HashSet<string>(StringComparer.Ordinal)
        {
            "FinalizeCloudOidcLoginCommand",
            "LoginUserCommand",
            "GetCurrentUserProfileQuery",
            "GetInitializationStatusQuery"
        };
        var requestDeclaration = new Regex(
            @"public\s+record\s+(\w+)(?:(?!public\s+record)[\s\S])*?:\s*I(Command|Query)",
            RegexOptions.Compiled);
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services");
        var violations = Directory
            .EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file);
                return requestDeclaration
                    .Matches(source)
                    .Select(match =>
                    {
                        var contextStart = match.Index;
                        for (var i = 0; i < 5 && contextStart > 0; i++)
                        {
                            var previousLine = source.LastIndexOf('\n', Math.Max(0, contextStart - 2));
                            contextStart = previousLine < 0 ? 0 : previousLine + 1;
                        }

                        return new
                        {
                            File = file,
                            LineNumber = source[..match.Index].Count(character => character == '\n') + 1,
                            RequestName = match.Groups[1].Value,
                            Context = source[contextStart..match.Index]
                        };
                    });
            })
            .Where(item => !publicRequests.Contains(item.RequestName))
            .Where(item => !item.Context.Contains("[AuthorizeRequirement(", StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(SolutionRoot, item.File)}:{item.LineNumber}: {item.RequestName}")
            .ToArray();

        violations.Should().BeEmpty(
            "service command/query requests must declare permission requirements unless explicitly public");
    }

    [Fact]
    public void ServiceStreamRequests_ShouldDeclareAuthorizeRequirement()
    {
        var requestDeclaration = new Regex(
            @"public\s+(?:sealed\s+)?record\s+(\w+)(?:(?!public\s+(?:sealed\s+)?record)[\s\S])*?:\s*IStreamRequest\b",
            RegexOptions.Compiled);
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services");
        var violations = Directory
            .EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file =>
            {
                var source = File.ReadAllText(file);
                return requestDeclaration
                    .Matches(source)
                    .Select(match =>
                    {
                        var contextStart = match.Index;
                        for (var i = 0; i < 5 && contextStart > 0; i++)
                        {
                            var previousLine = source.LastIndexOf('\n', Math.Max(0, contextStart - 2));
                            contextStart = previousLine < 0 ? 0 : previousLine + 1;
                        }

                        return new
                        {
                            File = file,
                            LineNumber = source[..match.Index].Count(character => character == '\n') + 1,
                            RequestName = match.Groups[1].Value,
                            Context = source[contextStart..match.Index]
                        };
                    });
            })
            .Where(item => !item.Context.Contains("[AuthorizeRequirement(", StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(SolutionRoot, item.File)}:{item.LineNumber}: {item.RequestName}")
            .ToArray();

        violations.Should().BeEmpty(
            "streaming MediatR requests are separate from IRequest pipeline behaviors and must declare explicit permission requirements");
    }

    [Fact]
    public void HttpApiControllers_ShouldRequireExplicitAuthorizeOrAllowAnonymous()
    {
        var controllerRoot = Path.Combine(SolutionRoot, "src", "hosts", "AICopilot.HttpApi", "Controllers");
        var controllerDeclaration = new Regex(
            @"public\s+class\s+(\w+Controller)\b",
            RegexOptions.Compiled);
        var actionDeclaration = new Regex(
            @"public\s+(?:async\s+)?(?:Task<\s*IActionResult\s*>|IActionResult|IResult)\s+(\w+)\s*\(",
            RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(controllerRoot, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var source = File.ReadAllText(file);
            var controllerMatch = controllerDeclaration.Match(source);
            if (!controllerMatch.Success)
            {
                continue;
            }

            var controllerContext = GetPrecedingLines(source, controllerMatch.Index, 8);
            if (HasAuthorizationMetadata(controllerContext))
            {
                continue;
            }

            violations.AddRange(actionDeclaration
                .Matches(source)
                .Cast<Match>()
                .Select(match => new
                {
                    ActionName = match.Groups[1].Value,
                    LineNumber = source[..match.Index].Count(character => character == '\n') + 1,
                    Context = GetPrecedingLines(source, match.Index, 8)
                })
                .Where(item => !HasAuthorizationMetadata(item.Context))
                .Select(item => $"{Path.GetRelativePath(SolutionRoot, file)}:{item.LineNumber}: {controllerMatch.Groups[1].Value}.{item.ActionName}"));
        }

        violations.Should().BeEmpty(
            "HTTP API controllers must require authorization by default or mark intentional anonymous endpoints explicitly");
    }

    [Fact]
    public void StreamPipelineBehaviors_ShouldNotOwnTransactionOrAuditBoundaries()
    {
        var forbidden = new Regex(
            @"IStreamPipelineBehavior[\s\S]{0,240}\b(Transaction|Transactional|Audit)\b|\b(Transaction|Transactional|Audit)\b[\s\S]{0,240}IStreamPipelineBehavior",
            RegexOptions.Compiled);
        var roots = new[]
        {
            Path.Combine("src", "hosts"),
            Path.Combine("src", "services"),
            Path.Combine("src", "infrastructure"),
            Path.Combine("src", "shared")
        };
        var violations = roots
            .SelectMany(root => ScanSource(root, forbidden))
            .ToArray();

        violations.Should().BeEmpty(
            "SSE/streaming MediatR pipelines must not hold transaction or audit boundaries open across async enumeration");
    }

    [Fact]
    public void MediatRBehaviors_ShouldBeRegisteredOnlyByUnifiedPipelineEntry()
    {
        var pipelineRegistrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.CrossCutting",
            "DependencyInjection.cs");
        var pipelineRegistration = File.ReadAllText(pipelineRegistrationFile);

        pipelineRegistration.Should().Contain("AddAICopilotMediatRPipeline");
        pipelineRegistration.Should().Contain("AuthorizationRequirementEvaluator");
        pipelineRegistration.Should().Contain("TelemetryBehavior<,>");
        pipelineRegistration.Should().Contain("ValidationBehavior<,>");
        pipelineRegistration.Should().Contain("AuthorizationBehavior<,>");
        pipelineRegistration.Should().Contain("IPipelineBehavior<,>");
        pipelineRegistration.Should().Contain("TelemetryStreamBehavior<,>");
        pipelineRegistration.Should().Contain("ValidationStreamBehavior<,>");
        pipelineRegistration.Should().Contain("AuthorizationStreamBehavior<,>");
        pipelineRegistration.Should().Contain("IStreamPipelineBehavior<,>");
        AssertInOrder(
            pipelineRegistration,
            "TelemetryBehavior<,>",
            "ValidationBehavior<,>",
            "AuthorizationBehavior<,>");
        AssertInOrder(
            pipelineRegistration,
            "TelemetryStreamBehavior<,>",
            "ValidationStreamBehavior<,>",
            "AuthorizationStreamBehavior<,>");

        var forbiddenRegistration = new Regex(
            @"AddBehavior\s*\(|TryAddEnumerable\s*\([\s\S]{0,160}I(?:Stream)?PipelineBehavior<,>|ServiceDescriptor\.\w+\s*\(\s*typeof\(I(?:Stream)?PipelineBehavior<,>\)",
            RegexOptions.Compiled);
        var violations = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Equals(pipelineRegistrationFile, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => forbiddenRegistration.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty(
            "MediatR cross-cutting behaviors must be registered through AddAICopilotMediatRPipeline only");
    }

    [Fact]
    public void MediatRTelemetry_ShouldUseServiceDefaultsOpenTelemetrySource()
    {
        var serviceDefaults = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.ServiceDefaults",
            "Extensions.cs"));
        var telemetry = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.CrossCutting",
            "Behaviors",
            "PipelineTelemetry.cs"));

        telemetry.Should().Contain("AICopilot.MediatR");
        serviceDefaults.Should().Contain("AddSource(\"AICopilot.MediatR\")");
    }

    [Fact]
    public void MediatRPipelineBehaviors_ShouldNotOwnTransactionOrAuditBoundaries()
    {
        var behaviorRoot = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.CrossCutting",
            "Behaviors");
        var forbidden = new Regex(
            @"\b(BeginTransactionAsync|UseTransactionAsync|ITransactionalExecutionService|AuditTransactionCoordinator|IAuditLogWriter|AuditDbContext|SaveChangesAsync)\b",
            RegexOptions.Compiled);
        var violations = Directory
            .EnumerateFiles(behaviorRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => forbidden.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty(
            "MediatR behaviors may route and observe requests, but transaction and audit persistence boundaries stay with explicit infrastructure owners");
    }

    [Fact]
    public void TransactionOwnership_ShouldStaySeparatedBetweenRepositoriesAndIdentity()
    {
        var auditCoordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "AuditTransactionCoordinator.cs"));
        var efRepositoryBase = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Repository",
            "EfRepositoryBase.cs"));
        var identityTransactionService = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "EfTransactionalExecutionService.cs"));
        var pipelineRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.CrossCutting",
            "DependencyInjection.cs"));

        efRepositoryBase.Should().Contain("transactionCoordinator.SaveChangesAsync");
        auditCoordinator.Should().Contain("CreateExecutionStrategy");
        auditCoordinator.Should().Contain("BeginTransactionAsync");
        auditCoordinator.Should().Contain("UseTransactionAsync");
        auditCoordinator.Should().Contain("transactionalAuditDbContext.SaveChangesAsync");
        identityTransactionService.Should().Contain("IdentityStoreDbContext");
        identityTransactionService.Should().Contain("BeginTransactionAsync");
        identityTransactionService.Should().Contain("dbContext.SaveChangesAsync");
        identityTransactionService.Should().NotContain("AuditTransactionCoordinator");
        identityTransactionService.Should().NotContain("AuditDbContext");
        pipelineRegistration.Should().NotContain("Transaction");
        pipelineRegistration.Should().NotContain("Audit");
    }

    [Fact]
    public void MediatRHosts_ShouldCallUnifiedPipelineRegistrationBeforeServiceModules()
    {
        var hostFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HttpApi"] = Path.Combine("src", "hosts", "AICopilot.HttpApi", "DependencyInjection.cs"),
            ["DataWorker"] = Path.Combine("src", "hosts", "AICopilot.DataWorker", "Program.cs"),
            ["RagWorker"] = Path.Combine("src", "hosts", "AICopilot.RagWorker", "Program.cs")
        };
        var violations = new List<string>();

        foreach (var (host, relativeFile) in hostFiles)
        {
            var file = Path.Combine(SolutionRoot, relativeFile);
            var source = File.ReadAllText(file);
            var pipelineIndex = source.IndexOf("AddAICopilotMediatRPipeline", StringComparison.Ordinal);
            if (pipelineIndex < 0)
            {
                violations.Add($"{relativeFile}: {host} does not call AddAICopilotMediatRPipeline");
                continue;
            }

            var firstServiceIndex = FindFirstIndex(
                source,
                "AddIdentityService",
                "AddAiGatewayService",
                "AddRagService",
                "AddDataAnalysisService",
                "AddMcpService");

            if (firstServiceIndex >= 0 && pipelineIndex > firstServiceIndex)
            {
                violations.Add($"{relativeFile}: {host} registers service modules before the MediatR pipeline");
            }
        }

        violations.Should().BeEmpty(
            "host composition roots that register MediatR handlers must register the shared pipeline first");
    }

    [Fact]
    public void AuthorizationBehavior_ShouldResolveIdentityServicesOnlyForPermissionProtectedRequests()
    {
        var behaviorSource = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.CrossCutting",
            "Behaviors",
            "AuthorizationRequirementEvaluator.cs"));
        var noRequirementIndex = behaviorSource.IndexOf("requiredPermissions.Length == 0", StringComparison.Ordinal);
        var userResolveIndex = behaviorSource.IndexOf("GetService(typeof(ICurrentUser))", StringComparison.Ordinal);
        var identityResolveIndex = behaviorSource.IndexOf("GetService(typeof(IIdentityAccessService))", StringComparison.Ordinal);

        noRequirementIndex.Should().BeGreaterThanOrEqualTo(0);
        userResolveIndex.Should().BeGreaterThan(noRequirementIndex);
        identityResolveIndex.Should().BeGreaterThan(userResolveIndex);
    }

    [Fact]
    public void CloudReadOnlyDirectDb_ShouldKeepReadonlyGuardsAndRejectSimulationFallback()
    {
        var semanticGuard = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "CloudReadOnlySemanticSqlGuard.cs"));
        var businessQuery = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.DataAnalysisService",
            "BusinessDatabases",
            "BusinessDatabaseReadonlyQuery.cs"));
        var governedSchema = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.Contracts",
            "Contracts",
            "CloudReadOnlyGovernedSchema.cs"));
        var seeder = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "MigrationWorkerCloudReadOnlySeeder.cs"));
        var semanticRunner = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "SemanticAnalysisRunner.cs"));

        var approvedTables = new[] { "devices", "mfg_processes", "device_logs", "hourly_capacity", "pass_station_records" };
        foreach (var table in approvedTables)
        {
            governedSchema.Should().Contain($"\"{table}\"");
        }

        semanticGuard.Should().Contain("ContainsWildcardProjection");
        semanticGuard.Should().Contain("CloudReadOnlyGovernedSchema.AllowedTables");
        semanticGuard.Should().Contain("CloudReadOnlyGovernedSchema.BlockedFieldFragments");
        businessQuery.Should().Contain("CloudReadOnlyBusinessQuerySchema");
        businessQuery.Should().Contain("CloudReadOnlyGovernedSchema.AllowedTables");
        businessQuery.Should().Contain("BlockedFieldFragments");
        governedSchema.Should().Contain("AllowedColumns");
        governedSchema.Should().Contain("bootstrap_secret");
        seeder.Should().Contain("IsSimulationSeedEnabled(configuration)");
        seeder.Should().Contain("isReadOnly: true");
        seeder.Should().Contain("ReadOnlyCredentialVerified");
        seeder.Should().Contain("DataAnalysis CloudReadOnly direct database mode cannot be enabled while CloudReadonly Simulation seeding is enabled.");

        semanticRunner.IndexOf("semanticPhysicalMappingProvider.TryGetMapping", StringComparison.Ordinal)
            .Should()
            .BeLessThan(semanticRunner.IndexOf("return await RunCloudAiReadAsync", StringComparison.Ordinal));

        governedSchema.Should().Contain("mfg_processes");
    }

    [Fact]
    public void CloudReadOnlyTextToSql_ShouldExposeOnlyGovernedSchemaAndKeepRepairSqlInMemory()
    {
        var agents = File.ReadAllText(Path.Combine(SolutionRoot, "AGENTS.md"));
        var businessRules = File.ReadAllText(Path.Combine(SolutionRoot, "资料", "AICopilot业务规则.md"));
        var generator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "CloudReadOnlyLlmTextToSqlGenerator.cs"));
        var runner = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "CloudReadOnlyTextToSqlFallbackRunner.cs"));
        var generationContracts = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.Contracts",
            "Contracts",
            "CloudReadOnlyTextToSqlGenerationContracts.cs"));
        var repairContracts = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.Contracts",
            "Contracts",
            "CloudReadOnlyTextToSqlRepairContracts.cs"));
        var auditRecorder = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "Executors",
            "DataAnalysisAuditRecorder.cs"));

        agents.Should().Contain("LLM prompt 可见的物理 schema 仅限 `CloudReadOnlyGovernedSchema`");
        agents.Should().Contain("不得暴露连接串、凭据");
        businessRules.Should().Contain("LLM prompt 可见的物理 schema 只能来自 `CloudReadOnlyGovernedSchema`");
        businessRules.Should().Contain("不得把连接串、凭据");

        generator.Should().Contain("governedSchema = request.AllowedTables");
        generator.Should().Contain("columns = request.AllowedColumns.TryGetValue");
        generator.Should().NotContain("ConnectionString");
        generator.Should().NotContain("Password");
        generator.Should().NotContain("Credential");

        generationContracts.Should().Contain("public string? PreviousSqlForRepair { get; init; }");
        runner.Should().Contain("string? previousSqlForRepair = null;");
        runner.Should().Contain("PreviousSqlForRepair = previousSqlForRepair");
        runner.Should().Contain("previousSqlForRepair = null;");
        generator.Should().Contain("previousSqlForRepair = request.PreviousSqlForRepair");

        ExtractBetween(
                generationContracts,
                "public sealed record CloudReadOnlyTextToSqlGenerationResult",
                "public interface ICloudReadOnlyTextToSqlGenerator")
            .Should()
            .NotContain("PreviousSqlForRepair");
        ExtractBetween(
                runner,
                "public sealed record CloudReadOnlyTextToSqlFallbackResult",
                "public sealed class CloudReadOnlyTextToSqlFallbackRunner")
            .Should()
            .NotContain("PreviousSqlForRepair");
        var repairAttemptRecord = ExtractBetween(
            repairContracts,
            "public sealed record CloudReadOnlyTextToSqlRepairAttemptRecord",
            "public static class CloudReadOnlyTextToSqlRepairClassifier");
        repairAttemptRecord.Should().Contain("string SqlHash");
        repairAttemptRecord.Should().Contain("int SqlLength");
        Regex
            .IsMatch(repairAttemptRecord, @"\bstring\s+Sql\s*[,)]", RegexOptions.CultureInvariant)
            .Should()
            .BeFalse("repair attempts may persist only SQL hash/length metadata, not raw SQL");

        auditRecorder.Should().Contain("[\"questionHash\"]");
        auditRecorder.Should().Contain("[\"sqlHash\"]");
        auditRecorder.Should().NotContain("PreviousSqlForRepair");
        auditRecorder.Should().NotContain("previousSqlForRepair");

        var allowedPreviousSqlFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/services/AICopilot.Services.Contracts/Contracts/CloudReadOnlyTextToSqlGenerationContracts.cs",
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/CloudReadOnlyTextToSqlFallbackRunner.cs",
            "src/services/AICopilot.AiGatewayService/Workflows/Executors/CloudReadOnlyLlmTextToSqlGenerator.cs"
        };
        var previousSqlViolations = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file =>
            {
                var relativeFile = Path
                    .GetRelativePath(SolutionRoot, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return File
                    .ReadLines(file)
                    .Select((line, index) => new
                    {
                        RelativeFile = relativeFile,
                        LineNumber = index + 1,
                        Line = line.Trim()
                    });
            })
            .Where(item => item.Line.Contains("PreviousSqlForRepair", StringComparison.Ordinal)
                           || item.Line.Contains("previousSqlForRepair", StringComparison.Ordinal))
            .Where(item => !item.RelativeFile.StartsWith("src/tests/", StringComparison.OrdinalIgnoreCase))
            .Where(item => !allowedPreviousSqlFiles.Contains(item.RelativeFile))
            .Select(item => $"{item.RelativeFile}:{item.LineNumber}: {item.Line}")
            .ToArray();

        previousSqlViolations.Should().BeEmpty(
            "PreviousSqlForRepair is current-call repair context only and must not enter audit, logs, state, results, or persistence models");
    }

    [Fact]
    public void CodeQualityGuidance_ShouldKeepLinqAndRepeatedEnumerationRules()
    {
        var editorConfig = File.ReadAllText(Path.Combine(SolutionRoot, ".editorconfig"));
        var agents = File.ReadAllText(Path.Combine(SolutionRoot, "AGENTS.md"));
        var businessRules = File.ReadAllText(Path.Combine(SolutionRoot, "资料", "AICopilot业务规则.md"));

        editorConfig.Should().Contain("dotnet_diagnostic.CA1851.severity = warning");
        agents.Should().Contain("简单数据转换、过滤、投影、分组默认优先使用 LINQ");
        agents.Should().Contain("IQueryable");
        agents.Should().Contain("热路径");
        agents.Should().Contain("for`/`foreach");
        agents.Should().Contain("重复枚举");
        agents.Should().Contain("N+1");
        businessRules.Should().Contain("先物化再过滤");
        businessRules.Should().Contain("CA1851 先作为 warning");
    }

    [Fact]
    public void PreviewPackages_ShouldStayInExplicitDebtWhitelist()
    {
        var allowedPreviewPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.SemanticKernel.Connectors.Qdrant|1.74.0-preview"
        };
        var sourceRoot = Path.Combine(SolutionRoot, "src");
        var prerelease = new Regex(@"-(preview|alpha|beta|rc)(\.|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var violations = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => XDocument
                .Load(file)
                .Descendants("PackageReference")
                .Select(reference => new
                {
                    File = Path.GetRelativePath(SolutionRoot, file),
                    Include = reference.Attribute("Include")?.Value ?? string.Empty,
                    Version = reference.Attribute("Version")?.Value ?? string.Empty
                }))
            .Where(package => prerelease.IsMatch(package.Version))
            .Where(package => !allowedPreviewPackages.Contains($"{package.Include}|{package.Version}"))
            .Select(package => $"{package.File}: {package.Include} {package.Version}")
            .ToArray();

        violations.Should().BeEmpty("preview packages require explicit user approval and debt tracking");
    }

    [Fact]
    public void AgentPluginAbstractions_ShouldNotContainRuntimeLoaderOrDependencyInjection()
    {
        var projectRoot = Path.Combine(SolutionRoot, "src", "shared", "AICopilot.AgentPlugin");
        var projectFile = Path.Combine(projectRoot, "AICopilot.AgentPlugin.csproj");
        var forbiddenSource = new Regex(
            @"\bAgentPluginLoader\b|\bAgentPluginRegistrar\b|\bActivatorUtilities\b|Microsoft\.Extensions\.DependencyInjection",
            RegexOptions.Compiled);
        var sourceViolations = ScanSource(Path.Combine("src", "shared", "AICopilot.AgentPlugin"), forbiddenSource);
        var packageViolations = XDocument
            .Load(projectFile)
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        sourceViolations.Should().BeEmpty("AgentPlugin shared project must contain abstractions only");
        packageViolations.Should().BeEmpty("AgentPlugin shared project must not depend on DI runtime packages");
    }

    [Fact]
    public void PluginRuntime_ShouldOwnAssemblyScanningAndDiActivation()
    {
        var runtimeRoot = Path.Combine(
            SolutionRoot,
            "src",
            "shared",
            "AICopilot.AgentPlugin.Runtime");
        var forbiddenOutsideRuntime = new Regex(
            @"\bActivatorUtilities\b|\.GetTypes\(\).*IAgentPlugin|typeof\(IAgentPlugin\)\.IsAssignableFrom",
            RegexOptions.Compiled);
        var violations = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.StartsWith(runtimeRoot, StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file =>
            {
                var relativeFile = Path.GetRelativePath(SolutionRoot, file);
                return File.ReadLines(file)
                    .Select((line, index) => new { Line = line.Trim(), LineNumber = index + 1 })
                    .Where(item => item.Line.Length > 0 && !item.Line.StartsWith("//", StringComparison.Ordinal))
                    .Where(item => forbiddenOutsideRuntime.IsMatch(item.Line))
                    .Select(item => $"{relativeFile}:{item.LineNumber}: {item.Line}");
            })
            .ToArray();

        violations.Should().BeEmpty("plugin assembly scanning and DI activation belong in AgentPlugin.Runtime");
    }

    [Fact]
    public void ServiceProjects_ShouldNotReferenceSharpTokenImplementation()
    {
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services");
        var sourceViolations = ScanSource(Path.Combine("src", "services"), new Regex(@"\bSharpToken\b", RegexOptions.Compiled));
        var packageViolations = Directory
            .EnumerateFiles(serviceRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(file => XDocument
                .Load(file)
                .Descendants("PackageReference")
                .Select(reference => new
                {
                    File = Path.GetRelativePath(SolutionRoot, file),
                    Include = reference.Attribute("Include")?.Value ?? string.Empty
                }))
            .Where(package => string.Equals(package.Include, "SharpToken", StringComparison.OrdinalIgnoreCase))
            .Select(package => $"{package.File}: {package.Include}")
            .ToArray();

        sourceViolations.Should().BeEmpty("service layer must depend on ITextTokenEstimator, not SharpToken");
        packageViolations.Should().BeEmpty("service projects must not reference SharpToken directly");
    }

    [Fact]
    public void ServicesAndCore_ShouldNotReferencePersistenceOrEventBusImplementations()
    {
        var forbidden = new Regex(
            @"\b(DbContext|DbSet<|IQueryable<|IPublishEndpoint|NpgsqlConnection|NpgsqlDataSource)\b|using\s+(Microsoft\.EntityFrameworkCore|Dapper|Npgsql|MassTransit)\b",
            RegexOptions.Compiled);

        var violations = ScanSource(Path.Combine("src", "services"), forbidden)
            .Concat(ScanSource(Path.Combine("src", "core"), forbidden))
            .ToArray();

        violations.Should().BeEmpty("application and domain layers must not depend on persistence or broker details");
    }

    [Fact]
    public void HttpApiControllers_ShouldNotReferenceCoreModules()
    {
        var forbidden = new Regex(@"AICopilot\.Core\.", RegexOptions.Compiled);
        var violations = ScanSource(Path.Combine("src", "hosts", "AICopilot.HttpApi", "Controllers"), forbidden);

        violations.Should().BeEmpty("controllers must speak request/response contracts, not aggregate types");
    }

    [Fact]
    public void ConversationTemplate_ShouldNotExposePublicSetters()
    {
        var publicSetters = typeof(ConversationTemplate)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name)
            .ToArray();

        publicSetters.Should().BeEmpty("ConversationTemplate is the first aggregate hardening template");
    }

    [Fact]
    public void HardenedAggregateRoots_ShouldNotExposePublicIdSetters()
    {
        var aggregateRootTypes = new[]
        {
            typeof(ApprovalPolicy),
            typeof(McpServerInfo),
            typeof(EmbeddingModel),
            typeof(KnowledgeBase)
        };

        var publicIdSetters = aggregateRootTypes
            .Select(type => new
            {
                Type = type.Name,
                IdProperty = type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            })
            .Where(item => item.IdProperty?.SetMethod?.IsPublic == true)
            .Select(item => $"{item.Type}.Id")
            .ToArray();

        publicIdSetters.Should().BeEmpty("hardened aggregate roots must keep identity changes inside the aggregate");
    }

    [Fact]
    public void BusinessEntities_ShouldUseStrongTypedIdentifiers()
    {
        var expectedIdTypes = new Dictionary<Type, Type>
        {
            [typeof(Session)] = typeof(SessionId),
            [typeof(LanguageModel)] = typeof(LanguageModelId),
            [typeof(ConversationTemplate)] = typeof(ConversationTemplateId),
            [typeof(ApprovalPolicy)] = typeof(ApprovalPolicyId),
            [typeof(KnowledgeBase)] = typeof(KnowledgeBaseId),
            [typeof(Document)] = typeof(DocumentId),
            [typeof(EmbeddingModel)] = typeof(EmbeddingModelId),
            [typeof(BusinessDatabase)] = typeof(BusinessDatabaseId),
            [typeof(McpServerInfo)] = typeof(McpServerId)
        };

        var violations = expectedIdTypes
            .Select(item => new
            {
                Entity = item.Key.Name,
                Expected = item.Value.Name,
                Actual = item.Key.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.PropertyType.Name
            })
            .Where(item => item.Actual != item.Expected)
            .Select(item => $"{item.Entity}.Id expected {item.Expected}, actual {item.Actual ?? "<missing>"}")
            .ToArray();

        violations.Should().BeEmpty("core business identifiers must stay strongly typed inside AICopilot");
    }

    [Fact]
    public void CoreEntitiesAndAggregates_ShouldNotExposePublicSetters()
    {
        var entityTypes = new[]
        {
            typeof(ApprovalPolicy),
            typeof(ConversationTemplate),
            typeof(LanguageModel),
            typeof(Session),
            typeof(Message),
            typeof(BusinessDatabase),
            typeof(McpServerInfo),
            typeof(EmbeddingModel),
            typeof(KnowledgeBase),
            typeof(Document),
            typeof(DocumentChunk)
        };

        var publicSetters = entityTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.SetMethod?.IsPublic == true)
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        publicSetters.Should().BeEmpty("domain state must be changed through aggregate behavior methods");
    }

    [Fact]
    public void ValueObjects_ShouldOnlyExposeInitSetters()
    {
        var valueObjectTypes = new[]
        {
            typeof(ModelParameters),
            typeof(TemplateSpecification)
        };

        var mutableSetters = valueObjectTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.SetMethod?.IsPublic == true && !IsInitOnly(property))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        mutableSetters.Should().BeEmpty("value objects may be initialized but must not remain mutable");
    }

    [Fact]
    public void EntityAbstractions_ShouldNotExposePublicIdSetters()
    {
        typeof(IEntity<Guid>).GetProperty(nameof(IEntity<Guid>.Id))!
            .SetMethod.Should().BeNull("entity identity must be read-only through the abstraction");

        var baseEntityIdSetter = typeof(BaseEntity<Guid>)
            .GetProperty(nameof(BaseEntity<Guid>.Id))!
            .SetMethod;

        baseEntityIdSetter.Should().NotBeNull();
        baseEntityIdSetter!.IsPublic.Should().BeFalse("BaseEntity must not let external code rewrite identity");
    }

    [Fact]
    public void McpServerDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "McpServerDbContext");

        var createOutboxTable = new Regex(
            @"CreateTable\s*\([\s\S]*?name:\s*""outbox_messages""",
            RegexOptions.Compiled);

        var violations = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => createOutboxTable.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty("Outbox table migrations are owned by the main Outbox infrastructure");
    }

    [Fact]
    public void DataAnalysisDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "DataAnalysisDbContext");

        var createOutboxTable = new Regex(
            @"CreateTable\s*\([\s\S]*?name:\s*""outbox_messages""",
            RegexOptions.Compiled);

        var violations = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => createOutboxTable.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty("Outbox table migrations are owned by the main Outbox infrastructure");
    }

    [Fact]
    public void RagDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "RagDbContext");

        var createOutboxTable = new Regex(
            @"CreateTable\s*\([\s\S]*?name:\s*""outbox_messages""",
            RegexOptions.Compiled);

        var violations = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => createOutboxTable.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty("Outbox table migrations are owned by the main Outbox infrastructure");
    }

    [Fact]
    public void AiGatewayDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext");

        var createOutboxTable = new Regex(
            @"CreateTable\s*\([\s\S]*?name:\s*""outbox_messages""",
            RegexOptions.Compiled);

        var violations = Directory
            .EnumerateFiles(migrationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => createOutboxTable.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        violations.Should().BeEmpty("Outbox table migrations are owned by the main Outbox infrastructure");
    }

    [Fact]
    public void OutboxRuntimeServices_ShouldNotDependOnAiCopilotDbContext()
    {
        var outboxRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox");

        var runtimeFiles = new[]
        {
            "OutboxDispatcher.cs",
            "OutboxIntegrationEventPublisher.cs"
        };

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(Path.Combine(outboxRoot, runtimeFile));

            source.Should().Contain("OutboxDbContext", runtimeFile);
            source.Should().NotContain("AiCopilotDbContext", runtimeFile);
        }

        var outboxContext = File.ReadAllText(Path.Combine(outboxRoot, "OutboxDbContext.cs"));

        outboxContext.Should().Contain("DbSet<OutboxMessage>");
        outboxContext.Should().Contain("OutboxMessageConfiguration");
        outboxContext.Should().NotContain("ExcludeFromMigrations");
    }

    [Fact]
    public void AuditRuntimeServices_ShouldNotDependOnAiCopilotDbContext()
    {
        var auditRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AuditLogs");

        var runtimeFiles = new[]
        {
            "AuditLogWriter.cs",
            "AuditLogQueryService.cs"
        };

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(Path.Combine(auditRoot, runtimeFile));

            source.Should().Contain("AuditDbContext", runtimeFile);
            source.Should().NotContain("AiCopilotDbContext", runtimeFile);
        }

        var auditContext = File.ReadAllText(Path.Combine(auditRoot, "AuditDbContext.cs"));

        auditContext.Should().Contain("DbSet<AuditLogEntry>");
        auditContext.Should().Contain("AuditLogEntryConfiguration");
        auditContext.Should().NotContain("ExcludeFromMigrations");
    }

    [Fact]
    public void IdentityManagementCommands_ShouldUseIdentityAuditWriter()
    {
        var commandRoot = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.IdentityService",
            "Commands");
        var commandFiles = new[]
        {
            "FinalizeCloudOidcLogin.cs",
            "CreateRole.cs",
            "UpdateRole.cs",
            "DeleteRole.cs",
            "CreatedUser.cs",
            "UpdateUserRole.cs",
            "DisableUser.cs",
            "EnableUser.cs",
            "ResetUserPassword.cs"
        };

        foreach (var commandFile in commandFiles)
        {
            var source = File.ReadAllText(Path.Combine(commandRoot, commandFile));

            source.Should().Contain("ITransactionalExecutionService", commandFile);
            source.Should().Contain("IIdentityAuditLogWriter", commandFile);
            source.Should().Contain("transactionalExecutionService.ExecuteAsync", commandFile);
            source.Should().NotContain("IAuditLogWriter", commandFile);
            source.Should().NotContain("auditLogWriter.SaveChangesAsync", commandFile);
            source.Should().NotContain("DbContext", commandFile);
        }
    }

    [Fact]
    public void AuditLogEntryMapping_ShouldStayInExplicitContextWhitelist()
    {
        var infrastructureRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("src", "infrastructure", "AICopilot.EntityFrameworkCore", "AuditLogs", "AuditDbContext.cs").Replace('\\', '/'),
            Path.Combine("src", "infrastructure", "AICopilot.EntityFrameworkCore", "IdentityStoreDbContext.cs").Replace('\\', '/')
        };

        var locations = Directory
            .EnumerateFiles(infrastructureRoot, "*DbContext.cs", SearchOption.AllDirectories)
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = Path.GetRelativePath(SolutionRoot, file).Replace('\\', '/'),
                    LineNumber = index + 1,
                    Line = line.Trim()
                }))
            .Where(item => item.Line.Contains("AuditLogEntryConfiguration", StringComparison.Ordinal))
            .ToArray();

        var violations = locations
            .Where(item => !allowedFiles.Contains(item.File))
            .Select(item => $"{item.File}:{item.LineNumber}: {item.Line}")
            .ToArray();

        violations.Should().BeEmpty("new DbContexts must not map audit_logs without explicit transaction-boundary review");
        locations.Select(item => item.File).Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().BeEquivalentTo(allowedFiles);
    }

    [Fact]
    public void AiCopilotDbContextSnapshot_ShouldDetachSplitModules_AndMapOutboxSchema()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiCopilotDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().NotContain(
            "AICopilot.Core.McpServer.Aggregates.McpServerInfo.McpServerInfo",
            "McpServerInfo is owned by McpServerDbContext");
        snapshot.Should().NotContain(
            "b.ToTable(\"mcp_server_info\"",
            "the main DbContext must not map the MCP server table");
        snapshot.Should().NotContain(
            "AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.BusinessDatabase",
            "BusinessDatabase is owned by DataAnalysisDbContext");
        snapshot.Should().NotContain(
            "b.ToTable(\"business_databases\"",
            "the main DbContext must not map the DataAnalysis business database table");
        snapshot.Should().NotContain(
            "AICopilot.Core.Rag.Aggregates.EmbeddingModel.EmbeddingModel",
            "EmbeddingModel is owned by RagDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeBase",
            "KnowledgeBase is owned by RagDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document",
            "Document is owned by RagDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.DocumentChunk",
            "DocumentChunk is owned by RagDbContext");
        snapshot.Should().NotContain(
            "b.ToTable(\"embedding_models\"",
            "the main DbContext must not map the RAG embedding model table");
        snapshot.Should().NotContain(
            "b.ToTable(\"knowledge_bases\"",
            "the main DbContext must not map the RAG knowledge base table");
        snapshot.Should().NotContain(
            "b.ToTable(\"documents\"",
            "the main DbContext must not map the RAG document table");
        snapshot.Should().NotContain(
            "b.ToTable(\"document_chunks\"",
            "the main DbContext must not map the RAG document chunk table");
        snapshot.Should().NotContain(
            "AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel",
            "LanguageModel is owned by AiGatewayDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate",
            "ConversationTemplate is owned by AiGatewayDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy.ApprovalPolicy",
            "ApprovalPolicy is owned by AiGatewayDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Session",
            "Session is owned by AiGatewayDbContext");
        snapshot.Should().NotContain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Message",
            "Message is owned by AiGatewayDbContext");
        snapshot.Should().NotContain(
            "b.ToTable(\"language_models\"",
            "the main DbContext must not map the AiGateway language model table");
        snapshot.Should().NotContain(
            "b.ToTable(\"conversation_templates\"",
            "the main DbContext must not map the AiGateway conversation template table");
        snapshot.Should().NotContain(
            "b.ToTable(\"approval_policies\"",
            "the main DbContext must not map the AiGateway approval policy table");
        snapshot.Should().NotContain(
            "b.ToTable(\"sessions\"",
            "the main DbContext must not map the AiGateway session table");
        snapshot.Should().NotContain(
            "b.ToTable(\"messages\"",
            "the main DbContext must not map the AiGateway message table");
        snapshot.Should().NotContain(
            "AICopilot.Services.Contracts.ApplicationUser",
            "Identity runtime tables are owned by IdentityStoreDbContext");
        snapshot.Should().NotContain(
            "Microsoft.AspNetCore.Identity.IdentityRole<System.Guid>",
            "Identity runtime tables are owned by IdentityStoreDbContext");
        snapshot.Should().NotContain(
            "AspNet",
            "the main DbContext must not map Identity tables");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\");",
            "OutboxMessage belongs to the outbox schema");
        snapshot.Should().NotContain(
            "b.ToTable(\"outbox_messages\", (string)null);",
            "OutboxMessage must not fall back to the public schema");
    }

    [Fact]
    public void IdentityRuntime_ShouldUseIdentityStoreDbContext_AndKeepMainContextDetached()
    {
        var mainContextFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "AiCopilotDbContext.cs");
        var identityContextFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "IdentityStoreDbContext.cs");
        var dependencyInjectionFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs");
        var transactionalExecutionFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "EfTransactionalExecutionService.cs");

        var mainContext = File.ReadAllText(mainContextFile);
        var identityContext = File.ReadAllText(identityContextFile);
        var dependencyInjection = File.ReadAllText(dependencyInjectionFile);
        var transactionalExecution = File.ReadAllText(transactionalExecutionFile);

        mainContext.Should().Contain(": DbContext");
        mainContext.Should().NotContain("IdentityDbContext");
        mainContext.Should().NotContain("ApplicationUser");
        mainContext.Should().NotContain("AspNet");

        identityContext.Should().Contain("IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>");
        identityContext.Should().Contain(
            "builder.Entity<ApplicationUser>().ToTable(\"AspNetUsers\", \"identity\");",
            "Identity users must live in the identity schema");
        identityContext.Should().Contain(
            "builder.Entity<IdentityRole<Guid>>().ToTable(\"AspNetRoles\", \"identity\");",
            "Identity roles must live in the identity schema");
        identityContext.Should().Contain(
            "ToTable(\"audit_logs\", table => table.ExcludeFromMigrations())",
            "Identity management audit rows are staged in the same context transaction without owning audit migrations");

        dependencyInjection.Should().Contain("AddNpgsqlDbContext<IdentityStoreDbContext>");
        dependencyInjection.Should().Contain("AddEntityFrameworkStores<IdentityStoreDbContext>");
        dependencyInjection.Should().NotContain("AddEntityFrameworkStores<AiCopilotDbContext>");

        transactionalExecution.Should().Contain("IdentityStoreDbContext");
        transactionalExecution.Should().NotContain("AiCopilotDbContext");
        transactionalExecution.Should().NotContain("AuditDbContext");
    }

    [Fact]
    public void MigrationWorkApp_ShouldRunIdentityStoreMigrationBeforeModuleMigrationsAndSeed()
    {
        var workerFile = Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "Worker.cs");
        var migratorFile = Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "MigrationWorkerDatabaseMigrator.cs");
        var workerSource = File.ReadAllText(workerFile);
        var migratorSource = File.ReadAllText(migratorFile);

        workerSource.Should().Contain("GetRequiredService<IdentityStoreDbContext>");
        workerSource.Should().NotContain("GetRequiredService<AuditDbContext>");
        workerSource.Should().NotContain("GetRequiredService<OutboxDbContext>");
        migratorSource.Should().Contain("MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync");

        var mainMigration = migratorSource.IndexOf("MigrationHistoryTables.AiCopilot", StringComparison.Ordinal);
        var identityMigration = migratorSource.IndexOf("MigrationHistoryTables.IdentityStore", StringComparison.Ordinal);
        var firstModuleMigration = migratorSource.IndexOf("MigrationHistoryTables.AiGateway", StringComparison.Ordinal);
        var bootstrap = migratorSource.IndexOf("await MigrationHistoryBootstrapper.BootstrapLegacyHistoryAsync", StringComparison.Ordinal);
        var runMigrations = migratorSource.IndexOf("foreach (var migrationContext in migrationContexts)", StringComparison.Ordinal);
        var createContexts = workerSource.IndexOf("MigrationWorkerDatabaseMigrator.CreateMigrationContexts", StringComparison.Ordinal);
        var runMigrationCall = workerSource.IndexOf("await MigrationWorkerDatabaseMigrator.RunMigrationsAsync", StringComparison.Ordinal);
        var identitySeed = workerSource.IndexOf("await MigrationWorkerIdentitySeeder.SeedAsync", StringComparison.Ordinal);

        mainMigration.Should().BeGreaterThanOrEqualTo(0);
        identityMigration.Should().BeGreaterThan(mainMigration);
        firstModuleMigration.Should().BeGreaterThan(identityMigration);
        bootstrap.Should().BeGreaterThan(firstModuleMigration);
        runMigrations.Should().BeGreaterThan(bootstrap);
        createContexts.Should().BeGreaterThanOrEqualTo(0);
        runMigrationCall.Should().BeGreaterThan(createContexts);
        identitySeed.Should().BeGreaterThan(runMigrationCall);
    }

    [Fact]
    public void AICopilotRules_ShouldDocumentCloudBusinessReadOnlyBoundary()
    {
        var agentsFile = Path.Combine(SolutionRoot, "AGENTS.md");
        var businessRulesFile = Path.Combine(SolutionRoot, "资料", "AICopilot业务规则.md");
        var retrospectiveFile = Path.Combine(SolutionRoot, "docs", "改动复盘与规则沉淀.md");

        var agents = File.ReadAllText(agentsFile);
        var businessRules = File.ReadAllText(businessRulesFile);
        var retrospective = File.ReadAllText(retrospectiveFile);
        var combinedRules = string.Join(Environment.NewLine, agents, businessRules, retrospective);

        agents.Should().Contain("Cloud Business Read-only Boundary");
        agents.Should().Contain("must not directly write to the Cloud database");
        agents.Should().Contain("must not create, update, delete, backfill, approve, dispatch, or trigger Cloud business records");
        agents.Should().Contain("Human-in-the-loop approval is not permission");
        agents.Should().Contain("Cloud AI-facing APIs");
        agents.Should().Contain("Known-vulnerable dependencies are forbidden");
        agents.Should().Contain("NU190x");
        agents.Should().Contain("npm audit");

        businessRules.Should().Contain("`AICopilot` 是分析助手和受控编排系统，不是制造业务主系统");
        businessRules.Should().Contain("AICopilot 对 `IIoT.CloudPlatform` 只能读取数据和规则");
        businessRules.Should().Contain("直接写云端数据库");
        businessRules.Should().Contain("间接调用云端写接口");
        businessRules.Should().Contain("Human-in-the-loop 不能作为放开云端业务写入的理由");
        businessRules.Should().Contain("当前默认不存在专门给 AICopilot 使用的云端写 API");
        businessRules.Should().Contain("AI-facing API");
        agents.Should().Contain("DataAnalysis `CloudReadOnly` 只读数据源直连真实 Cloud PostgreSQL");
        agents.Should().Contain("只能创建/更新专用 readonly role");
        businessRules.Should().Contain("真实 Cloud Text-to-SQL 验证不得走 Simulation 数据源冒充真实结果");
        businessRules.Should().Contain("不得授予写权限、schema create 权限、superuser、createdb、createrole 或 replication");

        agents.Should().Contain("docs/改动复盘与规则沉淀.md");
        agents.Should().Contain("Change Closure");
        businessRules.Should().Contain("改动收口门禁");
        businessRules.Should().Contain("执行复盘和改动沉淀流水统一放在项目 `docs/`");
        retrospective.Should().Contain("项目滚动复盘入口");
        retrospective.Should().Contain("无新增长期规则");
        combinedRules.Should().Contain("最终回复必须列出复盘文档、规则沉淀位置和验证命令");
        combinedRules.Should().Contain("已验收功能默认冻结");
        combinedRules.Should().Contain("规则沉淀");
    }

    [Fact]
    public void AICopilotProductionCode_ShouldNotReferenceCloudProjectsOrNamespaces()
    {
        var productionRoots = new[]
        {
            Path.Combine("src", "core"),
            Path.Combine("src", "services"),
            Path.Combine("src", "infrastructure"),
            Path.Combine("src", "hosts"),
            Path.Combine("src", "shared")
        };
        var forbidden = new Regex(
            @"\bIIoT\.(CloudPlatform|Core|Services|HttpApi|EmployeeService|MasterDataService|ProductionService|IdentityService)\b|IIoT\.CloudPlatform",
            RegexOptions.Compiled);

        var violations = productionRoots
            .SelectMany(root => ScanSource(root, forbidden))
            .ToArray();

        violations.Should().BeEmpty(
            "AICopilot must not directly reference Cloud implementation projects or namespaces; Cloud alignment must stay read-only until explicit AI-facing APIs exist");

        var sourceRoot = Path.Combine(SolutionRoot, "src");
        var projectReferenceViolations = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains("IIoT.CloudPlatform", StringComparison.Ordinal)
                           || File.ReadAllText(file).Contains("..\\..\\IIoT.CloudPlatform", StringComparison.Ordinal)
                           || File.ReadAllText(file).Contains("../../IIoT.CloudPlatform", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();

        projectReferenceViolations.Should().BeEmpty(
            "AICopilot projects must not reference Cloud projects directly; future Cloud access must go through explicit AI-facing contracts");
    }

    [Fact]
    public void CloudReadOnlyAlignmentPlan_ShouldKeepCloudWriteToolsOutOfScope()
    {
        var alignmentFile = Path.Combine(SolutionRoot, "\u8d44\u6599", "CloudReadOnlyAlignment.md");
        var alignment = File.ReadAllText(alignmentFile);

        alignment.Should().Contain("Cloud Read-only Alignment");
        alignment.Should().Contain("No direct Cloud database writes from AICopilot");
        alignment.Should().Contain("No MCP, Tool, Agent workflow, background job, SQL script, or hidden adapter");
        alignment.Should().Contain("DataAnalysis may query Cloud data only through read-only sources");
        alignment.Should().Contain("Future Cloud-related MCP tools must default to query-only behavior");
        alignment.Should().Contain("Forbidden tool semantics for Cloud business data");
        alignment.Should().Contain("Write-capable APIs require a separate design decision");
    }

    [Fact]
    public void MigratingDbContexts_ShouldUseIndependentMigrationHistoryTables()
    {
        var infrastructureRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore");
        var dependencyInjection = File.ReadAllText(Path.Combine(infrastructureRoot, "DependencyInjection.cs"));
        var historyTables = File.ReadAllText(Path.Combine(infrastructureRoot, "MigrationHistoryTables.cs"));
        var bootstrapper = File.ReadAllText(Path.Combine(infrastructureRoot, "MigrationHistoryBootstrapper.cs"));

        var expectedTables = new[]
        {
            ("AiCopilot", "public", "__EFMigrationsHistory_AiCopilot", "AiCopilotDbContextFactory.cs"),
            ("IdentityStore", "identity", "__EFMigrationsHistory_IdentityStore", "IdentityStoreDbContext.cs"),
            ("AiGateway", "aigateway", "__EFMigrationsHistory_AiGateway", "AiGatewayDbContext.cs"),
            ("Rag", "rag", "__EFMigrationsHistory_Rag", "RagDbContext.cs"),
            ("DataAnalysis", "dataanalysis", "__EFMigrationsHistory_DataAnalysis", "DataAnalysisDbContext.cs"),
            ("McpServer", "mcp", "__EFMigrationsHistory_McpServer", "McpServerDbContext.cs")
        };

        foreach (var (tableName, schema, historyTableName, factoryFile) in expectedTables)
        {
            historyTables.Should().Contain($"\"{schema}\"");
            historyTables.Should().Contain($"\"{historyTableName}\"");
            dependencyInjection.Should().Contain(
                $"ConfigureMigrationHistory(MigrationHistoryTables.{tableName})",
                $"{tableName} runtime DbContext must write to its own EF migrations history table");

            var factory = File.ReadAllText(Path.Combine(infrastructureRoot, factoryFile));
            factory.Should().Contain(
                $"UseNpgsqlWithMigrationHistory(connectionString, MigrationHistoryTables.{tableName})",
                $"{tableName} design-time migrations must use the same split history table as runtime");
        }

        historyTables.Should().Contain("LegacyTableName = \"__EFMigrationsHistory\"");
        bootstrapper.Should().Contain("GetMigrations()");
        bootstrapper.Should().Contain("partial target history");
        bootstrapper.Should().Contain("ON CONFLICT (\"MigrationId\") DO NOTHING");
        dependencyInjection.Should().NotContain("ConfigureMigrationHistory(MigrationHistoryTables.Audit");
        dependencyInjection.Should().NotContain("ConfigureMigrationHistory(MigrationHistoryTables.Outbox");
    }

    [Fact]
    public void IdentityStoreBaselineMigration_ShouldBeSnapshotOnly()
    {
        var migrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "IdentityStoreDbContext",
            "20260429021832_IdentityStoreMigrationBaseline.cs");
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "IdentityStoreDbContext",
            "IdentityStoreDbContextModelSnapshot.cs");
        var migration = File.ReadAllText(migrationFile);
        var snapshot = File.ReadAllText(snapshotFile);

        migration.Should().NotContain("CreateTable(");
        migration.Should().NotContain("DropTable(");
        migration.Should().NotContain("DROP TABLE");
        migration.Should().Contain("Baseline only");

        snapshot.Should().Contain("AICopilot.Services.Contracts.ApplicationUser");
        snapshot.Should().Contain("b.ToTable(\"AspNetUsers\", \"identity\");");
        snapshot.Should().Contain("b.ToTable(\"AspNetRoles\", \"identity\");");
        snapshot.Should().Contain("b.ToTable(\"audit_logs\", null, t =>");
        snapshot.Should().Contain("t.ExcludeFromMigrations();");
    }

    [Fact]
    public void IdentityGuidMigration_ShouldUseGuardedRawSqlDropsOnlyForAspNetTables()
    {
        var migrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "20260429002748_MigrateIdentityKeysToGuid.cs");
        var migration = File.ReadAllText(migrationFile);

        migration.Should().Contain("WARNING: existing rows in identity.AspNet* are never deleted silently");
        migration.Should().Contain("FOREACH identity_table IN ARRAY");
        migration.Should().Contain("RAISE EXCEPTION");
        migration.Should().Contain("Refusing to run destructive Identity GUID migration");
        migration.Should().Contain("DROP TABLE IF EXISTS public.\"AspNetUsers\" CASCADE");
        migration.Should().Contain("DROP TABLE IF EXISTS public.\"AspNetRoles\" CASCADE");
        migration.Should().Contain("DROP TABLE IF EXISTS identity.\"AspNetUsers\" CASCADE");
        migration.Should().Contain("DROP TABLE IF EXISTS identity.\"AspNetRoles\" CASCADE");
        migration.Should().Contain("schema: \"identity\"");
        migration.Should().Contain("table.Column<Guid>(type: \"uuid\"");
        migration.Should().NotContain("DropTable(", "the destructive drop is allowed only as guarded raw SQL in this active-development migration");
        migration.Should().NotContain("language_models");
        migration.Should().NotContain("business_databases");
        migration.Should().NotContain("mcp_server_info");
        migration.Should().NotContain("outbox_messages");
        migration.Should().NotContain("audit_logs");
    }

    [Fact]
    public void DetachIdentityMigration_ShouldBeSnapshotOnly()
    {
        var migrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "20260429010506_DetachIdentityFromAiCopilotDbContext.cs");
        var migration = File.ReadAllText(migrationFile);

        migration.Should().Contain("Runtime ownership moved to IdentityStoreDbContext");
        migration.Should().NotContain("DropTable(");
        migration.Should().NotContain("CreateTable(");
    }

    [Fact]
    public void DataAnalysisDbContextSnapshot_ShouldMapBusinessDatabaseToDataAnalysisSchema()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "DataAnalysisDbContext",
            "DataAnalysisDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.BusinessDatabase",
            "BusinessDatabase is owned by DataAnalysisDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"business_databases\", \"dataanalysis\");",
            "BusinessDatabase must be mapped to the dataanalysis schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\", t => t.ExcludeFromMigrations());",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void RagDbContextSnapshot_ShouldMapRagTablesToRagSchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "RagDbContext",
            "RagDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.EmbeddingModel.EmbeddingModel",
            "EmbeddingModel is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeBase",
            "KnowledgeBase is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document",
            "Document is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.DocumentChunk",
            "DocumentChunk is owned by RagDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"embedding_models\", \"rag\");",
            "EmbeddingModel must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"knowledge_bases\", \"rag\");",
            "KnowledgeBase must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"documents\", \"rag\");",
            "Document must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"document_chunks\", \"rag\");",
            "DocumentChunk must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\",",
            "module contexts may write Outbox rows but must not own the Outbox migration");
        snapshot.Should().Contain(
            "t.ExcludeFromMigrations();",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void AiGatewayDbContextSnapshot_ShouldMapAiGatewayTablesToAiGatewaySchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext",
            "AiGatewayDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel",
            "LanguageModel is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate",
            "ConversationTemplate is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy.ApprovalPolicy",
            "ApprovalPolicy is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Session",
            "Session is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Message",
            "Message is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"language_models\", \"aigateway\");",
            "LanguageModel must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"conversation_templates\", \"aigateway\");",
            "ConversationTemplate must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"approval_policies\", \"aigateway\");",
            "ApprovalPolicy must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"sessions\", \"aigateway\");",
            "Session must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"messages\", \"aigateway\");",
            "Message must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\",",
            "module contexts may write Outbox rows but must not own the Outbox migration");
        snapshot.Should().Contain(
            "t.ExcludeFromMigrations();",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void McpServerDbContextSnapshot_ShouldMapMcpTableToMcpSchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "McpServerDbContext",
            "McpServerDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.McpServer.Aggregates.McpServerInfo.McpServerInfo",
            "McpServerInfo is owned by McpServerDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"mcp_server_info\", \"mcp\");",
            "McpServerInfo must be mapped to the mcp schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\",",
            "module contexts may write Outbox rows but must not own the Outbox migration");
        snapshot.Should().Contain(
            "t.ExcludeFromMigrations();",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void McpServerInitialMigration_ShouldMovePublicTableWithoutCopyDrop()
    {
        var migrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "McpServerDbContext",
            "20260427000100_InitialMcpServerSchema.cs");
        var migration = File.ReadAllText(migrationFile);
        var downStart = migration.IndexOf("protected override void Down", StringComparison.Ordinal);
        var up = migration[..downStart];
        var down = migration[downStart..];

        up.Should().Contain("ALTER TABLE public.mcp_server_info SET SCHEMA mcp");
        up.Should().Contain("Both public.mcp_server_info and mcp.mcp_server_info exist");
        up.Should().NotContain("INSERT INTO mcp.mcp_server_info");
        up.Should().NotContain("DROP TABLE public.mcp_server_info");
        down.Should().Contain("ALTER TABLE mcp.mcp_server_info SET SCHEMA public");
        down.Should().Contain("Both mcp.mcp_server_info and public.mcp_server_info exist");
    }

    [Fact]
    public void OutboxSchemaMigration_ShouldBeOwnedByAiCopilotDbContext()
    {
        var migrationFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "20260427000200_MoveOutboxToOutboxSchemaAndDetachMcpServer.cs");
        var migration = File.ReadAllText(migrationFile);

        migration.Should().Contain("[DbContext(typeof(AiCopilotDbContext))]");
        migration.Should().Contain("migrationBuilder.EnsureSchema(name: \"outbox\")");
        migration.Should().Contain("ALTER TABLE public.outbox_messages SET SCHEMA outbox");
        migration.Should().Contain("DROP TABLE public.outbox_messages");
    }

    private static IReadOnlyList<string> ScanSource(string relativePath, Regex forbidden)
    {
        var root = Path.Combine(SolutionRoot, relativePath);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(SolutionRoot, file);
            var lineNumber = 0;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (forbidden.IsMatch(line))
                {
                    violations.Add($"{relativeFile}:{lineNumber}: {line}");
                }
            }
        }

        return violations;
    }

    private static string GetPrecedingLines(string source, int index, int lineCount)
    {
        var contextStart = index;
        for (var i = 0; i < lineCount && contextStart > 0; i++)
        {
            var previousLine = source.LastIndexOf('\n', Math.Max(0, contextStart - 2));
            contextStart = previousLine < 0 ? 0 : previousLine + 1;
        }

        return source[contextStart..index];
    }

    private static bool HasAuthorizationMetadata(string source)
    {
        return source.Contains("[Authorize", StringComparison.Ordinal)
               || source.Contains("[AllowAnonymous", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ScanSource(
        string projectRoot,
        string serviceProject,
        string? allowedCore,
        Regex coreReference)
    {
        if (!Directory.Exists(projectRoot))
        {
            return [];
        }

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match match in coreReference.Matches(line))
                {
                    var referencedCore = NormalizeCoreReference(match.Value);
                    if (allowedCore is not null
                        && string.Equals(referencedCore, allowedCore, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{serviceProject}: {Path.GetRelativePath(SolutionRoot, file)}:{lineNumber}: {referencedCore}");
                }
            }
        }

        return violations;
    }

    private static IReadOnlyList<string> ScanProjectReferences(
        string projectRoot,
        string serviceProject,
        string? allowedCore)
    {
        var projectFile = Path.Combine(projectRoot, $"{serviceProject}.csproj");
        if (!File.Exists(projectFile))
        {
            return [];
        }

        var violations = XDocument
            .Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('/', Path.DirectorySeparatorChar))
            .Where(include => include.Contains($"{Path.DirectorySeparatorChar}Core{Path.DirectorySeparatorChar}AICopilot.Core.", StringComparison.OrdinalIgnoreCase))
            .Select(include => NormalizeCoreReference(Path.GetFileNameWithoutExtension(include)))
            .Where(referencedCore => allowedCore is null
                                     || !string.Equals(referencedCore, allowedCore, StringComparison.Ordinal))
            .Select(referencedCore => $"{serviceProject}: project reference {referencedCore}")
            .ToArray();

        return violations;
    }

    private static string NormalizeCoreReference(string value)
    {
        var match = Regex.Match(value, @"AICopilot\.Core\.(AiGateway|DataAnalysis|McpServer|Rag)", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : value;
    }

    private static int FindFirstIndex(string source, params string[] values)
    {
        return values
            .Select(value => source.IndexOf(value, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static void AssertInOrder(string source, params string[] values)
    {
        var previousIndex = -1;
        foreach (var value in values)
        {
            var index = source.IndexOf(value, StringComparison.Ordinal);
            index.Should().BeGreaterThan(previousIndex, $"{value} must be registered after the previous pipeline behavior");
            previousIndex = index;
        }
    }

    private static string ExtractBetween(string source, string startText, string endText)
    {
        var start = source.IndexOf(startText, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{startText} must exist in the inspected source");
        var end = source.IndexOf(endText, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"{endText} must appear after {startText}");
        return source[start..end];
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AICopilot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the test output directory.");
    }

    private static bool IsInitOnly(PropertyInfo property)
    {
        return property.SetMethod?
            .ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit)) == true;
    }
}
