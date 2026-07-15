using AICopilot.Architecture.Analyzers;

namespace AICopilot.Architecture.AnalyzerTests;

public sealed class AICopilotArchitectureAnalyzerTests
{
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
        var unknownTarget = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source],
            "AICopilot.UnclassifiedBridge");

        foreach (var (assemblyName, references) in new[]
                 {
                     ("AICopilot.ArtifactGeneration", new[] { "AICopilot.Services.Contracts" }),
                     ("AICopilot.CloudReadClient", new[] { "AICopilot.Services.Contracts" }),
                     ("AICopilot.McpRuntime", new[] { "AICopilot.Core.AiGateway", "AICopilot.AgentPlugin" }),
                     ("AICopilot.SecretProtection", new[] { "AICopilot.Core.AiGateway", "AICopilot.Core.Rag" }),
                     ("AICopilot.SqlSafety", new[] { "AICopilot.Services.Contracts" })
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

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        wrongCore.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        testDependency.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        unknownSource.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        unknownTarget.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
    }

    [Fact]
    public async Task AIARCH002_ShouldResolveGlobalAliasesAndGenericRepositoryEntities()
    {
        const string validSource = """
            global using Repo = AICopilot.SharedKernel.Repository.IRepository<AICopilot.Core.AiGateway.Aggregates.Sessions.Session>;
            namespace AICopilot.SharedKernel.Domain
            {
                public interface IAggregateRoot { }
            }
            namespace AICopilot.SharedKernel.Repository
            {
                public interface IRepository<T> where T : class { }
            }
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
            namespace AICopilot.SharedKernel.Domain
            {
                public interface IAggregateRoot { }
            }
            namespace AICopilot.SharedKernel.Repository
            {
                public interface IRepository<T> where T : class { }
            }
            namespace Fixture
            {
                public sealed class LeafEntity { }
                public sealed class RogueAggregate : AICopilot.SharedKernel.Domain.IAggregateRoot { }
                public sealed class Consumer(Repo repository) { }
            }
            """;
        const string sameNameFakeRepository = """
            namespace AICopilot.SharedKernel.Domain
            {
                public interface IAggregateRoot { }
            }
            namespace Fixture
            {
                public interface IRepository<T> where T : class { }
                public sealed class LeafEntity { }
                public sealed class Consumer(IRepository<LeafEntity> repository) { }
            }
            """;

        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [validSource]);
        var invalid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [invalidSource]);
        var sameNameFake = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [sameNameFakeRepository]);

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
        invalid.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
        sameNameFake.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
    }

    [Fact]
    public async Task AIARCH003_ShouldResolveAliasedDbContextOwnership()
    {
        const string source = """
            global using Ef = Microsoft.EntityFrameworkCore;
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { public virtual int SaveChanges() => 0; }
            }
            namespace Fixture
            {
                public sealed class ServiceDb : Ef.DbContext
                {
                    public int Commit() => SaveChanges();
                }
            }
            """;

        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.EntityFrameworkCore",
            [source]);
        var invalid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source]);

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
        invalid.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
    }

    [Fact]
    public async Task AIARCH003_ShouldLimitTheInfrastructureDatabaseExceptionToTheExactSessionLock()
    {
        const string contracts = """
            namespace Npgsql
            {
                public sealed class NpgsqlConnection
                {
                    public void Open() { }
                }
            }
            """;
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
            [contracts, valid]);
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, invalid]);

        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
        invalidDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.PersistenceOwnerId);
    }

    [Fact]
    public async Task AIARCH004_ShouldFollowCrossFileGenericHelpersAndInterfaceDispatch()
    {
        const string contracts = """
            using System;
            using System.Threading.Tasks;
            namespace Microsoft.AspNetCore.Identity
            {
                public class IdentityUser<TKey>
                {
                    public bool LockoutEnabled { get; set; }
                    public System.DateTimeOffset? LockoutEnd { get; set; }
                }
                public class IdentityUserRole<TKey> { }
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
            namespace AICopilot.Services.Contracts
            {
                public interface ITransactionalExecutionService
                {
                    Task ExecuteAsync(Func<Task> action);
                }
                public interface IIdentityEnabledAdminInvariantGuard
                {
                    Task AcquireAsync();
                }
            }
            namespace AICopilot.IdentityService.Authorization
            {
                public sealed class EnabledAdminInvariantPolicy
                {
                    public Task AcquireAsync() => Task.CompletedTask;
                }
            }
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
                        await users.UpdateAsync(new User());
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
                    Microsoft.AspNetCore.Identity.UserManager<User> users,
                    Microsoft.AspNetCore.Identity.RoleManager<object> roles)
                {
                    public async Task MutateAsync(User user)
                    {
                        await users.SetLockoutEnabledAsync(user, true);
                        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                        user.LockoutEnabled = true;
                        user.LockoutEnd = DateTimeOffset.MaxValue;
                        await roles.DeleteAsync(new object());
                    }
                }

                public sealed class IdentityRelationHandler
                {
                    public void Remove(List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>> relations) =>
                        relations.Remove(new Microsoft.AspNetCore.Identity.IdentityUserRole<string>());
                }

                public sealed class DynamicIdentityHandler(Microsoft.AspNetCore.Identity.UserManager<User> users)
                {
                    public Task MutateAsync(dynamic unknown)
                    {
                        unknown.UpdateAsync(new User());
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
            using System;
            using System.Threading.Tasks;
            using AICopilot.Services.Contracts;
            using Microsoft.Extensions.DependencyInjection;
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

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, invalid]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, valid]);
        var disjointDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, disjoint]);
        var reversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, reversed]);
        var methodGroupValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupValid]);
        var methodGroupReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupReversed]);
        var methodGroupDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, methodGroupDualUse]);
        var storedMethodGroupValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupValid]);
        var storedMethodGroupReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupReversed]);
        var storedMethodGroupDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedMethodGroupDualUse]);
        var storedLambdaValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaValid]);
        var storedLambdaReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaReversed]);
        var storedLambdaDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, storedLambdaDualUse]);
        var crossHandlerDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, crossHandlerDualUse]);
        var memberDelegateValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateValid]);
        var memberDelegateReversedDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateReversed]);
        var memberDelegateDualUseDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, memberDelegateDualUse]);
        var hiddenRootValidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, hiddenRootValid]);
        var hiddenRootInvalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, hiddenRootInvalid]);
        var expandedMutationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, expandedMutationSurface]);
        var sameNameFakeIdentityDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, sameNameFakeIdentitySurface]);
        var exactInvariantImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, exactInvariantImplementations]);
        var invalidInvariantImplementationDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, invalidInvariantImplementations]);

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
                     "DynamicIdentityHandler",
                     "FakeGuardHandler"
                 })
        {
            expandedMutationDiagnostics.Should().Contain(diagnostic =>
                diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId &&
                diagnostic.GetMessage().Contains(expectedRoot, StringComparison.Ordinal));
        }
        sameNameFakeIdentityDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        exactInvariantImplementationDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        invalidInvariantImplementationDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task AIARCH005_ShouldRequireInheritedPluginManifestAndRejectProductionTestDoubles()
    {
        const string contracts = """
            using System.Collections.Generic;
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
            [contracts, invalid]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, valid]);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId);
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
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { public virtual int SaveChanges() => 0; }
            }
            namespace AICopilot.Services.Contracts
            {
                public interface ICloudAiReadClient { int Read(); }
            }
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
            namespace AICopilot.CloudReadClient
            {
                public sealed class CloudAiReadClient : AICopilot.Services.Contracts.ICloudAiReadClient
                {
                    public int Read() => 42;
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
                        var local = new AICopilot.CloudReadClient.CloudAiReadClient();
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
            namespace AICopilot.SharedKernel.Domain
            {
                public interface IAggregateRoot { }
            }
            namespace AICopilot.SharedKernel.Repository
            {
                public interface IRepository<T> where T : class
                {
                    Task UpdateAsync(T entity);
                }
            }
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
            namespace Dapper
            {
                public static class SqlMapper
                {
                    public static Task<int> ExecuteAsync(object connection, string sql) => Task.FromResult(1);
                }
            }
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
            using System.Threading.Tasks;
            namespace Fixture
            {
                public sealed class HttpWriteRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                    HttpClient http)
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

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, invalid]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, valid]);
        var neutralRootDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, neutralRoots]);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, sameNameFake]);
        var delegateMemberWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, delegateMemberWrites]);
        var exactRepositoryWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, exactRepositoryWrite]);
        var sameNameFakeRepositoryWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, sameNameFakeRepositoryWrite]);
        var exactDapperWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [contracts, exactDapperWrite]);
        var sameNameFakeDapperWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Dapper",
            [contracts, sameNameFakeDapperWrite]);
        var httpAndIndirectWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, httpAndIndirectWrites]);
        var formalHttpGetDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, formalHttpGetTransport]);
        var sameNameFakeHttpDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, sameNameFakeHttpClient]);
        var generatedCloudWriteDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Infrastructure",
            [contracts, generatedCloudWrite]);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        neutralRootDiagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(5);
        neutralRootDiagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().NotContain(diagnostic => diagnostic.GetMessage().Contains("GenericAgentOrchestrator", StringComparison.Ordinal));
        sameNameFakeDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        delegateMemberWriteDiagnostics.Count(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId).Should().Be(2);
        exactRepositoryWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        sameNameFakeRepositoryWriteDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        exactDapperWriteDiagnostics.Should().ContainSingle(
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
            diagnostic.GetMessage().Contains("OrdinaryGetAsync", StringComparison.Ordinal));
        formalHttpGetDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        sameNameFakeHttpDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        generatedCloudWriteDiagnostics.Should().ContainSingle(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldAllowOnlyTheExactAICopilotAuditWriterSideEffect()
    {
        const string valid = """
            using System.Threading.Tasks;
            namespace AICopilot.Services.Contracts
            {
                public interface IAuditLogWriter
                {
                    Task<int> SaveChangesAsync();
                }
            }
            namespace AICopilot.Services.Contracts
            {
                public interface ICloudAiReadClient { }
            }
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
        const string invalid = """
            using System.Threading.Tasks;
            namespace AICopilot.Services.Contracts
            {
                public interface ICloudAiReadClient { }
            }
            namespace Fixture
            {
                public interface IAuditLogWriter
                {
                    Task<int> SaveChangesAsync();
                }
                public sealed class CloudReadonlyRunner(AICopilot.Services.Contracts.ICloudAiReadClient client, IAuditLogWriter audit)
                {
                    public Task<int> RunAsync() => audit.SaveChangesAsync();
                }
            }
            """;

        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [valid]);
        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [invalid]);

        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        invalidDiagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectCommandAndMcpWriteDispatchFromCloudReadOnlyWorkflows()
    {
        const string source = """
            using System.Threading.Tasks;
            namespace AICopilot.SharedKernel.Messaging
            {
                public interface ICommand<T> { }
            }
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
            [source]);
        var sameNameFakeDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [sameNameFakes]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(2);
        sameNameFakeDiagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
    }

    [Fact]
    public async Task AIARCH007_ShouldResolveAuthorizationAliasesAndExactPublicExceptions()
    {
        const string contracts = """
            using System;
            namespace AICopilot.SharedKernel.Messaging { public interface IQuery<T> { } }
            namespace AICopilot.Services.CrossCutting.Attributes
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            }
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
            "AICopilot.SampleService",
            [contracts, source]);

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
            using System;
            namespace AICopilot.SharedKernel.Messaging
            {
                public interface IQuery<T> { }
            }
            namespace AICopilot.Services.CrossCutting.Attributes
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ResourceAuthorizationOwnerAttribute(Type ownerType) : Attribute { }
            }
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
            [source]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().ContainSingle()
            .Which.GetMessage().Should().Contain("Fixture.GetArtifactWorkspaceQuery");
    }

    [Fact]
    public async Task AIARCH007_ShouldRequireControllerMetadataAndCloudReadOnlySafetyMetadata()
    {
        const string source = """
            using System;
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpGetAttribute : Routing.HttpMethodAttribute { }
            }
            namespace Microsoft.AspNetCore.Mvc.Routing
            {
                [AttributeUsage(AttributeTargets.Method)]
                public abstract class HttpMethodAttribute : Attribute { }
            }
            namespace Microsoft.AspNetCore.Authorization
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AuthorizeAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AllowAnonymousAttribute : Attribute { }
            }
            namespace AICopilot.SharedKernel.Ai
            {
                public enum AiToolExternalSystemType { Unknown, CloudReadOnly }
                public enum AiToolCapabilityKind { ReadOnlyQuery, Diagnostics, SideEffecting }
                public static class AiToolSafetyDescriptor
                {
                    public static object Create(
                        bool readOnlyDeclared,
                        AiToolCapabilityKind capabilityKind,
                        AiToolExternalSystemType externalSystemType) => new();
                }
            }
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
            [source]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().Be(3);
        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId &&
            diagnostic.GetMessage().Contains("dynamic tool metadata", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AIARCH007_ShouldRejectRoleAuthorizationAndLimitRoleClaimConsumers()
    {
        const string authorizationContracts = """
            using System;
            namespace Microsoft.AspNetCore.Authorization
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AuthorizeAttribute : Attribute
                {
                    public string? Roles { get; set; }
                }
                public sealed class AuthorizationPolicyBuilder
                {
                    public AuthorizationPolicyBuilder RequireRole(params string[] roles) => this;
                }
            }
            """;
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
            [authorizationContracts, approvedRoleClaimConsumers, invalid, namesakes]);

        diagnostics.Where(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().HaveCount(4)
            .And.OnlyContain(diagnostic =>
                diagnostic.GetMessage().Contains("role", StringComparison.OrdinalIgnoreCase) ||
                diagnostic.GetMessage().Contains("ClaimTypes.Role", StringComparison.Ordinal));
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
