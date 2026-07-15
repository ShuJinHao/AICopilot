using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
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
using AICopilot.EntityFrameworkCore.Persistence;
using AICopilot.EntityFrameworkCore.Repository;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.HttpApi;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.IdentityService.Authorization;
using AICopilot.IdentityService.Commands;
using AICopilot.Infrastructure;
using AICopilot.Infrastructure.Storage;
using AICopilot.RagService.Commands.Documents;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting;
using AICopilot.Services.CrossCutting.Behaviors;
using AICopilot.SharedKernel.Domain;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AICopilot.ArchitectureTests;

[Collection("ProcessEnvironment")]
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
    public void EnabledAdminInvariant_ShouldUseSharedTransactionalGuardTypes()
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

        var implementationAssemblies = new[]
        {
            typeof(IdentityTransactionalExecutionService).Assembly,
            typeof(EnabledAdminInvariantPolicy).Assembly,
            typeof(ITransactionalExecutionService).Assembly
        }.Distinct().ToArray();
        implementationAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(ITransactionalExecutionService).IsAssignableFrom(type))
            .Should().ContainSingle()
            .Which.Should().Be<IdentityTransactionalExecutionService>();
        implementationAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                           typeof(IIdentityEnabledAdminInvariantGuard).IsAssignableFrom(type))
            .Should().ContainSingle()
            .Which.Should().Be<PostgresIdentityEnabledAdminInvariantGuard>();

        const string encryptionKeyVariable = "AICopilotSecurity__ApiKeyEncryptionKey";
        var originalEncryptionKey = Environment.GetEnvironmentVariable(encryptionKeyVariable);
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ai-copilot"] =
                "Host=localhost;Database=architecture;Username=test;Password=test",
            ["AiGateway:Deployment:Mode"] = "SingleInstance",
            ["AiGateway:FinalAgentContextStore:Provider"] = "Memory"
        });
        try
        {
            Environment.SetEnvironmentVariable(
                encryptionKeyVariable,
                "architecture-test-key-do-not-use-outside-tests");
            builder.AddInfrastructures();
            builder.AddApplicationService();
        }
        finally
        {
            Environment.SetEnvironmentVariable(encryptionKeyVariable, originalEncryptionKey);
        }

        AssertExactFinalScopedBinding(
            builder.Services,
            typeof(ITransactionalExecutionService),
            typeof(IdentityTransactionalExecutionService));
        AssertExactFinalScopedBinding(
            builder.Services,
            typeof(IIdentityEnabledAdminInvariantGuard),
            typeof(PostgresIdentityEnabledAdminInvariantGuard));
    }

    [Fact]
    public void DatabaseBackedFileWrites_ShouldUseReconciliationBoundary()
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
    }

    [Fact]
    public void PreviewPackages_ShouldStayInExplicitDebtWhitelist()
    {
        var allowedPreviewPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.SemanticKernel.Connectors.Qdrant|1.74.0-preview"
        };
        var previewMarkers = new[] { "-preview", "-alpha", "-beta", "-rc" };
        var violations = EnumeratePackageReferences(Path.Combine(SolutionRoot, "src"))
            .Where(package => previewMarkers.Any(marker =>
                package.Version.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Where(package => !allowedPreviewPackages.Contains($"{package.Include}|{package.Version}"))
            .Select(package => $"{package.File}: {package.Include} {package.Version}")
            .ToArray();

        violations.Should().BeEmpty("preview packages require explicit approval and debt tracking");
    }

    [Fact]
    public void ServiceProjects_ShouldNotReferenceSharpTokenPackage()
    {
        var violations = EnumeratePackageReferences(Path.Combine(SolutionRoot, "src", "services"))
            .Where(package => string.Equals(
                package.Include,
                "SharpToken",
                StringComparison.OrdinalIgnoreCase))
            .Select(package => $"{package.File}: {package.Include}")
            .ToArray();

        violations.Should().BeEmpty("service projects depend on the token-estimator contract");
    }

    [Fact]
    public void ConversationTemplate_ShouldNotExposePublicSetters()
    {
        var publicSetters = typeof(ConversationTemplate)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => property.Name)
            .ToArray();

        publicSetters.Should().BeEmpty();
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
        var violations = aggregateRootTypes
            .Where(type => type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
                ?.SetMethod?.IsPublic == true)
            .Select(type => $"{type.Name}.Id")
            .ToArray();

        violations.Should().BeEmpty("aggregate identity changes stay inside the aggregate");
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
            .Where(item => item.Key.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
                ?.PropertyType != item.Value)
            .Select(item => $"{item.Key.Name}.Id must be {item.Value.Name}")
            .ToArray();

        violations.Should().BeEmpty();
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

        publicSetters.Should().BeEmpty("domain state changes use aggregate behavior");
    }

    [Fact]
    public void ValueObjects_ShouldOnlyExposeInitSetters()
    {
        var mutableSetters = new[] { typeof(ModelParameters), typeof(TemplateSpecification) }
            .SelectMany(type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.SetMethod?.IsPublic == true && !IsInitOnly(property))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        mutableSetters.Should().BeEmpty();
    }

    [Fact]
    public void EntityAbstractions_ShouldNotExposePublicIdSetters()
    {
        typeof(IEntity<Guid>).GetProperty(nameof(IEntity<Guid>.Id))!
            .SetMethod.Should().BeNull();

        var baseEntityIdSetter = typeof(BaseEntity<Guid>)
            .GetProperty(nameof(BaseEntity<Guid>.Id))!
            .SetMethod;
        baseEntityIdSetter.Should().NotBeNull();
        baseEntityIdSetter!.IsPublic.Should().BeFalse();
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
            migration.UpOperations.Should().BeEmpty();
            migration.DownOperations.Should().BeEmpty();
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
    public void UnifiedMediatRPipeline_ShouldRegisterOrderedBehaviorsIdempotently()
    {
        var services = new ServiceCollection();

        services.AddAICopilotMediatRPipeline();
        services.AddAICopilotMediatRPipeline();

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should()
            .Equal(
                typeof(TelemetryBehavior<,>),
                typeof(ValidationBehavior<,>),
                typeof(AuthorizationBehavior<,>));
        services
            .Where(descriptor => descriptor.ServiceType == typeof(IStreamPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should()
            .Equal(
                typeof(TelemetryStreamBehavior<,>),
                typeof(ValidationStreamBehavior<,>),
                typeof(AuthorizationStreamBehavior<,>));
    }

    [Fact]
    public void PipelineBehaviors_ShouldKeepPersistenceOutOfConstructorGraph()
    {
        Type[] behaviorTypes =
        [
            typeof(TelemetryBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(TelemetryStreamBehavior<,>),
            typeof(ValidationStreamBehavior<,>),
            typeof(AuthorizationStreamBehavior<,>)
        ];

        var forbiddenParameters = behaviorTypes
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.Namespace?.StartsWith("AICopilot.EntityFrameworkCore", StringComparison.Ordinal) == true ||
                           type.Name is "ITransactionalExecutionService" or "IAuditLogWriter" ||
                           type.Name.EndsWith("DbContext", StringComparison.Ordinal))
            .ToArray();

        forbiddenParameters.Should().BeEmpty();
    }

    [Fact]
    public void PipelineTelemetry_ShouldExposeStableActivitySourceName()
    {
        PipelineTelemetry.ActivitySourceName.Should().Be("AICopilot.MediatR");
        PipelineTelemetry.ActivitySource.Name.Should().Be(PipelineTelemetry.ActivitySourceName);
    }

    private static void AssertExactFinalScopedBinding(
        IServiceCollection services,
        Type serviceType,
        Type implementationType)
    {
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .ToArray();

        descriptors.Should().ContainSingle(
            $"{serviceType.FullName} must not have an earlier, fallback, noop, or post-override binding");
        descriptors[0].Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptors[0].ImplementationType.Should().Be(implementationType);
        descriptors[0].ImplementationFactory.Should().BeNull();
        descriptors[0].ImplementationInstance.Should().BeNull();
    }

    private static Type[] ConstructorParameterTypes(Type type)
        => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

    private static IEnumerable<PackageReference> EnumeratePackageReferences(string root)
    {
        foreach (var projectFile in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(projectFile);
            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "PackageReference"))
            {
                yield return new PackageReference(
                    Path.GetRelativePath(SolutionRoot, projectFile),
                    reference.Attribute("Include")?.Value ?? string.Empty,
                    reference.Attribute("Version")?.Value
                    ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value
                    ?? string.Empty);
            }
        }
    }

    private static bool IsInitOnly(PropertyInfo property)
        => property.SetMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit)) == true;

    private static string FindSolutionRoot()
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

        throw new DirectoryNotFoundException("AICopilot repository root was not found.");
    }

    private sealed record PackageReference(string File, string Include, string Version);
}
