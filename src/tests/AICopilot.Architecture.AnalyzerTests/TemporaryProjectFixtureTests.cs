using System.Diagnostics;
using System.Security;
using AICopilot.Architecture.Analyzers;

namespace AICopilot.Architecture.AnalyzerTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnalyzerFixtureCollection
{
    public const string Name = "Analyzer temporary csproj fixtures";
}

[Collection(AnalyzerFixtureCollection.Name)]
public sealed class TemporaryProjectFixtureTests
{
    public static TheoryData<string> RuleIds =>
    [
        AICopilotArchitectureAnalyzer.ProjectBoundaryId,
        AICopilotArchitectureAnalyzer.AggregateBoundaryId,
        AICopilotArchitectureAnalyzer.PersistenceOwnerId,
        AICopilotArchitectureAnalyzer.EnabledAdminInvariantId,
        AICopilotArchitectureAnalyzer.AgentPluginBoundaryId,
        AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId,
        AICopilotArchitectureAnalyzer.SecurityMetadataId
    ];

    [Theory]
    [MemberData(nameof(RuleIds))]
    public async Task EachRule_ShouldBuildARealValidProject_AndFailARealInvalidProject(string ruleId)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerProject = Path.Combine(
            solutionRoot,
            "src",
            "analyzers",
            "AICopilot.Architecture.Analyzers",
            "AICopilot.Architecture.Analyzers.csproj");
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-fixtures",
            ruleId,
            Guid.NewGuid().ToString("N"));

        try
        {
            var validProject = WriteFixture(fixtureRoot, "Valid", ruleId, valid: true, analyzerProject);
            var invalidProject = WriteFixture(fixtureRoot, "Invalid", ruleId, valid: false, analyzerProject);

            var valid = await BuildAsync(validProject);
            var invalid = await BuildAsync(invalidProject);

            valid.ExitCode.Should().Be(
                0,
                $"the valid {ruleId} temporary csproj must build. Output:{Environment.NewLine}{valid.Output}");
            invalid.ExitCode.Should().NotBe(
                0,
                $"the invalid {ruleId} temporary csproj must fail the build");
            invalid.Output.Should().Contain(
                ruleId,
                $"the invalid fixture must fail with its stable diagnostic. Output:{Environment.NewLine}{invalid.Output}");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static string WriteFixture(
        string fixtureRoot,
        string variant,
        string ruleId,
        bool valid,
        string analyzerProject)
    {
        var specification = GetSpecification(ruleId, valid);
        var projectRoot = Path.Combine(fixtureRoot, variant);
        Directory.CreateDirectory(projectRoot);

        string projectReferenceXml = string.Empty;
        if (specification.DirectProjectReference is not null)
        {
            var dependencyRoot = Path.Combine(projectRoot, specification.DirectProjectReference);
            Directory.CreateDirectory(dependencyRoot);
            File.WriteAllText(
                Path.Combine(dependencyRoot, $"{specification.DirectProjectReference}.csproj"),
                BuildPlainProject(specification.DirectProjectReference));
            File.WriteAllText(
                Path.Combine(dependencyRoot, "Dependency.cs"),
                "namespace FixtureDependency; public sealed class DependencyMarker { }");
            var dependencyProject = Path.Combine(
                dependencyRoot,
                $"{specification.DirectProjectReference}.csproj");
            projectReferenceXml = $"""
                    <ProjectReference Include="{Escape(dependencyProject)}" />
                    <AdditionalFiles Include="{Escape(dependencyProject)}" />
                """;
        }

        var projectPath = Path.Combine(projectRoot, "Fixture.csproj");
        File.WriteAllText(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{Escape(specification.AssemblyName)}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{Escape(analyzerProject)}"
                                  OutputItemType="Analyzer"
                                  ReferenceOutputAssembly="false"
                                  PrivateAssets="all" />
            {projectReferenceXml}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectRoot, "Fixture.cs"), specification.Source);
        return projectPath;
    }

    private static FixtureSpecification GetSpecification(string ruleId, bool valid) => ruleId switch
    {
        AICopilotArchitectureAnalyzer.ProjectBoundaryId => new(
            "AICopilot.AiGatewayService",
            "namespace Fixture; public sealed class EntryPoint { }",
            valid ? "AICopilot.Core.AiGateway" : "AICopilot.Sample.Tests"),
        AICopilotArchitectureAnalyzer.AggregateBoundaryId => new(
            "AICopilot.Core.AiGateway",
            valid ? """
            namespace AICopilot.SharedKernel.Domain { public interface IAggregateRoot { } }
            namespace AICopilot.SharedKernel.Repository { public interface IRepository<T> where T : class { } }
            namespace AICopilot.Core.AiGateway.Aggregates.Sessions
            {
                public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
            }
            namespace Fixture
            {
                public sealed class Consumer(AICopilot.SharedKernel.Repository.IRepository<AICopilot.Core.AiGateway.Aggregates.Sessions.Session> repository) { }
            }
            """ : """
            namespace AICopilot.SharedKernel.Domain { public interface IAggregateRoot { } }
            namespace AICopilot.SharedKernel.Repository { public interface IRepository<T> where T : class { } }
            namespace Fixture
            {
                public sealed class LeafEntity { }
                public sealed class Consumer(AICopilot.SharedKernel.Repository.IRepository<LeafEntity> repository) { }
            }
            """,
            null),
        AICopilotArchitectureAnalyzer.PersistenceOwnerId => new(
            valid ? "AICopilot.EntityFrameworkCore" : "AICopilot.AiGatewayService",
            """
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { public int SaveChanges() => 0; }
            }
            namespace Fixture
            {
                public sealed class OwnedDb : Microsoft.EntityFrameworkCore.DbContext
                {
                    public int Commit() => SaveChanges();
                }
            }
            """,
            null),
        AICopilotArchitectureAnalyzer.EnabledAdminInvariantId => new(
            "AICopilot.IdentityService",
            IdentityFixture(valid),
            null),
        AICopilotArchitectureAnalyzer.AgentPluginBoundaryId => new(
            "AICopilot.AiGatewayService",
            PluginFixture(valid),
            null),
        AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId => new(
            "AICopilot.AiGatewayService",
            CloudReadFixture(valid),
            null),
        AICopilotArchitectureAnalyzer.SecurityMetadataId => new(
            "AICopilot.SampleService",
            AuthorizationFixture(valid),
            null),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleId), ruleId, "Unknown analyzer rule fixture")
    };

    private static string IdentityFixture(bool valid) => $$"""
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
            public interface ITransactionalExecutionService { Task ExecuteAsync(Func<Task> action); }
            public sealed class EnabledAdminInvariantPolicy { public Task AcquireAsync() => Task.CompletedTask; }
        }
        namespace Fixture
        {
            using AICopilot.IdentityService.Authorization;
            public sealed class Handler(
                Microsoft.AspNetCore.Identity.UserManager<object> users{{(valid ? ", ITransactionalExecutionService transaction, EnabledAdminInvariantPolicy invariant" : string.Empty)}})
            {
                public Task HandleAsync() => {{(valid ?
                    "transaction.ExecuteAsync(async () => { await invariant.AcquireAsync(); await users.RemoveFromRoleAsync(new object(), \"Admin\"); })" :
                    "users.RemoveFromRoleAsync(new object(), \"Admin\")")}};
            }
        }
        """;

    private static string PluginFixture(bool valid) => $$"""
        using System.ComponentModel;
        namespace AICopilot.AgentPlugin
        {
            public enum ChatExposureMode { Disabled, Advisory }
            public interface IAgentPlugin { string Description { get; } ChatExposureMode ChatExposureMode { get; } }
            public abstract class AgentPluginBase : IAgentPlugin
            {
                public virtual string Description => string.Empty;
                public virtual ChatExposureMode ChatExposureMode => ChatExposureMode.Disabled;
            }
        }
        namespace Fixture
        {
            public sealed class FixturePlugin : AICopilot.AgentPlugin.AgentPluginBase
            {
                {{(valid ?
                    "public override string Description => \"fixture\"; public override AICopilot.AgentPlugin.ChatExposureMode ChatExposureMode => AICopilot.AgentPlugin.ChatExposureMode.Advisory; [Description(\"read\")] public string Read() => \"ok\";" :
                    string.Empty)}}
            }
        }
        """;

    private static string CloudReadFixture(bool valid) => $$"""
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
            }
        }
        namespace Fixture
        {
            public interface ICloudAiReadClient { }
            public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
            public sealed class CloudReadonlyRunner(
                ICloudAiReadClient client,
                AICopilot.SharedKernel.Repository.IRepository<Session> repository)
            {
                public {{(valid ? "int Query() => 42" : "Task Query() => repository.AddAsync(new Session())")}};
            }
        }
        """;

    private static string AuthorizationFixture(bool valid) => $$"""
        using System;
        namespace MediatR { public interface IQuery<T> { } }
        namespace AICopilot.Services.Contracts
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
        }
        namespace Fixture
        {
            {{(valid ? "[AICopilot.Services.Contracts.AuthorizeRequirement(\"Fixture.Read\")]" : string.Empty)}}
            public sealed record FixtureQuery : MediatR.IQuery<string>;
        }
        """;

    private static string BuildPlainProject(string assemblyName) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>{Escape(assemblyName)}</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    private static async Task<BuildResult> BuildAsync(string projectPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(projectPath)!
            }
        };
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add(projectPath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("--nologo");
        process.StartInfo.ArgumentList.Add("--disable-build-servers");
        process.StartInfo.ArgumentList.Add("-v:minimal");
        process.StartInfo.ArgumentList.Add("-p:RestoreIgnoreFailedSources=true");
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return new BuildResult(
            process.ExitCode,
            string.Concat(await stdout, Environment.NewLine, await stderr));
    }

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

        throw new DirectoryNotFoundException("Could not locate AICopilot.slnx from the analyzer test output directory.");
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? value;

    private sealed record FixtureSpecification(
        string AssemblyName,
        string Source,
        string? DirectProjectReference);

    private sealed record BuildResult(int ExitCode, string Output);
}
