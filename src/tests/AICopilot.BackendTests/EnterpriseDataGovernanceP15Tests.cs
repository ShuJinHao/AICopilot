using AICopilot.DataAnalysisService.SimulationBusiness;

namespace AICopilot.BackendTests;

[Trait("Suite", "EnterpriseDataGovernanceP15")]
public sealed class EnterpriseDataGovernanceP15Tests
{
    [Fact]
    public void EnterpriseGovernanceMigrations_ShouldHaveDesignerAndSnapshotMarkers()
    {
        var root = FindAicopilotRoot();
        var migrationRoot = Path.Combine(root, "src", "infrastructure", "AICopilot.EntityFrameworkCore", "Migrations");

        AssertFileContains(
            Path.Combine(migrationRoot, "DataAnalysisDbContext", "20260519090000_AddEnterpriseDataSourceGovernance.Designer.cs"),
            "AddEnterpriseDataSourceGovernance",
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.DataAnalysisDbContext))",
            "category",
            "default_query_limit",
            "is_selectable_in_agent");
        AssertFileContains(
            Path.Combine(migrationRoot, "DataAnalysisDbContext", "DataAnalysisDbContextModelSnapshot.cs"),
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.DataAnalysisDbContext))",
            "BusinessDomain",
            "DefaultQueryLimit",
            "is_selectable_in_agent");

        AssertFileContains(
            Path.Combine(migrationRoot, "RagDbContext", "20260519091000_AddKnowledgeGovernanceP0.Designer.cs"),
            "AddKnowledgeGovernanceP0",
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.RagDbContext))",
            "knowledge_categories",
            "knowledge_supplements",
            "document_group_id",
            "superseded_by_document_id");
        AssertFileContains(
            Path.Combine(migrationRoot, "RagDbContext", "RagDbContextModelSnapshot.cs"),
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.RagDbContext))",
            "KnowledgeCategory",
            "KnowledgeSupplement",
            "document_group_id",
            "superseded_by_document_id");

        AssertFileContains(
            Path.Combine(migrationRoot, "AiGatewayDbContext", "20260519101000_AddPromptPolicyP1.Designer.cs"),
            "AddPromptPolicyP1",
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))",
            "prompt_policies",
            "prompt_policy_versions",
            "ActiveVersionNo");
        AssertFileContains(
            Path.Combine(migrationRoot, "AiGatewayDbContext", "AiGatewayDbContextModelSnapshot.cs"),
            "DbContext(typeof(global::AICopilot.EntityFrameworkCore.AiGatewayDbContext))",
            "PromptPolicy",
            "prompt_policy_versions",
            "ActiveVersionNo");
    }

    [Fact]
    public void SimulationBusinessSeedProfiles_ShouldKeepSmallFastAndMediumAcceptanceCounts()
    {
        var generator = new SimulationBusinessSeedGenerator();
        var small = generator.CreatePlan(SimulationBusinessProfile.Small);
        var medium = generator.CreatePlan(SimulationBusinessProfile.Medium);

        small.DatabaseName.Should().Be("aicopilot_sim_business");
        small.TableCounts.Single(item => item.TableName == "employees").RowCount.Should().Be(30);
        small.TableCounts.Single(item => item.TableName == "attendance").RowCount.Should().Be(1800);
        small.TableCounts.Single(item => item.TableName == "production_records").RowCount.Should().Be(3000);
        small.TableCounts.Single(item => item.TableName == "device_events").RowCount.Should().Be(5000);
        small.TableCounts.Single(item => item.TableName == "quality_inspections").RowCount.Should().Be(2000);
        small.TableCounts.Single(item => item.TableName == "inventory_movements").RowCount.Should().Be(3000);
        small.TableCounts.Single(item => item.TableName == "sales_orders").RowCount.Should().Be(300);

        medium.TableCounts.Single(item => item.TableName == "employees").RowCount.Should().Be(300);
        medium.TableCounts.Single(item => item.TableName == "attendance").RowCount.Should().Be(18000);
        medium.TableCounts.Single(item => item.TableName == "production_records").RowCount.Should().Be(30000);
        medium.TableCounts.Single(item => item.TableName == "device_events").RowCount.Should().Be(50000);
        medium.TableCounts.Single(item => item.TableName == "quality_inspections").RowCount.Should().Be(20000);
        medium.TableCounts.Single(item => item.TableName == "inventory_movements").RowCount.Should().Be(30000);
        medium.TableCounts.Single(item => item.TableName == "sales_orders").RowCount.Should().Be(3000);
    }

    [Fact]
    public void SimulationBusinessSeedSql_ShouldBeIdempotentAndReadonlyBoundaryOriented()
    {
        var plan = new SimulationBusinessSeedGenerator().CreatePlan(SimulationBusinessProfile.Small);

        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS employees");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS attendance");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS production_devices");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS production_records");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS device_events");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS quality_inspections");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS inventory_movements");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS purchase_orders");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS sales_orders");
        plan.SqlScript.Should().Contain("CREATE TABLE IF NOT EXISTS delivery_records");
        plan.SqlScript.Should().Contain("ON CONFLICT");

        plan.SqlScript.Should().NotContain("DROP TABLE");
        plan.SqlScript.Should().NotContain("TRUNCATE");
        plan.SqlScript.Should().NotContain("GRANT ALL");
        plan.SqlScript.Should().NotContain("SUPERUSER");
    }

    [Fact]
    public void EnterpriseGovernanceP15AcceptanceScript_ShouldUseTempOutputsAndP15Report()
    {
        var root = FindAicopilotRoot();
        var script = Path.Combine(root, "scripts", "Run-EnterpriseDataGovernanceP1_5Acceptance.ps1");

        AssertFileContains(
            script,
            "enterprise-data-governance-p1_5-latest.md",
            "aicopilot-enterprise-data-governance-p1_5",
            "-o (Join-Path $buildOutputRoot",
            "EnterpriseDataGovernanceP15Tests",
            "Build EntityFrameworkCore",
            "Temporary PostgreSQL Migration Smoke",
            "P1.5 Acceptance");
    }

    private static void AssertFileContains(string path, params string[] expectedSnippets)
    {
        File.Exists(path).Should().BeTrue($"{path} must exist");
        var content = File.ReadAllText(path);
        foreach (var expectedSnippet in expectedSnippets)
        {
            content.Should().Contain(expectedSnippet, $"{Path.GetFileName(path)} must contain {expectedSnippet}");
        }
    }

    private static string FindAicopilotRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[]
        {
            Path.GetDirectoryName(sourceFile),
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        })
        {
            var root = TryFindAicopilotRoot(start);
            if (root is not null)
            {
                return root;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the AICopilot repository root.");
    }

    private static string? TryFindAicopilotRoot(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "infrastructure", "AICopilot.EntityFrameworkCore");
            if (Directory.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
