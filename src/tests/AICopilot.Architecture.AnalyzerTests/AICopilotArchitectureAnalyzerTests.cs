using AICopilot.Architecture.Analyzers;

namespace AICopilot.Architecture.AnalyzerTests;

public sealed class AICopilotArchitectureAnalyzerTests
{
    private static readonly FixtureAssemblyReference SharedKernelReference = new(
        "AICopilot.SharedKernel",
        """
        using System;
        using System.Threading.Tasks;
        namespace AICopilot.SharedKernel.Domain
        {
            public interface IAggregateRoot { }
        }
        namespace AICopilot.SharedKernel.Repository
        {
            public interface IRepository<T> where T : class
            {
                Task AddAsync(T entity);
                Task UpdateAsync(T entity);
            }
            public interface IReadRepository<T> where T : class { }
        }
        namespace AICopilot.SharedKernel.Messaging
        {
            public interface ICommand { }
            public interface ICommand<T> : ICommand { }
            public interface IQuery<T> { }
        }
        namespace AICopilot.SharedKernel.Ai
        {
            public enum AiToolExternalSystemType { Unknown, CloudReadOnly }
            public enum AiToolCapabilityKind { ReadOnlyQuery, Diagnostics, SideEffecting }
            public sealed record AiToolSafetyDescriptor(
                bool ReadOnlyDeclared,
                AiToolCapabilityKind CapabilityKind,
                AiToolExternalSystemType ExternalSystemType)
            {
                public static AiToolSafetyDescriptor Create(
                    bool readOnlyDeclared,
                    AiToolCapabilityKind capabilityKind,
                    AiToolExternalSystemType externalSystemType) =>
                    new(readOnlyDeclared, capabilityKind, externalSystemType);
            }
            public static class AiToolSafetyPolicy { }
        }
        """);

    private static readonly FixtureAssemblyReference ServicesContractsReference = new(
        "AICopilot.Services.Contracts",
        """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace AICopilot.Services.Contracts
        {
            public interface ITransactionalExecutionService { Task ExecuteAsync(Func<Task> action); }
            public interface IIdentityEnabledAdminInvariantGuard { Task AcquireAsync(); }
            public interface ICloudAiReadClient { int Read(); }
            public interface ICloudReadOnlyTextToSqlGenerator { }
            public sealed record AuditLogWriteRequest;
            public interface IAuditLogWriter
            {
                Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default);
                Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
            }
            public sealed record ModelQuotaReservationRequest;
            public sealed record ModelQuotaReservationOutcome;
            public sealed record ModelQuotaSettlement;
            public enum ModelQuotaReservationResult { Granted }
            public interface IModelQuotaReservationStore
            {
                Task<ModelQuotaReservationOutcome> TryReserveAsync(
                    ModelQuotaReservationRequest request,
                    CancellationToken cancellationToken = default);
                Task<ModelQuotaReservationResult> SettleAsync(
                    ModelQuotaSettlement settlement,
                    CancellationToken cancellationToken = default);
                Task<int> ReclaimExpiredAsync(
                    DateTimeOffset nowUtc,
                    int maxItems,
                    CancellationToken cancellationToken = default);
            }
        }
        """);

    private static readonly FixtureAssemblyReference InvariantInfrastructureReference = new(
        "AICopilot.EntityFrameworkCore",
        """
        using System;
        using System.Threading.Tasks;
        using AICopilot.Services.Contracts;
        namespace AICopilot.EntityFrameworkCore.Transactions
        {
            public sealed class IdentityTransactionalExecutionService : ITransactionalExecutionService
            {
                public Task ExecuteAsync(Func<Task> action) => action();
            }
        }
        namespace AICopilot.EntityFrameworkCore.Locking
        {
            public sealed class PostgresIdentityEnabledAdminInvariantGuard : IIdentityEnabledAdminInvariantGuard
            {
                public Task AcquireAsync() => Task.CompletedTask;
            }
        }
        """);

    private static readonly FixtureAssemblyReference CrossCuttingReference = new(
        "AICopilot.Services.CrossCutting",
        """
        using System;
        namespace AICopilot.Services.CrossCutting.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ResourceAuthorizationOwnerAttribute(Type ownerType) : Attribute { }
        }
        """);

    private static readonly FixtureAssemblyReference IdentityServiceReference = new(
        "AICopilot.IdentityService",
        """
        using System.Threading.Tasks;
        namespace AICopilot.IdentityService.Authorization
        {
            public sealed class EnabledAdminInvariantPolicy
            {
                public Task AcquireAsync() => Task.CompletedTask;
            }
        }
        """);

    private static readonly FixtureAssemblyReference AgentPluginReference = new(
        "AICopilot.AgentPlugin",
        """
        namespace AICopilot.AgentPlugin
        {
            public enum ChatExposureMode { Disabled, Advisory, Control }
            public interface IAgentPlugin
            {
                string Description { get; }
                ChatExposureMode ChatExposureMode { get; }
            }
            public abstract class AgentPluginBase : IAgentPlugin
            {
                public virtual string Description => string.Empty;
                public virtual ChatExposureMode ChatExposureMode => ChatExposureMode.Disabled;
            }
        }
        """);

    private static readonly FixtureAssemblyReference CloudReadClientReference = new(
        "AICopilot.Infrastructure",
        """
        namespace AICopilot.Infrastructure.CloudRead
        {
            public sealed class CloudAiReadClient : AICopilot.Services.Contracts.ICloudAiReadClient
            {
                public int Read() => 42;
            }
        }
        """);

