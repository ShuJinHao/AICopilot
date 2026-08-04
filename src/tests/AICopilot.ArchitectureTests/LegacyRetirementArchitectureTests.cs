using System.Reflection;
using System.Security.Cryptography;
using AICopilot.AiGatewayService.Agents;
using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Tools;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Runtime.ModelQuota;
using AICopilot.DataWorker;
using AICopilot.EntityFrameworkCore;
using AICopilot.HttpApi.Infrastructure;
using AICopilot.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AICopilot.ArchitectureTests;

public sealed class LegacyRetirementArchitectureTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

    private static readonly string[] RetiredTypeMarkers =
    [
        ".AgentTasks.",
        ".ApprovalPolicies.",
        ".Artifacts.",
        ".RoutingModels.",
        ".Workflows.",
        "AgentArtifact",
        "AgentTask",
        "ApprovalPolicy",
        ".Aggregates.Approvals.ApprovalRequest",
        "ArtifactWorkspace",
        "ChatRuntimeSettings",
        "FinalAgent",
        "MessageEvent",
        "Onsite",
        "RoutingModel"
    ];

    private static readonly string[] RetiredPathMarkers =
    [
        "/AgentTasks/",
        "/ApprovalPolicies/",
        "/Aggregates/Approvals/",
        "/Artifacts/",
        "/RoutingModels/",
        "/RuntimeSettings/",
        "/src/services/AICopilot.AiGatewayService/Workflows/",
        "/Workspaces/",
        "AgentArtifact",
        "ArtifactWorkspace",
        "AgentTask",
        "ApprovalPolicy",
        "RoutingModel",
        "ChatRuntimeSettings",
        "FinalAgent",
        "IntentRouting",
        "AgentWorkflow",
        "NodeRun",
        "PlanDraft",
        "MessageEvent",
        "/AICopilot.SimulationTests/",
        "/AICopilot.SimulationDockerTests/",
        "/AgentRunThread.vue",
        "/Run-AgentSimulationAcceptance.ps1",
        "/Test-AgentSimulationScope.ps1",
        "/aicopilot-simulation-release-candidate.yml"
    ];

    [Fact]
    public void ProductionAssemblies_ShouldNotContainRetiredOrchestrationTypes()
    {
        var assemblies = new[]
        {
            typeof(Session).Assembly,
            typeof(ChatStreamHandler).Assembly,
            typeof(AiGatewayDbContext).Assembly,
            typeof(AICopilot.Infrastructure.DependencyInjection).Assembly,
            typeof(ApiControllerBase).Assembly,
            typeof(PersistenceMaintenanceWorker).Assembly
        };

        var violations = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace is null ||
                !type.Namespace.Contains(".Migrations", StringComparison.Ordinal))
            .Where(type => !typeof(Migration).IsAssignableFrom(type))
            .Select(type => type.FullName ?? type.Name)
            .Where(typeName => RetiredTypeMarkers.Any(marker =>
                typeName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "the Harness is the only main-chat orchestration and retired framework types must be physically absent");
    }

    [Fact]
    public void RepositorySurface_ShouldNotRestoreRetiredOrchestrationPaths()
    {
        var scanRoots = new[]
        {
            Path.Combine(SolutionRoot, "src"),
            Path.Combine(SolutionRoot, "scripts"),
            Path.Combine(SolutionRoot, ".github", "workflows")
        };
        var violations = scanRoots
            .Where(Directory.Exists)
            .SelectMany(EnumerateSurfaceFiles)
            .Select(path => "/" + Path.GetRelativePath(SolutionRoot, path).Replace('\\', '/'))
            .Where(path => !IsFrozenProductionMigrationPath(path))
            .Where(path => RetiredPathMarkers.Any(marker =>
                path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().BeEmpty(
            "retired orchestration projects, source folders, workers, scripts, and frontend entries must remain physically absent");
    }

    private static bool IsFrozenProductionMigrationPath(string repositoryPath)
    {
        const string migrationPrefix =
            "/src/infrastructure/AICopilot.EntityFrameworkCore/Migrations/AiGatewayDbContext/";
        if (!repositoryPath.StartsWith(migrationPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = Path.GetFileName(repositoryPath);
        return AiGatewayProductionUpgradeContract.ProductionMigrationIds.Any(migrationId =>
            fileName.StartsWith($"{migrationId}.", StringComparison.Ordinal));
    }

    [Fact]
    public void MainHarnessRuntime_ShouldRemainNativeMafAndModeIndependent()
    {
        var runtimeRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.AiRuntime");
        var runtimeSource = string.Join(
            '\n',
            Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(runtimeRoot, path)
                    .Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj"))
                .Select(File.ReadAllText));
        var factorySource = File.ReadAllText(Path.Combine(
            runtimeRoot,
            "HarnessAgentRuntimeFactory.cs"));
        var guardSource = File.ReadAllText(Path.Combine(
            runtimeRoot,
            "ToolInvocationGuardChatClient.cs"));
        var runtimeAgentSource = File.ReadAllText(Path.Combine(
            runtimeRoot,
            "HarnessRuntimeChatAgent.cs"));
        var builtInPromptSource = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "core",
            "AICopilot.Core.AiGateway",
            "Aggregates",
            "ConversationTemplate",
            "BuiltInConversationTemplates.cs"));
        var mainChatToolCatalogFileSource = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Agents",
            "MainChatToolCatalog.cs"));
        var mainChatToolCatalogSource = mainChatToolCatalogFileSource.Split(
            "internal sealed class AgentSessionCheckpointSink",
            StringSplitOptions.None)[0];
        var mainChatToolGateSource = File.ReadAllText(Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.AiGatewayService",
            "Agents",
            "MainChatToolGate.cs"));

        runtimeSource.Should().NotContain("HarnessToolSurfacePolicy");
        runtimeSource.Should().NotContain("ToolSurfaceGuardChatClient");
        runtimeSource.Should().NotContain("Never call mode_set");
        factorySource.Should().Contain("AgentModeProviderOptions = null");
        factorySource.Should().Contain("new ToolInvocationGuardChatClient(modelClient)");
        guardSource.Should().NotContain("RuntimeAgentMode");
        guardSource.Should().NotContain("mode_set");
        guardSource.Should().NotContain(".Tools =");
        guardSource.Should().Contain("ResolveAllowedToolNames(guardedOptions)");
        runtimeAgentSource.Should().NotContain("SynchronizeToolSurface");
        runtimeAgentSource.Should().NotContain("toolSurfacePolicy");
        builtInPromptSource.Should().Contain("MAF 原生行为模式");
        builtInPromptSource.Should().Contain("模型可使用官方 mode_get / mode_set");
        builtInPromptSource.Should().Contain("模式与授权正交");
        builtInPromptSource.Should().NotContain("Plan 只做规划，不执行外部或业务工具");
        builtInPromptSource.Should().NotContain("Never call mode_set");
        string[] forbiddenModeInputs =
        [
            "RuntimeAgentMode",
            "AgentModeProvider",
            "GetModeAsync",
            "SetModeAsync",
            "IAgentSessionStateStore",
        ];
        foreach (var marker in forbiddenModeInputs)
        {
            mainChatToolCatalogSource.Should().NotContain(marker);
            mainChatToolGateSource.Should().NotContain(marker);
        }
    }

    [Fact]
    public void AiGatewayDbContext_ShouldPersistOnlyHarnessRuntimeAndCurrentCatalogs()
    {
        var actualDbSets = typeof(AiGatewayDbContext)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType.IsGenericType)
            .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments().Single())
            .ToArray();

        actualDbSets.Should().BeEquivalentTo(
        [
            typeof(LanguageModel),
            typeof(ConversationTemplate),
            typeof(Session),
            typeof(Message),
            typeof(ToolRegistration),
            typeof(AgentSessionState),
            typeof(ModelQuotaReservation)
        ]);
    }

    [Fact]
    public void AiGatewayMigrationChain_ShouldPreserveProductionBytesAndAppendCurrentUpgrade()
    {
        var migrationDirectory = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext");
        var migrationFiles = Directory
            .EnumerateFiles(migrationDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var migrationIds = migrationFiles
            .Where(name => !name!.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(name => !string.Equals(
                name,
                "AiGatewayDbContextModelSnapshot.cs",
                StringComparison.Ordinal))
            .Select(name => Path.GetFileNameWithoutExtension(name!))
            .ToArray();

        migrationIds.Should().Equal(
            AiGatewayProductionUpgradeContract.ProductionMigrationIds
                .Append(AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId));
        migrationFiles.Should().NotContain(name =>
            name!.Contains("AiGatewayHarnessBaseline", StringComparison.Ordinal));

        var expectedProductionFiles = FrozenProductionMigrationBytes
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[1], parts => parts[0], StringComparer.Ordinal);
        var actualProductionFiles = migrationFiles
            .Where(name => AiGatewayProductionUpgradeContract.ProductionMigrationIds.Any(id =>
                name!.StartsWith(id, StringComparison.Ordinal)))
            .ToArray();

        actualProductionFiles.Should().BeEquivalentTo(expectedProductionFiles.Keys);
        foreach (var (fileName, expectedSha256) in expectedProductionFiles)
        {
            var actualSha256 = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(migrationDirectory, fileName))))
                .ToLowerInvariant();
            actualSha256.Should().Be(
                expectedSha256,
                $"published migration {fileName} is append-only and its bytes are immutable");
        }

        var currentUpgradeSource = File.ReadAllText(Path.Combine(
            migrationDirectory,
            $"{AiGatewayProductionUpgradeContract.CurrentUpgradeMigrationId}.cs"));
        currentUpgradeSource.Should().Contain(
            "Restore the verified PostgreSQL backup instead.",
            "the destructive retired-table upgrade must not advertise an empty synthetic EF rollback");
    }

    private const string FrozenProductionMigrationBytes =
        """
        b2db5032511334ec0c38bf48bd59b286816acf4787aec9d4f4e22e0015f868df 20260515030952_AiGatewayFreshBaseline.Designer.cs
        e386e4040d2dd9e11e5113f1eaf529251776b6d53e841893bb3276ba72d0a5fc 20260515030952_AiGatewayFreshBaseline.cs
        d000ef5b3b2301ee9929a1e7104fa57c6448dda44e5d9617373c166816fca490 20260519101000_AddPromptPolicyP1.Designer.cs
        fdb79bb4ca4068ba405bb24c49eb5aa349d9f15eb97db3ecb11dffd322a67969 20260519101000_AddPromptPolicyP1.cs
        eea3fcaf84c6c4cd0873a13e10fa8f1cdc48151bf91e0006f5be9ba1b1f0b646 20260519112000_AddToolGovernanceP4.Designer.cs
        6f1662685b3286d99cdcb7bb3604ff34a37f014f807708ee72bd140e0bc31376 20260519112000_AddToolGovernanceP4.cs
        adb861c42e4bc6e77faadd3d780e59d5f501c9759c87a9328bbaaadec20aa2d3 20260520055258_AddArtifactWorkspaceGovernanceP9.Designer.cs
        a3e50cc98ead8c571de61b5e1ac7f7e8317434b7646edb9ad0b5ffb906ae4db5 20260520055258_AddArtifactWorkspaceGovernanceP9.cs
        fa1a544ac3bafde58ce8ba9f32a6a30d900a42c7b5cd44b1fc9344b17ab04af6 20260520071856_AddTrialOperationsP10.Designer.cs
        117115da77c27c565add301342fc436743bd58f90d7e9f2ab5c232416d4c93a5 20260520071856_AddTrialOperationsP10.cs
        0a5ccda53aefae80d4c31f531bbf943e0b9af83b75eed854f77c994e5138a008 20260521083354_AddProductionOperationsP142.Designer.cs
        46cbee645ec54bf9a3e66e0b997e6e568bac4191d66df2ff681b6c42e09f8d8a 20260521083354_AddProductionOperationsP142.cs
        67d4d26df433cd0f22ff4762751e6e76d586d11e483bbb09caad99b231b44d6b 20260522020407_AddProductionPilotHardeningP160.Designer.cs
        a8170f2f135dcb153c5a290cc22433d212b24b54a2e05341f027eff4a62c7df7 20260522020407_AddProductionPilotHardeningP160.cs
        58d676357e2849673f75aca6db2fefb1350ea6968a6dab4c8bb9b95d9d70876d 20260524050227_AddPilotAuthorizationWorkflowM2.Designer.cs
        257ef58071364236f84c68722a5892926cfa3ae2e18750c64beed3489a27c1e5 20260524050227_AddPilotAuthorizationWorkflowM2.cs
        0d6e47dd39da64d001bf0787e9d45746cfe5e5f8f67bda735726da6770a21293 20260524065030_AddPilotAuthorizationHardeningM21.Designer.cs
        f91d1603a8d50597c3ef84118c5de137eb724043fe38fdcfe12779a5dadbdea8 20260524065030_AddPilotAuthorizationHardeningM21.cs
        ebb51b5f7fb471cd85bbc3cdc76fb26bd11317e774f000ae936b310318b449dd 20260617041001_AddProductionControlledPilotIntentCloudQueryFields.Designer.cs
        836c7a6924abba574bd0cfc7d371178e999329e6ac9ded9d3402fd17bbde83b4 20260617041001_AddProductionControlledPilotIntentCloudQueryFields.cs
        a4405852f0002cd362b167966657922b2c351147bf9788206de5fd0cc8a26646 20260622022909_AddMessageRenderPayload.cs
        e22faf4c465ef4e91620a910cb2cd0259ae175137e0dad9c91045bf1d87b9a18 20260622053440_DropLegacyTrialPilotModels.Designer.cs
        f253ff1cc1f360a131861f93dd8a2711b68dc4a431338ec981cec038dc640455 20260622053440_DropLegacyTrialPilotModels.cs
        1e9e9429b5384bc9e632a6b9af14f3138da0cce7fe5e1811b2da0d600b1d192a 20260622062000_AddMessageEventsProjection.cs
        0f5bf49dae5f8fff748a6a4808c2579a7b99c687afa2a25fcdfba831c36e01ff 20260622075032_AddSkillDefinitions.Designer.cs
        bec2b58513e53bc13643124ad60bbd22f487e1fb043e17453995faeaf0ba1ffe 20260622075032_AddSkillDefinitions.cs
        84c2d2503d81ad4cf7942867ef16e26971977e8a80c5b0215952b6025f0620cc 20260623010000_DropPromptPolicies.cs
        6cf78779631358aca05e3a1f6fd38f7afbe9696fcc919d15780e0f48c4e016e5 20260625032000_DisableMockMcpTools.cs
        2a1492f329b60b5de3c805cb39c46d27579609f67334757269c038a53f06311f 20260625052000_RemoveRuntimeSummaryThreshold.cs
        3d52ce36be3a967a5078ce188bdb125c01e0089dcc1673dafd3f32b20df1c4da 20260722090000_AddAgentExecutionRuntimeP1.cs
        b40883a2648028cb70a0e68d61e622878cb99f68ded7f707e3bccfe1a458d064 20260722150000_DropSkillDefinitions.cs
        976392c3a730c77fe3ec2f7d5b08c62c7c7523baca9a7611d28740247b88ff4c 20260722160000_AddDagNodeScheduling.cs
        3977665fb0c27c324d60f9f6ae5135158f96b7131ae8766db932febc14ff2357 20260722170000_AddArtifactEvidenceSetDigest.cs
        """;

    [Fact]
    public void AiGatewayHttpSurface_ShouldKeepHarnessRoutesAndRemoveRetiredRoutes()
    {
        var controllerTypes = typeof(ApiControllerBase).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "AICopilot.HttpApi.Controllers")
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();
        var routes = controllerTypes
            .SelectMany(controller => controller.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.Template is null ? [] : new[] { attribute.Template })
            .ToArray();

        routes.Should().Contain(
        [
            "chat",
            "session/{sessionId:guid}/agent-mode",
            "approval/decision",
            "approval/pending",
            "chat-message/list",
            "tools/catalog"
        ]);
        routes.Should().NotContain(route => new[]
        {
            "agent-task",
            "artifact",
            "business-approval",
            "routing-model",
            "runtime-settings",
            "timeline",
            "agent-upload"
        }.Any(retired => route.Contains(retired, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> EnumerateSurfaceFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(directory);
                if (name is "bin" or "obj" or "node_modules" or "dist" or "artifacts")
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
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

        throw new DirectoryNotFoundException("AICopilot repository root was not found.");
    }
}
