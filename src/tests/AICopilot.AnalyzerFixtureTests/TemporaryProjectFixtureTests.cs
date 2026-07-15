using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using AICopilot.Architecture.Analyzers;

namespace AICopilot.AnalyzerFixtureTests;

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

    public static TheoryData<string> SuppressionModes =>
    [
        "Pragma",
        "NoWarn",
        "EditorConfig",
        "GlobalConfig"
    ];

    [Theory]
    [MemberData(nameof(RuleIds))]
    public async Task EachRule_ShouldBuildARealValidProject_AndFailARealInvalidProject(string ruleId)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerOutputFingerprint = GetReleaseAnalyzerOutputFingerprint(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-fixtures",
            ruleId,
            Guid.NewGuid().ToString("N"));

        try
        {
            var validProject = WriteFixture(fixtureRoot, "Valid", ruleId, valid: true, analyzerAssembly);
            var invalidProject = WriteFixture(fixtureRoot, "Invalid", ruleId, valid: false, analyzerAssembly);

            var valid = await BuildAsync(validProject);
            var invalid = await BuildAsync(invalidProject);

            GetReleaseAnalyzerOutputFingerprint(solutionRoot).Should().Be(
                analyzerOutputFingerprint,
                "temporary analyzer fixtures must not rebuild or mutate inventory-bound production outputs");
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

    [Theory]
    [MemberData(nameof(SuppressionModes))]
    public async Task AIARCH007_ShouldRemainABuildError_WhenARealProjectAttemptsSuppression(string suppressionMode)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerOutputFingerprint = GetReleaseAnalyzerOutputFingerprint(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-suppression-fixtures",
            suppressionMode,
            Guid.NewGuid().ToString("N"));

        try
        {
            var projectPath = WriteFixture(
                fixtureRoot,
                "Invalid",
                AICopilotArchitectureAnalyzer.SecurityMetadataId,
                valid: false,
                analyzerAssembly);
            ApplySuppressionAttempt(projectPath, suppressionMode);

            var result = await BuildAsync(projectPath);

            GetReleaseAnalyzerOutputFingerprint(solutionRoot).Should().Be(
                analyzerOutputFingerprint,
                "suppression fixtures must not rebuild or mutate inventory-bound production outputs");
            result.ExitCode.Should().NotBe(
                0,
                $"{suppressionMode} must not downgrade a NotConfigurable architecture diagnostic");
            result.Output.Should().Contain(
                AICopilotArchitectureAnalyzer.SecurityMetadataId,
                $"the real build must still report AIARCH007. Output:{Environment.NewLine}{result.Output}");
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private static void ApplySuppressionAttempt(string projectPath, string suppressionMode)
    {
        var projectRoot = Path.GetDirectoryName(projectPath)!;
        var sourcePath = Path.Combine(projectRoot, "Fixture.cs");
        switch (suppressionMode)
        {
            case "Pragma":
                File.WriteAllText(
                    sourcePath,
                    $"#pragma warning disable {AICopilotArchitectureAnalyzer.SecurityMetadataId}{Environment.NewLine}{File.ReadAllText(sourcePath)}");
                break;
            case "NoWarn":
                File.WriteAllText(
                    projectPath,
                    File.ReadAllText(projectPath).Replace(
                        "</PropertyGroup>",
                        $"  <NoWarn>{AICopilotArchitectureAnalyzer.SecurityMetadataId}</NoWarn>{Environment.NewLine}  </PropertyGroup>",
                        StringComparison.Ordinal));
                break;
            case "EditorConfig":
                File.WriteAllText(
                    Path.Combine(projectRoot, ".editorconfig"),
                    $"root = true{Environment.NewLine}[*.cs]{Environment.NewLine}dotnet_diagnostic.{AICopilotArchitectureAnalyzer.SecurityMetadataId}.severity = none{Environment.NewLine}");
                break;
            case "GlobalConfig":
                File.WriteAllText(
                    Path.Combine(projectRoot, "Architecture.globalconfig"),
                    $"is_global = true{Environment.NewLine}dotnet_diagnostic.{AICopilotArchitectureAnalyzer.SecurityMetadataId}.severity = none{Environment.NewLine}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(suppressionMode), suppressionMode, "Unknown suppression mode");
        }
    }

    private static string WriteFixture(
        string fixtureRoot,
        string variant,
        string ruleId,
        bool valid,
        string analyzerAssembly)
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
                <Analyzer Include="{Escape(analyzerAssembly)}" />
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
            valid ? "AICopilot.ArtifactGeneration" : "AICopilot.UnclassifiedBridge",
            "namespace Fixture; public sealed class EntryPoint { }",
            "AICopilot.Services.Contracts"),
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
        namespace AICopilot.Services.Contracts
        {
            public interface ITransactionalExecutionService { Task ExecuteAsync(Func<Task> action); }
        }
        namespace AICopilot.IdentityService.Authorization
        {
            public sealed class EnabledAdminInvariantPolicy { public Task AcquireAsync() => Task.CompletedTask; }
        }
        namespace Fixture
        {
            using AICopilot.IdentityService.Authorization;
            using AICopilot.Services.Contracts;
            public sealed class InlineMethodGroupHandler(
                Microsoft.AspNetCore.Identity.UserManager<object> users, ITransactionalExecutionService transaction, EnabledAdminInvariantPolicy invariant)
            {
                public async Task HandleAsync()
                {
                    await transaction.ExecuteAsync(ReduceAsync);
                    {{(valid ? string.Empty : "await ReduceAsync();")}}
                }

                private async Task ReduceAsync()
                {
                    await invariant.AcquireAsync();
                    await users.RemoveFromRoleAsync(new object(), "Admin");
                }
            }

            public sealed class StoredMethodGroupHandler(
                Microsoft.AspNetCore.Identity.UserManager<object> users, ITransactionalExecutionService transaction, EnabledAdminInvariantPolicy invariant)
            {
                public async Task HandleAsync()
                {
                    Func<Task> action = ReduceAsync;
                    await transaction.ExecuteAsync(action);
                    {{(valid ? string.Empty : "await action();")}}
                }

                private async Task ReduceAsync()
                {
                    await invariant.AcquireAsync();
                    await users.RemoveFromRoleAsync(new object(), "Admin");
                }
            }

            public sealed class StoredLambdaHandler(
                Microsoft.AspNetCore.Identity.UserManager<object> users, ITransactionalExecutionService transaction, EnabledAdminInvariantPolicy invariant)
            {
                public async Task HandleAsync()
                {
                    Func<Task> action = async () =>
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    };
                    await transaction.ExecuteAsync(action);
                    {{(valid ? string.Empty : "await action();")}}
                }
            }

            public sealed class MemberDelegateHandler
            {
                private readonly Microsoft.AspNetCore.Identity.UserManager<object> users;
                private readonly ITransactionalExecutionService transaction;
                private readonly EnabledAdminInvariantPolicy invariant;
                private readonly Func<Task> fieldAction;
                private Func<Task> PropertyAction { get; }

                public MemberDelegateHandler(
                    Microsoft.AspNetCore.Identity.UserManager<object> users,
                    ITransactionalExecutionService transaction,
                    EnabledAdminInvariantPolicy invariant)
                {
                    this.users = users;
                    this.transaction = transaction;
                    this.invariant = invariant;
                    fieldAction = ReduceAsync;
                    PropertyAction = async () =>
                    {
                        await invariant.AcquireAsync();
                        await users.RemoveFromRoleAsync(new object(), "Admin");
                    };
                }

                public async Task HandleAsync()
                {
                    await transaction.ExecuteAsync(fieldAction);
                    await transaction.ExecuteAsync(PropertyAction);
                    {{(valid ? string.Empty : "await fieldAction(); await PropertyAction();")}}
                }

                private async Task ReduceAsync()
                {
                    await invariant.AcquireAsync();
                    await users.RemoveFromRoleAsync(new object(), "Admin");
                }
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
            }
        }
        namespace AICopilot.Services.Contracts
        {
            public interface ICloudAiReadClient { int Read(); }
        }
        namespace Fixture
        {
            public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
            public sealed class NeutralRunner
            {
                private readonly AICopilot.Services.Contracts.ICloudAiReadClient client;
                private readonly Func<Session, Task> write;

                public NeutralRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    AICopilot.SharedKernel.Repository.IRepository<Session> repository)
                {
                    this.client = client;
                    write = {{(valid ? "static _ => Task.CompletedTask" : "repository.AddAsync")}};
                }

                public async Task<int> Run()
                {
                    _ = client.Read();
                    await write(new Session());
                    return 42;
                }
            }
        }
        """;

    private static string AuthorizationFixture(bool valid) => $$"""
        using System;
        namespace AICopilot.SharedKernel.Messaging { public interface IQuery<T> { } }
        namespace AICopilot.Services.CrossCutting.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
        }
        namespace Fixture
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            {{(valid ? "[AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirement(\"Fixture.Read\")]" : "[Fixture.AuthorizeRequirement(\"Fixture.Read\")]")}}
            public sealed record FixtureQuery : AICopilot.SharedKernel.Messaging.IQuery<string>;
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

    private static string GetReleaseAnalyzerAssemblyPath(string solutionRoot) => Path.Combine(
        solutionRoot,
        "src",
        "analyzers",
        "AICopilot.Architecture.Analyzers",
        "bin",
        "Release",
        "netstandard2.0",
        "AICopilot.Architecture.Analyzers.dll");

    private static string GetReleaseAnalyzerOutputFingerprint(string solutionRoot)
    {
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerSymbols = Path.ChangeExtension(analyzerAssembly, ".pdb");
        return string.Concat(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(analyzerAssembly))),
            ":",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(analyzerSymbols))));
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