    private static readonly FixtureAssemblyReference EntityFrameworkReference = new(
        "Microsoft.EntityFrameworkCore",
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public enum EntityState { Detached, Unchanged, Deleted, Modified, Added }
            public class DbContext
            {
                public virtual int SaveChanges() => 0;
                public ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class => new();
            }
            public static class EntityFrameworkQueryableExtensions
            {
                public static int ExecuteDelete<T>(this System.Collections.Generic.IEnumerable<T> source) => 0;
            }
        }
        namespace Microsoft.EntityFrameworkCore.ChangeTracking
        {
            public class EntityEntry<TEntity> where TEntity : class
            {
                public Microsoft.EntityFrameworkCore.EntityState State { get; set; }
            }
        }
        """);

    private static readonly FixtureAssemblyReference DependencyInjectionAbstractionsReference = new(
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        """
        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ActivatorUtilities
            {
                public static T CreateInstance<T>(System.IServiceProvider provider, params object[] parameters) => default!;
                public static T GetServiceOrCreateInstance<T>(System.IServiceProvider provider) => default!;
            }
        }
        """);

    private static readonly FixtureAssemblyReference DapperReference = new(
        "Dapper",
        """
        namespace Dapper
        {
            public static class SqlMapper
            {
                public static System.Threading.Tasks.Task<int> ExecuteAsync(
                    object connection,
                    string sql,
                    object? param = null) => System.Threading.Tasks.Task.FromResult(1);
            }
        }
        """);

    private static readonly FixtureAssemblyReference NpgsqlReference = new(
        "Npgsql",
        """
        namespace Npgsql
        {
            public sealed class NpgsqlConnection
            {
                public void Open() { }
            }
        }
        """);

    private static readonly FixtureAssemblyReference IdentityStoresReference = new(
        "Microsoft.Extensions.Identity.Stores",
        """
        namespace Microsoft.AspNetCore.Identity
        {
            public class IdentityUser<TKey>
            {
                public bool LockoutEnabled { get; set; }
                public System.DateTimeOffset? LockoutEnd { get; set; }
            }
            public class IdentityUserRole<TKey> { }
        }
        """);

    private static readonly FixtureAssemblyReference IdentityCoreReference = new(
        "Microsoft.Extensions.Identity.Core",
        """
        using System.Threading.Tasks;
        namespace Microsoft.AspNetCore.Identity
        {
            public class UserManager<T>
            {
                public Task RemoveFromRoleAsync(T user, string role) => Task.CompletedTask;
                public Task RemoveFromRolesAsync(T user, System.Collections.Generic.IEnumerable<string> roles) => Task.CompletedTask;
                public Task UpdateAsync(T user) => Task.CompletedTask;
                public Task DeleteAsync(T user) => Task.CompletedTask;
                public Task SetLockoutEnabledAsync(T user, bool enabled) => Task.CompletedTask;
                public Task SetLockoutEndDateAsync(T user, System.DateTimeOffset? lockoutEnd) => Task.CompletedTask;
            }
            public class RoleManager<T>
            {
                public Task DeleteAsync(T role) => Task.CompletedTask;
            }
        }
        """);

    private static readonly FixtureAssemblyReference AuthorizationReference = new(
        "Microsoft.AspNetCore.Authorization",
        """
        using System;
        namespace Microsoft.AspNetCore.Authorization
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
            public sealed class AuthorizeAttribute : Attribute
            {
                public string? Roles { get; set; }
            }
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
            public sealed class AllowAnonymousAttribute : Attribute { }
            public sealed class AuthorizationPolicyBuilder
            {
                public AuthorizationPolicyBuilder RequireRole(params string[] roles) => this;
            }
        }
        """);

    private static readonly FixtureAssemblyReference MvcCoreReference = new(
        "Microsoft.AspNetCore.Mvc.Core",
        """
        using System;
        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class HttpGetAttribute : Routing.HttpMethodAttribute { }
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class RouteAttribute(string template) : Attribute { }
            [AttributeUsage(AttributeTargets.Method)]
            public sealed class NonActionAttribute : Attribute { }
        }
        namespace Microsoft.AspNetCore.Mvc.Routing
        {
            [AttributeUsage(AttributeTargets.Method)]
            public abstract class HttpMethodAttribute : Attribute { }
        }
        """);

    [Fact]
    public void SupportedDiagnostics_ShouldBeErrorEnabledAndNotConfigurable_AndPreserveCompilationEndTags()
    {
        var analyzer = new AICopilotArchitectureAnalyzer();
        var compilationEndIds = new HashSet<string>(StringComparer.Ordinal)
        {
            AICopilotArchitectureAnalyzer.ProjectBoundaryId,
            AICopilotArchitectureAnalyzer.EnabledAdminInvariantId,
            AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId
        };

        analyzer.SupportedDiagnostics.Should().HaveCount(7);
        foreach (var descriptor in analyzer.SupportedDiagnostics)
        {
            descriptor.DefaultSeverity.Should().Be(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            descriptor.IsEnabledByDefault.Should().BeTrue();
            descriptor.CustomTags.Should().Contain(Microsoft.CodeAnalysis.WellKnownDiagnosticTags.NotConfigurable);
            descriptor.CustomTags.Contains(Microsoft.CodeAnalysis.WellKnownDiagnosticTags.CompilationEnd)
                .Should().Be(compilationEndIds.Contains(descriptor.Id));
        }
    }

    [Fact]
    public async Task AIARCH001_ShouldClassifyEveryProductionProject_AndRejectUnknownTestOrReverseLayerEdges()
    {
        const string source = "namespace Fixture; public sealed class Marker { }";

        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            "AICopilot.Core.AiGateway",
            "AICopilot.Services.Contracts");
        var wrongCore = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            "AICopilot.Core.Rag");
        var testDependency = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [source],
            "AICopilot.Sample.TestKit");
        var unknownSource = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.UnclassifiedBridge",
            [source]);
        var serviceNamedUnknownSource = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.UnclassifiedService",
            [source]);
        var unknownTarget = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            "AICopilot.UnclassifiedBridge");

        foreach (var (assemblyName, references) in new[]
                 {
                     ("AICopilot.Infrastructure", Array.Empty<string>()),
                     ("AICopilot.Embedding", Array.Empty<string>()),
                     ("AICopilot.EntityFrameworkCore", Array.Empty<string>()),
                     ("AICopilot.Dapper", Array.Empty<string>())
                 })
        {
            var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
                assemblyName,
                [source],
                references);
            diagnostics.Should().NotContain(
                diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId,
                $"{assemblyName} must remain an explicitly classified infrastructure project");
        }

        foreach (var assemblyName in new[]
                 {
                     "AICopilot.AiGatewayService",
                     "AICopilot.DataAnalysisService",
                     "AICopilot.IdentityService",
                     "AICopilot.McpService",
                     "AICopilot.RagService",
                     "AICopilot.Services.Contracts",
                     "AICopilot.Services.CrossCutting"
                 })
        {
            var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
                assemblyName,
                [source]);
            diagnostics.Should().NotContain(
                diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId,
                $"{assemblyName} must remain the exact explicitly classified service identity");
        }

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        wrongCore.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        testDependency.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        unknownSource.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        serviceNamedUnknownSource.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        unknownTarget.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
    }

    [Fact]
    public async Task AIARCH002_ShouldResolveGlobalAliasesAndGenericRepositoryEntities()
    {
        const string validSource = """
            global using Repo = AICopilot.SharedKernel.Repository.IRepository<AICopilot.Core.AiGateway.Aggregates.Sessions.Session>;
            namespace AICopilot.Core.AiGateway.Aggregates.Sessions
            {
                public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
            }
            namespace Fixture
            {
                public sealed class Consumer(Repo repository) { }
            }
            """;
        const string invalidSource = """
            global using Repo = AICopilot.SharedKernel.Repository.IRepository<Fixture.LeafEntity>;
            namespace Fixture
            {
                public sealed class LeafEntity { }
                public sealed class RogueAggregate : AICopilot.SharedKernel.Domain.IAggregateRoot { }
                public sealed class Consumer(Repo repository) { }
            }
            """;
        const string sameNameFakeRepository = """
            namespace Fixture
            {
                public interface IRepository<T> where T : class { }
                public sealed class LeafEntity { }
                public sealed class Consumer(IRepository<LeafEntity> repository) { }
            }
            """;

        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [validSource],
            [SharedKernelReference]);
        var invalid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [invalidSource],
            [SharedKernelReference]);
        var sameNameFake = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [sameNameFakeRepository],
            [SharedKernelReference]);

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
        invalid.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
        sameNameFake.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
    }

    [Fact]
    public async Task AIARCH003_ShouldResolveAliasedDbContextOwnership()
    {
        const string source = """
            global using Ef = Microsoft.EntityFrameworkCore;
            namespace Fixture
            {
                public sealed class ServiceDb : Ef.DbContext
                {
                    public int Commit() => SaveChanges();
                }
            }
            """;
        const string sameNameFake = """
            namespace Fixture
            {
                public abstract class DbContext { }
                public sealed class HarmlessStore : DbContext { }
            }
            """;
        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [source],
            [EntityFrameworkReference]);
        var invalid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [EntityFrameworkReference]);
        var fake = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameNameFake]);

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
        invalid.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
        fake.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
    }

    [Fact]
    public async Task AIARCH003_ShouldLimitTheInfrastructureDatabaseExceptionToTheExactSessionLock()
    {
        const string valid = """
            namespace AICopilot.Infrastructure.AiGateway
            {
                public sealed class PostgreSqlSessionExecutionLock
                {
                    private sealed class Releaser(Npgsql.NpgsqlConnection connection)
                    {
                        public void Open() => connection.Open();
                    }
                }
            }
            """;
        const string invalid = """
            namespace Fixture
            {
                public sealed class PostgreSqlSessionExecutionLock(Npgsql.NpgsqlConnection connection)
                {
                    public void Open() => connection.Open();
                }
            }
            """;

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [valid],
            [NpgsqlReference]);
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [invalid],
            [NpgsqlReference]);

        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
        invalidDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
    }

    [Fact]
    public async Task AIARCH004_ShouldFollowCrossFileGenericHelpersAndInterfaceDispatch()
    {
        const string contracts = """
            using System;
            using System.Threading.Tasks;
            namespace Microsoft.Extensions.Hosting
            {
                public abstract class BackgroundService
                {
                    protected abstract Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
                }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
                        where TImplementation : TService => services;
                    public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services)
                        where TImplementation : TService => services;
                }
            }
            namespace Fixture
            {
                public interface IAdminReducer { Task ReduceAsync(); }
            }
            """;
        const string invalid = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                internal sealed class AdminReducer(Microsoft.AspNetCore.Identity.UserManager<object> users) : IAdminReducer
                {
                    public Task ReduceAsync() => ReduceCoreAsync(users);
                    private static Task ReduceCoreAsync<T>(Microsoft.AspNetCore.Identity.UserManager<T> manager) =>
                        manager.RemoveFromRoleAsync(default!, "Admin");
                }
                public sealed class Handler(IAdminReducer reducer)
                {
                    public Task HandleAsync() => reducer.ReduceAsync();
                }
            }
            """;
        const string valid = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                internal sealed class AdminReducer(Microsoft.AspNetCore.Identity.UserManager<object> users) : IAdminReducer
                {
                    public Task ReduceAsync() => ReduceCoreAsync(users);
                    private static Task ReduceCoreAsync<T>(Microsoft.AspNetCore.Identity.UserManager<T> manager) =>
                        manager.RemoveFromRoleAsync(default!, "Admin");
                }
                public sealed class Handler(
                    IAdminReducer reducer,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        await reducer.ReduceAsync();
                    });
                }
            }
            """;
        const string disjoint = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        await transaction.ExecuteAsync(async () => await invariant.AcquireAsync());
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }
            }
            """;
        const string reversed = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                        await invariant.AcquireAsync();
                    });
                }
            }
            """;
        const string methodGroupValid = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(ReduceAsync);

                    private async Task ReduceAsync()
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }
            }
            """;
        const string methodGroupReversed = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(ReduceAsync);

                    private async Task ReduceAsync()
                    {
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                        await invariant.AcquireAsync();
                    }
                }
            }
            """;
        const string methodGroupDualUse = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        await transaction.ExecuteAsync(ReduceAsync);
                        await ReduceAsync();
                    }

                    private async Task ReduceAsync()
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }
            }
            """;
        const string storedMethodGroupValid = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = ReduceAsync;
                        await transaction.ExecuteAsync(action);
                    }

                    private async Task ReduceAsync()
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }
            }
            """;
        const string storedMethodGroupReversed = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = ReduceAsync;
                        await transaction.ExecuteAsync(action);
                    }

                    private async Task ReduceAsync()
                    {
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                        await invariant.AcquireAsync();
                    }
                }
            }
            """;
        const string storedMethodGroupDualUse = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = ReduceAsync;
                        await transaction.ExecuteAsync(action);
                        await action();
                    }

                    private async Task ReduceAsync()
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }
            }
            """;
        const string storedLambdaValid = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = async () =>
                        {
                            await invariant.AcquireAsync();
                            await users.RemoveFromRoleAsync(new object(), "Admin");
                        };
                        await transaction.ExecuteAsync(action);
                    }
                }
            }
            """;
        const string storedLambdaReversed = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = async () =>
                        {
                            await users.RemoveFromRoleAsync(new object(), "Admin");
                            await invariant.AcquireAsync();
                        };
                        await transaction.ExecuteAsync(action);
                    }
                }
            }
            """;
        const string storedLambdaDualUse = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task HandleAsync()
                    {
                        Func<Task> action = async () =>
                        {
                            await invariant.AcquireAsync();
                            await users.RemoveFromRoleAsync(new object(), "Admin");
                        };
                        await transaction.ExecuteAsync(action);
                        await action();
                    }
                }
            }
            """;
        const string crossHandlerDualUse = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class Reducer(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public async Task ReduceAsync()
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                }

                public sealed class TransactionHandler(
                    Reducer reducer,
                    ITransactionalExecutionService transaction)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(reducer.ReduceAsync);
                }

                public sealed class DirectHandler(Reducer reducer)
                {
                    public Task HandleAsync() => reducer.ReduceAsync();
                }
            }
            """;
        const string expandedMutationSurface = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace Fixture
            {
                public sealed class User : Microsoft.AspNetCore.Identity.IdentityUser<string> { }

                public sealed class UnobservedGuardHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        _ = invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new User(), "Admin");
                    });
                }

                public sealed class UnobservedMutationHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        _ = users.DeleteAsync(new User());
                    });
                }

                public sealed class FireAndForgetTransactionHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync()
                    {
                        _ = transaction.ExecuteAsync(async () =>
                        {
                            await invariant.AcquireAsync();
                            await users.RemoveFromRolesAsync(new User(), ["Admin"]);
                        });
                        return Task.CompletedTask;
                    }
                }

                public sealed class ExpandedIdentityMutationHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users)
                {
                    public async Task MutateAsync(User user)
                    {
                        await users.SetLockoutEnabledAsync(user, true);
                        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                        user.LockoutEnabled = true;
                        user.LockoutEnd = DateTimeOffset.MaxValue;
                    }
                }

                public sealed class IdentityRelationHandler
                {
                    public void Remove(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.Remove(new Microsoft.AspNetCore.Identity.IdentityUserRole<string>());

                    public void RemoveRange(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.RemoveRange(0, 1);

                    public void RemoveAll(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.RemoveAll(_ => true);

                    public void RemoveAt(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.RemoveAt(0);

                    public void Clear(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.Clear();
                }

                public sealed class IdentityRelationStateHandler(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public void Delete(Microsoft.AspNetCore.Identity.IdentityUserRole<string> relation) =>
                        db.Entry(relation).State = Microsoft.EntityFrameworkCore.EntityState.Deleted;
                }

                public sealed class HelperReducer(Microsoft.AspNetCore.Identity.UserManager<User> users) : IAdminReducer
                {
                    public Task ReduceAsync() => users.DeleteAsync(new User());
                }

                public sealed class UnobservedHelperHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    ITransactionalExecutionService transaction,
                    AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        _ = ReduceAsync();
                    });

                    private Task ReduceAsync() => users.DeleteAsync(new User());
                }

                public sealed class UnobservedInterfaceHelperHandler(
                    IAdminReducer reducer,
                    ITransactionalExecutionService transaction,
                    AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        _ = reducer.ReduceAsync();
                    });
                }

                public sealed class UnobservedDelegateHelperHandler(
                    HelperReducer reducer,
                    ITransactionalExecutionService transaction,
                    AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        Func<Task> reduction = reducer.ReduceAsync;
                        _ = reduction();
                    });
                }

                public sealed class ObservedHelperHandler(
                    HelperReducer reducer,
                    ITransactionalExecutionService transaction,
                    AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        await reducer.ReduceAsync();
                    });
                }

                public sealed class DynamicIdentityHandler(Microsoft.AspNetCore.Identity.UserManager<User> users)
                {
                    public Task MutateAsync(dynamic unknown)
                    {
                        unknown.RemoveFromRoleAsync(new User(), "Admin");
                        return Task.CompletedTask;
                    }
                }

                public sealed class EnabledAdminInvariantPolicy
                {
                    public Task AcquireAsync() => Task.CompletedTask;
                }

                public sealed class FakeGuardHandler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    ITransactionalExecutionService transaction,
                    Fixture.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        await users.UpdateAsync(new User());
                    });
                }
            }
            """;
        const string sameNameFakeIdentitySurface = """
            using System.Threading.Tasks;
            namespace Fixture.Fakes
            {
                public sealed class IdentityUser
                {
                    public bool LockoutEnabled { get; set; }
                    public object? LockoutEnd { get; set; }
                }
                public sealed class UserManager<T>
                {
                    public Task UpdateAsync(T user) => Task.CompletedTask;
                    public Task SetLockoutEnabledAsync(T user, bool enabled) => Task.CompletedTask;
                }
                public sealed class HarmlessHandler(UserManager<IdentityUser> users)
                {
                    public async Task UpdateAsync(IdentityUser user)
                    {
                        user.LockoutEnabled = true;
                        user.LockoutEnd = new object();
                        await users.UpdateAsync(user);
                        await users.SetLockoutEnabledAsync(user, true);
                    }
                }
            }
            """;
        const string exactInvariantImplementations = """
            using AICopilot.Services.Contracts;
            using Microsoft.Extensions.DependencyInjection;
            namespace Fixture.ValidRegistration
            {
                public static class Registration
                {
                    public static void Add(IServiceCollection services)
                    {
                        services.AddScoped<ITransactionalExecutionService,
                            AICopilot.EntityFrameworkCore.Transactions.IdentityTransactionalExecutionService>();
                        services.AddScoped<IIdentityEnabledAdminInvariantGuard,
                            AICopilot.EntityFrameworkCore.Locking.PostgresIdentityEnabledAdminInvariantGuard>();
                    }
                }
            }
            """;
        const string invalidInvariantImplementations = """
            using System;
            using System.Threading.Tasks;
            using AICopilot.Services.Contracts;
            using Microsoft.Extensions.DependencyInjection;
            namespace Fixture.InvalidRegistration
            {
                public sealed class NoopTransaction : ITransactionalExecutionService
                {
                    public Task ExecuteAsync(Func<Task> action) => Task.CompletedTask;
                }
                public sealed class NoopGuard : IIdentityEnabledAdminInvariantGuard
                {
                    public Task AcquireAsync() => Task.CompletedTask;
                }
                public static class Registration
                {
                    public static void Add(IServiceCollection services)
                    {
                        services.AddSingleton<ITransactionalExecutionService, NoopTransaction>();
                        services.AddScoped<IIdentityEnabledAdminInvariantGuard, NoopGuard>();
                    }
                }
            }
            """;

        static string MemberDelegateSource(bool reversed, bool dualUse)
        {
            var reduction = reversed
                ? "await users.RemoveFromRoleAsync(new object(), \"Admin\"); await invariant.AcquireAsync();"
                : "await invariant.AcquireAsync(); await users.RemoveFromRoleAsync(new object(), \"Admin\");";
            var directField = dualUse ? "await _action();" : string.Empty;
            var directProperty = dualUse ? "await Action();" : string.Empty;
            return $$"""
                using System;
                using System.Threading.Tasks;
                using AICopilot.IdentityService.Authorization;
                using AICopilot.Services.Contracts;
                namespace Fixture
                {
                    public sealed class FieldMethodGroupHandler
                    {
                        private readonly Microsoft.AspNetCore.Identity.UserManager<object> users;
                        private readonly EnabledAdminInvariantPolicy invariant;
                        private readonly ITransactionalExecutionService transaction;
                        private readonly Func<Task> _action;

                        public FieldMethodGroupHandler(
                            Microsoft.AspNetCore.Identity.UserManager<object> users,
                            EnabledAdminInvariantPolicy invariant,
                            ITransactionalExecutionService transaction)
                        {
                            this.users = users;
                            this.invariant = invariant;
                            this.transaction = transaction;
                            _action = ReduceAsync;
                        }

                        public async Task HandleAsync()
                        {
                            await transaction.ExecuteAsync(_action);
                            {{directField}}
                        }

                        private async Task ReduceAsync() { {{reduction}} }
                    }

                    public sealed class FieldLambdaHandler(
                        Microsoft.AspNetCore.Identity.UserManager<object> users,
                        EnabledAdminInvariantPolicy invariant,
                        ITransactionalExecutionService transaction)
                    {
                        private readonly Func<Task> _action = async () => { {{reduction}} };

                        public async Task HandleAsync()
                        {
                            await transaction.ExecuteAsync(_action);
                            {{directField}}
                        }
                    }

                    public sealed class PropertyMethodGroupHandler(
                        Microsoft.AspNetCore.Identity.UserManager<object> users,
                        EnabledAdminInvariantPolicy invariant,
                        ITransactionalExecutionService transaction)
                    {
                        private Func<Task> Action => ReduceAsync;

                        public async Task HandleAsync()
                        {
                            await transaction.ExecuteAsync(Action);
                            {{directProperty}}
                        }

                        private async Task ReduceAsync() { {{reduction}} }
                    }

                    public sealed class PropertyLambdaHandler(
                        Microsoft.AspNetCore.Identity.UserManager<object> users,
                        EnabledAdminInvariantPolicy invariant,
                        ITransactionalExecutionService transaction)
                    {
                        private Func<Task> Action { get; } = async () => { {{reduction}} };

                        public async Task HandleAsync()
                        {
                            await transaction.ExecuteAsync(Action);
                            {{directProperty}}
                        }
                    }
                }
                """;
        }

        static string HiddenRootSource(bool valid)
        {
            var body = valid
                ? "return transaction.ExecuteAsync(async () => { await invariant.AcquireAsync(); await users.RemoveFromRoleAsync(new object(), \"Admin\"); });"
                : "return users.RemoveFromRoleAsync(new object(), \"Admin\");";
            return $$"""
                using System.Threading.Tasks;
                using AICopilot.IdentityService.Authorization;
                using AICopilot.Services.Contracts;
                namespace Fixture
                {
                    public sealed class IdentityBackgroundWorker(
                        Microsoft.AspNetCore.Identity.UserManager<object> users,
                        ITransactionalExecutionService transaction,
                        EnabledAdminInvariantPolicy invariant)
                        : Microsoft.Extensions.Hosting.BackgroundService
                    {
                        protected override Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
                        {
                            {{body}}
                        }
                    }

                    internal static class MigrationWorkerIdentitySeeder
                    {
                        internal static Task SeedAsync(
                            Microsoft.AspNetCore.Identity.UserManager<object> users,
                            ITransactionalExecutionService transaction,
                            EnabledAdminInvariantPolicy invariant)
                        {
                            {{body}}
                        }
                    }

                    internal sealed class InternalIdentityEntry(
                        Microsoft.AspNetCore.Identity.UserManager<object> users,
                        ITransactionalExecutionService transaction,
                        EnabledAdminInvariantPolicy invariant)
                    {
                        public Task RunAsync()
                        {
                            {{body}}
                        }
                    }
                }
                """;
        }

        var memberDelegateValid = MemberDelegateSource(reversed: false, dualUse: false);
        var memberDelegateReversed = MemberDelegateSource(reversed: true, dualUse: false);
        var memberDelegateDualUse = MemberDelegateSource(reversed: false, dualUse: true);
        var hiddenRootValid = HiddenRootSource(valid: true);
        var hiddenRootInvalid = HiddenRootSource(valid: false);
        var formalReferences = new[]
        {
            IdentityStoresReference,
            IdentityCoreReference,
            ServicesContractsReference,
            IdentityServiceReference,
            EntityFrameworkReference
        };

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, invalid],
            formalReferences);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, valid],
            formalReferences);
        var disjointDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, disjoint],
            formalReferences);
        var reversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, reversed],
            formalReferences);
        var methodGroupValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupValid],
            formalReferences);
        var methodGroupReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupReversed],
            formalReferences);
        var methodGroupDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupDualUse],
            formalReferences);
        var storedMethodGroupValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupValid],
            formalReferences);
        var storedMethodGroupReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupReversed],
            formalReferences);
        var storedMethodGroupDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupDualUse],
            formalReferences);
        var storedLambdaValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaValid],
            formalReferences);
        var storedLambdaReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaReversed],
            formalReferences);
        var storedLambdaDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaDualUse],
            formalReferences);
        var crossHandlerDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, crossHandlerDualUse],
            formalReferences);
        var memberDelegateValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateValid],
            formalReferences);
        var memberDelegateReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateReversed],
            formalReferences);
        var memberDelegateDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateDualUse],
            formalReferences);
        var hiddenRootValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, hiddenRootValid],
            formalReferences);
        var hiddenRootInvalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, hiddenRootInvalid],
            formalReferences);
        var expandedMutationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, expandedMutationSurface],
            formalReferences);
        var sameNameFakeIdentityDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, sameNameFakeIdentitySurface],
            formalReferences);
        var exactInvariantImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, exactInvariantImplementations],
            [.. formalReferences, InvariantInfrastructureReference]);
        var invalidInvariantImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, invalidInvariantImplementations],
            formalReferences);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        disjointDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        reversedDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        methodGroupValidDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        methodGroupReversedDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        methodGroupDualUseDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedMethodGroupValidDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedMethodGroupReversedDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedMethodGroupDualUseDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedLambdaValidDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedLambdaReversedDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        storedLambdaDualUseDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        crossHandlerDualUseDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        memberDelegateValidDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        memberDelegateReversedDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId).Should().Be(4);
        memberDelegateDualUseDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId).Should().Be(4);
        hiddenRootValidDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        hiddenRootInvalidDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId).Should().Be(3);
        foreach (var expectedRoot in new[]
                 {
                     "UnobservedGuardHandler",
                     "UnobservedMutationHandler",
                     "FireAndForgetTransactionHandler",
                     "ExpandedIdentityMutationHandler",
                     "IdentityRelationHandler",
                     "IdentityRelationStateHandler",
                     "UnobservedHelperHandler",
                     "UnobservedInterfaceHelperHandler",
                     "UnobservedDelegateHelperHandler",
                     "DynamicIdentityHandler"
                 })
        {
            expandedMutationDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId &&
                diagnostic.GetMessage().Contains(expectedRoot, StringComparison.Ordinal));
        }
        sameNameFakeIdentityDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        expandedMutationDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId &&
            diagnostic.GetMessage().Contains("Fixture.ObservedHelperHandler.", StringComparison.Ordinal));
        expandedMutationDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId &&
            diagnostic.GetMessage().Contains("Fixture.FakeGuardHandler.", StringComparison.Ordinal));
        exactInvariantImplementationDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        invalidInvariantImplementationDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task AIARCH004_ShouldIgnoreOrdinaryUserUpdatesAndRoleDeletes()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class User : Microsoft.AspNetCore.Identity.IdentityUser<string> { }

                public sealed class Handler(
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    Microsoft.AspNetCore.Identity.RoleManager<object> roles)
                {
                    public async Task HandleAsync(
                        User user,
                        object role)
                    {
                        await users.UpdateAsync(user);
                        await roles.DeleteAsync(role);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [source],
            [IdentityStoresReference, IdentityCoreReference, ServicesContractsReference, IdentityServiceReference]);

        diagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
    }

    [Fact]
    public async Task AIARCH004_ShouldTreatFormalSynchronousIdentityDecreaseAsObservedAtTheCallSite()
    {
        const string valid = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace AICopilot.IdentityService.Authorization
            {
                public static class IdentityGovernanceHelper
                {
                    public static void MarkUserDisabled(object user) { }
                }
            }
            namespace Fixture
            {
                public sealed class Handler(
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        IdentityGovernanceHelper.MarkUserDisabled(new object());
                    });
                }
            }
            """;
        const string beforeGuard = """
            using System.Threading.Tasks;
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            namespace AICopilot.IdentityService.Authorization
            {
                public static class IdentityGovernanceHelper
                {
                    public static void MarkUserDisabled(object user) { }
                }
            }
            namespace Fixture
            {
                public sealed class Handler(
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        IdentityGovernanceHelper.MarkUserDisabled(new object());
                        await invariant.AcquireAsync();
                    });
                }
            }
            """;
        const string sameNameFake = """
            namespace Fixture
            {
                public static class IdentityGovernanceHelper
                {
                    public static void MarkUserDisabled(object user) { }
                }

                public sealed class Handler
                {
                    public void Handle() => IdentityGovernanceHelper.MarkUserDisabled(new object());
                }
            }
            """;
        var formalReferences = new[]
        {
            IdentityStoresReference,
            IdentityCoreReference,
            ServicesContractsReference,
            IdentityServiceReference,
            EntityFrameworkReference
        };

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [valid],
            formalReferences);
        var beforeGuardDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [beforeGuard],
            formalReferences);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [sameNameFake],
            formalReferences);

        validDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        beforeGuardDiagnostics.Should().Contain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        sameNameFakeDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
    }

    [Fact]
    public async Task AIARCH005_ShouldRequireInheritedPluginManifestAndRejectProductionTestDoubles()
    {
        const string contracts = """
            namespace Fixture
            {
                public interface IAgentToolExecutor { }
            }
            """;
        const string invalid = """
            using AICopilot.AgentPlugin;
            namespace Fixture
            {
                public sealed class MissingManifestPlugin : AgentPluginBase { }
                public sealed class FakeAgentToolExecutor : IAgentToolExecutor { }
            }
            """;
        const string valid = """
            using System.ComponentModel;
            using AICopilot.AgentPlugin;
            namespace Fixture
            {
                public sealed class DiagnosticPlugin : AgentPluginBase
                {
                    public override string Description => "diagnostic";
                    public override ChatExposureMode ChatExposureMode => ChatExposureMode.Advisory;
                    [Description("read-only diagnostic")]
                    public string Inspect() => "ok";
                }
            }
            """;

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, invalid],
            [AgentPluginReference]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, valid],
            [AgentPluginReference]);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
    }

    [Fact]
    public async Task AIARCH005_ShouldOwnExactAssemblyDiscoveryAndActivatorUtilitiesSurfaces()
    {
        const string exactSurfaces = """
            using System;
            namespace Fixture
            {
                public sealed class RuntimeSurface
                {
                    public Type[] GetTypes() => typeof(RuntimeSurface).Assembly.GetTypes();
                    public Type[] GetExportedTypes() => typeof(RuntimeSurface).Assembly.GetExportedTypes();
                    public object DefinedTypes() => typeof(RuntimeSurface).Assembly.DefinedTypes;
                    public object Create(IServiceProvider provider) =>
                        Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<object>(provider);
                    public object GetOrCreate(IServiceProvider provider) =>
                        Microsoft.Extensions.DependencyInjection.ActivatorUtilities.GetServiceOrCreateInstance<object>(provider);
                }
            }
            """;
        const string sameNameFakes = """
            namespace Fixture
            {
                public sealed class Assembly
                {
                    public object GetTypes() => new object();
                    public object GetExportedTypes() => new object();
                    public object DefinedTypes => new object();
                }
                public static class ActivatorUtilities
                {
                    public static object CreateInstance(object provider) => new object();
                    public static object GetServiceOrCreateInstance(object provider) => new object();
                }
                public sealed class Harmless
                {
                    public object Run(Assembly assembly, object provider)
                    {
                        _ = assembly.GetTypes();
                        _ = assembly.GetExportedTypes();
                        _ = assembly.DefinedTypes;
                        _ = ActivatorUtilities.CreateInstance(provider);
                        return ActivatorUtilities.GetServiceOrCreateInstance(provider);
                    }
                }
            }
            """;

        var forbidden = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [exactSurfaces],
            [DependencyInjectionAbstractionsReference]);
        var runtime = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AgentPlugin.Runtime",
            [exactSurfaces],
            [DependencyInjectionAbstractionsReference]);
        var fakes = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameNameFakes]);

        forbidden.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId)
            .Should().Be(5);
        runtime.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
        fakes.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
    }

    [Fact]
    public async Task AIARCH005_ShouldAllowOnlyTheExactInternalDevelopmentMockException()
    {
        const string valid = """
            namespace AICopilot.AiGatewayService.AgentTasks
            {
                public interface IAgentToolExecutor { }
                internal sealed class MockMcpAgentToolExecutor : IAgentToolExecutor { }
            }
            """;
        const string invalid = """
            namespace AICopilot.AiGatewayService.AgentTasks
            {
                public interface IAgentToolExecutor { }
            }
            namespace Fixture
            {
                internal sealed class MockMcpAgentToolExecutor :
                    AICopilot.AiGatewayService.AgentTasks.IAgentToolExecutor { }
            }
            """;

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [valid]);
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [invalid]);

        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
        invalidDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldFollowInterfaceDispatchFromCloudAiReadEntryToDatabaseWrite()
    {
        const string contracts = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceProviderServiceExtensions
                {
                    public static T GetRequiredService<T>(this System.IServiceProvider provider) => default!;
                }
            }
            namespace Microsoft.Extensions.Hosting
            {
                public abstract class BackgroundService
                {
                    protected abstract System.Threading.Tasks.Task ExecuteAsync(
                        System.Threading.CancellationToken stoppingToken);
                }
            }
            namespace Fixture
            {
                public interface IDataGateway { int Execute(); }
            }
            """;
        const string invalid = """
            namespace Fixture
            {
                internal sealed class DataGateway(Microsoft.EntityFrameworkCore.DbContext db) : IDataGateway
                {
                    public int Execute() => db.SaveChanges();
                }
                public sealed class CloudReadonlyRunner(AICopilot.Services.Contracts.ICloudAiReadClient client, IDataGateway gateway)
                {
                    public int Query() => gateway.Execute();
                }
            }
            """;
        const string neutralRoots = """
            using Microsoft.Extensions.DependencyInjection;
            namespace Fixture
            {
                public interface INeutralReader { int Read(); }

                public sealed class NeutralReader(AICopilot.Services.Contracts.ICloudAiReadClient client) : INeutralReader
                {
                    public int Read() => client.Read();
                }

                public sealed class NeutralFactoryRunner(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run(System.IServiceProvider services)
                    {
                        var local = services.GetRequiredService<AICopilot.Services.Contracts.ICloudAiReadClient>();
                        _ = local.Read();
                        return db.SaveChanges();
                    }
                }

                public sealed class NeutralHelperRunner(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run(System.IServiceProvider services)
                    {
                        _ = Resolve(services).Read();
                        return db.SaveChanges();
                    }

                    private static AICopilot.Services.Contracts.ICloudAiReadClient Resolve(System.IServiceProvider services) =>
                        services.GetRequiredService<AICopilot.Services.Contracts.ICloudAiReadClient>();
                }

                public sealed class NeutralConstructedLocalRunner(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run()
                    {
                        var local = new AICopilot.Infrastructure.CloudRead.CloudAiReadClient();
                        _ = local.Read();
                        return db.SaveChanges();
                    }
                }

                public sealed class GenericAgentOrchestrator(
                    INeutralReader reader,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run()
                    {
                        _ = reader.Read();
                        return db.SaveChanges();
                    }
                }

                internal sealed class InternalCloudWorker(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    internal int Run()
                    {
                        _ = client.Read();
                        return db.SaveChanges();
                    }
                }

                public sealed class CloudHostedWorker(
                    System.IServiceProvider services,
                    Microsoft.EntityFrameworkCore.DbContext db)
                    : Microsoft.Extensions.Hosting.BackgroundService
                {
                    protected override System.Threading.Tasks.Task ExecuteAsync(
                        System.Threading.CancellationToken stoppingToken)
                    {
                        var local = services.GetRequiredService<AICopilot.Services.Contracts.ICloudAiReadClient>();
                        _ = local.Read();
                        _ = db.SaveChanges();
                        return System.Threading.Tasks.Task.CompletedTask;
                    }
                }
            }
            """;
        const string sameNameFake = """
            namespace Fixture
            {
                public interface ICloudAiReadClient { int Read(); }

                public sealed class NeutralRunner(
                    ICloudAiReadClient client,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run()
                    {
                        _ = client.Read();
                        return db.SaveChanges();
                    }
                }
            }
            """;
        const string exactNameWrongAssembly = """
            namespace Fixture
            {
                public sealed class NeutralRunner(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run()
                    {
                        var local = new AICopilot.Infrastructure.CloudRead.CloudAiReadClient();
                        _ = local.Read();
                        return db.SaveChanges();
                    }
                }
            }
            """;
        const string valid = """
            namespace Fixture
            {
                internal sealed class DataGateway : IDataGateway
                {
                    public int Execute() => 42;
                }
                public sealed class CloudReadonlyRunner(AICopilot.Services.Contracts.ICloudAiReadClient client, IDataGateway gateway)
                {
                    public int Query() => gateway.Execute();
                }
            }
            """;
        const string delegateMemberWrites = """
            using System;
            namespace Fixture
            {
                public sealed class FieldWriteHolder
                {
                    private readonly Func<int> _write;
                    private readonly Microsoft.EntityFrameworkCore.DbContext db;

                    public FieldWriteHolder(Microsoft.EntityFrameworkCore.DbContext db)
                    {
                        this.db = db;
                        _write = Write;
                    }

                    public int Invoke() => _write();
                    private int Write() => db.SaveChanges();
                }

                public sealed class PropertyWriteHolder(Microsoft.EntityFrameworkCore.DbContext db)
                {
                    private Func<int> WriteAction => Write;
                    public int Invoke() => WriteAction();
                    private int Write() => db.SaveChanges();
                }

                public sealed class CloudFieldRunner
                {
                    public int Run(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        FieldWriteHolder holder)
                    {
                        _ = client.Read();
                        return holder.Invoke();
                    }
                }

                public sealed class CloudPropertyRunner
                {
                    public int Run(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        PropertyWriteHolder holder)
                    {
                        _ = client.Read();
                        return holder.Invoke();
                    }
                }
            }
            """;
        const string exactRepositoryWrite = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
                public sealed class CloudRepositoryRunner
                {
                    public Task RunAsync(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        AICopilot.SharedKernel.Repository.IRepository<Session> repository)
                    {
                        _ = client.Read();
                        return repository.UpdateAsync(new Session());
                    }
                }
            }
            """;
        const string sameNameFakeRepositoryWrite = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public interface IRepository<T> where T : class
                {
                    Task UpdateAsync(T entity);
                }
                public sealed class Session { }
                public sealed class CloudRepositoryRunner
                {
                    public Task RunAsync(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        IRepository<Session> repository)
                    {
                        _ = client.Read();
                        return repository.UpdateAsync(new Session());
                    }
                }
            }
            """;
        const string exactDapperWrite = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudDapperRunner
                {
                    public Task<int> RunAsync(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        object connection)
                    {
                        _ = client.Read();
                        return Dapper.SqlMapper.ExecuteAsync(connection, "delete from sessions");
                    }
                }
            }
            """;
        const string dapperWriteWithBenignNonSqlArgument = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudDapperRunner
                {
                    public Task<int> RunAsync(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        object connection)
                    {
                        _ = client.Read();
                        return Dapper.SqlMapper.ExecuteAsync(
                            connection,
                            BuildWriteSql(),
                            "SET TRANSACTION READ ONLY");
                    }

                    private static string BuildWriteSql() => "delete from sessions";
                }
            }
            """;
        const string sameNameFakeDapperWrite = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public static class SqlMapper
                {
                    public static Task<int> ExecuteAsync(object connection, string sql) => Task.FromResult(1);
                }
                public sealed class CloudDapperRunner
                {
                    public Task<int> RunAsync(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        object connection)
                    {
                        _ = client.Read();
                        return SqlMapper.ExecuteAsync(connection, "delete from sessions");
                    }
                }
            }
            """;
        const string httpAndIndirectWrites = """
            using System;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class HttpWriteRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    HttpClient http,
                    HttpMessageInvoker invoker)
                {
                    public Task<HttpResponseMessage> PostAsync()
                    {
                        _ = cloud.Read();
                        return http.PostAsync("https://cloud.invalid/write", null);
                    }
                    public Task<HttpResponseMessage> PutAsync()
                    {
                        _ = cloud.Read();
                        return http.PutAsync("https://cloud.invalid/write", null);
                    }
                    public Task<HttpResponseMessage> PatchAsync()
                    {
                        _ = cloud.Read();
                        return http.PatchAsync("https://cloud.invalid/write", null);
                    }
                    public Task<HttpResponseMessage> DeleteAsync()
                    {
                        _ = cloud.Read();
                        return http.DeleteAsync("https://cloud.invalid/write");
                    }
                    public Task<HttpResponseMessage> SendAsync()
                    {
                        _ = cloud.Read();
                        return http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read"));
                    }
                    public Task<HttpResponseMessage> OrdinaryGetAsync()
                    {
                        _ = cloud.Read();
                        return http.GetAsync("https://cloud.invalid/read");
                    }
                    public HttpResponseMessage SendSync()
                    {
                        _ = cloud.Read();
                        return http.Send(new HttpRequestMessage(HttpMethod.Post, "https://cloud.invalid/write"));
                    }
                    public Task<HttpResponseMessage> InvokerSendAsync()
                    {
                        _ = cloud.Read();
                        return invoker.SendAsync(
                            new HttpRequestMessage(HttpMethod.Post, "https://cloud.invalid/write"),
                            default);
                    }
                    public Task<HttpResponseMessage> PostJsonAsync()
                    {
                        _ = cloud.Read();
                        return http.PostAsJsonAsync("https://cloud.invalid/write", new { Value = 1 });
                    }
                    public Task<HttpResponseMessage> PutJsonAsync()
                    {
                        _ = cloud.Read();
                        return http.PutAsJsonAsync("https://cloud.invalid/write", new { Value = 1 });
                    }
                    public Task<HttpResponseMessage> PatchJsonAsync()
                    {
                        _ = cloud.Read();
                        return http.PatchAsJsonAsync("https://cloud.invalid/write", new { Value = 1 });
                    }
                    public Task<object?> DeleteJsonAsync()
                    {
                        _ = cloud.Read();
                        return http.DeleteFromJsonAsync<object>("https://cloud.invalid/write");
                    }
                    public Task<object?> GetJsonAsync()
                    {
                        _ = cloud.Read();
                        return http.GetFromJsonAsync<object>("https://cloud.invalid/read");
                    }
                }

                public sealed class GenericCloudRunner<T>(
                    T cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                    where T : AICopilot.Services.Contracts.ICloudAiReadClient
                {
                    public int Run()
                    {
                        _ = cloud.Read();
                        return db.SaveChanges();
                    }
                }

                public sealed class CallbackCloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public Task<int> TaskRunAsync()
                    {
                        _ = cloud.Read();
                        return Task.Run(() => db.SaveChanges());
                    }

                    public int CustomCallback()
                    {
                        _ = cloud.Read();
                        return Invoke(() => db.SaveChanges());
                    }

                    private static int Invoke(Func<int> action) => action();
                }

                public sealed class DynamicCloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    public void Run(dynamic unknown)
                    {
                        _ = cloud.Read();
                        unknown.SaveChanges();
                    }
                }
            }
            """;
        const string formalHttpGetTransport = """
            using System.Net.Http;
            using System.Threading.Tasks;
            namespace AICopilot.Infrastructure.CloudRead
            {
                public sealed class CloudAiReadHttpTransport(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    HttpClient http)
                {
                    public Task<HttpResponseMessage> GetAsync()
                    {
                        _ = cloud.Read();
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read");
                        return http.SendAsync(request);
                    }
                    public Task<HttpResponseMessage> ReassignedPostAsync()
                    {
                        _ = cloud.Read();
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read");
                        request = new HttpRequestMessage(HttpMethod.Post, "https://cloud.invalid/write");
                        return http.SendAsync(request);
                    }
                    public Task<HttpResponseMessage> MutatedPostAsync()
                    {
                        _ = cloud.Read();
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read");
                        request.Method = HttpMethod.Post;
                        return http.SendAsync(request);
                    }
                    public Task<HttpResponseMessage> RequestToAliasPostAsync()
                    {
                        _ = cloud.Read();
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read");
                        var alias = request;
                        alias.Method = HttpMethod.Post;
                        return http.SendAsync(request);
                    }
                    public Task<HttpResponseMessage> AliasToRequestPostAsync()
                    {
                        _ = cloud.Read();
                        var alias = new HttpRequestMessage(HttpMethod.Get, "https://cloud.invalid/read");
                        var request = alias;
                        alias.Method = HttpMethod.Post;
                        return http.SendAsync(request);
                    }
                }
            }
            """;
        const string sameNameFakeHttpClient = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class HttpClient
                {
                    public Task<int> PostAsync(string uri, object? content) => Task.FromResult(42);
                }
                public sealed class FakeHttpRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    HttpClient http)
                {
                    public Task<int> RunAsync()
                    {
                        _ = cloud.Read();
                        return http.PostAsync("not-http", null);
                    }
                }
            }
            """;
        const string generatedCloudWrite = """
            // <auto-generated/>
            namespace Fixture.Generated
            {
                public sealed class GeneratedCloudWriter(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public int Run()
                    {
                        _ = cloud.Read();
                        return db.SaveChanges();
                    }
                }
            }
            """;

        var formalReferences = new[]
        {
            EntityFrameworkReference,
            DapperReference,
            ServicesContractsReference,
            CloudReadClientReference,
            SharedKernelReference
        };
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, invalid],
            formalReferences);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, valid],
            formalReferences);
        var neutralRootDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, neutralRoots],
            formalReferences);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, sameNameFake],
            formalReferences);
        var exactNameWrongAssemblyDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, exactNameWrongAssembly],
            [
                EntityFrameworkReference,
                DapperReference,
                ServicesContractsReference,
                SharedKernelReference,
                new FixtureAssemblyReference(
                    "AICopilot.FakeCloudRead",
                    """
                    namespace AICopilot.Infrastructure.CloudRead
                    {
                        public sealed class CloudAiReadClient
                        {
                            public int Read() => 42;
                        }
                    }
                    """)
            ]);
        var delegateMemberWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, delegateMemberWrites],
            formalReferences);
        var exactRepositoryWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, exactRepositoryWrite],
            formalReferences);
        var sameNameFakeRepositoryWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, sameNameFakeRepositoryWrite],
            formalReferences);
        var exactDapperWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [contracts, exactDapperWrite],
            formalReferences);
        var dapperWriteWithBenignNonSqlArgumentDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [contracts, dapperWriteWithBenignNonSqlArgument],
            formalReferences);
        var sameNameFakeDapperWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [contracts, sameNameFakeDapperWrite],
            formalReferences);
        var httpAndIndirectWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, httpAndIndirectWrites],
            formalReferences);
        var formalHttpGetDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, formalHttpGetTransport],
            formalReferences);
        var sameNameFakeHttpDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, sameNameFakeHttpClient],
            formalReferences);
        var generatedCloudWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [
                new AnalyzerFixtureSource("Contracts.cs", contracts),
                new AnalyzerFixtureSource("ActualCloudWriter.g.cs", generatedCloudWrite)
            ],
            formalReferences);
        var generatedSuffixCloudWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [
                new AnalyzerFixtureSource("Contracts.cs", contracts),
                new AnalyzerFixtureSource("ActualCloudWriter.generated.cs", generatedCloudWrite)
            ],
            formalReferences);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        neutralRootDiagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(5);
        neutralRootDiagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().NotContain(diagnostic => diagnostic.GetMessage().Contains("GenericAgentOrchestrator", StringComparison.Ordinal));
        sameNameFakeDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        exactNameWrongAssemblyDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        delegateMemberWriteDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId).Should().Be(2);
        exactRepositoryWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        sameNameFakeRepositoryWriteDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        exactDapperWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        dapperWriteWithBenignNonSqlArgumentDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        sameNameFakeDapperWriteDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        foreach (var expectedMethod in new[]
                 {
                     "PostAsync",
                     "PutAsync",
                     "PatchAsync",
                     "DeleteAsync",
                     "SendAsync",
                     "SendSync",
                     "InvokerSendAsync",
                     "PostJsonAsync",
                     "PutJsonAsync",
                     "PatchJsonAsync",
                     "DeleteJsonAsync",
                     "GenericCloudRunner<T>.Run",
                     "TaskRunAsync",
                     "CustomCallback",
                     "DynamicCloudRunner.Run"
                 })
        {
            httpAndIndirectWriteDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
                diagnostic.GetMessage().Contains(expectedMethod, StringComparison.Ordinal));
        }
        httpAndIndirectWriteDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            (diagnostic.GetMessage().Contains("OrdinaryGetAsync", StringComparison.Ordinal) ||
             diagnostic.GetMessage().Contains("GetJsonAsync", StringComparison.Ordinal)));
        formalHttpGetDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId).Should().Be(4);
        foreach (var expectedMethod in new[]
                 {
                     "ReassignedPostAsync",
                     "MutatedPostAsync",
                     "RequestToAliasPostAsync",
                     "AliasToRequestPostAsync"
                 })
        {
            formalHttpGetDiagnostics.Should().Contain(diagnostic =>
                diagnostic.GetMessage().Contains(expectedMethod, StringComparison.Ordinal));
        }
        formalHttpGetDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("CloudAiReadHttpTransport.GetAsync", StringComparison.Ordinal));
        sameNameFakeHttpDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        generatedCloudWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        generatedSuffixCloudWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldAllowOnlyTheExactAICopilotAuditWriterSideEffect()
    {
        var auditImplementationReference = new FixtureAssemblyReference(
            "AICopilot.EntityFrameworkCore",
            """
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore.AuditLogs
            {
                public sealed class AuditDbContext : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess) => Task.FromResult(1);
                }

                public sealed class AuditLogWriter(AuditDbContext db)
                    : AICopilot.Services.Contracts.IAuditLogWriter
                {
                    public Task WriteAsync(
                        AICopilot.Services.Contracts.AuditLogWriteRequest request,
                        CancellationToken cancellationToken = default) => Task.CompletedTask;

                    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
                        db.SaveChangesAsync(true);

                    public Task<int> WriteBusinessAsync() => db.SaveChangesAsync(true);
                }
            }
            """);
        const string valid = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudReadonlyRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    AICopilot.Services.Contracts.IAuditLogWriter audit)
                {
                    public Task<int> RunAsync() => audit.SaveChangesAsync();
                }
            }
            """;
        const string invalidImplementationMethod = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudReadonlyRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    AICopilot.EntityFrameworkCore.AuditLogs.AuditLogWriter audit)
                {
                    public Task<int> RunAsync()
                    {
                        _ = client.Read();
                        return audit.WriteBusinessAsync();
                    }
                }
            }
            """;
        const string rogueImplementation = """
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore
            {
                public sealed class BusinessDbContext : Microsoft.EntityFrameworkCore.DbContext
                {
                    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
                }
                public sealed class RogueAuditWriter(BusinessDbContext db)
                    : AICopilot.Services.Contracts.IAuditLogWriter
                {
                    public Task WriteAsync(
                        AICopilot.Services.Contracts.AuditLogWriteRequest request,
                        CancellationToken cancellationToken = default) => Task.CompletedTask;
                    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
                        db.SaveChangesAsync(cancellationToken);
                }
            }
            """;
        const string wrongContextForFormalWriter = """
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore.AuditLogs
            {
                public sealed class BusinessDbContext : Microsoft.EntityFrameworkCore.DbContext { }
                public sealed class AuditLogWriter(BusinessDbContext db)
                    : AICopilot.Services.Contracts.IAuditLogWriter
                {
                    public Task WriteAsync(
                        AICopilot.Services.Contracts.AuditLogWriteRequest request,
                        CancellationToken cancellationToken = default) => Task.CompletedTask;
                    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
                        Task.FromResult(db.SaveChanges());
                }
            }
            """;
        const string invalid = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public interface IAuditLogWriter
                {
                    Task<int> SaveChangesAsync();
                }
                public sealed class FakeAuditLogWriter(Microsoft.EntityFrameworkCore.DbContext db) : IAuditLogWriter
                {
                    public Task<int> SaveChangesAsync()
                    {
                        _ = db.SaveChanges();
                        return Task.FromResult(1);
                    }
                }
                public sealed class CloudReadonlyRunner(AICopilot.Services.Contracts.ICloudAiReadClient client, IAuditLogWriter audit)
                {
                    public Task<int> RunAsync() => audit.SaveChangesAsync();
                }
            }
            """;

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [valid],
            [ServicesContractsReference]);
        var crossProjectValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [valid],
            [EntityFrameworkReference, ServicesContractsReference, auditImplementationReference]);
        var invalidImplementationMethodDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [invalidImplementationMethod],
            [EntityFrameworkReference, ServicesContractsReference, auditImplementationReference]);
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [invalid],
            [EntityFrameworkReference, ServicesContractsReference]);
        var rogueImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [rogueImplementation],
            [EntityFrameworkReference, ServicesContractsReference]);
        var wrongContextDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [wrongContextForFormalWriter],
            [EntityFrameworkReference, ServicesContractsReference]);

        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        crossProjectValidDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        invalidImplementationMethodDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        invalidDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        rogueImplementationDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("RogueAuditWriter", StringComparison.Ordinal));
        wrongContextDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("AuditLogWriter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH006_ShouldAllowOnlyTheExactModelQuotaOperationalStore()
    {
        var quotaImplementationReference = new FixtureAssemblyReference(
            "AICopilot.EntityFrameworkCore",
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore
            {
                public sealed class AiGatewayDbContext : Microsoft.EntityFrameworkCore.DbContext { }
            }
            namespace AICopilot.EntityFrameworkCore.Transactions
            {
                public sealed class AgentExecutionTransactionRunner(
                    AICopilot.EntityFrameworkCore.AiGatewayDbContext db)
                {
                    public Task<TResult> ExecuteAsync<TResult>(Func<TResult> action)
                    {
                        _ = db.SaveChanges();
                        return Task.FromResult(action());
                    }
                }
            }
            namespace AICopilot.EntityFrameworkCore.Repository
            {
                public sealed class PostgresModelQuotaReservationStore(
                    AICopilot.EntityFrameworkCore.Transactions.AgentExecutionTransactionRunner runner)
                    : AICopilot.Services.Contracts.IModelQuotaReservationStore
                {
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationOutcome> TryReserveAsync(
                        AICopilot.Services.Contracts.ModelQuotaReservationRequest request,
                        CancellationToken cancellationToken = default) =>
                        runner.ExecuteAsync(() => new AICopilot.Services.Contracts.ModelQuotaReservationOutcome());

                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationResult> SettleAsync(
                        AICopilot.Services.Contracts.ModelQuotaSettlement settlement,
                        CancellationToken cancellationToken = default) =>
                        runner.ExecuteAsync(() => AICopilot.Services.Contracts.ModelQuotaReservationResult.Granted);

                    public Task<int> ReclaimExpiredAsync(
                        DateTimeOffset nowUtc,
                        int maxItems,
                        CancellationToken cancellationToken = default) =>
                        runner.ExecuteAsync(() => 1);

                    public Task<int> WriteBusinessAsync() => runner.ExecuteAsync(() => 1);
                }
            }
            """);
        const string valid = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudReadonlyRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    AICopilot.Services.Contracts.IModelQuotaReservationStore quota)
                {
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationOutcome> RunAsync()
                    {
                        _ = client.Read();
                        return quota.TryReserveAsync(new AICopilot.Services.Contracts.ModelQuotaReservationRequest());
                    }
                }
            }
            """;
        const string invalidImplementationMethod = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudReadonlyRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    AICopilot.EntityFrameworkCore.Repository.PostgresModelQuotaReservationStore quota)
                {
                    public Task<int> RunAsync()
                    {
                        _ = client.Read();
                        return quota.WriteBusinessAsync();
                    }
                }
            }
            """;
        const string rogueImplementation = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore.Repository
            {
                public sealed class RogueModelQuotaReservationStore
                    : AICopilot.Services.Contracts.IModelQuotaReservationStore
                {
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationOutcome> TryReserveAsync(
                        AICopilot.Services.Contracts.ModelQuotaReservationRequest request,
                        CancellationToken cancellationToken = default) =>
                        Task.FromResult(new AICopilot.Services.Contracts.ModelQuotaReservationOutcome());
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationResult> SettleAsync(
                        AICopilot.Services.Contracts.ModelQuotaSettlement settlement,
                        CancellationToken cancellationToken = default) =>
                        Task.FromResult(AICopilot.Services.Contracts.ModelQuotaReservationResult.Granted);
                    public Task<int> ReclaimExpiredAsync(
                        DateTimeOffset nowUtc,
                        int maxItems,
                        CancellationToken cancellationToken = default) => Task.FromResult(0);
                }
            }
            """;
        const string wrongContextForFormalStore = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace AICopilot.EntityFrameworkCore
            {
                public sealed class IdentityDbContext : Microsoft.EntityFrameworkCore.DbContext { }
            }
            namespace AICopilot.EntityFrameworkCore.Transactions
            {
                public sealed class AgentExecutionTransactionRunner(
                    AICopilot.EntityFrameworkCore.IdentityDbContext db) { }
            }
            namespace AICopilot.EntityFrameworkCore.Repository
            {
                public sealed class PostgresModelQuotaReservationStore(
                    AICopilot.EntityFrameworkCore.Transactions.AgentExecutionTransactionRunner runner)
                    : AICopilot.Services.Contracts.IModelQuotaReservationStore
                {
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationOutcome> TryReserveAsync(
                        AICopilot.Services.Contracts.ModelQuotaReservationRequest request,
                        CancellationToken cancellationToken = default) =>
                        Task.FromResult(new AICopilot.Services.Contracts.ModelQuotaReservationOutcome());
                    public Task<AICopilot.Services.Contracts.ModelQuotaReservationResult> SettleAsync(
                        AICopilot.Services.Contracts.ModelQuotaSettlement settlement,
                        CancellationToken cancellationToken = default) =>
                        Task.FromResult(AICopilot.Services.Contracts.ModelQuotaReservationResult.Granted);
                    public Task<int> ReclaimExpiredAsync(
                        DateTimeOffset nowUtc,
                        int maxItems,
                        CancellationToken cancellationToken = default) => Task.FromResult(0);
                }
            }
            """;
        const string sameNameFake = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public interface IModelQuotaReservationStore { Task<int> TryReserveAsync(); }
                public sealed class FakeModelQuotaReservationStore(Microsoft.EntityFrameworkCore.DbContext db)
                    : IModelQuotaReservationStore
                {
                    public Task<int> TryReserveAsync()
                    {
                        _ = db.SaveChanges();
                        return Task.FromResult(1);
                    }
                }
                public sealed class CloudReadonlyRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    IModelQuotaReservationStore quota)
                {
                    public Task<int> RunAsync()
                    {
                        _ = client.Read();
                        return quota.TryReserveAsync();
                    }
                }
            }
            """;

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [valid],
            [ServicesContractsReference]);
        var crossProjectValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [valid],
            [EntityFrameworkReference, ServicesContractsReference, quotaImplementationReference]);
        var invalidImplementationMethodDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [invalidImplementationMethod],
            [EntityFrameworkReference, ServicesContractsReference, quotaImplementationReference]);
        var rogueImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [rogueImplementation],
            [EntityFrameworkReference, ServicesContractsReference]);
        var wrongContextDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [wrongContextForFormalStore],
            [EntityFrameworkReference, ServicesContractsReference]);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameNameFake],
            [EntityFrameworkReference, ServicesContractsReference]);

        validDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        crossProjectValidDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        invalidImplementationMethodDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        rogueImplementationDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("RogueModelQuotaReservationStore", StringComparison.Ordinal));
        wrongContextDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("PostgresModelQuotaReservationStore", StringComparison.Ordinal));
        sameNameFakeDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectCommandAndMcpWriteDispatchFromCloudReadOnlyWorkflows()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed record UpdateCloudCommand : AICopilot.SharedKernel.Messaging.ICommand<int>;
                public interface ISender { Task<int> Send(UpdateCloudCommand command); }
            }
            namespace AICopilot.AiGatewayService.AgentTasks
            {
                public interface IAgentToolExecutor { Task ExecuteAsync(); }
            }
            namespace AICopilot.AiGatewayService.Workflows.Executors
            {
                public sealed class CloudReadOnlyTextToSqlFallbackRunner(
                    Fixture.ISender sender,
                    AICopilot.AiGatewayService.AgentTasks.IAgentToolExecutor toolExecutor)
                {
                    public Task<int> DispatchCommandAsync() => sender.Send(new Fixture.UpdateCloudCommand());
                    public Task DispatchMcpWriteAsync() => toolExecutor.ExecuteAsync();
                }
            }
            """;
        const string sameNameFakes = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public interface ICommand<T> { }
                public sealed record UpdateCloudCommand : ICommand<int>;
                public interface ISender { Task<int> Send(UpdateCloudCommand command); }
                public sealed class McpAgentToolExecutor { public Task ExecuteAsync() => Task.CompletedTask; }
            }
            namespace AICopilot.AiGatewayService.Workflows.Executors
            {
                public sealed class CloudReadOnlyTextToSqlFallbackRunner(
                    Fixture.ISender sender,
                    Fixture.McpAgentToolExecutor toolExecutor)
                {
                    public Task<int> DispatchCommandAsync() => sender.Send(new Fixture.UpdateCloudCommand());
                    public Task DispatchMcpWriteAsync() => toolExecutor.ExecuteAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [SharedKernelReference]);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameNameFakes],
            [SharedKernelReference]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(2);
        sameNameFakeDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH007_ShouldResolveAuthorizationAliasesAndExactPublicExceptions()
    {
        const string contracts = """
            namespace Fixture.Contracts { public sealed class Marker { } }
            """;
        const string source = """
            global using Auth = AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirementAttribute;
            global using QueryOfString = AICopilot.SharedKernel.Messaging.IQuery<string>;
            namespace Fixture
            {
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class AuthorizeRequirementAttribute(string permission) : System.Attribute { }
                [Auth("Fixture.Read")]
                public sealed record AuthorizedQuery : QueryOfString;
                [AuthorizeRequirement("Fixture.Read")]
                public sealed record ForgedAuthorizationQuery : QueryOfString;
                public sealed record MissingAuthorizationQuery : QueryOfString;
            }
            namespace AICopilot.IdentityService.Queries
            {
                public sealed record GetCurrentUserProfileQuery : QueryOfString;
                public sealed record GetInitializationStatusQuery : QueryOfString;
            }
            namespace AICopilot.IdentityService.Commands
            {
                public sealed record FinalizeCloudOidcLoginCommand : QueryOfString;
                public sealed record LoginUserCommand : QueryOfString;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, source],
            [SharedKernelReference, CrossCuttingReference]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().HaveCount(2)
            .And.OnlyContain(diagnostic =>
                diagnostic.GetMessage().Contains("MissingAuthorizationQuery", StringComparison.Ordinal) ||
                diagnostic.GetMessage().Contains("ForgedAuthorizationQuery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH007_ShouldAllowOnlyExactResourceAuthorizationRequestAndOwnerPairs()
    {
        const string source = """
            namespace AICopilot.AiGatewayService.Workspaces
            {
                using AICopilot.Services.CrossCutting.Attributes;
                public sealed class ArtifactWorkspaceQueryCoordinator { }
                [ResourceAuthorizationOwner(typeof(ArtifactWorkspaceQueryCoordinator))]
                public sealed record GetArtifactWorkspaceQuery : AICopilot.SharedKernel.Messaging.IQuery<string>;
            }
            namespace Fixture
            {
                using AICopilot.Services.CrossCutting.Attributes;
                public sealed class ArtifactWorkspaceQueryCoordinator { }
                [ResourceAuthorizationOwner(typeof(ArtifactWorkspaceQueryCoordinator))]
                public sealed record GetArtifactWorkspaceQuery : AICopilot.SharedKernel.Messaging.IQuery<string>;
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [SharedKernelReference, CrossCuttingReference]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().ContainSingle()
            .Which.GetMessage().Should().Contain("Fixture.GetArtifactWorkspaceQuery");
    }

    [Fact]
    public async Task AIARCH007_ShouldRequireControllerMetadataAndCloudReadOnlySafetyMetadata()
    {
        const string source = """
            using System;
            namespace Fixture
            {
                using Microsoft.AspNetCore.Mvc;
                using AICopilot.SharedKernel.Ai;
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AuthorizeAttribute : Attribute { }

                [Authorize]
                public sealed class ForgedAuthorizationController : ControllerBase
                {
                    [HttpGet]
                    public object Get() => AiToolSafetyDescriptor.Create(
                        true,
                        AiToolCapabilityKind.Diagnostics,
                        AiToolExternalSystemType.CloudReadOnly);
                }

                [Microsoft.AspNetCore.Authorization.Authorize]
                public sealed class DynamicMetadataController : ControllerBase
                {
                    [HttpGet]
                    public object Get(
                        bool readOnlyDeclared,
                        AiToolCapabilityKind capabilityKind,
                        AiToolExternalSystemType externalSystemType) =>
                        AiToolSafetyDescriptor.Create(
                            readOnlyDeclared,
                            capabilityKind,
                            externalSystemType);
                }

                [Microsoft.AspNetCore.Authorization.Authorize]
                public sealed class SafeController : ControllerBase
                {
                    [HttpGet]
                    public object Get() => AiToolSafetyDescriptor.Create(
                        true,
                        AiToolCapabilityKind.ReadOnlyQuery,
                        AiToolExternalSystemType.CloudReadOnly);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [source],
            [AuthorizationReference, MvcCoreReference, SharedKernelReference]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().Be(3);
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId &&
            diagnostic.GetMessage().Contains("dynamic tool metadata", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH007_ShouldTreatRouteOnlyAndConventionalPublicMethodsAsControllerActions()
    {
        const string source = """
            using System;
            using Microsoft.AspNetCore.Mvc;
            namespace Fixture
            {
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class NonActionAttribute : Attribute { }

                public sealed class MissingController : ControllerBase
                {
                    public object Index() => new object();
                    [Route("details")]
                    public object Details() => new object();
                }

                public sealed class SafeController : ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.NonAction]
                    public object Helper() => new object();
                    private object PrivateHelper() => new object();
                    public static object StaticHelper() => new object();
                    public override string ToString() => nameof(SafeController);
                    [Microsoft.AspNetCore.Authorization.Authorize]
                    public object AuthorizedAction() => new object();
                    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
                    public object AnonymousAction() => new object();
                }

                public sealed class ForgedNonActionController : ControllerBase
                {
                    [NonAction]
                    public object StillAnAction() => new object();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [source],
            [AuthorizationReference, MvcCoreReference]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().HaveCount(3)
            .And.OnlyContain(diagnostic =>
                diagnostic.GetMessage().Contains("MissingController.Index", StringComparison.Ordinal) ||
                diagnostic.GetMessage().Contains("MissingController.Details", StringComparison.Ordinal) ||
                diagnostic.GetMessage().Contains("ForgedNonActionController.StillAnAction", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH007_ShouldRejectRoleAuthorizationAndLimitRoleClaimConsumers()
    {
        const string approvedRoleClaimConsumers = """
            using System.Security.Claims;
            namespace AICopilot.Infrastructure.Authentication
            {
                public sealed class JwtTokenGenerator
                {
                    public string Issue() => ClaimTypes.Role;
                }
            }
            namespace AICopilot.HttpApi.Infrastructure
            {
                public sealed class CurrentUser
                {
                    public string AuditRole() => ClaimTypes.Role;
                }
            }
            """;
        const string invalid = """
            using System.Security.Claims;
            using Microsoft.AspNetCore.Authorization;
            namespace Fixture
            {
                [Authorize(Roles = "Admin")]
                public sealed class RoleProtectedEndpoint { }

                public sealed class RolePolicy
                {
                    public AuthorizationPolicyBuilder Build(AuthorizationPolicyBuilder builder) =>
                        builder.RequireRole("Admin");

                    public string ReadRoleClaim() => ClaimTypes.Role;

                    public AuthorizeAttribute CreateRuntimeRoleAttribute() =>
                        new() { Roles = "Admin" };
                }
            }
            """;
        const string namesakes = """
            namespace Fixture.Namesakes
            {
                public sealed class AuthorizeAttribute
                {
                    public string Roles { get; set; } = string.Empty;
                }
                public sealed class PolicyBuilder
                {
                    public PolicyBuilder RequireRole(string role) => this;
                }
                public sealed class Consumer
                {
                    public object Configure() => new AuthorizeAttribute { Roles = "display-only" };
                    public PolicyBuilder Build(PolicyBuilder builder) => builder.RequireRole("display-only");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [approvedRoleClaimConsumers, invalid, namesakes],
            [AuthorizationReference]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().HaveCount(4)
            .And.OnlyContain(diagnostic =>
                diagnostic.GetMessage().Contains("role", StringComparison.OrdinalIgnoreCase) ||
                diagnostic.GetMessage().Contains("ClaimTypes.Role", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallGraphRules_ShouldFollowConstructorsAccessorsAndUserDefinedConversions()
    {
        const string cloudSource = """
            namespace Fixture
            {
                internal sealed class Writer
                {
                    private readonly Microsoft.EntityFrameworkCore.DbContext db;
                    internal Writer(Microsoft.EntityFrameworkCore.DbContext db)
                    {
                        this.db = db;
                        _ = db.SaveChanges();
                    }
                    internal int Value => db.SaveChanges();
                    public static implicit operator int(Writer value) => value.db.SaveChanges();
                }
                internal sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    internal int Construct() { _ = cloud.Read(); _ = new Writer(db); return 0; }
                    internal int Read(Writer writer) { _ = cloud.Read(); return writer.Value; }
                    internal int Convert(Writer writer) { _ = cloud.Read(); return writer; }
                }
            }
            """;
        const string identitySource = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                internal sealed class Reducer
                {
                    private readonly Microsoft.AspNetCore.Identity.UserManager<object> users;
                    internal Reducer(Microsoft.AspNetCore.Identity.UserManager<object> users)
                    {
                        this.users = users;
                        _ = users.RemoveFromRoleAsync(new object(), "Admin");
                    }
                    internal Task Reduction => users.RemoveFromRoleAsync(new object(), "Admin");
                    public static implicit operator Task(Reducer value) =>
                        value.users.RemoveFromRoleAsync(new object(), "Admin");
                }
                internal sealed class IdentityRunner(Microsoft.AspNetCore.Identity.UserManager<object> users)
                {
                    internal Task Construct() { _ = new Reducer(users); return Task.CompletedTask; }
                    internal Task Read(Reducer reducer) => reducer.Reduction;
                    internal Task Convert(Reducer reducer) => reducer;
                }
            }
            """;

        var cloudDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [cloudSource],
            [EntityFrameworkReference, ServicesContractsReference]);
        var identityDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [identitySource],
            [IdentityCoreReference]);

        cloudDiagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(3);
        identityDiagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId)
            .Should().Be(3);
    }

    [Fact]
    public async Task CallGraphRules_ShouldResolveDelegateFactoriesAndFailClosedForUnknownDelegates()
    {
        const string invalid = """
            using System;
            namespace Fixture
            {
                public sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    private Func<int> Build() => Write;
                    private int Write() => db.SaveChanges();
                    public int Query() { _ = cloud.Read(); return Build()(); }
                }
            }
            """;
        const string unresolved = """
            using System;
            namespace Fixture
            {
                public sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    private static Func<int> Build(Func<int> callback) => callback;
                    public int Query(Func<int> callback) { _ = cloud.Read(); return Build(callback)(); }
                }
            }
            """;
        const string valid = """
            using System;
            namespace Fixture
            {
                public sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    private Func<int> Build() => Read;
                    private int Read() => 42;
                    public int Query() { _ = cloud.Read(); return Build()(); }
                }
            }
            """;
        var references = new[] { EntityFrameworkReference, ServicesContractsReference };
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService", [invalid], references);
        var unresolvedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService", [unresolved], references);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService", [valid], references);

        invalidDiagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        unresolvedDiagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("delegate invocation target", StringComparison.Ordinal));
        validDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldTrackDelegateCompoundAssignmentsAcrossAllEffectGraphs()
    {
        const string sameProjectSource = """
            using System;
            namespace Fixture
            {
                public sealed class LocalRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    Microsoft.EntityFrameworkCore.DbContext db)
                {
                    public void Query()
                    {
                        _ = cloud.Read();
                        Action callback = static () => { };
                        callback += () => { _ = db.SaveChanges(); };
                        callback();
                    }
                }

                public sealed class AdditionHolder
                {
                    private Action callback = static () => { };

                    public AdditionHolder(Microsoft.EntityFrameworkCore.DbContext db) =>
                        callback += () => { _ = db.SaveChanges(); };

                    public void Invoke() => callback();
                }

                public sealed class RemovalHolder
                {
                    private Action callback = static () => { };

                    public RemovalHolder(Microsoft.EntityFrameworkCore.DbContext db) =>
                        callback -= () => { _ = db.SaveChanges(); };

                    public void Invoke() => callback();
                }

                public sealed class MemberRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    AdditionHolder addition,
                    RemovalHolder removal)
                {
                    public void QueryAddition()
                    {
                        _ = cloud.Read();
                        addition.Invoke();
                    }

                    public void QueryRemoval()
                    {
                        _ = cloud.Read();
                        removal.Invoke();
                    }
                }
            }
            """;
        var compoundDelegateReference = new FixtureAssemblyReference(
            "AICopilot.DelegateEffects",
            """
            using System;
            namespace AICopilot.DelegateEffects
            {
                public sealed class CompoundDelegateHolder
                {
                    private Action callback = static () => { };

                    public CompoundDelegateHolder(Microsoft.EntityFrameworkCore.DbContext db) =>
                        callback += () => { _ = db.SaveChanges(); };

                    public void Invoke() => callback();
                }

                public sealed class RemovedDelegateHolder
                {
                    private Action callback = static () => { };

                    public RemovedDelegateHolder(Microsoft.EntityFrameworkCore.DbContext db) =>
                        callback -= () => { _ = db.SaveChanges(); };

                    public void Invoke() => callback();
                }
            }
            """);
        const string crossProjectSource = """
            namespace Fixture
            {
                public sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    AICopilot.DelegateEffects.CompoundDelegateHolder added,
                    AICopilot.DelegateEffects.RemovedDelegateHolder removed)
                {
                    public void QueryAddition()
                    {
                        _ = cloud.Read();
                        added.Invoke();
                    }

                    public void QueryRemoval()
                    {
                        _ = cloud.Read();
                        removed.Invoke();
                    }
                }
            }
            """;

        var sameProjectDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameProjectSource],
            [EntityFrameworkReference, ServicesContractsReference]);
        var crossProjectDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [crossProjectSource],
            [EntityFrameworkReference, ServicesContractsReference, compoundDelegateReference]);

        sameProjectDiagnostics.Count(diagnostic =>
                diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(2);
        sameProjectDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("QueryRemoval", StringComparison.Ordinal));
        crossProjectDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        crossProjectDiagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
            diagnostic.GetMessage().Contains("QueryRemoval", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallGraphRules_ShouldPropagateKnownDelegateArgumentsThroughConditionalInvocation()
    {
        const string source = """
            using System;
            namespace Fixture
            {
                public sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    private static int Apply(Func<int>? callback) => callback?.Invoke() ?? 0;
                    public int Known() { _ = cloud.Read(); return Apply(() => 42); }
                    public int Unknown(Func<int>? callback) { _ = cloud.Read(); return Apply(callback); }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [ServicesContractsReference]);
        var cloudDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .ToArray();

        cloudDiagnostics.Should().ContainSingle();
        cloudDiagnostics[0].GetMessage().Should().Contain("Unknown");
        cloudDiagnostics[0].GetMessage().Should().Contain("delegate invocation target");
    }

    [Fact]
    public async Task CallGraphRules_ShouldHandleRecursiveGenericsAndNeutralDependencyInjectionWithoutFalseRoots()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
                }
            }
            namespace Fixture
            {
                public class Recursive<T> { }
                public sealed class GenericConsumer<T> where T : Recursive<T>
                {
                    public T Echo(T value) => value;
                    public object Invoke(Func<object> callback) => callback();
                }
                public sealed class Registration
                {
                    public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                        services.AddScoped<AICopilot.Services.Contracts.ICloudAiReadClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [ServicesContractsReference]);

        diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId ||
            diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
    }

    [Fact]
    public async Task CallGraphRules_ShouldMapInitializersAndCallbacksToExecutableOwners()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                internal static class Database
                {
                    internal static readonly Microsoft.EntityFrameworkCore.DbContext Instance = new();
                }
                internal sealed class InstanceWriter
                {
                    private readonly int fieldWrite = Database.Instance.SaveChanges();
                    private int PropertyWrite { get; } = Database.Instance.SaveChanges();
                }
                internal static class StaticWriter
                {
                    private static readonly int write = Database.Instance.SaveChanges();
                    internal static int Read() => write;
                }
                internal sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    internal object Construct() { _ = cloud.Read(); return new InstanceWriter(); }
                    internal int StaticRead() { _ = cloud.Read(); return StaticWriter.Read(); }
                    internal Task<int> Callback()
                    {
                        _ = cloud.Read();
                        return Task.Run(() => Database.Instance.SaveChanges());
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [EntityFrameworkReference, ServicesContractsReference]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(3);
    }

    [Fact]
    public async Task AIARCH003_ShouldRejectDynamicDelegateAndAdoDatabaseOwnershipBypasses()
    {
        const string source = """
            using System;
            using System.Data;
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class DatabaseBypass
                {
                    public int Dynamic(Microsoft.EntityFrameworkCore.DbContext db) =>
                        (int)((dynamic)db).SaveChanges();
                    public int MethodGroup(Microsoft.EntityFrameworkCore.DbContext db)
                    {
                        Func<int> write = db.SaveChanges;
                        return write();
                    }
                    public Task<int> TaskRun(Microsoft.EntityFrameworkCore.DbContext db) =>
                        Task.Run(db.SaveChanges);
                    public object Ado(IDbConnection connection, IDbCommand command)
                    {
                        _ = connection.CreateCommand();
                        _ = command.ExecuteNonQuery();
                        _ = command.ExecuteReader();
                        return command.ExecuteScalar();
                    }
                    public object DynamicAdo(IDbCommand command) =>
                        ((dynamic)command).ExecuteScalar();
                }
                public sealed class Namesake
                {
                    public int SaveChanges() => 0;
                    public int Opaque(dynamic value) => (int)value.SaveChanges();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            [EntityFrameworkReference]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId)
            .Should().Be(8);
        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId)
            .Should().NotContain(diagnostic =>
                diagnostic.GetMessage().Contains("Namesake", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH007_ShouldRejectWrongAssemblyExactFqnAuthorizationAndRequirementShadows()
    {
        const string controllerShadow = """
            using System;
            namespace Microsoft.AspNetCore.Authorization
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AuthorizeAttribute : Attribute { }
            }
            namespace Fixture
            {
                [Microsoft.AspNetCore.Authorization.Authorize]
                public sealed class ShadowedController : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    [Microsoft.AspNetCore.Mvc.HttpGet]
                    public string Get() => "ok";
                }
            }
            """;
        const string requirementShadow = """
            using System;
            namespace AICopilot.Services.CrossCutting.Attributes
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            }
            namespace Fixture
            {
                [AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirement("forged")]
                public sealed class ForgedCommand : AICopilot.SharedKernel.Messaging.ICommand { }
            }
            """;

        var controllerDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [controllerShadow],
            [AuthorizationReference, MvcCoreReference]);
        var requirementDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [requirementShadow],
            [SharedKernelReference, CrossCuttingReference]);

        controllerDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId);
        requirementDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId);
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectParameterizedDapperHelperUsedThroughDelegate()
    {
        const string sameProjectSource = """
            using System;
            using System.Threading.Tasks;
            namespace Fixture
            {
                internal sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    internal Task<int> QueryAsync(object connection)
                    {
                        _ = cloud.Read();
                        _ = ExecuteSessionCommandAsync(connection, "SET TRANSACTION READ ONLY");
                        Func<object, string, Task<int>> execute = ExecuteSessionCommandAsync;
                        return execute(connection, "delete from sessions");
                    }

                    private static Task<int> ExecuteSessionCommandAsync(
                        object connection,
                        string commandText) =>
                        Dapper.SqlMapper.ExecuteAsync(connection, commandText);
                }
            }
            """;
        var dapperDelegateReference = new FixtureAssemblyReference(
            "AICopilot.DapperBridge",
            """
            using System;
            using System.Threading.Tasks;
            namespace AICopilot.DapperBridge
            {
                public sealed class DelegateCommandBridge
                {
                    public Task<int> RunAsync(object connection)
                    {
                        _ = ExecuteSessionCommandAsync(connection, "SET TRANSACTION READ ONLY");
                        Func<object, string, Task<int>> execute = ExecuteSessionCommandAsync;
                        return execute(connection, "delete from sessions");
                    }

                    private static Task<int> ExecuteSessionCommandAsync(
                        object connection,
                        string commandText) =>
                        Dapper.SqlMapper.ExecuteAsync(connection, commandText);
                }
            }
            """);
        const string crossProjectSource = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    AICopilot.DapperBridge.DelegateCommandBridge bridge)
                {
                    public Task<int> QueryAsync(object connection)
                    {
                        _ = cloud.Read();
                        return bridge.RunAsync(connection);
                    }
                }
            }
            """;

        var sameProjectDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [sameProjectSource],
            [DapperReference, ServicesContractsReference]);
        var crossProjectDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [crossProjectSource],
            [DapperReference, ServicesContractsReference, dapperDelegateReference]);

        sameProjectDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        crossProjectDiagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldUseRealDatabaseSinksAndExplicitDelegateBindings()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            namespace Fixture
            {
                internal sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                {
                    internal Task<int> ReadAsync(object connection)
                    {
                        _ = cloud.Read();
                        Configure(static () => { });
                        Configure();
                        var lease = new CallbackLease(static () => { });
                        lease.Complete();
                        return ExecuteSessionCommandAsync(
                            connection,
                            "SET TRANSACTION READ ONLY");
                    }

                    private static void Configure(Action? configure = null) => configure?.Invoke();

                    private static Task<int> ExecuteSessionCommandAsync(
                        object connection,
                        string commandText) =>
                        Dapper.SqlMapper.ExecuteAsync(connection, commandText);

                    private static Task ExecuteNonQueryAsync() => Task.CompletedTask;
                }

                internal sealed class CallbackLease
                {
                    private readonly Action release;

                    internal CallbackLease(Action release)
                    {
                        this.release = release;
                    }

                    internal void Complete() => release();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [source],
            [DapperReference, ServicesContractsReference]);

        diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task SemanticRules_ShouldNotReportNamesakesOrApprovedReadOnlyPaths()
    {
        const string source = """
            namespace Fixture
            {
                public sealed class HarmlessStore
                {
                    public void DeleteAsync() { }
                    public void SaveChangesForPreview() { }
                }
                public sealed class CloudReadonlyFormatter
                {
                    public string Format() => "readonly";
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source]);

        diagnostics.Should().BeEmpty();
    }
}
