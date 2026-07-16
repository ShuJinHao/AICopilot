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

    public static TheoryData<string> CrossProjectRuleIds =>
    [
        AICopilotArchitectureAnalyzer.EnabledAdminInvariantId,
        AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId
    ];

    public static TheoryData<string> InvalidSummaryModes =>
    [
        "Missing",
        "Corrupt",
        "SpoofedProducer",
        "OldSchema"
    ];

    public static TheoryData<string> InvalidProductionAssemblyNames =>
    [
        "AICopilot.Core.Unclassified",
        "AICopilot.FakesService",
        "AICopilot.IdentityService.Tests"
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

    [Theory]
    [MemberData(nameof(CrossProjectRuleIds))]
    public async Task CrossProjectCallGraph_ShouldRejectSensitiveImplementationReachedThroughMetadata(
        string ruleId)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerOutputFingerprint = GetReleaseAnalyzerOutputFingerprint(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-cross-project-fixtures",
            ruleId,
            Guid.NewGuid().ToString("N"));

        try
        {
            var fixture = WriteCrossProjectFixture(fixtureRoot, ruleId, analyzerAssembly);

            var dependency = await BuildAsync(fixture.DependencyProject);
            var consumer = await BuildAsync(fixture.ConsumerProject);

            GetReleaseAnalyzerOutputFingerprint(solutionRoot).Should().Be(
                analyzerOutputFingerprint,
                "cross-project fixtures must not rebuild or mutate inventory-bound production outputs");
            dependency.ExitCode.Should().Be(
                0,
                $"the dependency fixture must be valid in isolation. Output:{Environment.NewLine}{dependency.Output}");
            consumer.ExitCode.Should().NotBe(
                0,
                $"the consumer must not hide {ruleId} behind a referenced assembly metadata boundary. Output:{Environment.NewLine}{consumer.Output}");
            consumer.Output.Should().Contain(
                ruleId,
                $"the cross-project consumer must fail with {ruleId}. Output:{Environment.NewLine}{consumer.Output}");
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
    [MemberData(nameof(CrossProjectRuleIds))]
    public async Task CompositionHost_ShouldRejectSiblingConsumerAndImplementationClosure(string ruleId)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerOutputFingerprint = GetReleaseAnalyzerOutputFingerprint(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-composition-fixtures",
            ruleId,
            Guid.NewGuid().ToString("N"));

        try
        {
            var fixture = WriteCompositionFixture(fixtureRoot, ruleId, analyzerAssembly);

            var contract = await BuildAsync(fixture.ContractProject);
            var implementation = await BuildAsync(fixture.ImplementationProject);
            var consumer = await BuildAsync(fixture.ConsumerProject);
            var host = await BuildAsync(fixture.HostProject);

            GetReleaseAnalyzerOutputFingerprint(solutionRoot).Should().Be(
                analyzerOutputFingerprint,
                "composition fixtures must not rebuild or mutate inventory-bound production outputs");
            contract.ExitCode.Should().Be(0, contract.Output);
            implementation.ExitCode.Should().Be(
                0,
                $"the implementation is valid in isolation. Output:{Environment.NewLine}{implementation.Output}");
            consumer.ExitCode.Should().Be(
                0,
                $"the consumer cannot see its sibling implementation. Output:{Environment.NewLine}{consumer.Output}");
            host.ExitCode.Should().NotBe(
                0,
                $"the composition host must close the sibling summary graph for {ruleId}. Output:{Environment.NewLine}{host.Output}");
            host.Output.Should().Contain(ruleId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectThreeAssemblyTransitiveSummaryChain()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var analyzerOutputFingerprint = GetReleaseAnalyzerOutputFingerprint(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-transitive-summary-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formalReferences = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId,
                analyzerAssembly);
            var entityFrameworkProject = formalReferences.Single(reference =>
                Path.GetFileNameWithoutExtension(reference.ProjectPath) == "Microsoft.EntityFrameworkCore");
            var contractsProject = formalReferences.Single(reference =>
                Path.GetFileNameWithoutExtension(reference.ProjectPath) == "AICopilot.Services.Contracts");
            var leafRoot = Path.Combine(fixtureRoot, "Leaf");
            var bridgeRoot = Path.Combine(fixtureRoot, "Bridge");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(leafRoot);
            Directory.CreateDirectory(bridgeRoot);
            Directory.CreateDirectory(consumerRoot);

            var leafProject = Path.Combine(leafRoot, "Leaf.csproj");
            File.WriteAllText(
                leafProject,
                BuildAnalyzedProject(
                    "AICopilot.EntityFrameworkCore",
                    analyzerAssembly,
                    entityFrameworkProject.ProjectPath));
            File.WriteAllText(
                Path.Combine(leafRoot, "Leaf.cs"),
                """
                namespace Fixture.Leaf
                {
                    public sealed class Writer(Microsoft.EntityFrameworkCore.DbContext db)
                    {
                        public int Execute() => db.SaveChanges();
                    }
                }
                """);

            var bridgeProject = Path.Combine(bridgeRoot, "Bridge.csproj");
            File.WriteAllText(
                bridgeProject,
                BuildAnalyzedProject("AICopilot.Infrastructure", analyzerAssembly, leafProject));
            File.WriteAllText(
                Path.Combine(bridgeRoot, "Bridge.cs"),
                """
                namespace Fixture.Bridge
                {
                    public sealed class Gateway(Fixture.Leaf.Writer writer)
                    {
                        public int Forward() => writer.Execute();
                    }
                }
                """);

            var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumerProject,
                BuildAnalyzedProject(
                    "AICopilot.HttpApi",
                    analyzerAssembly,
                    bridgeProject,
                    contractsProject.ProjectPath));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                namespace Fixture.Consumer
                {
                    public sealed class CloudRunner(
                        AICopilot.Services.Contracts.ICloudAiReadClient client,
                        Fixture.Bridge.Gateway gateway)
                    {
                        public int Query()
                        {
                            _ = client.Read();
                            return gateway.Forward();
                        }
                    }
                }
                """);

            (await BuildAsync(leafProject)).ExitCode.Should().Be(0);
            (await BuildAsync(bridgeProject)).ExitCode.Should().Be(0);
            var consumer = await BuildAsync(consumerProject);

            GetReleaseAnalyzerOutputFingerprint(solutionRoot).Should().Be(analyzerOutputFingerprint);
            consumer.ExitCode.Should().NotBe(
                0,
                $"the A -> B -> C summary closure must report AIARCH006. Output:{Environment.NewLine}{consumer.Output}");
            consumer.Output.Should().Contain(AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
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
    [MemberData(nameof(InvalidSummaryModes))]
    public async Task AIARCH001_ShouldFailClosedForMissingCorruptSpoofedOrOldReferencedSummary(
        string mode)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-invalid-summary-fixture",
            mode,
            Guid.NewGuid().ToString("N"));

        try
        {
            var dependencyRoot = Path.Combine(fixtureRoot, "Dependency");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(dependencyRoot);
            Directory.CreateDirectory(consumerRoot);

            var dependencyProject = Path.Combine(dependencyRoot, "AICopilot.Infrastructure.csproj");
            File.WriteAllText(dependencyProject, BuildPlainProject("AICopilot.Infrastructure"));
            File.WriteAllText(
                Path.Combine(dependencyRoot, "Dependency.cs"),
                BuildInvalidSummarySource(mode));

            var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumerProject,
                BuildAnalyzedProject("AICopilot.HttpApi", analyzerAssembly, dependencyProject));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                "namespace Fixture.Consumer; public sealed class Entry(Fixture.DependencyMarker marker) { public object Marker => marker; }");

            var dependency = await BuildAsync(dependencyProject);
            var consumer = await BuildAsync(consumerProject);

            dependency.ExitCode.Should().Be(0, dependency.Output);
            consumer.ExitCode.Should().NotBe(
                0,
                $"{mode} referenced summaries must fail closed. Output:{Environment.NewLine}{consumer.Output}");
            consumer.Output.Should().Contain(AICopilotArchitectureAnalyzer.ProjectBoundaryId);
            consumer.Output.Should().Contain("effect");
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
    [MemberData(nameof(InvalidProductionAssemblyNames))]
    public async Task AIARCH001_ShouldRejectProductionNamesThatTryToEscapeExactLayerClassification(
        string assemblyName)
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-unclassified-name-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(fixtureRoot);
            var project = Path.Combine(fixtureRoot, "Fixture.csproj");
            File.WriteAllText(project, BuildAnalyzedProject(assemblyName, analyzerAssembly));
            File.WriteAllText(
                Path.Combine(fixtureRoot, "Fixture.cs"),
                "namespace Fixture; public sealed class EntryPoint { }");

            var result = await BuildAsync(project);

            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.ProjectBoundaryId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectDefaultInterfaceBodyAcrossARealProjectBoundary()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-default-interface-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formal = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId,
                analyzerAssembly);
            var entityFramework = GetFormalProject(formal, "Microsoft.EntityFrameworkCore");
            var contracts = GetFormalProject(formal, "AICopilot.Services.Contracts");
            var producerRoot = Path.Combine(fixtureRoot, "Producer");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(producerRoot);
            Directory.CreateDirectory(consumerRoot);

            var producer = Path.Combine(producerRoot, "Producer.csproj");
            File.WriteAllText(
                producer,
                BuildAnalyzedProject(
                    "AICopilot.EntityFrameworkCore",
                    analyzerAssembly,
                    entityFramework));
            File.WriteAllText(
                Path.Combine(producerRoot, "Producer.cs"),
                """
                namespace Fixture.Producer
                {
                    internal static class Database
                    {
                        internal static readonly Microsoft.EntityFrameworkCore.DbContext Instance = new();
                    }
                    public interface INeutralGateway
                    {
                        int Execute() => Database.Instance.SaveChanges();
                    }
                }
                """);

            var consumer = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumer,
                BuildAnalyzedProject("AICopilot.HttpApi", analyzerAssembly, producer, contracts));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                namespace Fixture.Consumer
                {
                    public sealed class CloudRunner(
                        AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                        Fixture.Producer.INeutralGateway gateway)
                    {
                        public int Query() { _ = cloud.Read(); return gateway.Execute(); }
                    }
                }
                """);

            (await BuildAsync(producer)).ExitCode.Should().Be(0);
            var result = await BuildAsync(consumer);
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectImplicitMembersInitializersAndCallbacksAcrossARealProjectBoundary()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-implicit-member-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formal = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId,
                analyzerAssembly);
            var entityFramework = GetFormalProject(formal, "Microsoft.EntityFrameworkCore");
            var contracts = GetFormalProject(formal, "AICopilot.Services.Contracts");
            var producerRoot = Path.Combine(fixtureRoot, "Producer");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(producerRoot);
            Directory.CreateDirectory(consumerRoot);

            var producer = Path.Combine(producerRoot, "Producer.csproj");
            File.WriteAllText(
                producer,
                BuildAnalyzedProject(
                    "AICopilot.EntityFrameworkCore",
                    analyzerAssembly,
                    entityFramework));
            File.WriteAllText(
                Path.Combine(producerRoot, "Producer.cs"),
                """
                using System.Threading.Tasks;
                namespace Fixture.Producer
                {
                    internal static class Database
                    {
                        internal static readonly Microsoft.EntityFrameworkCore.DbContext Instance = new();
                    }
                    public sealed class ConstructorWriter
                    {
                        public ConstructorWriter() => Database.Instance.SaveChanges();
                    }
                    public sealed class PropertyWriter
                    {
                        public int Value => Database.Instance.SaveChanges();
                    }
                    public sealed class ConversionWriter
                    {
                        public static implicit operator int(ConversionWriter _) =>
                            Database.Instance.SaveChanges();
                    }
                    public sealed class InitializerWriter
                    {
                        private readonly int value = Database.Instance.SaveChanges();
                        public int Read() => value;
                    }
                    public sealed class CallbackWriter
                    {
                        public Task<int> ExecuteAsync() =>
                            Task.Run(() => Database.Instance.SaveChanges());
                    }
                }
                """);

            var consumer = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumer,
                BuildAnalyzedProject("AICopilot.HttpApi", analyzerAssembly, producer, contracts));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                using System.Threading.Tasks;
                namespace Fixture.Consumer
                {
                    public sealed class CloudRunner(AICopilot.Services.Contracts.ICloudAiReadClient cloud)
                    {
                        public object Construct() { _ = cloud.Read(); return new Fixture.Producer.ConstructorWriter(); }
                        public int ReadProperty() { _ = cloud.Read(); return new Fixture.Producer.PropertyWriter().Value; }
                        public int Convert() { _ = cloud.Read(); return new Fixture.Producer.ConversionWriter(); }
                        public int ReadInitializer() { _ = cloud.Read(); return new Fixture.Producer.InitializerWriter().Read(); }
                        public Task<int> RunCallback() { _ = cloud.Read(); return new Fixture.Producer.CallbackWriter().ExecuteAsync(); }
                    }
                }
                """);

            var producerBuild = await BuildAsync(producer);
            producerBuild.ExitCode.Should().Be(0, producerBuild.Output);
            var result = await BuildAsync(consumer);
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH006_ShouldRejectDelegateFactoryReturnAcrossARealProjectBoundary()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-delegate-factory-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formal = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId,
                analyzerAssembly);
            var entityFramework = GetFormalProject(formal, "Microsoft.EntityFrameworkCore");
            var contracts = GetFormalProject(formal, "AICopilot.Services.Contracts");
            var producerRoot = Path.Combine(fixtureRoot, "Producer");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(producerRoot);
            Directory.CreateDirectory(consumerRoot);

            var producer = Path.Combine(producerRoot, "Producer.csproj");
            File.WriteAllText(
                producer,
                BuildAnalyzedProject(
                    "AICopilot.EntityFrameworkCore",
                    analyzerAssembly,
                    entityFramework));
            File.WriteAllText(
                Path.Combine(producerRoot, "Producer.cs"),
                """
                using System;
                namespace Fixture.Producer
                {
                    public sealed class DelegateFactory(Microsoft.EntityFrameworkCore.DbContext db)
                    {
                        public Func<int> Build() => Write;
                        private int Write() => db.SaveChanges();
                    }
                }
                """);

            var consumer = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumer,
                BuildAnalyzedProject("AICopilot.HttpApi", analyzerAssembly, producer, contracts));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                namespace Fixture.Consumer
                {
                    public sealed class CloudRunner(
                        AICopilot.Services.Contracts.ICloudAiReadClient cloud,
                        Fixture.Producer.DelegateFactory factory)
                    {
                        public int Query() { _ = cloud.Read(); return factory.Build()(); }
                    }
                }
                """);

            var producerBuild = await BuildAsync(producer);
            producerBuild.ExitCode.Should().Be(0, producerBuild.Output);
            var result = await BuildAsync(consumer);
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH007_ShouldRejectExactFqnSecurityMetadataFromWrongAssembliesInARealProject()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-security-shadow-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formal = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.SecurityMetadataId,
                analyzerAssembly);
            var sharedKernel = GetFormalProject(formal, "AICopilot.SharedKernel");
            var mvcRoot = Path.Combine(fixtureRoot, "Microsoft.AspNetCore.Mvc.Core");
            var shadowRoot = Path.Combine(fixtureRoot, "Shadow.Security");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(mvcRoot);
            Directory.CreateDirectory(shadowRoot);
            Directory.CreateDirectory(consumerRoot);

            var mvcProject = Path.Combine(mvcRoot, "Microsoft.AspNetCore.Mvc.Core.csproj");
            File.WriteAllText(mvcProject, BuildPlainProject("Microsoft.AspNetCore.Mvc.Core"));
            File.WriteAllText(
                Path.Combine(mvcRoot, "Mvc.cs"),
                """
                using System;
                namespace Microsoft.AspNetCore.Mvc.Routing
                {
                    public abstract class HttpMethodAttribute : Attribute { }
                }
                namespace Microsoft.AspNetCore.Mvc
                {
                    public abstract class ControllerBase { }
                    public sealed class HttpGetAttribute : Routing.HttpMethodAttribute { }
                }
                """);

            var shadowProject = Path.Combine(shadowRoot, "Shadow.Security.csproj");
            File.WriteAllText(shadowProject, BuildPlainProject("Shadow.Security"));
            File.WriteAllText(
                Path.Combine(shadowRoot, "Security.cs"),
                """
                using System;
                namespace Microsoft.AspNetCore.Authorization
                {
                    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                    public sealed class AuthorizeAttribute : Attribute { }
                }
                namespace AICopilot.Services.CrossCutting.Attributes
                {
                    [AttributeUsage(AttributeTargets.Class)]
                    public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
                }
                """);

            var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumerProject,
                BuildAnalyzedProject(
                    "AICopilot.HttpApi",
                    analyzerAssembly,
                    sharedKernel,
                    mvcProject,
                    shadowProject));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                namespace Fixture
                {
                    [Microsoft.AspNetCore.Authorization.Authorize]
                    public sealed class ShadowedController : Microsoft.AspNetCore.Mvc.ControllerBase
                    {
                        [Microsoft.AspNetCore.Mvc.HttpGet]
                        public string Get() => "ok";
                    }

                    [AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirement("Fixture.Read")]
                    public sealed record ShadowedQuery : AICopilot.SharedKernel.Messaging.IQuery<string>;
                }
                """);

            (await BuildAsync(mvcProject)).ExitCode.Should().Be(0);
            (await BuildAsync(shadowProject)).ExitCode.Should().Be(0);
            var result = await BuildAsync(consumerProject);
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.SecurityMetadataId);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task AIARCH004_ShouldRejectExactFqnInvariantImplementationsFromWrongAssemblyInARealProject()
    {
        var solutionRoot = FindSolutionRoot();
        var analyzerAssembly = GetReleaseAnalyzerAssemblyPath(solutionRoot);
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            "aicopilot-analyzer-invariant-shadow-fixture",
            Guid.NewGuid().ToString("N"));

        try
        {
            var formal = WriteRuleFormalReferences(
                fixtureRoot,
                AICopilotArchitectureAnalyzer.EnabledAdminInvariantId,
                analyzerAssembly);
            var contracts = GetFormalProject(formal, "AICopilot.Services.Contracts");
            var shadowRoot = Path.Combine(fixtureRoot, "Shadow.Invariant");
            var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
            Directory.CreateDirectory(shadowRoot);
            Directory.CreateDirectory(consumerRoot);

            var shadowProject = Path.Combine(shadowRoot, "Shadow.Invariant.csproj");
            File.WriteAllText(
                shadowProject,
                BuildPlainProject("Shadow.Invariant", contracts));
            File.WriteAllText(
                Path.Combine(shadowRoot, "Invariant.cs"),
                """
                using System;
                using System.Threading.Tasks;
                namespace AICopilot.EntityFrameworkCore.Transactions
                {
                    public sealed class IdentityTransactionalExecutionService
                        : AICopilot.Services.Contracts.ITransactionalExecutionService
                    {
                        public Task ExecuteAsync(Func<Task> action) => action();
                    }
                }
                namespace AICopilot.EntityFrameworkCore.Locking
                {
                    public sealed class PostgresIdentityEnabledAdminInvariantGuard
                        : AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard
                    {
                        public Task AcquireAsync() => Task.CompletedTask;
                    }
                }
                """);

            var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
            File.WriteAllText(
                consumerProject,
                BuildAnalyzedProject(
                    "AICopilot.IdentityService",
                    analyzerAssembly,
                    contracts,
                    shadowProject));
            File.WriteAllText(
                Path.Combine(consumerRoot, "Consumer.cs"),
                """
                namespace Microsoft.Extensions.DependencyInjection
                {
                    public interface IServiceCollection { }
                    public static class RegistrationExtensions
                    {
                        public static IServiceCollection AddScoped<TService, TImplementation>(
                            this IServiceCollection services) where TImplementation : TService => services;
                    }
                }
                namespace Fixture
                {
                    using AICopilot.Services.Contracts;
                    using Microsoft.Extensions.DependencyInjection;
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
                """);

            var shadowBuild = await BuildAsync(shadowProject);
            shadowBuild.ExitCode.Should().Be(0, shadowBuild.Output);
            var result = await BuildAsync(consumerProject);
            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain(AICopilotArchitectureAnalyzer.EnabledAdminInvariantId);
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
        var formalReferences = WriteRuleFormalReferences(projectRoot, ruleId, analyzerAssembly);
        if (formalReferences.Count != 0)
        {
            projectReferenceXml = string.Join(
                Environment.NewLine,
                formalReferences.Select(reference => reference.IsAICopilot
                    ? $"""
                            <ProjectReference Include="{Escape(reference.ProjectPath)}" />
                            <AdditionalFiles Include="{Escape(reference.ProjectPath)}" />
                        """
                    : $"""
                            <ProjectReference Include="{Escape(reference.ProjectPath)}" />
                        """));
        }
        if (specification.DirectProjectReference is not null)
        {
            var dependencyRoot = Path.Combine(projectRoot, specification.DirectProjectReference);
            Directory.CreateDirectory(dependencyRoot);
            File.WriteAllText(
                Path.Combine(dependencyRoot, $"{specification.DirectProjectReference}.csproj"),
                specification.DirectProjectReference.StartsWith("AICopilot.", StringComparison.Ordinal)
                    ? BuildAnalyzedProject(specification.DirectProjectReference, analyzerAssembly)
                    : BuildPlainProject(specification.DirectProjectReference));
            File.WriteAllText(
                Path.Combine(dependencyRoot, "Dependency.cs"),
                "namespace FixtureDependency; public sealed class DependencyMarker { }");
            var dependencyProject = Path.Combine(
                dependencyRoot,
                $"{specification.DirectProjectReference}.csproj");
            projectReferenceXml += $"""
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
                <DefaultItemExcludes>$(DefaultItemExcludes);Formal/**;AICopilot.*/**;Microsoft.*/**</DefaultItemExcludes>
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

    private static IReadOnlyCollection<FormalProjectReference> WriteRuleFormalReferences(
        string projectRoot,
        string ruleId,
        string analyzerAssembly)
    {
        var formalRoot = Path.Combine(projectRoot, "Formal");
        var references = new List<FormalProjectReference>();

        void Add(string assemblyName, string source, bool isAICopilot)
        {
            var root = Path.Combine(formalRoot, assemblyName);
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, $"{assemblyName}.csproj");
            File.WriteAllText(
                projectPath,
                isAICopilot
                    ? BuildAnalyzedProject(assemblyName, analyzerAssembly)
                    : BuildPlainProject(assemblyName));
            File.WriteAllText(Path.Combine(root, "Contract.cs"), source);
            references.Add(new FormalProjectReference(projectPath, isAICopilot));
        }

        if (ruleId is AICopilotArchitectureAnalyzer.AggregateBoundaryId or
            AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId or
            AICopilotArchitectureAnalyzer.SecurityMetadataId)
        {
            Add(
                "AICopilot.SharedKernel",
                """
                using System.Threading.Tasks;
                namespace AICopilot.SharedKernel.Domain { public interface IAggregateRoot { } }
                namespace AICopilot.SharedKernel.Repository
                {
                    public interface IRepository<T> where T : class { Task AddAsync(T entity); }
                }
                namespace AICopilot.SharedKernel.Messaging { public interface IQuery<T> { } }
                namespace AICopilot.SharedKernel.Ai
                {
                    public enum AiToolExternalSystemType { Unknown, CloudReadOnly }
                    public enum AiToolCapabilityKind { ReadOnlyQuery, Diagnostics, SideEffecting }
                    public sealed record AiToolSafetyDescriptor(
                        bool ReadOnlyDeclared,
                        AiToolCapabilityKind CapabilityKind,
                        AiToolExternalSystemType ExternalSystemType);
                }
                """,
                isAICopilot: true);
        }

        if (ruleId is AICopilotArchitectureAnalyzer.EnabledAdminInvariantId or
            AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
        {
            Add(
                "AICopilot.Services.Contracts",
                """
                using System;
                using System.Threading.Tasks;
                namespace AICopilot.Services.Contracts
                {
                    public interface ITransactionalExecutionService { Task ExecuteAsync(Func<Task> action); }
                    public interface IIdentityEnabledAdminInvariantGuard { Task AcquireAsync(); }
                    public interface ICloudAiReadClient { int Read(); }
                }
                """,
                isAICopilot: true);
        }

        if (ruleId == AICopilotArchitectureAnalyzer.SecurityMetadataId)
        {
            Add(
                "AICopilot.Services.CrossCutting",
                """
                using System;
                namespace AICopilot.Services.CrossCutting.Attributes
                {
                    [AttributeUsage(AttributeTargets.Class)]
                    public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
                }
                """,
                isAICopilot: true);
        }

        if (ruleId == AICopilotArchitectureAnalyzer.AgentPluginBoundaryId)
        {
            Add(
                "AICopilot.AgentPlugin",
                """
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
                """,
                isAICopilot: true);
        }

        if (ruleId is AICopilotArchitectureAnalyzer.PersistenceOwnerId or
            AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId)
        {
            Add(
                "Microsoft.EntityFrameworkCore",
                "namespace Microsoft.EntityFrameworkCore { public class DbContext { public int SaveChanges() => 0; } }",
                isAICopilot: false);
        }

        if (ruleId == AICopilotArchitectureAnalyzer.EnabledAdminInvariantId)
        {
            Add(
                "Microsoft.Extensions.Identity.Core",
                """
                using System.Threading.Tasks;
                namespace Microsoft.AspNetCore.Identity
                {
                    public class UserManager<T>
                    {
                        public Task RemoveFromRoleAsync(T user, string role) => Task.CompletedTask;
                    }
                }
                """,
                isAICopilot: false);
        }

        return references;
    }

    private static CrossProjectFixture WriteCrossProjectFixture(
        string fixtureRoot,
        string ruleId,
        string analyzerAssembly)
    {
        var specification = GetCrossProjectSpecification(ruleId);
        var formalReferences = WriteRuleFormalReferences(fixtureRoot, ruleId, analyzerAssembly);
        var dependencyRoot = Path.Combine(fixtureRoot, "Dependency");
        var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
        Directory.CreateDirectory(dependencyRoot);
        Directory.CreateDirectory(consumerRoot);

        var dependencyProject = Path.Combine(dependencyRoot, "Dependency.csproj");
        File.WriteAllText(
            dependencyProject,
            BuildAnalyzedProject(
                specification.DependencyAssemblyName,
                analyzerAssembly,
                formalReferences.Select(reference => reference.ProjectPath).ToArray()));
        File.WriteAllText(Path.Combine(dependencyRoot, "Dependency.cs"), specification.DependencySource);

        var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
        File.WriteAllText(
            consumerProject,
            BuildAnalyzedProject(
                specification.ConsumerAssemblyName,
                analyzerAssembly,
                [
                    dependencyProject,
                    .. formalReferences
                        .Where(reference =>
                            ruleId == AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId &&
                            Path.GetFileNameWithoutExtension(reference.ProjectPath) == "AICopilot.Services.Contracts")
                        .Select(reference => reference.ProjectPath)
                ]));
        File.WriteAllText(Path.Combine(consumerRoot, "Consumer.cs"), specification.ConsumerSource);

        return new CrossProjectFixture(dependencyProject, consumerProject);
    }

    private static CompositionFixture WriteCompositionFixture(
        string fixtureRoot,
        string ruleId,
        string analyzerAssembly)
    {
        var specification = GetCompositionSpecification(ruleId);
        var formalReferences = WriteRuleFormalReferences(fixtureRoot, ruleId, analyzerAssembly);
        var externalReferences = formalReferences
            .Where(reference => !reference.IsAICopilot)
            .Select(reference => reference.ProjectPath)
            .ToArray();
        var contractRoot = Path.Combine(fixtureRoot, "Contract");
        var implementationRoot = Path.Combine(fixtureRoot, "Implementation");
        var consumerRoot = Path.Combine(fixtureRoot, "Consumer");
        var hostRoot = Path.Combine(fixtureRoot, "Host");
        Directory.CreateDirectory(contractRoot);
        Directory.CreateDirectory(implementationRoot);
        Directory.CreateDirectory(consumerRoot);
        Directory.CreateDirectory(hostRoot);

        var contractProject = Path.Combine(contractRoot, "Contract.csproj");
        File.WriteAllText(
            contractProject,
            BuildAnalyzedProject("AICopilot.Services.Contracts", analyzerAssembly));
        File.WriteAllText(Path.Combine(contractRoot, "Contract.cs"), specification.ContractSource);

        var implementationProject = Path.Combine(implementationRoot, "Implementation.csproj");
        File.WriteAllText(
            implementationProject,
            BuildAnalyzedProject(
                specification.ImplementationAssemblyName,
                analyzerAssembly,
                [contractProject, .. externalReferences]));
        File.WriteAllText(
            Path.Combine(implementationRoot, "Implementation.cs"),
            specification.ImplementationSource);

        var consumerProject = Path.Combine(consumerRoot, "Consumer.csproj");
        File.WriteAllText(
            consumerProject,
            BuildAnalyzedProject(
                "AICopilot.AiGatewayService",
                analyzerAssembly,
                contractProject));
        File.WriteAllText(Path.Combine(consumerRoot, "Consumer.cs"), specification.ConsumerSource);

        var hostProject = Path.Combine(hostRoot, "Host.csproj");
        File.WriteAllText(
            hostProject,
            BuildAnalyzedProject(
                "AICopilot.HttpApi",
                analyzerAssembly,
                consumerProject,
                implementationProject));
        File.WriteAllText(
            Path.Combine(hostRoot, "Host.cs"),
            specification.HostSource);

        return new CompositionFixture(contractProject, implementationProject, consumerProject, hostProject);
    }

    private static CompositionSpecification GetCompositionSpecification(string ruleId) => ruleId switch
    {
        AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId => new(
            "AICopilot.EntityFrameworkCore",
            """
            namespace AICopilot.Services.Contracts
            {
                public interface ICloudAiReadClient { int Read(); }
                public interface INeutralGateway { int Execute(); }
            }
            """,
            """
            namespace Fixture.Implementation
            {
                internal sealed class DataGateway(Microsoft.EntityFrameworkCore.DbContext db)
                    : AICopilot.Services.Contracts.INeutralGateway
                {
                    public int Execute() => db.SaveChanges();
                }
            }
            """,
            """
            namespace Fixture.Consumer
            {
                public sealed class NeutralBridge(AICopilot.Services.Contracts.INeutralGateway gateway)
                {
                    public int Forward() => gateway.Execute();
                }
            }
            """,
            """
            namespace Fixture.Host
            {
                public sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    Fixture.Consumer.NeutralBridge bridge)
                {
                    public int Query()
                    {
                        _ = client.Read();
                        return bridge.Forward();
                    }
                }
            }
            """),
        AICopilotArchitectureAnalyzer.EnabledAdminInvariantId => new(
            "AICopilot.IdentityService",
            """
            using System;
            using System.Threading.Tasks;
            namespace AICopilot.Services.Contracts
            {
                public interface IAdminReducer { Task ReduceAsync(); }
                public interface ITransactionalExecutionService { Task ExecuteAsync(Func<Task> action); }
            }
            """,
            """
            using System.Threading.Tasks;
            namespace AICopilot.IdentityService.Authorization
            {
                public sealed class EnabledAdminInvariantPolicy
                {
                    public Task AcquireAsync() => Task.CompletedTask;
                }
            }
            namespace Fixture.Implementation
            {
                internal sealed class AdminReducer(Microsoft.AspNetCore.Identity.UserManager<object> users)
                    : AICopilot.Services.Contracts.IAdminReducer
                {
                    public Task ReduceAsync() => users.RemoveFromRoleAsync(new object(), "Admin");
                }

                public sealed class SafeHandler(
                    AICopilot.Services.Contracts.IAdminReducer reducer,
                    AICopilot.Services.Contracts.ITransactionalExecutionService transaction,
                    AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy invariant)
                {
                    public Task HandleAsync() => transaction.ExecuteAsync(async () =>
                    {
                        await invariant.AcquireAsync();
                        await reducer.ReduceAsync();
                    });
                }
            }
            """,
            """
            using System.Threading.Tasks;
            namespace Fixture.Consumer
            {
                public sealed class NeutralBridge(AICopilot.Services.Contracts.IAdminReducer reducer)
                {
                    public Task ForwardAsync() => reducer.ReduceAsync();
                }
            }
            """,
            """
            using System.Threading.Tasks;
            namespace Fixture.Host
            {
                public sealed class ExternalCaller(Fixture.Consumer.NeutralBridge bridge)
                {
                    public Task BypassAsync() => bridge.ForwardAsync();
                }
            }
            """),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleId), ruleId, "Unknown composition fixture")
    };

    private static CrossProjectSpecification GetCrossProjectSpecification(string ruleId) => ruleId switch
    {
        AICopilotArchitectureAnalyzer.EnabledAdminInvariantId => new(
            "AICopilot.IdentityService",
            "AICopilot.HttpApi",
            """
            using System;
            using System.Threading.Tasks;
            namespace AICopilot.IdentityService.Authorization
            {
                public sealed class EnabledAdminInvariantPolicy
                {
                    public Task AcquireAsync() => Task.CompletedTask;
                }
            }
            namespace Fixture
            {
                using AICopilot.IdentityService.Authorization;
                using AICopilot.Services.Contracts;
                using Microsoft.AspNetCore.Identity;

                public interface IAdminReducer
                {
                    Task ReduceAsync();
                }

                internal sealed class AdminReducer(UserManager<object> users) : IAdminReducer
                {
                    public Task ReduceAsync() => users.RemoveFromRoleAsync(new object(), "Admin");
                }

                public sealed class SafeHandler(
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
            """,
            """
            using System.Threading.Tasks;
            namespace Fixture.Consumer
            {
                public sealed class ExternalCaller(Fixture.IAdminReducer reducer)
                {
                    public Task BypassAsync() => reducer.ReduceAsync();
                }
            }
            """),
        AICopilotArchitectureAnalyzer.CloudReadOnlyBoundaryId => new(
            "AICopilot.EntityFrameworkCore",
            "AICopilot.HttpApi",
            """
            namespace Fixture
            {
                public interface INeutralGateway
                {
                    int Execute();
                }

                internal sealed class DataGateway(Microsoft.EntityFrameworkCore.DbContext db)
                    : INeutralGateway
                {
                    public int Execute() => db.SaveChanges();
                }
            }
            """,
            """
            namespace Fixture.Consumer
            {
                public sealed class CloudRunner(
                    AICopilot.Services.Contracts.ICloudAiReadClient client,
                    Fixture.INeutralGateway gateway)
                {
                    public int Query()
                    {
                        _ = client.Read();
                        return gateway.Execute();
                    }
                }
            }
            """),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleId), ruleId, "Unknown cross-project rule fixture")
    };

    private static FixtureSpecification GetSpecification(string ruleId, bool valid) => ruleId switch
    {
        AICopilotArchitectureAnalyzer.ProjectBoundaryId => new(
            valid ? "AICopilot.Infrastructure" : "AICopilot.UnclassifiedService",
            "namespace Fixture; public sealed class EntryPoint { }",
            "AICopilot.Services.Contracts"),
        AICopilotArchitectureAnalyzer.AggregateBoundaryId => new(
            "AICopilot.Core.AiGateway",
            valid ? """
            namespace AICopilot.Core.AiGateway.Aggregates.Sessions
            {
                public sealed class Session : AICopilot.SharedKernel.Domain.IAggregateRoot { }
            }
            namespace Fixture
            {
                public sealed class Consumer(AICopilot.SharedKernel.Repository.IRepository<AICopilot.Core.AiGateway.Aggregates.Sessions.Session> repository) { }
            }
            """ : """
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
            "AICopilot.AiGatewayService",
            AuthorizationFixture(valid),
            null),
        _ => throw new ArgumentOutOfRangeException(nameof(ruleId), ruleId, "Unknown analyzer rule fixture")
    };

    private static string IdentityFixture(bool valid) => $$"""
        using System;
        using System.Threading.Tasks;
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
        namespace Fixture
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class AuthorizeRequirementAttribute(string permission) : Attribute { }
            {{(valid ? "[AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirement(\"Fixture.Read\")]" : "[Fixture.AuthorizeRequirement(\"Fixture.Read\")]")}}
            public sealed record FixtureQuery : AICopilot.SharedKernel.Messaging.IQuery<string>;
        }
        """;

    private static string BuildInvalidSummarySource(string mode)
    {
        const string prefix = "AICopilot.Architecture.EffectSummary.";
        var metadata = mode switch
        {
            "Missing" => string.Empty,
            "Corrupt" => $$"""
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Schema", "2")]
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Count", "1")]
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Entry.000000", "not-base64")]
                """,
            "SpoofedProducer" => $$"""
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Schema", "2")]
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Count", "1")]
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Entry.000000", "{{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("AICopilot.Spoof\nAICopilot.Infrastructure\nM:Fixture.DependencyMarker.Touch\n1\nidentity-effect"))}}")]
                """,
            "OldSchema" => $$"""
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Schema", "1")]
                [assembly: System.Reflection.AssemblyMetadata("{{prefix}}Count", "0")]
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown invalid summary mode")
        };
        return $$"""
            {{metadata}}
            namespace Fixture
            {
                public sealed class DependencyMarker
                {
                    public void Touch() { }
                }
            }
            """;
    }

    private static string GetFormalProject(
        IEnumerable<FormalProjectReference> references,
        string assemblyName) =>
        references.Single(reference =>
            Path.GetFileNameWithoutExtension(reference.ProjectPath) == assemblyName).ProjectPath;

    private static string BuildPlainProject(
        string assemblyName,
        params string[] dependencyProjects)
    {
        var dependencyItems = string.Join(
            Environment.NewLine,
            dependencyProjects.Select(dependencyProject =>
                $"<ProjectReference Include=\"{Escape(dependencyProject)}\" />"));
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{Escape(assemblyName)}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
            {dependencyItems}
              </ItemGroup>
            </Project>
            """;
    }

    private static string BuildAnalyzedProject(
        string assemblyName,
        string analyzerAssembly,
        params string[] dependencyProjects)
    {
        var dependencyItems = string.Join(
            Environment.NewLine,
            dependencyProjects.Select(dependencyProject =>
            {
                var projectReference = $"<ProjectReference Include=\"{Escape(dependencyProject)}\" />";
                return Path.GetFileNameWithoutExtension(dependencyProject)
                    .StartsWith("AICopilot.", StringComparison.Ordinal)
                    ? $"""
                        {projectReference}
                        <AdditionalFiles Include="{Escape(dependencyProject)}" />
                    """
                    : projectReference;
            }));
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>{Escape(assemblyName)}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Analyzer Include="{Escape(analyzerAssembly)}" />
            {dependencyItems}
              </ItemGroup>
            </Project>
            """;
    }

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

    private sealed record FormalProjectReference(string ProjectPath, bool IsAICopilot);

    private sealed record CrossProjectSpecification(
        string DependencyAssemblyName,
        string ConsumerAssemblyName,
        string DependencySource,
        string ConsumerSource);

    private sealed record CrossProjectFixture(
        string DependencyProject,
        string ConsumerProject);

    private sealed record CompositionSpecification(
        string ImplementationAssemblyName,
        string ContractSource,
        string ImplementationSource,
        string ConsumerSource,
        string HostSource);

    private sealed record CompositionFixture(
        string ContractProject,
        string ImplementationProject,
        string ConsumerProject,
        string HostProject);

    private sealed record BuildResult(int ExitCode, string Output);
}
