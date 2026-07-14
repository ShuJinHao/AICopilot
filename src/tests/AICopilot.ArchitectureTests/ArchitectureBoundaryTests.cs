using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AICopilot.AiGatewayService.Workflows.Executors;
using AICopilot.AiGatewayService.Uploads;
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
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.Locking;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.Infrastructure.Storage;
using AICopilot.IdentityService.Commands;
using AICopilot.IdentityService.Authorization;
using AICopilot.RagService.Commands.Documents;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Domain;
using AICopilot.Services.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AICopilot.ArchitectureTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    [Fact]
    public void HttpApiControllers_ShouldUseBaseAndConstructorInjectedSender()
    {
        var baseControllerType = typeof(ApiControllerBase);
        var senderProperty = baseControllerType.GetProperty(
            "Sender",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var baseConstructors = baseControllerType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var controllerTypes = baseControllerType.Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Namespace == "AICopilot.HttpApi.Controllers")
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        senderProperty.Should().NotBeNull();
        senderProperty!.PropertyType.Should().Be<ISender>();
        senderProperty.SetMethod.Should().BeNull();
        baseConstructors.Should().ContainSingle();
        baseConstructors[0].GetParameters()
            .Should()
            .ContainSingle(parameter => parameter.ParameterType == typeof(ISender));
        controllerTypes.Should().NotBeEmpty();

        foreach (var controllerType in controllerTypes)
        {
            controllerType.Should().BeAssignableTo<ApiControllerBase>();
            controllerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Should()
                .NotBeEmpty()
                .And.OnlyContain(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(ISender)));
        }
    }

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
        typeof(EfRepositoryBase<,>).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Should().Contain(parameter => parameter.ParameterType == typeof(RepositoryPersistenceCommitter));
        typeof(PersistenceCommitEngine).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Should().ContainSingle(method => method.Name == nameof(PersistenceCommitEngine.CommitAsync));
        typeof(PersistenceCommitEngine).Assembly
            .GetType("AICopilot.EntityFrameworkCore.Transactions.AuditTransactionCoordinator")
            .Should().BeNull();

        var identityConstructor = typeof(IdentityTransactionalExecutionService).GetConstructors()
            .Should().ContainSingle().Subject;
        identityConstructor.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().BeEquivalentTo(
                [typeof(IdentityStoreDbContext), typeof(PersistenceCommitEngine)]);
    }

    [Fact]
    public void EnabledAdminInvariant_ShouldUseOneTransactionalLockAcrossAllDecreasePaths()
    {
        ConstructorParameterTypes(typeof(DisableUserCommandHandler))
            .Should().Contain(typeof(EnabledAdminInvariantPolicy));
        ConstructorParameterTypes(typeof(UpdateUserRoleCommandHandler))
            .Should().Contain(typeof(EnabledAdminInvariantPolicy));
        typeof(PostgresIdentityEnabledAdminInvariantGuard)
            .Should().BeAssignableTo<IIdentityEnabledAdminInvariantGuard>();
        ConstructorParameterTypes(typeof(PostgresIdentityEnabledAdminInvariantGuard))
            .Should().BeEquivalentTo([typeof(IdentityStoreDbContext)]);
        typeof(PostgreSqlAdvisoryLock).GetMethod(
                nameof(PostgreSqlAdvisoryLock.AcquireTransactionAsync),
                BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull();

        var mutationFiles = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Select(file => new
            {
                File = file,
                RelativePath = Path.GetRelativePath(SolutionRoot, file),
                Source = File.ReadAllText(file)
            })
            .Where(item =>
                item.Source.Contains("IdentityGovernanceHelper.MarkUserDisabled(", StringComparison.Ordinal) ||
                item.Source.Contains("userManager.RemoveFromRoleAsync(", StringComparison.Ordinal) ||
                item.Source.Contains("userManager.RemoveFromRolesAsync(", StringComparison.Ordinal) ||
                item.Source.Contains("userManager.DeleteAsync(", StringComparison.Ordinal))
            .ToArray();

        mutationFiles.Select(item => item.RelativePath).Should().BeEquivalentTo(
            new[]
            {
                Path.Combine(
                    "src", "hosts", "AICopilot.MigrationWorkApp",
                    "MigrationWorkerIdentitySeeder.cs"),
                Path.Combine(
                    "src", "services", "AICopilot.IdentityService", "Commands",
                    "DisableUser.cs"),
                Path.Combine(
                    "src", "services", "AICopilot.IdentityService", "Commands",
                    "UpdateUserRole.cs")
            },
            "the current production surface has no user deletion path; every new identity decrease path requires explicit invariant review");
        var violations = new List<string>();
        foreach (var mutationFile in mutationFiles)
        {
            var acquireIndex = mutationFile.Source.IndexOf(
                "enabledAdminInvariant.AcquireAsync(",
                StringComparison.Ordinal);
            var firstIdentityReadIndex = FindFirstIndex(
                mutationFile.Source,
                "userManager.FindByIdAsync(",
                "userManager.FindByNameAsync(",
                "roleManager.RoleExistsAsync(",
                "userManager.GetRolesAsync(",
                "userManager.GetUsersInRoleAsync(");
            if (!mutationFile.Source.Contains("EnabledAdminInvariantPolicy", StringComparison.Ordinal) ||
                acquireIndex < 0 ||
                (firstIdentityReadIndex >= 0 && acquireIndex > firstIdentityReadIndex))
            {
                violations.Add(
                    $"{mutationFile.RelativePath}: enabled Admin mutation must acquire the shared invariant before Identity reads");
            }
        }

        violations.Should().BeEmpty();

        mutationFiles.Single(item => item.RelativePath.EndsWith("DisableUser.cs", StringComparison.Ordinal))
            .Source.Should().Contain("enabledAdminInvariant.IsLastEnabledAdminAsync(");
        mutationFiles.Single(item => item.RelativePath.EndsWith("UpdateUserRole.cs", StringComparison.Ordinal))
            .Source.Should().Contain("enabledAdminInvariant.IsLastEnabledAdminAsync(");
        mutationFiles.Single(item => item.RelativePath.EndsWith(
                "MigrationWorkerIdentitySeeder.cs",
                StringComparison.Ordinal))
            .Source.Should().Contain("enabledAdminInvariant.HasEnabledAdminAsync(");

        var transactionLockOwners = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(file => File.ReadAllText(file).Contains(
                "pg_advisory_xact_lock",
                StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(SolutionRoot, file))
            .ToArray();
        transactionLockOwners.Should().Equal(
            Path.Combine(
                "src",
                "infrastructure",
                "AICopilot.EntityFrameworkCore",
                "Locking",
                "PostgreSqlAdvisoryLock.cs"));

        var migrationProgram = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "Program.cs"));
        var migrationWorker = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.MigrationWorkApp",
            "Worker.cs"));
        migrationProgram.Should().Contain("AddScoped<EnabledAdminInvariantPolicy>()");
        migrationWorker.Should().Contain("GetRequiredService<EnabledAdminInvariantPolicy>()");
    }

    [Fact]
    public void AgentWorkflowFinalContext_ShouldBindCancellationCleanupToIdempotentCompensation()
    {
        var source = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workflows",
            "AgentWorkflowPipeline.cs"));

        source.Should().Contain(
            "await using var compensation = new FinalAgentContextCompensation(",
            "the actual final-agent async-enumeration path must own the compensation scope");
        source.Should().Contain("compensation.MarkCompleted();");
        source.Should().Contain("await compensation.RemoveAndCompleteAsync();");
        source.Should().Contain("Interlocked.CompareExchange(ref completionState, 1, 0)");
        source.Should().Contain("store.RemoveAsync(sessionId, CancellationToken.None)");
        source.Should().NotContain("finalAgentContextStore.RemoveAsync(agentContext.SessionId",
            "final-context removal must not bypass the exactly-once compensation owner");
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

        governedSchema.Should().Contain("mfg_processes");
    }

    [Fact]
    public void SemanticAnalysisRunner_ShouldExposeOnlyCloudAiReadPlannerAndLoggerDependencies()
    {
        var constructor = typeof(SemanticAnalysisRunner)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .Should()
            .ContainSingle()
            .Subject;

        var expectedDependencies = new[]
        {
            typeof(ICloudAiReadClient),
            typeof(ISemanticQueryPlanner),
            typeof(ILogger<SemanticAnalysisRunner>)
        };
        constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should()
            .BeEquivalentTo(expectedDependencies);
    }

    [Fact]
    public void CloudReadonlyAgentQuery_ShouldStayBehindPlanDraftConfirmation()
    {
        var agentTaskRoot = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks");
        var draftCoordinator = File.ReadAllText(Path.Combine(agentTaskRoot, "PlanAgentTaskCoordinator.cs"));
        var confirmationService = File.ReadAllText(Path.Combine(agentTaskRoot, "AgentPlanDraftConfirmationService.cs"));
        var runtimeTool = File.ReadAllText(Path.Combine(
            agentTaskRoot,
            "Runtime",
            "AgentRuntimeCloudReadonlyBasicToolService.cs"));

        draftCoordinator.Should().NotContain("ICloudAiReadClient");
        draftCoordinator.Should().NotContain("ICloudReadonlyAgentPlanService");
        draftCoordinator.Should().NotContain("QuerySemanticAsync");
        confirmationService.Should().Contain("ResolveCloudReadonlyIntentAsync");
        confirmationService.Should().Contain("cloudReadonlyPlanService.CreateIntentAsync");
        runtimeTool.Should().Contain("cloudReadonlyToolExecutor.ExecuteAsync");
    }

    [Fact]
    public void CloudReadOnlyReadonlyGrantSources_ShouldStayAlignedWithGovernedSchema()
    {
        var governedSchema = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.Services.Contracts",
            "Contracts",
            "CloudReadOnlyGovernedSchema.cs"));
        var applyGrantSql = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "cloud-readonly",
            "apply-readonly-grants.sql"));
        var checkGrantSql = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "cloud-readonly",
            "check-readonly-grants.sql"));
        var applyGrantScript = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "scripts",
            "apply-cloud-readonly-grants.sh"));
        var checkGrantScript = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "scripts",
            "check-cloud-readonly-grants.sh"));
        var provisionWorkflow = File.ReadAllText(Path.Combine(
            SolutionRoot,
            ".github",
            "workflows",
            "aicopilot-provision-cloud-readonly-db-role.yml"));
        var deployRelease = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "deploy-release.sh"));
        var localRelease = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "local-release.sh"));

        var governedTables = ExtractGovernedSchemaTables(governedSchema);
        governedTables.Should().BeEquivalentTo(["devices", "mfg_processes", "device_logs", "hourly_capacity", "pass_station_records"]);

        ExtractPublicTableNames(applyGrantSql)
            .Should()
            .BeEquivalentTo(governedTables, "the grant SQL is the authoritative role grant source");
        ExtractPublicTableNames(checkGrantSql)
            .Should()
            .BeEquivalentTo(governedTables, "the probe SQL must verify every governed table");

        applyGrantScript.Should().Contain("apply-readonly-grants.sql");
        applyGrantScript.Should().Contain("check-cloud-readonly-grants.sh");
        checkGrantScript.Should().Contain("check-readonly-grants.sql");
        provisionWorkflow.Should().Contain("actions/checkout");
        provisionWorkflow.Should().Contain("apply-readonly-grants.sql");
        provisionWorkflow.Should().Contain("check-readonly-grants.sql");
        provisionWorkflow.Should().NotContain("GRANT SELECT ON TABLE public.devices, public.mfg_processes");
        deployRelease.Should().Contain("check_cloud_readonly_preflight");
        deployRelease.Should().Contain("check-cloud-readonly-grants.sh");
        localRelease.Should().Contain("find scripts cloud-readonly -type f");
        localRelease.Should().Contain("prepare_support_release");
        applyGrantSql.Should().NotContain("GRANT SELECT ON ALL TABLES");
        applyGrantSql.Should().NotContain("ALTER DEFAULT PRIVILEGES");
    }

    [Fact]
    public void ModelProviderSmokeCheck_ShouldStayAvailableAsServerSideDiagnostic()
    {
        var smokeScript = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "scripts",
            "check-model-provider-openai.sh"));
        var deployRelease = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "deploy-release.sh"));
        var localRelease = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "local-release.sh"));
        var deployReadme = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "deploy",
            "enterprise-ai",
            "README.md"));
        var deployGuide = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "AICopilot 项目部署与维护指南.md"));

        smokeScript.Should().Contain("/chat/completions");
        smokeScript.Should().Contain("AICOPILOT_MODEL_SMOKE_BASE_URL");
        smokeScript.Should().Contain("AICOPILOT_MODEL_SMOKE_API_KEY or --api-key is required");
        smokeScript.Should().Contain("AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY");
        smokeScript.Should().Contain("--allow-dummy-key");
        smokeScript.Should().Contain("Model smoke API key uses dummy-key");
        smokeScript.Should().Contain("validate_model_base_url");
        smokeScript.Should().Contain("validate_header_value");
        smokeScript.Should().Contain("Model base URL must not include userinfo credentials.");
        smokeScript.Should().Contain("Model base URL must not include query string or fragment.");
        smokeScript.Should().Contain("cannot contain whitespace or HTTP header control characters.");
        smokeScript.Should().NotMatchRegex(@"10\.98\.\d{1,3}\.\d{1,3}");
        smokeScript.Should().NotContain("Authorization: Bearer dummy-key");
        smokeScript.Should().NotContain("sk-");
        deployRelease.Should().Contain("check_model_provider_preflight");
        deployRelease.Should().Contain("check-model-provider-openai.sh");
        localRelease.Should().Contain("find scripts cloud-readonly -type f");
        deployReadme.Should().Contain("check-model-provider-openai.sh");
        deployGuide.Should().Contain("check-model-provider-openai.sh");
    }

    [Fact]
    public void AgentTaskLifecycleRunRetryCancel_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskLifecycleCommandHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskLifecycleCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentTaskLifecycleCoordinator");
        handlers.Should().Contain("lifecycleCoordinator.QueueRunAsync");
        handlers.Should().Contain("lifecycleCoordinator.RetryAsync");
        handlers.Should().Contain("lifecycleCoordinator.CancelAsync");
        handlers.Should().Contain("AgentTaskDtoQueryService");
        handlers.Should().NotContain("IAgentTaskRunAttemptStore");
        handlers.Should().NotContain("IAgentTaskRunQueue runQueue");
        handlers.Should().NotContain("RecordRunQueueOperationAsync");
        handlers.Should().NotContain("CancelPendingApprovalsAsync");
        handlers.Should().NotMatchRegex(
            @"RunAgentTaskCommandHandler[\s\S]{0,500}\b(IReadRepository<ArtifactWorkspace>|IReadRepository<ApprovalRequest>|IAgentTaskRunQueueStore|AgentTaskDtoComposer)\b");
        handlers.Should().NotMatchRegex(
            @"RetryAgentTaskCommandHandler[\s\S]{0,500}\b(IReadRepository<ArtifactWorkspace>|IReadRepository<ApprovalRequest>|IAgentTaskRunQueueStore|AgentTaskDtoComposer)\b");
        handlers.Should().NotMatchRegex(
            @"CancelAgentTaskCommandHandler[\s\S]{0,500}\b(IReadRepository<ArtifactWorkspace>|IReadRepository<ApprovalRequest>|IAgentTaskRunQueueStore|AgentTaskDtoComposer)\b");

        coordinator.Should().Contain("IAgentTaskRunQueue runQueue");
        coordinator.Should().Contain("IAgentTaskRunAttemptStore");
        coordinator.Should().Contain("CancelPendingApprovalsAsync");
        coordinator.Should().Contain("RecordRunQueueOperationAsync");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskDtoQueryService>");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskLifecycleCoordinator>");
    }

    [Fact]
    public void PlanAgentTask_ShouldStayBehindCoordinator()
    {
        var handler = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskCommands.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "PlanAgentTaskCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handler.Should().Contain("PlanAgentTaskCoordinator");
        handler.Should().Contain("planCoordinator.PlanAsync");
        handler.Should().NotContain("IRepository<AgentTask>");
        handler.Should().NotContain("IRepository<ApprovalRequest>");
        handler.Should().NotContain("IReadRepository<Session>");
        handler.Should().NotContain("IReadRepository<UploadRecord>");
        handler.Should().NotContain("AgentTaskPlanPreparationService");
        handler.Should().NotContain("AgentTaskPlanStepBuilder");
        handler.Should().NotContain("AgentTaskPlanDocument");

        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IRepository<ApprovalRequest>");
        coordinator.Should().Contain("IReadRepository<Session>");
        coordinator.Should().Contain("IReadRepository<UploadRecord>");
        coordinator.Should().Contain("AgentTaskPlanPreparationService");
        coordinator.Should().Contain("AgentTaskPlanStepBuilder");
        coordinator.Should().Contain("AgentTaskPlanDocument");
        serviceRegistration.Should().Contain("AddScoped<PlanAgentTaskCoordinator>");
    }

    [Fact]
    public void UploadRecordCommand_ShouldStayBehindCoordinator()
    {
        var handler = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Uploads",
            "UploadRecords.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Uploads",
            "UploadRecordCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handler.Should().Contain("UploadRecordCoordinator");
        handler.Should().Contain("uploadRecordCoordinator.UploadAsync");
        handler.Should().NotContain("IRepository<UploadRecord>");
        handler.Should().NotContain("IReadRepository<Session>");
        handler.Should().NotContain("IReadRepository<AgentTask>");
        handler.Should().NotContain("IFileStorageService");
        handler.Should().NotContain("IRagDocumentUploadBridge");
        handler.Should().NotContain("IKnowledgeBaseAccessChecker");
        handler.Should().NotContain("IAuditLogWriter");
        handler.Should().NotContain("CanWriteAsync(");

        coordinator.Should().Contain("IRepository<UploadRecord>");
        coordinator.Should().Contain("IReadRepository<Session>");
        coordinator.Should().Contain("IReadRepository<AgentTask>");
        coordinator.Should().Contain("IPersistenceFileStorageService");
        coordinator.Should().Contain("IAuditLogWriter");
        coordinator.Should().NotContain("IRagDocumentUploadBridge");
        coordinator.Should().NotContain("IKnowledgeBaseAccessChecker");
        coordinator.Should().NotContain("CanWriteAsync(");
        coordinator.Should().Contain("AiGatewayUploadSecurityPolicy.ValidateAndNormalizeAsync");
        serviceRegistration.Should().Contain("AddScoped<UploadRecordCoordinator>");

        var obsoleteRagUploadBridgeReferences = Directory
            .EnumerateFiles(Path.Combine(SolutionRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Select(file => new
            {
                File = Path.GetRelativePath(SolutionRoot, file),
                Source = File.ReadAllText(file)
            })
            .Where(item => item.Source.Contains(
                "IRagDocumentUploadBridge",
                StringComparison.Ordinal))
            .Select(item => item.File)
            .ToArray();
        obsoleteRagUploadBridgeReferences.Should().BeEmpty(
            "knowledge-base documents have one write owner: the RAG document API");
    }

    [Fact]
    public void DatabaseBackedFileWrites_ShouldUseTheReconciliationBoundary()
    {
        typeof(IFileStorageService).GetMethod("SaveAsync").Should().BeNull();
        ConstructorParameterTypes(typeof(UploadDocumentCommandHandler))
            .Should().Contain(typeof(IPersistenceFileStorageService))
            .And.NotContain(typeof(IFileStorageService));
        ConstructorParameterTypes(typeof(UploadRecordCoordinator))
            .Should().Contain(typeof(IPersistenceFileStorageService))
            .And.NotContain(typeof(IFileStorageService));
        ConstructorParameterTypes(typeof(LocalPersistenceFileStorageService))
            .Should().Contain([
                typeof(LocalFileStorageService),
                typeof(IPersistenceFileReconciliationJournal),
                typeof(IPersistenceFileReconciliationLeaseManager),
                typeof(IPersistenceCommitScope)
            ]);
        ConstructorParameterTypes(typeof(PersistenceFileMaintenanceService))
            .Should().Contain([
                typeof(PersistenceCommitMarkerDbContext),
                typeof(IPersistenceFileReconciliationJournal),
                typeof(IPersistenceFileReconciliationLeaseManager),
                typeof(IFileStorageService)
            ]);

        var directProtocolCalls = Directory
            .EnumerateFiles(
                Path.Combine(SolutionRoot, "src", "services"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file => !file.EndsWith(
                "PersistenceFileContracts.cs",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File
                .ReadLines(file)
                .Select((line, index) => new
                {
                    File = Path.GetRelativePath(SolutionRoot, file),
                    Line = index + 1,
                    Text = line.Trim()
                }))
            .Where(item => Regex.IsMatch(
                item.Text,
                @"\.(?:ConfirmBestEffortAsync|RollbackBestEffortAsync|LeavePendingAsync)\s*\("))
            .Select(item => $"{item.File}:{item.Line}: {item.Text}")
            .ToArray();
        directProtocolCalls.Should().BeEmpty(
            "database-backed upload callers must use the single PersistenceFileCommitProtocol");

        var infrastructureRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "DependencyInjection.cs"));
        infrastructureRegistration.Should().Contain("AddSingleton<LocalFileStorageService>");
        infrastructureRegistration.Should().Contain("AddSingleton<IFileStorageService>");
        infrastructureRegistration.Should().Contain("AddSingleton<IPersistenceFileReconciliationJournal>");
        infrastructureRegistration.Should().Contain("AddScoped<IPersistenceFileReconciliationLeaseManager");
        infrastructureRegistration.Should().Contain("AddScoped<IPersistenceFileStorageService");

        var workerProgram = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "hosts",
            "AICopilot.DataWorker",
            "Program.cs"));
        workerProgram.Should().Contain("AddScoped<PersistenceFileMaintenanceService>");
        workerProgram.Should().Contain("AddHostedService<PersistenceMaintenanceWorker>");
        workerProgram.Should().Contain("ValidateOnStart");

        var efRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "DependencyInjection.cs"));
        efRegistration.Should().NotContain("AddScoped<PersistenceFileMaintenanceService>");
        efRegistration.Should().NotContain("AddScoped<IPersistenceFileReconciliationLeaseManager");

        var localStorage = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.Infrastructure",
            "Storage",
            "LocalFileStorageService.cs"));
        localStorage.Should().Contain("PlatformNotSupportedException");
        localStorage.Should().NotContain("MoveFileEx");
    }

    [Fact]
    public void ArtifactWorkspaceQueries_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceQueryHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("ArtifactWorkspaceQueryCoordinator");
        handlers.Should().Contain("workspaceQueryCoordinator.GetAsync");
        handlers.Should().Contain("workspaceQueryCoordinator.DownloadAsync");
        handlers.Should().NotMatchRegex(
            @"GetArtifactWorkspaceQueryHandler[\s\S]{0,500}\b(IReadRepository<ArtifactWorkspace>|IReadRepository<AgentTask>|IReadRepository<ApprovalRequest>|IArtifactWorkspaceFileStore|IIdentityAccessService|WorkspaceAccess|ArtifactWorkspaceMapper)\b");
        handlers.Should().NotMatchRegex(
            @"DownloadArtifactQueryHandler[\s\S]{0,500}\b(IReadRepository<ArtifactWorkspace>|IReadRepository<AgentTask>|IReadRepository<ApprovalRequest>|IArtifactWorkspaceFileStore|IAuditLogWriter|AgentAuditRecorder|IIdentityAccessService|AgentApprovalPermissions|WorkspaceAccess)\b");

        coordinator.Should().Contain("IReadRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IReadRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ApprovalRequest>");
        coordinator.Should().Contain("IArtifactWorkspaceFileStore");
        coordinator.Should().Contain("AgentAuditRecorder");
        coordinator.Should().Contain("IAuditLogWriter");
        coordinator.Should().Contain("IIdentityAccessService");
        coordinator.Should().Contain("WorkspaceAccess.LoadByCodeForOwnerOrPermissionAsync");
        coordinator.Should().Contain("RecordArtifactDownloadAsync");
        serviceRegistration.Should().Contain("AddScoped<ArtifactWorkspaceQueryCoordinator>");
    }

    [Fact]
    public void ArtifactVersioningQueries_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactVersioningQueryHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactVersioningQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("ArtifactVersioningQueryCoordinator");
        handlers.Should().Contain("versioningQueryCoordinator.GetContentAsync");
        handlers.Should().Contain("versioningQueryCoordinator.GetVersionsAsync");
        handlers.Should().Contain("versioningQueryCoordinator.DownloadVersionAsync");
        handlers.Should().Contain("versioningQueryCoordinator.GetDiffAsync");
        handlers.Should().NotContain("IReadRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("IReadRepository<AgentTask>");
        handlers.Should().NotContain("IReadRepository<ApprovalRequest>");
        handlers.Should().NotContain("IArtifactWorkspaceFileStore");
        handlers.Should().NotContain("AgentAuditRecorder");
        handlers.Should().NotContain("IAuditLogWriter");
        handlers.Should().NotContain("IIdentityAccessService");
        handlers.Should().NotContain("ArtifactVersioningAccess");
        handlers.Should().NotContain("ArtifactVersioningFiles");

        coordinator.Should().Contain("IReadRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IReadRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ApprovalRequest>");
        coordinator.Should().Contain("IArtifactWorkspaceFileStore");
        coordinator.Should().Contain("AgentAuditRecorder");
        coordinator.Should().Contain("IAuditLogWriter");
        coordinator.Should().Contain("ArtifactVersioningAccess.LoadArtifactForReadAsync");
        coordinator.Should().Contain("ArtifactVersioningFiles");
        serviceRegistration.Should().Contain("AddScoped<ArtifactVersioningQueryCoordinator>");
    }

    [Fact]
    public void ArtifactVersioningCommands_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactVersioningCommandHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactVersioningCommandCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("ArtifactVersioningCommandCoordinator");
        handlers.Should().Contain("versioningCommandCoordinator.UpdateContentAsync");
        handlers.Should().Contain("versioningCommandCoordinator.RestoreVersionAsync");
        handlers.Should().NotContain("IRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("IReadRepository<AgentTask>");
        handlers.Should().NotContain("IReadRepository<ApprovalRequest>");
        handlers.Should().NotContain("IArtifactWorkspaceFileStore");
        handlers.Should().NotContain("AgentAuditRecorder");
        handlers.Should().NotContain("IAuditLogWriter");
        handlers.Should().NotContain("IIdentityAccessService");
        handlers.Should().NotContain("ArtifactVersioningAccess");
        handlers.Should().NotContain("ArtifactVersioningFiles");

        coordinator.Should().Contain("IRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IReadRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ApprovalRequest>");
        coordinator.Should().Contain("IArtifactWorkspaceFileStore");
        coordinator.Should().Contain("AgentAuditRecorder");
        coordinator.Should().NotContain("IAuditLogWriter");
        coordinator.Should().Contain("ArtifactVersioningAccess.LoadArtifactForOwnerEditAsync");
        coordinator.Should().Contain("ArchiveCurrentVersionAsync");
        serviceRegistration.Should().Contain("AddScoped<ArtifactVersioningCommandCoordinator>");
    }

    [Fact]
    public void ArtifactWorkspaceP9_ShouldStayBehindCoordinator()
    {
        var queryHandlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceP9QueryHandlers.cs"));
        var commandHandlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceP9CommandHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceP9Coordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        queryHandlers.Should().Contain("ArtifactWorkspaceP9Coordinator");
        queryHandlers.Should().Contain("artifactWorkspaceP9Coordinator.GetPreviewAsync");
        commandHandlers.Should().Contain("ArtifactWorkspaceP9Coordinator");
        commandHandlers.Should().Contain("artifactWorkspaceP9Coordinator.CreateRevisionCommentAsync");
        commandHandlers.Should().Contain("artifactWorkspaceP9Coordinator.RegenerateDraftAsync");
        commandHandlers.Should().Contain("artifactWorkspaceP9Coordinator.SubmitForFinalApprovalAsync");

        var handlers = queryHandlers + commandHandlers;
        handlers.Should().NotContain("IRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("IRepository<AgentTask>");
        handlers.Should().NotContain("IRepository<ApprovalRequest>");
        handlers.Should().NotContain("IReadRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("IReadRepository<AgentTask>");
        handlers.Should().NotContain("IReadRepository<ApprovalRequest>");
        handlers.Should().NotContain("IArtifactWorkspaceFileStore");
        handlers.Should().NotContain("AgentAuditRecorder");
        handlers.Should().NotContain("IAuditLogWriter");
        handlers.Should().NotContain("IIdentityAccessService");
        handlers.Should().NotContain("ArtifactVersioningAccess");
        handlers.Should().NotContain("ArtifactWorkspaceP9Policy");

        coordinator.Should().Contain("IRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IRepository<ApprovalRequest>");
        coordinator.Should().Contain("IArtifactWorkspaceFileStore");
        coordinator.Should().Contain("AgentAuditRecorder");
        coordinator.Should().Contain("IAuditLogWriter");
        coordinator.Should().Contain("ArtifactPreviewBuilder.BuildAsync");
        coordinator.Should().Contain("ArtifactWorkspaceP9Policy.ValidateDraftMutationAsync");
        coordinator.Should().Contain("RecordFinalReviewSubmittedAsync");
        serviceRegistration.Should().Contain("AddScoped<ArtifactWorkspaceP9Coordinator>");
    }

    [Fact]
    public void AgentApprovalDecision_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentApprovalDecisionCommandHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentApprovalDecisionCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentApprovalDecisionCoordinator");
        handlers.Should().Contain("approvalDecisionCoordinator.ApproveAsync");
        handlers.Should().Contain("approvalDecisionCoordinator.RejectAsync");
        handlers.Should().NotContain("IRepository<");
        handlers.Should().NotContain("IAgentTaskRunQueue");
        handlers.Should().NotContain("AgentAuditRecorder");
        handlers.Should().NotContain("IIdentityAccessService");
        handlers.Should().NotContain("AgentPlanDraftConfirmationService");
        handlers.Should().NotContain("MessageTimelineProjectionWriter");

        coordinator.Should().Contain("IRepository<ApprovalRequest>");
        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IAgentTaskRunQueue runQueue");
        coordinator.Should().Contain("RecordApprovalDecisionAsync");
        coordinator.Should().Contain("StageApprovalDecidedAsync");
        coordinator.Should().Contain("runQueue.EnqueueAsync");
        serviceRegistration.Should().Contain("AddScoped<AgentApprovalDecisionCoordinator>");
    }

    [Fact]
    public void AgentApprovalQueries_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentApprovalQueryHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentApprovalQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentApprovalQueryCoordinator");
        handlers.Should().Contain("approvalQueryCoordinator.GetPendingAsync");
        handlers.Should().Contain("approvalQueryCoordinator.GetByTaskAsync");
        handlers.Should().NotContain("IRepository<AgentTask>");
        handlers.Should().NotContain("IReadRepository<ApprovalRequest>");
        handlers.Should().NotContain("IReadRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("PendingApprovalRequestsSpec");
        handlers.Should().NotContain("ApprovalRequestsByTaskSpec");
        handlers.Should().NotContain("AgentApprovalDtoMapper.Map");

        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ApprovalRequest>");
        coordinator.Should().Contain("IReadRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("PendingApprovalRequestsSpec");
        coordinator.Should().Contain("ApprovalRequestsByTaskSpec");
        coordinator.Should().Contain("AgentApprovalDtoMapper.Map");
        coordinator.Should().Contain("CanViewPendingApproval");
        serviceRegistration.Should().Contain("AddScoped<AgentApprovalQueryCoordinator>");
    }

    [Fact]
    public void ArtifactWorkspaceSubmitFinalize_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceCommandHandlers.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Workspaces",
            "ArtifactWorkspaceLifecycleCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("ArtifactWorkspaceLifecycleCoordinator");
        handlers.Should().Contain("workspaceLifecycleCoordinator.SubmitFinalReviewAsync");
        handlers.Should().Contain("workspaceLifecycleCoordinator.FinalizeAsync");
        handlers.Should().NotContain("IRepository<");
        handlers.Should().NotContain("IAgentTaskRunAttemptStore");
        handlers.Should().NotContain("IArtifactWorkspaceFileStore");
        handlers.Should().NotContain("AgentAuditRecorder");
        handlers.Should().NotContain("IAuditLogWriter");
        handlers.Should().NotContain("ICurrentUser");
        handlers.Should().NotContain("IIdentityAccessService");
        handlers.Should().NotContain("MessageTimelineProjectionWriter");
        handlers.Should().NotContain("WorkspaceAccess.");

        coordinator.Should().Contain("IRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IRepository<ApprovalRequest>");
        coordinator.Should().Contain("IAgentTaskRunAttemptStore");
        coordinator.Should().Contain("IArtifactWorkspaceFileStore fileStore");
        coordinator.Should().NotContain("IAuditLogWriter");
        coordinator.Should().Contain("RecordFinalReviewSubmittedAsync");
        coordinator.Should().Contain("RecordWorkspaceFinalizedAsync");
        coordinator.Should().Contain("StageApprovalRequestedAsync");
        coordinator.Should().Contain("StageWorkspaceFinalizedAsync");
        coordinator.Should().Contain("ReleaseRunLease");
        serviceRegistration.Should().Contain("AddScoped<ArtifactWorkspaceLifecycleCoordinator>");
    }

    [Fact]
    public void AgentTaskRuntimeEvents_ShouldStayBehindRecorder()
    {
        var runtime = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskRuntime.cs"));
        var eventRecorder = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "Runtime",
            "AgentRuntimeEventRecorder.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        runtime.Should().Contain("AgentRuntimeEventRecorder");
        runtime.Should().Contain("runtimeEventRecorder.BeginToolExecution");
        runtime.Should().Contain("runtimeEventRecorder.MarkToolExecutionSucceeded");
        runtime.Should().Contain("runtimeEventRecorder.RecordToolSucceededAsync");
        runtime.Should().Contain("runtimeEventRecorder.RecordToolFailedAsync");
        runtime.Should().Contain("runtimeEventRecorder.RecordToolRejectedAsync");
        runtime.Should().Contain("runtimeEventRecorder.StageApprovalRequestedAsync");
        runtime.Should().NotContain("IToolExecutionAuditStore");
        runtime.Should().NotContain("new ToolExecutionRecord");
        runtime.Should().NotContain("AgentAuditRecorder");
        runtime.Should().NotContain("MessageTimelineProjectionWriter");
        runtime.Should().NotContain("RecordToolAsync");
        runtime.Should().NotContain("timelineProjectionWriter");

        eventRecorder.Should().Contain("IToolExecutionAuditStore");
        eventRecorder.Should().Contain("new ToolExecutionRecord");
        eventRecorder.Should().Contain("AgentAuditRecorder");
        eventRecorder.Should().Contain("MessageTimelineProjectionWriter");
        eventRecorder.Should().Contain("RecordToolAsync");
        eventRecorder.Should().Contain("StageApprovalRequestedAsync");
        eventRecorder.Should().Contain("StageStepStartedAsync");
        eventRecorder.Should().Contain("StageStepCompletedAsync");
        serviceRegistration.Should().Contain("AddScoped<AgentRuntimeEventRecorder>");
    }

    [Fact]
    public void AgentTaskRunQueueWorker_ShouldStayBehindCoordinator()
    {
        var worker = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskRunQueueWorker.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskRunQueueWorkerCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        worker.Should().Contain("AgentTaskRunQueueWorkerCoordinator");
        worker.Should().Contain("workerCoordinator.RecoverExpiredStartedLeasesAsync");
        worker.Should().Contain("workerCoordinator.LeaseNextAsync");
        worker.Should().Contain("workerCoordinator.ExecuteQueueItemAsync");
        worker.Should().Contain("workerCoordinator.FailQueueItemAsync");
        worker.Should().NotContain("IAgentTaskRunQueueStore");
        worker.Should().NotContain("IRepository<AgentTask>");
        worker.Should().NotContain("IAgentTaskRunAttemptStore");
        worker.Should().NotContain("AgentAuditRecorder");
        worker.Should().NotContain("ToolExecutionRecordSanitizer");
        worker.Should().NotContain("new AgentTaskByIdSpec");
        worker.Should().NotContain("ResolveAttemptAsync");

        coordinator.Should().Contain("IAgentTaskRunQueueStore");
        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IAgentTaskRunAttemptStore");
        coordinator.Should().Contain("IAgentTaskRunQueue runQueue");
        coordinator.Should().Contain("IAgentTaskRuntime runtime");
        coordinator.Should().Contain("RecordRunQueueOperationAsync");
        coordinator.Should().Contain("ToolExecutionRecordSanitizer");
        coordinator.Should().Contain("ResolveAttemptAsync");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskRunQueueWorkerCoordinator>");
    }

    [Fact]
    public void SessionTimelineQuery_ShouldStayBehindCoordinator()
    {
        var handler = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Queries",
            "Sessions",
            "GetSessionTimeline.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Queries",
            "Sessions",
            "SessionTimelineQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handler.Should().Contain("SessionTimelineQueryCoordinator");
        handler.Should().Contain("timelineQueryCoordinator.GetAsync");
        handler.Should().NotContain("IReadRepository<");
        handler.Should().NotContain("IMessageTimelineProjectionStore");
        handler.Should().NotContain("ICurrentUser");
        handler.Should().NotContain("JsonDocument");
        handler.Should().NotContain("AgentTasksBySessionForUserSpec");
        handler.Should().NotContain("ApprovalRequestsByTasksSpec");
        handler.Should().NotContain("ArtifactWorkspaceByIdSpec");

        coordinator.Should().Contain("IReadRepository<Session>");
        coordinator.Should().Contain("IMessageTimelineProjectionStore");
        coordinator.Should().Contain("IReadRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ApprovalRequest>");
        coordinator.Should().Contain("IReadRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("AgentTasksBySessionForUserSpec");
        coordinator.Should().Contain("ApprovalRequestsByTasksSpec");
        coordinator.Should().Contain("ArtifactWorkspaceByIdSpec");
        coordinator.Should().Contain("ResolveStepOutput");
        serviceRegistration.Should().Contain("AddScoped<SessionTimelineQueryCoordinator>");
    }

    [Fact]
    public void AgentTaskToolExecutionQuery_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskAuditQueries.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskToolExecutionQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentTaskToolExecutionQueryCoordinator");
        handlers.Should().Contain("toolExecutionQueryCoordinator.GetAsync");
        handlers.Should().NotMatchRegex(
            @"GetAgentTaskToolExecutionsQueryHandler[\s\S]{0,500}\b(IToolExecutionAuditStore|IRepository<AgentTask>|ICurrentUser|ToolExecutionStatus|Pagination)\b");

        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IToolExecutionAuditStore");
        coordinator.Should().Contain("ICurrentUser");
        coordinator.Should().Contain("ToolExecutionStatus");
        coordinator.Should().Contain("Pagination");
        coordinator.Should().Contain("ToolRegistrationMapper.Map");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskToolExecutionQueryCoordinator>");
    }

    [Fact]
    public void AgentTaskAuditQueries_ShouldStayBehindCoordinator()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskAuditQueries.cs"));
        var coordinator = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskAuditQueryCoordinator.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentTaskAuditQueryCoordinator");
        handlers.Should().Contain("auditQueryCoordinator.GetSummaryAsync");
        handlers.Should().Contain("auditQueryCoordinator.GetRunAttemptsAsync");
        handlers.Should().Contain("auditQueryCoordinator.GetRunQueueAsync");
        handlers.Should().NotMatchRegex(
            @"GetAgentTaskAuditSummaryQueryHandler[\s\S]{0,500}\b(IRepository<AgentTask>|IReadRepository<ArtifactWorkspace>|IToolExecutionAuditStore|IAuditLogQueryService|ICurrentUser|JsonDocument|ArtifactWorkspaceByIdSpec)\b");
        handlers.Should().NotMatchRegex(
            @"GetAgentTaskRunAttemptsQueryHandler[\s\S]{0,500}\b(IRepository<AgentTask>|IAgentTaskRunAttemptStore|ICurrentUser|Pagination|AgentTaskRunAttemptDtoMapper)\b");
        handlers.Should().NotMatchRegex(
            @"GetAgentTaskRunQueueQueryHandler[\s\S]{0,500}\b(IRepository<AgentTask>|IAgentTaskRunQueueStore|ICurrentUser|Pagination|AgentTaskRunQueueItemDtoMapper)\b");

        coordinator.Should().Contain("IRepository<AgentTask>");
        coordinator.Should().Contain("IReadRepository<ArtifactWorkspace>");
        coordinator.Should().Contain("IToolExecutionAuditStore");
        coordinator.Should().Contain("IAuditLogQueryService");
        coordinator.Should().Contain("IAgentTaskRunAttemptStore");
        coordinator.Should().Contain("IAgentTaskRunQueueStore");
        coordinator.Should().Contain("ICurrentUser");
        coordinator.Should().Contain("JsonDocument");
        coordinator.Should().Contain("ArtifactWorkspaceByIdSpec");
        coordinator.Should().Contain("AgentTaskRunAttemptDtoMapper.Map");
        coordinator.Should().Contain("AgentTaskRunQueueItemDtoMapper.Map");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskAuditQueryCoordinator>");
    }

    [Fact]
    public void AgentTaskQueries_ShouldKeepDtoCompositionBehindService()
    {
        var handlers = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskQueries.cs"));
        var dtoService = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "AgentTasks",
            "AgentTaskDtoComposer.cs"));
        var serviceRegistration = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "DependencyInjection.cs"));

        handlers.Should().Contain("AgentTaskDtoQueryService");
        handlers.Should().Contain("dtoQueryService.MapAsync");
        handlers.Should().Contain("dtoQueryService.MapManyAsync");
        handlers.Should().NotContain("IReadRepository<ArtifactWorkspace>");
        handlers.Should().NotContain("IReadRepository<ApprovalRequest>");
        handlers.Should().NotContain("IAgentTaskRunQueueStore");
        handlers.Should().NotContain("AgentTaskDtoComposer");

        dtoService.Should().Contain("IReadRepository<ArtifactWorkspace>");
        dtoService.Should().Contain("IReadRepository<ApprovalRequest>");
        dtoService.Should().Contain("IAgentTaskRunQueueStore");
        dtoService.Should().Contain("AgentTaskDtoComposer.MapAsync");
        serviceRegistration.Should().Contain("AddScoped<AgentTaskDtoQueryService>");
    }

    [Fact]
    public void AiGatewayHandlers_ShouldNotAddMultiPersistenceDependencyDebt()
    {
        var knownDebt = new HashSet<string>(StringComparer.Ordinal);
        var resolvedDebt = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/services/AICopilot.AiGatewayService/Uploads/UploadRecords.cs:UploadRecordCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningCommandHandlers.cs:RestoreArtifactVersionCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningCommandHandlers.cs:UpdateArtifactContentCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningQueryHandlers.cs:DownloadArtifactVersionQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningQueryHandlers.cs:GetArtifactContentQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningQueryHandlers.cs:GetArtifactVersionDiffQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactVersioningQueryHandlers.cs:GetArtifactVersionsQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9CommandHandlers.cs:CreateArtifactRevisionCommentCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9CommandHandlers.cs:RegenerateDraftArtifactCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9CommandHandlers.cs:SubmitArtifactForFinalApprovalCommandHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceP9QueryHandlers.cs:GetAgentArtifactPreviewQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceQueryHandlers.cs:DownloadArtifactQueryHandler",
            "src/services/AICopilot.AiGatewayService/Workspaces/ArtifactWorkspaceQueryHandlers.cs:GetArtifactWorkspaceQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentApprovalQueryHandlers.cs:GetAgentTaskApprovalsQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentApprovalQueryHandlers.cs:GetPendingAgentApprovalsQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskAuditQueries.cs:GetAgentTaskAuditSummaryQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskAuditQueries.cs:GetAgentTaskRunAttemptsQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskAuditQueries.cs:GetAgentTaskRunQueueQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskCommands.cs:PlanAgentTaskCommandHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskLifecycleCommandHandlers.cs:CancelAgentTaskCommandHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskLifecycleCommandHandlers.cs:RetryAgentTaskCommandHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskLifecycleCommandHandlers.cs:RunAgentTaskCommandHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskQueries.cs:GetAgentTaskQueryHandler",
            "src/services/AICopilot.AiGatewayService/AgentTasks/AgentTaskQueries.cs:GetListAgentTasksBySessionQueryHandler",
            "src/services/AICopilot.AiGatewayService/Queries/Sessions/GetSessionTimeline.cs:GetSessionTimelineQueryHandler"
        };

        var violations = FindMultiPersistenceHandlerDependencies();

        violations
            .Where(violation => !knownDebt.Contains(violation.Key))
            .Select(violation => violation.Description)
            .Should()
            .BeEmpty("new AiGateway handlers must not directly inject three or more repository/store dependencies");
        violations
            .Where(violation => resolvedDebt.Contains(violation.Key))
            .Select(violation => violation.Description)
            .Should()
            .BeEmpty("handler persistence dependency debt that has been moved behind coordinators must not regress");
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
        generator.Should().Contain("request.AllowedColumns.TryGetValue");
        generator.Should().Contain("CloudReadOnlyGovernedSchema.AllowedColumnTypes");
        generator.Should().Contain("CloudReadOnlyGovernedSchema.JoinHints");
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

    [Theory]
    [InlineData("McpServerDbContext")]
    [InlineData("DataAnalysisDbContext")]
    [InlineData("RagDbContext")]
    [InlineData("AiGatewayDbContext")]
    public void SplitContextMigrations_ShouldNotCreateOutboxTable(string contextName)
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            contextName);

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
    public void DeadOutboxDetachmentMigrations_ShouldBeSnapshotOnly()
    {
        Microsoft.EntityFrameworkCore.Migrations.Migration[] migrations =
        [
            new EntityFrameworkCore.Migrations.DataAnalysisDbContext.DetachDeadOutboxMapping(),
            new EntityFrameworkCore.Migrations.McpServerDbContext.DetachDeadOutboxMapping()
        ];

        foreach (var migration in migrations)
        {
            migration.UpOperations.Should().BeEmpty(
                $"{migration.GetType().FullName} only detaches dead model metadata and must not change the database");
            migration.DownOperations.Should().BeEmpty(
                $"{migration.GetType().FullName} only detaches dead model metadata and must not change the database");
        }
    }

    [Theory]
    [InlineData(typeof(AiCopilotDbContext))]
    [InlineData(typeof(DataAnalysisDbContext))]
    [InlineData(typeof(McpServerDbContext))]
    [InlineData(typeof(AiGatewayDbContext))]
    [InlineData(typeof(RagDbContext))]
    public void NormalRepositoryDbContexts_ShouldNotOwnPersistenceAlgorithms(Type contextType)
    {
        var saveOverride = contextType.GetMethod(
                nameof(DbContext.SaveChangesAsync),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                [typeof(CancellationToken)]);

        saveOverride.Should().BeNull(contextType.FullName);
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
    public void IdentityResultCommands_ShouldUseTransactionalResultBoundary()
    {
        var commandRoot = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.IdentityService",
            "Commands");
        var commandSources = Directory
            .EnumerateFiles(commandRoot, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        var commandHandlers = typeof(CreateRoleCommandHandler).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Namespace?.StartsWith(
                "AICopilot.IdentityService.Commands",
                StringComparison.Ordinal) == true)
            .Select(type => new
            {
                HandlerType = type,
                CommandInterface = type.GetInterfaces().SingleOrDefault(contract =>
                    contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            })
            .Where(item => item.CommandInterface is not null)
            .OrderBy(item => item.HandlerType.FullName, StringComparer.Ordinal)
            .ToArray();
        var nonResultHandlers = commandHandlers
            .Where(item => !typeof(AICopilot.SharedKernel.Result.IResult).IsAssignableFrom(
                item.CommandInterface!.GetGenericArguments()[1]))
            .Select(item => item.HandlerType.FullName)
            .ToArray();
        nonResultHandlers.Should().BeEmpty(
            "Identity commands must return Result so rejected writes can roll back atomically");

        var resultCommandHandlers = commandHandlers
            .Where(item => typeof(AICopilot.SharedKernel.Result.IResult).IsAssignableFrom(
                item.CommandInterface!.GetGenericArguments()[1]))
            .ToArray();

        resultCommandHandlers.Should().HaveCount(10);

        foreach (var handler in resultCommandHandlers)
        {
            var sourceEntries = commandSources
                .Where(entry => Regex.IsMatch(
                    entry.Value,
                    $@"\bclass\s+{Regex.Escape(handler.HandlerType.Name)}\b"))
                .ToArray();
            sourceEntries.Should().ContainSingle(handler.HandlerType.FullName);
            var sourceEntry = sourceEntries[0];
            var source = sourceEntry.Value;
            var commandFile = Path.GetFileName(sourceEntry.Key);
            var constructorParameters = handler.HandlerType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            constructorParameters.Should().Contain(typeof(ITransactionalExecutionService), commandFile);
            source.Should().NotContain("IAuditLogWriter", commandFile);
            source.Should().NotContain("auditLogWriter.SaveChangesAsync", commandFile);
            source.Should().NotContain("DbContext", commandFile);

            if (handler.HandlerType == typeof(LoginUserCommandHandler))
            {
                source.Should().Contain("transactionalExecutionService.ExecuteAsync", commandFile);
                source.Should().NotContain("ExecuteResultAsync", commandFile);
                continue;
            }

            constructorParameters.Should().Contain(typeof(IIdentityAuditLogWriter), commandFile);
            source.Should().Contain("transactionalExecutionService.ExecuteResultAsync", commandFile);
        }
    }

    [Fact]
    public void ApprovedIdentityNonCommandWriters_ShouldStayInsideExplicitTransactionBoundary()
    {
        var approvedFiles = new[]
        {
            Path.Combine(
                SolutionRoot,
                "src", "services", "AICopilot.IdentityService", "Services",
                "CloudIdentityStatusValidator.cs"),
            Path.Combine(
                SolutionRoot,
                "src", "hosts", "AICopilot.MigrationWorkApp",
                "MigrationWorkerIdentitySeeder.cs")
        };

        foreach (var approvedFile in approvedFiles)
        {
            var source = File.ReadAllText(approvedFile);
            source.Should().Contain("ITransactionalExecutionService", approvedFile);
            source.Should().Contain("transactionalExecutionService.ExecuteAsync", approvedFile);
            source.Should().NotContain("ExecuteResultAsync", approvedFile);
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
        var mainContext = File.ReadAllText(mainContextFile);
        var identityContext = File.ReadAllText(identityContextFile);
        var dependencyInjection = File.ReadAllText(dependencyInjectionFile);

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
        dependencyInjection.Should().Contain("IdentityTransactionalExecutionService");
        dependencyInjection.Should().NotContain("EfTransactionalExecutionService");

        typeof(IdentityTransactionalExecutionService).GetConstructors()
            .Should().ContainSingle().Which.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Should().BeEquivalentTo(
                [typeof(IdentityStoreDbContext), typeof(PersistenceCommitEngine)]);
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
    public void RagDbContext_ShouldOwnRagTables_AndUseApplicationAssignedDocumentIds()
    {
        using var dbContext = new RagDbContext(
            new DbContextOptionsBuilder<RagDbContext>()
                .UseNpgsql("Host=localhost;Database=architecture;Username=test;Password=test")
                .Options);
        var mappedTables = dbContext.Model.GetEntityTypes()
            .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
            .ToArray();

        mappedTables.Should().Contain(new[]
        {
            "rag.embedding_models",
            "rag.knowledge_bases",
            "rag.documents",
            "rag.document_chunks"
        });
        mappedTables.Should().NotContain("outbox.outbox_messages");
        dbContext.Model.FindEntityType(typeof(Document))!
            .FindProperty(nameof(Document.Id))!
            .ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
    }

    [Fact]
    public void AiGatewayDbContext_ShouldOwnAiGatewayTables_AndNotMapSharedOutbox()
    {
        using var dbContext = new AiGatewayDbContext(
            new DbContextOptionsBuilder<AiGatewayDbContext>()
                .UseNpgsql("Host=localhost;Database=architecture;Username=test;Password=test")
                .Options);
        var mappedTables = dbContext.Model.GetEntityTypes()
            .Select(entity => $"{entity.GetSchema()}.{entity.GetTableName()}")
            .ToArray();

        mappedTables.Should().Contain(new[]
        {
            "aigateway.language_models",
            "aigateway.conversation_templates",
            "aigateway.approval_policies",
            "aigateway.sessions",
            "aigateway.messages"
        });
        mappedTables.Should().NotContain("outbox.outbox_messages");
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

    private static IReadOnlyCollection<HandlerPersistenceDependencyViolation> FindMultiPersistenceHandlerDependencies()
    {
        var serviceRoot = Path.Combine(SolutionRoot, "src", "services", "AICopilot.AiGatewayService");
        var handlerPattern = new Regex(
            @"public\s+(?:sealed\s+)?class\s+(?<name>\w+Handler)\s*\((?<parameters>[\s\S]*?)\)\s*:\s*I(?:Command|Query)Handler<",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var persistenceDependencyPattern = new Regex(
            @"\b(?:I(?:Read)?Repository<[^>]+>|I[A-Za-z0-9_]*(?:Store|ProjectionStore|AuditStore)\b|IAuditLogQueryService\b|IFinalAgentContextStore\b)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var violations = new List<HandlerPersistenceDependencyViolation>();

        foreach (var file in Directory.EnumerateFiles(serviceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            foreach (Match handlerMatch in handlerPattern.Matches(source))
            {
                var handlerName = handlerMatch.Groups["name"].Value;
                var parameters = handlerMatch.Groups["parameters"].Value;
                var dependencies = persistenceDependencyPattern
                    .Matches(parameters)
                    .Select(match => match.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (dependencies.Length < 3)
                {
                    continue;
                }

                var relativePath = Path
                    .GetRelativePath(SolutionRoot, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                var key = $"{relativePath}:{handlerName}";
                violations.Add(new HandlerPersistenceDependencyViolation(
                    key,
                    $"{key} has {dependencies.Length} persistence dependencies: {string.Join(", ", dependencies)}"));
            }
        }

        return violations
            .OrderBy(violation => violation.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record HandlerPersistenceDependencyViolation(string Key, string Description);

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

    private static IReadOnlyCollection<Type> ConstructorParameterTypes(Type type)
    {
        return type.GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
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

    private static IReadOnlyCollection<string> ExtractGovernedSchemaTables(string source)
    {
        var allowedTablesBlock = ExtractBetween(
            source,
            "public static readonly IReadOnlySet<string> AllowedTables",
            "public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedColumns");

        return Regex
            .Matches(allowedTablesBlock, "\"(?<table>[a-z_]+)\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["table"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyCollection<string> ExtractPublicTableNames(string source)
    {
        return Regex
            .Matches(source, @"public\.(?<table>[a-z_]+)", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["table"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
