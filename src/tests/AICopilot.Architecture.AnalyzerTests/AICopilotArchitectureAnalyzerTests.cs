using AICopilot.Architecture.Analyzers;

namespace AICopilot.Architecture.AnalyzerTests;

public sealed class AICopilotArchitectureAnalyzerTests
{
    [Fact]
    public async Task AIARCH001_ShouldUseDirectMsbuildProjectReferences_AndRejectTestOrReverseLayerEdges()
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

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        wrongCore.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        testDependency.Should().ContainSingle(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.ProjectBoundaryId);
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

        var valid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [validSource]);
        var invalid = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.Core.AiGateway",
            [invalidSource]);

        valid.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
        invalid.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.AggregateBoundaryId);
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
                public class UserManager<T>
                {
                    public Task RemoveFromRoleAsync(T user, string role) => Task.CompletedTask;
                }
            }
            namespace AICopilot.IdentityService.Authorization
            {
                public interface ITransactionalExecutionService
                {
                    Task ExecuteAsync(Func<Task> action);
                }
                public sealed class EnabledAdminInvariantPolicy
                {
                    public Task AcquireAsync() => Task.CompletedTask;
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

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, invalid]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.IdentityService",
            [contracts, valid]);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
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
            namespace Fixture
            {
                public interface ICloudAiReadClient { }
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
                public sealed class CloudReadonlyRunner(ICloudAiReadClient client, IDataGateway gateway)
                {
                    public int Query() => gateway.Execute();
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
                public sealed class CloudReadonlyRunner(ICloudAiReadClient client, IDataGateway gateway)
                {
                    public int Query() => gateway.Execute();
                }
            }
            """;

        var invalidDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, invalid]);
        var validDiagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [contracts, valid]);

        invalidDiagnostics.Should().Contain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        validDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
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
            namespace Fixture
            {
                public interface ICloudAiReadClient { }
                public sealed class CloudReadonlyRunner(
                    ICloudAiReadClient client,
                    AICopilot.Services.Contracts.IAuditLogWriter audit)
                {
                    public Task<int> RunAsync() => audit.SaveChangesAsync();
                }
            }
            """;
        const string invalid = """
            using System.Threading.Tasks;
            namespace Fixture
            {
                public interface ICloudAiReadClient { }
                public interface IAuditLogWriter
                {
                    Task<int> SaveChangesAsync();
                }
                public sealed class CloudReadonlyRunner(ICloudAiReadClient client, IAuditLogWriter audit)
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
                public interface ICloudAiReadClient { }
                public sealed record UpdateCloudCommand : AICopilot.SharedKernel.Messaging.ICommand<int>;
                public interface ISender { Task<int> Send(UpdateCloudCommand command); }
                public interface IMcpWriteTool { Task DeleteRecordAsync(); }
                public sealed class CloudReadonlyWorkflow(
                    ICloudAiReadClient client,
                    ISender sender,
                    IMcpWriteTool mcpWriteTool)
                {
                    public Task<int> DispatchCommandAsync() => sender.Send(new UpdateCloudCommand());
                    public Task DispatchMcpWriteAsync() => mcpWriteTool.DeleteRecordAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.AiGatewayService",
            [source]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
            .Should().Be(2);
    }

    [Fact]
    public async Task AIARCH007_ShouldResolveAuthorizationAliasesAndExactPublicExceptions()
    {
        const string contracts = """
            using System;
            namespace MediatR { public interface IQuery<T> { } }
            namespace AICopilot.Services.Contracts
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            }
            """;
        const string source = """
            global using Auth = AICopilot.Services.Contracts.AuthorizeRequirementAttribute;
            global using QueryOfString = MediatR.IQuery<string>;
            namespace Fixture
            {
                [Auth("Fixture.Read")]
                public sealed record AuthorizedQuery : QueryOfString;
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
            .Should().ContainSingle()
            .Which.GetMessage().Should().Contain("MissingAuthorizationQuery");
    }

    [Fact]
    public async Task AIARCH007_ShouldAllowOnlyExactResourceAuthorizationRequestAndOwnerPairs()
    {
        const string source = """
            using System;
            namespace MediatR
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
                public sealed record GetArtifactWorkspaceQuery : MediatR.IQuery<string>;
            }
            namespace Fixture
            {
                using AICopilot.Services.CrossCutting.Attributes;
                public sealed class ArtifactWorkspaceQueryCoordinator { }
                [ResourceAuthorizationOwner(typeof(ArtifactWorkspaceQueryCoordinator))]
                public sealed record GetArtifactWorkspaceQuery : MediatR.IQuery<string>;
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
                public sealed class HttpGetAttribute : Attribute { }
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
                public enum AiToolCapabilityKind { ReadOnlyQuery, SideEffecting }
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
                public sealed class UnsafeController : ControllerBase
                {
                    [HttpGet]
                    public object Get() => AiToolSafetyDescriptor.Create(
                        false,
                        AiToolCapabilityKind.SideEffecting,
                        AiToolExternalSystemType.CloudReadOnly);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHarness.GetArchitectureDiagnosticsAsync(
            "AICopilot.HttpApi",
            [source]);

        diagnostics.Count(diagnostic => diagnostic.Id == AICopilotArchitectureAnalyzer.SecurityMetadataId)
            .Should().Be(2);
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
