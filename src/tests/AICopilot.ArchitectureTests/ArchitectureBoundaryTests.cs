using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
using AICopilot.SharedKernel.Domain;

namespace AICopilot.ArchitectureTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string SolutionRoot = FindSolutionRoot();

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

    [Fact]
    public void McpServerDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "McpServerDbContext");

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
    public void DataAnalysisDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "DataAnalysisDbContext");

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
    public void RagDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "RagDbContext");

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
    public void AiGatewayDbContextMigrations_ShouldNotCreateOutboxTable()
    {
        var migrationRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext");

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
    public void OutboxRuntimeServices_ShouldNotDependOnAiCopilotDbContext()
    {
        var outboxRoot = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Outbox");

        var runtimeFiles = new[]
        {
            "OutboxDispatcher.cs",
            "OutboxIntegrationEventPublisher.cs"
        };

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(Path.Combine(outboxRoot, runtimeFile));

            source.Should().Contain("OutboxDbContext", runtimeFile);
            source.Should().NotContain("AiCopilotDbContext", runtimeFile);
        }

        var outboxContext = File.ReadAllText(Path.Combine(outboxRoot, "OutboxDbContext.cs"));

        outboxContext.Should().Contain("DbSet<OutboxMessage>");
        outboxContext.Should().Contain("OutboxMessageConfiguration");
        outboxContext.Should().NotContain("ExcludeFromMigrations");
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
    public void IdentityManagementCommands_ShouldUseIdentityAuditWriter()
    {
        var commandRoot = Path.Combine(
            SolutionRoot,
            "src",
            "services",
            "AICopilot.IdentityService",
            "Commands");
        var commandFiles = new[]
        {
            "CreateRole.cs",
            "UpdateRole.cs",
            "DeleteRole.cs",
            "CreatedUser.cs",
            "UpdateUserRole.cs",
            "DisableUser.cs",
            "EnableUser.cs",
            "ResetUserPassword.cs"
        };

        foreach (var commandFile in commandFiles)
        {
            var source = File.ReadAllText(Path.Combine(commandRoot, commandFile));

            source.Should().Contain("ITransactionalExecutionService", commandFile);
            source.Should().Contain("IIdentityAuditLogWriter", commandFile);
            source.Should().Contain("transactionalExecutionService.ExecuteAsync", commandFile);
            source.Should().NotContain("IAuditLogWriter", commandFile);
            source.Should().NotContain("auditLogWriter.SaveChangesAsync", commandFile);
            source.Should().NotContain("DbContext", commandFile);
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
        var transactionalExecutionFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Transactions",
            "EfTransactionalExecutionService.cs");

        var mainContext = File.ReadAllText(mainContextFile);
        var identityContext = File.ReadAllText(identityContextFile);
        var dependencyInjection = File.ReadAllText(dependencyInjectionFile);
        var transactionalExecution = File.ReadAllText(transactionalExecutionFile);

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

        transactionalExecution.Should().Contain("IdentityStoreDbContext");
        transactionalExecution.Should().NotContain("AiCopilotDbContext");
        transactionalExecution.Should().NotContain("AuditDbContext");
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
        var source = File.ReadAllText(workerFile);

        source.Should().Contain("GetRequiredService<IdentityStoreDbContext>");

        var mainMigration = source.IndexOf("await RunMigrationAsync(dbContext", StringComparison.Ordinal);
        var identityMigration = source.IndexOf("await RunMigrationAsync(identityStoreDbContext", StringComparison.Ordinal);
        var firstModuleMigration = source.IndexOf("await RunMigrationAsync(aiGatewayDbContext", StringComparison.Ordinal);
        var identitySeed = source.IndexOf("await SeedIdentityAsync", StringComparison.Ordinal);

        mainMigration.Should().BeGreaterThanOrEqualTo(0);
        identityMigration.Should().BeGreaterThan(mainMigration);
        firstModuleMigration.Should().BeGreaterThan(identityMigration);
        identitySeed.Should().BeGreaterThan(firstModuleMigration);
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
    public void DataAnalysisDbContextSnapshot_ShouldMapBusinessDatabaseToDataAnalysisSchema()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "DataAnalysisDbContext",
            "DataAnalysisDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.BusinessDatabase",
            "BusinessDatabase is owned by DataAnalysisDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"business_databases\", \"dataanalysis\");",
            "BusinessDatabase must be mapped to the dataanalysis schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\", t => t.ExcludeFromMigrations());",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void RagDbContextSnapshot_ShouldMapRagTablesToRagSchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "RagDbContext",
            "RagDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.EmbeddingModel.EmbeddingModel",
            "EmbeddingModel is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeBase",
            "KnowledgeBase is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document",
            "Document is owned by RagDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.DocumentChunk",
            "DocumentChunk is owned by RagDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"embedding_models\", \"rag\");",
            "EmbeddingModel must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"knowledge_bases\", \"rag\");",
            "KnowledgeBase must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"documents\", \"rag\");",
            "Document must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"document_chunks\", \"rag\");",
            "DocumentChunk must be mapped to the rag schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\", t => t.ExcludeFromMigrations());",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void AiGatewayDbContextSnapshot_ShouldMapAiGatewayTablesToAiGatewaySchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "AiGatewayDbContext",
            "AiGatewayDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel",
            "LanguageModel is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate",
            "ConversationTemplate is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy.ApprovalPolicy",
            "ApprovalPolicy is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Session",
            "Session is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Message",
            "Message is owned by AiGatewayDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"language_models\", \"aigateway\");",
            "LanguageModel must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"conversation_templates\", \"aigateway\");",
            "ConversationTemplate must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"approval_policies\", \"aigateway\");",
            "ApprovalPolicy must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"sessions\", \"aigateway\");",
            "Session must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"messages\", \"aigateway\");",
            "Message must be mapped to the aigateway schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\",",
            "module contexts may write Outbox rows but must not own the Outbox migration");
        snapshot.Should().Contain(
            "t.ExcludeFromMigrations();",
            "module contexts may write Outbox rows but must not own the Outbox migration");
    }

    [Fact]
    public void McpServerDbContextSnapshot_ShouldMapMcpTableToMcpSchema_AndExcludeOutboxFromMigrations()
    {
        var snapshotFile = Path.Combine(
            SolutionRoot,
            "src",
            "infrastructure",
            "AICopilot.EntityFrameworkCore",
            "Migrations",
            "McpServerDbContext",
            "McpServerDbContextModelSnapshot.cs");
        var snapshot = File.ReadAllText(snapshotFile);

        snapshot.Should().Contain(
            "AICopilot.Core.McpServer.Aggregates.McpServerInfo.McpServerInfo",
            "McpServerInfo is owned by McpServerDbContext");
        snapshot.Should().Contain(
            "b.ToTable(\"mcp_server_info\", \"mcp\");",
            "McpServerInfo must be mapped to the mcp schema");
        snapshot.Should().Contain(
            "b.ToTable(\"outbox_messages\", \"outbox\",",
            "module contexts may write Outbox rows but must not own the Outbox migration");
        snapshot.Should().Contain(
            "t.ExcludeFromMigrations();",
            "module contexts may write Outbox rows but must not own the Outbox migration");
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
