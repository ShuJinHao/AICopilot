using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AICopilot.Architecture.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AICopilotArchitectureAnalyzer : DiagnosticAnalyzer
{
    public const string ProjectBoundaryId = "AIARCH001";
    public const string AggregateBoundaryId = "AIARCH002";
    public const string PersistenceOwnerId = "AIARCH003";
    public const string EnabledAdminInvariantId = "AIARCH004";
    public const string AgentPluginBoundaryId = "AIARCH005";
    public const string CloudReadOnlyBoundaryId = "AIARCH006";
    public const string SecurityMetadataId = "AIARCH007";

    private static readonly ImmutableHashSet<string> CompilationEndRuleIds = ImmutableHashSet.Create(StringComparer.Ordinal, ProjectBoundaryId, EnabledAdminInvariantId, CloudReadOnlyBoundaryId);
    private static readonly DiagnosticDescriptor ProjectBoundaryRule = CreateRule(ProjectBoundaryId, "Project dependency violates the AICopilot layer graph", "Project '{0}' must not reference '{1}': {2}");
    private static readonly DiagnosticDescriptor AggregateBoundaryRule = CreateRule(AggregateBoundaryId, "Aggregate and repository ownership must be explicit", "Symbol '{0}' violates the approved aggregate/repository boundary: {1}");
    private static readonly DiagnosticDescriptor PersistenceOwnerRule = CreateRule(PersistenceOwnerId, "Database technology must stay with its approved owner", "'{0}' uses '{1}', which is owned by AICopilot.EntityFrameworkCore, AICopilot.Dapper, or the explicit migration/lock composition boundary");
    private static readonly DiagnosticDescriptor EnabledAdminInvariantRule = CreateRule(EnabledAdminInvariantId, "Enabled Admin reduction requires the shared invariant transaction", "'{0}' can reduce enabled Admin membership without the required transaction/guard ordering: {1}");
    private static readonly DiagnosticDescriptor AgentPluginBoundaryRule = CreateRule(AgentPluginBoundaryId, "Agent plugin capability and host boundaries must be explicit", "Plugin symbol '{0}' violates the plugin boundary: {1}");
    private static readonly DiagnosticDescriptor CloudReadOnlyBoundaryRule = CreateRule(CloudReadOnlyBoundaryId, "Cloud read-only call graphs must not reach side effects", "Cloud read-only entry '{0}' can reach forbidden side effect '{1}'");
    private static readonly DiagnosticDescriptor SecurityMetadataRule = CreateRule(SecurityMetadataId, "Authorization and read-only metadata must not be bypassed", "'{0}' must declare valid authorization/read-only metadata: {1}");

    private static readonly ImmutableHashSet<string> ApprovedAggregateNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.Core.AiGateway.Aggregates.Sessions.Session",
            "AICopilot.Core.AiGateway.Aggregates.AgentTasks.AgentTask",
            "AICopilot.Core.AiGateway.Aggregates.Artifacts.ArtifactWorkspace",
            "AICopilot.Core.AiGateway.Aggregates.Approvals.ApprovalRequest",
            "AICopilot.Core.AiGateway.Aggregates.LanguageModel.LanguageModel",
            "AICopilot.Core.AiGateway.Aggregates.ConversationTemplate.ConversationTemplate",
            "AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy.ApprovalPolicy",
            "AICopilot.Core.AiGateway.Aggregates.RoutingModel.RoutingModelConfiguration",
            "AICopilot.Core.AiGateway.Aggregates.Tools.ToolRegistration",
            "AICopilot.Core.AiGateway.Aggregates.RuntimeSettings.ChatRuntimeSettings",
            "AICopilot.Core.AiGateway.Aggregates.Uploads.UploadRecord",
            "AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.BusinessDatabase",
            "AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase.DataSourcePermissionGrant",
            "AICopilot.Core.McpServer.Aggregates.McpServerInfo.McpServerInfo",
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeBase",
            "AICopilot.Core.Rag.Aggregates.EmbeddingModel.EmbeddingModel",
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeCategory",
            "AICopilot.Core.Rag.Aggregates.KnowledgeBase.KnowledgeSupplement");

    private static readonly ImmutableHashSet<string> ExplicitPublicRequestNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.IdentityService.Commands.FinalizeCloudOidcLoginCommand",
            "AICopilot.IdentityService.Commands.LoginUserCommand",
            "AICopilot.IdentityService.Queries.GetCurrentUserProfileQuery",
            "AICopilot.IdentityService.Queries.GetInitializationStatusQuery");

    private static readonly ImmutableDictionary<string, string> ExplicitResourceAuthorizationOwners =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new[]
            {
                new KeyValuePair<string, string>(
                    "AICopilot.AiGatewayService.Workspaces.GetArtifactWorkspaceQuery",
                    "AICopilot.AiGatewayService.Workspaces.ArtifactWorkspaceQueryCoordinator"),
                new KeyValuePair<string, string>(
                    "AICopilot.AiGatewayService.Workspaces.DownloadArtifactQuery",
                    "AICopilot.AiGatewayService.Workspaces.ArtifactWorkspaceQueryCoordinator"),
                new KeyValuePair<string, string>(
                    "AICopilot.AiGatewayService.AgentTasks.ApproveAgentApprovalCommand",
                    "AICopilot.AiGatewayService.AgentTasks.AgentApprovalDecisionCoordinator"),
                new KeyValuePair<string, string>(
                    "AICopilot.AiGatewayService.AgentTasks.RejectAgentApprovalCommand",
                    "AICopilot.AiGatewayService.AgentTasks.AgentApprovalDecisionCoordinator")
            });

    private static readonly ImmutableHashSet<string> IdentityDecreaseMethodNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "RemoveFromRoleAsync",
            "RemoveFromRolesAsync",
            "DeleteAsync",
            "SetLockoutEndDateAsync",
            "SetLockoutEnabledAsync",
            "MarkUserDisabled");

    private static readonly ImmutableHashSet<string> DynamicDatabaseMemberNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SaveChanges",
            "SaveChangesAsync",
            "CreateCommand",
            "ExecuteNonQuery",
            "ExecuteNonQueryAsync",
            "ExecuteReader",
            "ExecuteReaderAsync",
            "ExecuteScalar",
            "ExecuteScalarAsync",
            "ExecuteSqlRaw",
            "ExecuteSqlRawAsync",
            "ExecuteSqlInterpolated",
            "ExecuteSqlInterpolatedAsync");

    private const string AuthorizeRequirementAttributeName =
        "AICopilot.Services.CrossCutting.Attributes.AuthorizeRequirementAttribute";
    private const string AuthorizeAttributeName =
        "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";
    private const string AllowAnonymousAttributeName =
        "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";
    private const string DescriptionAttributeName =
        "System.ComponentModel.DescriptionAttribute";
    private const string ToolSafetyDescriptorName =
        "AICopilot.SharedKernel.Ai.AiToolSafetyDescriptor";
    private const string ToolSafetyPolicyName =
        "AICopilot.SharedKernel.Ai.AiToolSafetyPolicy";
    private const string AssemblyMetadataAttributeName =
        "System.Reflection.AssemblyMetadataAttribute";
    private const string EffectSummaryMetadataPrefix =
        "AICopilot.Architecture.EffectSummary.";
    private const string EffectSummarySchemaKey =
        EffectSummaryMetadataPrefix + "Schema";
    private const string EffectSummaryCountKey =
        EffectSummaryMetadataPrefix + "Count";
    private const string EffectSummaryEntryPrefix =
        EffectSummaryMetadataPrefix + "Entry.";
    private const string EffectSummarySchemaVersion = "2";
    private const string FormalAuditLogWriterName =
        "AICopilot.EntityFrameworkCore.AuditLogs.AuditLogWriter";
    private const string FormalAuditDbContextName =
        "AICopilot.EntityFrameworkCore.AuditLogs.AuditDbContext";
    private static class FormalQuotaTypes
    {
        public const string Contract = "AICopilot.Services.Contracts.IModelQuotaReservationStore";
        public const string Store = "AICopilot.EntityFrameworkCore.Repository.PostgresModelQuotaReservationStore";
        public const string TransactionRunner = "AICopilot.EntityFrameworkCore.Transactions.AgentExecutionTransactionRunner";
        public const string DbContext = "AICopilot.EntityFrameworkCore.AiGatewayDbContext";
    }
    private const int MaximumEffectSummaryEntries = 4096;
    private const int CrossProjectIdentityEffect = 1;
    private const int CrossProjectCloudSideEffect = 2;
    private const int CrossProjectCloudRoot = 4;
    private const int CrossProjectIdentityRoot = 8;
    private const int CrossProjectNormalCallEdge = 16;
    private const int CrossProjectGuardedIdentityCallEdge = 32;
    private const int CrossProjectDelegateReturnIdentityEffect = 64;
    private const int CrossProjectDelegateReturnCloudSideEffect = 128;
    private const int CrossProjectDelegateReturnResolved = 256;
    private static readonly ImmutableHashSet<string> TrustedCloudReadTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.Services.Contracts.ICloudAiReadClient",
            "AICopilot.Services.Contracts.IBusinessTextToSqlGenerator",
            "AICopilot.Services.Contracts.IBusinessQueryProvider",
            "AICopilot.Services.Contracts.IBusinessQueryProviderRegistry",
            "AICopilot.Services.Contracts.IBusinessDataSourceProfileRegistry",
            "AICopilot.Services.Contracts.IBusinessQueryContextStore",
            "AICopilot.Services.Contracts.IDatabaseConnector",
            "AICopilot.Services.Contracts.ISqlGuardrail",
            "AICopilot.Infrastructure.CloudRead.CloudAiReadClient",
            "AICopilot.Dapper.DapperDatabaseConnector",
            "AICopilot.Dapper.Security.AstSqlGuardrail");
    private static readonly ImmutableHashSet<string> FormalCloudReadOnlyWorkflowTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.AiGatewayService.Workflows.Executors.BusinessTextToSqlFallbackRunner",
            "AICopilot.AiGatewayService.Workflows.Executors.BusinessQueryProviderRegistry",
            "AICopilot.AiGatewayService.Workflows.Executors.CloudAiReadBusinessQueryProvider",
            "AICopilot.DataAnalysisService.BusinessDatabases.BusinessDataSourceProfileRegistry",
            "AICopilot.DataAnalysisService.BusinessDatabases.BusinessQueryContextStore",
            "AICopilot.Dapper.DapperDatabaseConnector",
            "AICopilot.Dapper.Security.AstSqlGuardrail");
    private static readonly ImmutableArray<string> ExplicitProductionAssemblyNamesByDescendingLength =
    [
        "AICopilot.Services.CrossCutting",
        "AICopilot.Services.Contracts",
        "AICopilot.Core.DataAnalysis",
        "AICopilot.Core.McpServer",
        "AICopilot.Core.AiGateway",
        "AICopilot.AgentPlugin.Runtime",
        "AICopilot.Core.Rag",
        "AICopilot.DataAnalysisService",
        "AICopilot.EntityFrameworkCore",
        "AICopilot.AiGatewayService",
        "AICopilot.MigrationWorkApp",
        "AICopilot.ServiceDefaults",
        "AICopilot.IdentityService",
        "AICopilot.RagService",
        "AICopilot.McpService",
        "AICopilot.Infrastructure",
        "AICopilot.Visualization",
        "AICopilot.AgentPlugin",
        "AICopilot.SharedKernel",
        "AICopilot.Embedding",
        "AICopilot.AiRuntime",
        "AICopilot.DataWorker",
        "AICopilot.RagWorker",
        "AICopilot.EventBus",
        "AICopilot.HttpApi",
        "AICopilot.AppHost",
        "AICopilot.Dapper"
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            ProjectBoundaryRule,
            AggregateBoundaryRule,
            PersistenceOwnerRule,
            EnabledAdminInvariantRule,
            AgentPluginBoundaryRule,
            CloudReadOnlyBoundaryRule,
            SecurityMetadataRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var state = new CompilationState(startContext.Compilation);

            startContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedType((INamedTypeSymbol)symbolContext.Symbol, symbolContext, state),
                SymbolKind.NamedType);
            startContext.RegisterSymbolAction(
                symbolContext =>
                {
                    var method = Normalize((IMethodSymbol)symbolContext.Symbol);
                    state.AddMethod(method);
                    AnalyzeRoleAuthorizationSymbol(method, symbolContext, state);
                },
                SymbolKind.Method);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation((IInvocationOperation)operationContext.Operation, operationContext, state),
                OperationKind.Invocation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeDynamicInvocation(operationContext, state),
                OperationKind.DynamicInvocation,
                OperationKind.DynamicObjectCreation,
                OperationKind.DynamicIndexerAccess);
            startContext.RegisterOperationAction(
                operationContext =>
                {
                    AnalyzeImplicitMethodCall(operationContext, state);
                    if (operationContext.Operation is IPropertyReferenceOperation property)
                    {
                        AnalyzePluginRuntimePropertyReference(property, operationContext, state);
                    }
                },
                OperationKind.PropertyReference,
                OperationKind.EventAssignment,
                OperationKind.Conversion,
                OperationKind.Binary,
                OperationKind.Unary,
                OperationKind.CompoundAssignment,
                OperationKind.Increment,
                OperationKind.Decrement);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeRoleClaimReference(
                    (IFieldReferenceOperation)operationContext.Operation,
                    operationContext,
                    state),
                OperationKind.FieldReference);
            startContext.RegisterOperationAction(
                operationContext =>
                {
                    var objectCreation = (IObjectCreationOperation)operationContext.Operation;
                    foreach (var argument in objectCreation.Arguments.Where(argument =>
                                 !argument.IsImplicit &&
                                 argument.Parameter is not null &&
                                 IsDelegateType(argument.Parameter.Type)))
                    {
                        state.AddDelegateParameterDefinition(argument.Parameter!, argument.Value);
                    }

                    foreach (var owner in GetOperationOwners(
                                 objectCreation,
                                 operationContext.ContainingSymbol))
                    {
                        CollectImplicitMethodCallFacts(
                            objectCreation,
                            objectCreation.Constructor,
                            owner,
                            state);
                    }
                    AnalyzeDatabaseTarget(
                        objectCreation.Constructor,
                        objectCreation.Syntax.GetLocation(),
                        operationContext.ContainingSymbol,
                        state);
                    AnalyzeRoleAuthorizationObjectCreation(objectCreation, operationContext, state);
                    AnalyzeCloudReadObjectCreation(objectCreation, operationContext, state);
                },
                OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeAnonymousFunction((IAnonymousFunctionOperation)operationContext.Operation, operationContext, state),
                OperationKind.AnonymousFunction);
            startContext.RegisterOperationAction(
                operationContext => state.AddDelegateMethodReference(
                    ((IMethodReferenceOperation)operationContext.Operation).Method),
                OperationKind.MethodReference);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeDelegateMemberDefinition(operationContext, state),
                OperationKind.FieldInitializer,
                OperationKind.PropertyInitializer,
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.Return);
            startContext.RegisterCompilationEndAction(endContext => AnalyzeCompilation(endContext, state));
        });
    }

    private static DiagnosticDescriptor CreateRule(
        string id,
        string title,
        string message) =>
        new(
            id,
            title,
            message,
            "Architecture",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "AICopilot architecture diagnostics are stable build errors and may only be changed with the corresponding formal contract and executable fixtures.",
            customTags: CompilationEndRuleIds.Contains(id)
                ? [WellKnownDiagnosticTags.CompilationEnd, WellKnownDiagnosticTags.NotConfigurable]
                : [WellKnownDiagnosticTags.NotConfigurable]);

    private static void AnalyzeNamedType(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (!type.Locations.Any(location => location.IsInSource))
        {
            return;
        }

        foreach (var constructor in type.InstanceConstructors.Concat(type.StaticConstructors))
        {
            state.AddMethod(constructor);
        }

        if (type.TypeKind == TypeKind.Class &&
            ImplementsFullyQualified(type, "AICopilot.SharedKernel.Domain.IAggregateRoot") &&
            state.AssemblyName.StartsWith("AICopilot.Core.", StringComparison.Ordinal) &&
            !ApprovedAggregateNames.Contains(type.ToDisplayString()))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AggregateBoundaryRule,
                FirstSourceLocation(type),
                type.ToDisplayString(),
                "the type is not in the approved aggregate-root inventory"));
        }

        AnalyzeRepositoryTypes(type, context);
        AnalyzePersistenceOwner(type, context, state);
        AnalyzePluginType(type, context, state);
        AnalyzeAuditWriterImplementation(type, context, state);
        AnalyzeModelQuotaReservationStoreImplementation(type, context, state);
        AnalyzeAuthorizationType(type, context, state);
        AnalyzeRoleAuthorizationSymbol(type, context, state);
        AnalyzeInvariantImplementation(type, context, state);
    }

    private static void AnalyzeRepositoryTypes(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        var observed = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        AddType(type.BaseType, observed);
        foreach (var @interface in type.Interfaces)
        {
            AddType(@interface, observed);
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field:
                    AddType(field.Type, observed);
                    break;
                case IPropertySymbol property:
                    AddType(property.Type, observed);
                    break;
                case IMethodSymbol method:
                    AddType(method.ReturnType, observed);
                    foreach (var parameter in method.Parameters)
                    {
                        AddType(parameter.Type, observed);
                    }

                    break;
            }
        }

        foreach (var repository in observed
                     .OfType<INamedTypeSymbol>()
                     .Where(IsRepositoryType)
                     .Where(repository => repository.TypeArguments.Length == 1))
        {
            var entity = repository.TypeArguments[0];
            if (entity is ITypeParameterSymbol ||
                ImplementsFullyQualified(entity, "AICopilot.SharedKernel.Domain.IAggregateRoot"))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                AggregateBoundaryRule,
                FirstSourceLocation(type),
                repository.ToDisplayString(),
                $"repository entity '{entity.ToDisplayString()}' is not an aggregate root"));
        }
    }

    private static void AnalyzePersistenceOwner(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (DerivesFromFullyQualified(type, "Microsoft.EntityFrameworkCore.DbContext") &&
            !IsEfOwner(state.AssemblyName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceOwnerRule,
                FirstSourceLocation(type),
                type.ToDisplayString(),
                "Microsoft.EntityFrameworkCore.DbContext"));
        }
    }

    private static void AnalyzeAuditWriterImplementation(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (!IsConcreteProductionImplementation(
                type,
                state,
                "AICopilot.Services.Contracts.IAuditLogWriter"))
        {
            return;
        }

        var databaseContextDependencies = type.InstanceConstructors
            .SelectMany(constructor => constructor.Parameters)
            .Select(parameter => parameter.Type)
            .Where(parameterType =>
                DerivesFromFullyQualified(parameterType, "Microsoft.EntityFrameworkCore.DbContext"))
            .ToArray();
        var isExactWriter = HasExpectedTypeIdentity(type, FormalAuditLogWriterName);
        var hasOnlyExactAuditContext = databaseContextDependencies.Length > 0 &&
                                       databaseContextDependencies.All(parameterType =>
                                           HasExpectedTypeIdentity(
                                               parameterType as INamedTypeSymbol,
                                               FormalAuditDbContextName));
        if (isExactWriter && hasOnlyExactAuditContext)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            CloudReadOnlyBoundaryRule,
            FirstSourceLocation(type),
            type.ToDisplayString(),
            $"IAuditLogWriter has one approved production implementation: {FormalAuditLogWriterName} backed only by {FormalAuditDbContextName}"));
    }

    private static void AnalyzeModelQuotaReservationStoreImplementation(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (!IsConcreteProductionImplementation(type, state, FormalQuotaTypes.Contract))
        {
            return;
        }

        var constructors = type.InstanceConstructors
            .Where(constructor => !constructor.IsImplicitlyDeclared)
            .ToArray();
        var hasExactStoreConstructor = constructors.Length == 1 &&
                                       constructors[0].Parameters.Length == 1 &&
                                       HasExpectedTypeIdentity(
                                           constructors[0].Parameters[0].Type as INamedTypeSymbol,
                                           FormalQuotaTypes.TransactionRunner);
        var transactionRunner = constructors
            .SelectMany(constructor => constructor.Parameters)
            .Select(parameter => parameter.Type as INamedTypeSymbol)
            .SingleOrDefault(parameterType =>
                HasExpectedTypeIdentity(parameterType, FormalQuotaTypes.TransactionRunner));
        var runnerDatabaseContexts = transactionRunner?.InstanceConstructors
            .SelectMany(constructor => constructor.Parameters)
            .Select(parameter => parameter.Type)
            .Where(parameterType =>
                DerivesFromFullyQualified(parameterType, "Microsoft.EntityFrameworkCore.DbContext"))
            .ToArray() ?? [];
        var hasOnlyExactAiGatewayContext = runnerDatabaseContexts.Length == 1 &&
                                           HasExpectedTypeIdentity(
                                               runnerDatabaseContexts[0] as INamedTypeSymbol,
                                               FormalQuotaTypes.DbContext);

        if (HasExpectedTypeIdentity(type, FormalQuotaTypes.Store) &&
            hasExactStoreConstructor &&
            hasOnlyExactAiGatewayContext)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            CloudReadOnlyBoundaryRule,
            FirstSourceLocation(type),
            type.ToDisplayString(),
            $"{FormalQuotaTypes.Contract} has one approved production implementation: {FormalQuotaTypes.Store}, using only {FormalQuotaTypes.TransactionRunner} backed by {FormalQuotaTypes.DbContext}"));
    }

    private static bool IsConcreteProductionImplementation(
        INamedTypeSymbol type,
        CompilationState state,
        string contractTypeName) =>
        IsProductionAssembly(state.AssemblyName) &&
        !ContainsTestAssemblyMarker(state.AssemblyName) &&
        type.TypeKind == TypeKind.Class &&
        !type.IsAbstract &&
        ImplementsFullyQualified(type, contractTypeName);

    private static void AnalyzePluginType(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        var isPlugin = ImplementsFullyQualified(type, "AICopilot.AgentPlugin.IAgentPlugin");
        var isAgentExecutor = ImplementsFullyQualified(
            type,
            "AICopilot.AiGatewayService.AgentTasks.IAgentToolExecutor");
        if ((isPlugin || isAgentExecutor) &&
            LooksLikeTestDouble(type.Name) &&
            !IsApprovedDevelopmentMock(type, state.AssemblyName))
        {
            ReportPlugin(context, type, "production plugin/test-double implementations are forbidden");
        }

        if (!isPlugin)
        {
            return;
        }

        if (state.AssemblyName.StartsWith("AICopilot.", StringComparison.Ordinal) &&
            (state.AssemblyName.Contains(".Hosts.", StringComparison.Ordinal) ||
             IsHostAssembly(state.AssemblyName)))
        {
            ReportPlugin(context, type, "host projects may compose plugins but may not implement them");
        }

        if (type.IsAbstract ||
            state.AssemblyName == "AICopilot.AgentPlugin" ||
            state.AssemblyName == "AICopilot.AgentPlugin.Runtime")
        {
            return;
        }

        var description = type.GetMembers("Description").OfType<IPropertySymbol>()
            .FirstOrDefault(property => SymbolEqualityComparer.Default.Equals(property.ContainingType, type));
        if (description is null || !description.IsOverride)
        {
            ReportPlugin(context, type, "concrete plugins must explicitly override Description");
        }

        var exposure = type.GetMembers("ChatExposureMode").OfType<IPropertySymbol>()
            .FirstOrDefault(property => SymbolEqualityComparer.Default.Equals(property.ContainingType, type));
        if (exposure is null || !exposure.IsOverride)
        {
            ReportPlugin(context, type, "concrete plugins must explicitly override ChatExposureMode");
        }

        var toolMethods = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => !method.IsStatic && method.DeclaredAccessibility == Accessibility.Public)
            .Where(method => HasAttribute(method, DescriptionAttributeName))
            .ToArray();
        if (toolMethods.Length == 0)
        {
            ReportPlugin(context, type, "concrete plugins must expose at least one instance tool with DescriptionAttribute");
        }

    }

    private static void AnalyzeAuthorizationType(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (type.TypeKind == TypeKind.Class &&
            state.AssemblyName.Contains("Service", StringComparison.Ordinal) &&
            type.DeclaredAccessibility == Accessibility.Public &&
            IsServiceRequest(type))
        {
            var isStream = ImplementsDefinition(type, "MediatR", "IStreamRequest");
            var isPublicException = !isStream &&
                                    ExplicitPublicRequestNames.Contains(type.ToDisplayString()) &&
                                    HasExpectedAssemblyIdentity(type, type.ToDisplayString());
            var hasResourceAuthorization = TryGetResourceAuthorizationOwner(type, out var resourceOwner);
            var hasValidResourceAuthorization =
                !isStream &&
                hasResourceAuthorization &&
                ExplicitResourceAuthorizationOwners.TryGetValue(type.ToDisplayString(), out var expectedOwner) &&
                string.Equals(resourceOwner, expectedOwner, StringComparison.Ordinal);

            if (hasResourceAuthorization && !hasValidResourceAuthorization)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SecurityMetadataRule,
                    FirstSourceLocation(type),
                    type.ToDisplayString(),
                    "ResourceAuthorizationOwnerAttribute must match the exact approved request/coordinator pair"));
            }
            else if (!isPublicException &&
                     !hasValidResourceAuthorization &&
                     !HasAttribute(type, AuthorizeRequirementAttributeName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SecurityMetadataRule,
                    FirstSourceLocation(type),
                    type.ToDisplayString(),
                    isStream
                        ? "stream requests always require AuthorizeRequirementAttribute"
                        : "service commands and queries require AuthorizeRequirementAttribute, an exact resource-authorization owner, or an exact public request"));
            }
        }

        if (!IsHttpApiAssembly(state.AssemblyName) ||
            type.TypeKind != TypeKind.Class ||
            !DerivesFromFullyQualified(type, "Microsoft.AspNetCore.Mvc.ControllerBase") ||
            !type.Name.EndsWith("Controller", StringComparison.Ordinal))
        {
            return;
        }

        if (HasAuthorizationMetadata(type))
        {
            return;
        }

        foreach (var action in type.GetMembers().OfType<IMethodSymbol>().Where(IsControllerAction))
        {
            if (!HasAuthorizationMetadata(action))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SecurityMetadataRule,
                    FirstSourceLocation(action),
                    action.ToDisplayString(),
                    "HTTP actions require AuthorizeAttribute or AllowAnonymousAttribute on the controller/action"));
            }
        }
    }

    private static void AnalyzeInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var containingMethods = GetOperationOwners(invocation, context.ContainingSymbol);
        if (containingMethods.Count == 0)
        {
            return;
        }

        foreach (var containingMethod in containingMethods)
        {
            CollectInvocationFacts(invocation, containingMethod, state);
        }

        AnalyzeDatabaseInvocation(invocation, context, state);
        AnalyzePluginRuntimeInvocation(invocation, context, state);
        AnalyzeToolSafetyMetadata(invocation, context);
        AnalyzeRepositoryInvocation(invocation, context);
        AnalyzeRoleAuthorizationInvocation(invocation, context, state);
        AnalyzeInvariantServiceRegistration(invocation, context);
    }

    private static void CollectInvocationFacts(
        IInvocationOperation invocation,
        IMethodSymbol containingMethod,
        CompilationState state)
    {
        var caller = Normalize(containingMethod);
        var invokedTarget = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var target = Normalize(invokedTarget);
        var isDirectIdentityDecrease = IsIdentityDecreaseInvocation(invocation);
        state.AddObservedInvocation(caller, invocation);
        state.AddMethod(caller);
        state.GetFacts(caller).AddInvocation(
            target,
            invocation.Syntax,
            isDirectIdentityDecrease || state.HasReferencedIdentityEffect(target),
            IsCompletionObserved(invocation) ||
            isDirectIdentityDecrease && target.ReturnsVoid);

        foreach (var argument in invocation.Arguments.Where(argument =>
                     !argument.IsImplicit &&
                     argument.Parameter is not null &&
                     IsDelegateType(argument.Parameter.Type)))
        {
            state.AddDelegateParameterDefinition(argument.Parameter!, argument.Value);
        }

        if (InvocationUsesTrustedCloudReadType(invocation))
        {
            state.GetFacts(caller).UsesTrustedCloudReadType = true;
        }

        if (target.MethodKind == MethodKind.DelegateInvoke && invocation.Instance is not null)
        {
            state.AddDeferredDelegateCall(caller, invocation.Instance, invocation, isTransactionScope: false);
        }

        if (IsTransactionalBoundary(target))
        {
            foreach (var argument in invocation.Arguments)
            {
                if (!argument.IsImplicit &&
                    IsDelegateType(argument.Parameter?.Type ?? argument.Value.Type))
                {
                    state.AddDeferredDelegateCall(caller, argument.Value, invocation, isTransactionScope: true);
                }
            }

            if (!IsCompletionObserved(invocation))
            {
                state.GetFacts(caller).HasUnobservedTransactionBoundary = true;
            }
        }
        else
        {
            // Task.Run, custom callback APIs and stored delegates are ordinary call-graph edges.
            // Treating only direct delegate.Invoke as an edge leaves the most common hidden roots.
            foreach (var argument in invocation.Arguments.Where(argument =>
                         !argument.IsImplicit &&
                         IsDelegateType(argument.Parameter?.Type ?? argument.Value.Type)))
            {
                state.AddDeferredDelegateCall(caller, argument.Value, invocation, isTransactionScope: false);
            }
        }

        var sideEffect = GetForbiddenSideEffect(invokedTarget) ??
                         GetInvocationSideEffect(invocation, caller, state) ??
                         state.GetReferencedCloudSideEffect(target);
        if (sideEffect is not null && !IsAllowedCloudReadOnlyOperationalWrite(invokedTarget))
        {
            state.GetFacts(caller).ForbiddenSideEffect = sideEffect;
        }
    }

    private static void AnalyzeImplicitMethodCall(
        OperationAnalysisContext context,
        CompilationState state)
    {
        var containingMethods = GetOperationOwners(context.Operation, context.ContainingSymbol);
        if (containingMethods.Count == 0)
        {
            return;
        }

        foreach (var target in GetImplicitCallTargets(context.Operation))
        {
            foreach (var containingMethod in containingMethods)
            {
                CollectImplicitMethodCallFacts(context.Operation, target, containingMethod, state);
            }
            AnalyzeDatabaseTarget(
                target,
                context.Operation.Syntax.GetLocation(),
                context.ContainingSymbol,
                state);
        }
    }

    private static IEnumerable<IMethodSymbol> GetImplicitCallTargets(IOperation operation)
    {
        switch (operation)
        {
            case IPropertyReferenceOperation property:
                var isWrite = property.Parent switch
                {
                    ISimpleAssignmentOperation assignment => ReferenceEquals(assignment.Target, property),
                    ICompoundAssignmentOperation compound => ReferenceEquals(compound.Target, property),
                    IIncrementOrDecrementOperation increment => ReferenceEquals(increment.Target, property),
                    _ => false
                };
                var isRead = property.Parent is not ISimpleAssignmentOperation || !isWrite;
                if (isRead && property.Property.GetMethod is not null)
                {
                    yield return property.Property.GetMethod;
                }

                if (isWrite && property.Property.SetMethod is not null)
                {
                    yield return property.Property.SetMethod;
                }

                yield break;
            case IEventAssignmentOperation eventAssignment:
                if (eventAssignment.EventReference is not IEventReferenceOperation eventReference)
                {
                    yield break;
                }

                var accessor = eventAssignment.Adds
                    ? eventReference.Event.AddMethod
                    : eventReference.Event.RemoveMethod;
                if (accessor is not null)
                {
                    yield return accessor;
                }

                yield break;
            case IConversionOperation conversion when conversion.OperatorMethod is not null:
                yield return conversion.OperatorMethod;
                yield break;
            case IBinaryOperation binary when binary.OperatorMethod is not null:
                yield return binary.OperatorMethod;
                yield break;
            case IUnaryOperation unary when unary.OperatorMethod is not null:
                yield return unary.OperatorMethod;
                yield break;
            case ICompoundAssignmentOperation compound when compound.OperatorMethod is not null:
                yield return compound.OperatorMethod;
                yield break;
            case IIncrementOrDecrementOperation increment when increment.OperatorMethod is not null:
                yield return increment.OperatorMethod;
                yield break;
        }
    }

    private static void CollectImplicitMethodCallFacts(
        IOperation operation,
        IMethodSymbol? target,
        IMethodSymbol? containingMethod,
        CompilationState state)
    {
        if (target is null || containingMethod is null || operation.Syntax is AttributeSyntax)
        {
            return;
        }

        var anonymousOwner = FindAnonymousOwner(operation);
        var caller = Normalize(anonymousOwner?.Symbol ?? containingMethod);
        target = Normalize(target);
        state.AddMethod(caller);
        state.GetFacts(caller).AddInvocation(
            target,
            operation.Syntax,
            state.HasReferencedIdentityEffect(target),
            completionObserved: true);

        if (IsTrustedCloudReadType(operation.Type) ||
            IsTrustedCloudReadType(target.ContainingType) ||
            IsTrustedCloudReadType(target.ReturnType) ||
            target.Parameters.Any(parameter => IsTrustedCloudReadType(parameter.Type)))
        {
            state.GetFacts(caller).UsesTrustedCloudReadType = true;
        }

        var sideEffect = GetForbiddenSideEffect(target) ?? state.GetReferencedCloudSideEffect(target);
        if (sideEffect is not null && !IsAllowedCloudReadOnlyOperationalWrite(target))
        {
            state.GetFacts(caller).ForbiddenSideEffect = sideEffect;
        }
    }

    private static void AnalyzeDynamicInvocation(OperationAnalysisContext context, CompilationState state)
    {
        var operation = context.Operation;
        var containingMethods = GetOperationOwners(operation, context.ContainingSymbol);
        if (containingMethods.Count == 0)
        {
            return;
        }

        foreach (var containingMethod in containingMethods)
        {
            CollectDynamicOperationFacts(operation, containingMethod, state);
        }
        AnalyzeDynamicDatabaseOperation(operation, context, state);
    }

    private static void CollectDynamicOperationFacts(
        IOperation operation,
        IMethodSymbol containingMethod,
        CompilationState state)
    {
        var caller = Normalize(containingMethod);
        state.AddMethod(caller);
        var facts = state.GetFacts(caller);
        facts.HasDynamicInvocation = true;
        facts.ForbiddenSideEffect ??= "dynamic invocation cannot be proven read-only";
    }

    private static void AnalyzeInvariantImplementation(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (!IsProductionAssembly(state.AssemblyName) || type.TypeKind != TypeKind.Class || type.IsAbstract)
        {
            return;
        }

        var typeName = type.ToDisplayString();
        if (ImplementsFullyQualified(type, "AICopilot.Services.Contracts.ITransactionalExecutionService")
            && typeName != "AICopilot.EntityFrameworkCore.Transactions.IdentityTransactionalExecutionService")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EnabledAdminInvariantRule,
                FirstSourceLocation(type),
                typeName,
                "ITransactionalExecutionService has exactly one approved production implementation"));
        }

        if (ImplementsFullyQualified(type, "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard")
            && typeName != "AICopilot.EntityFrameworkCore.Locking.PostgresIdentityEnabledAdminInvariantGuard")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EnabledAdminInvariantRule,
                FirstSourceLocation(type),
                typeName,
                "IIdentityEnabledAdminInvariantGuard has exactly one approved production implementation"));
        }
    }

    private static void AnalyzeInvariantServiceRegistration(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (target.TypeArguments.Length < 2 ||
            target.Name is not ("AddScoped" or "AddSingleton" or "AddTransient"))
        {
            return;
        }

        var serviceName = target.TypeArguments[0].ToDisplayString();
        var implementationName = target.TypeArguments[1].ToDisplayString();
        if (!HasExpectedTypeIdentity(target.TypeArguments[0] as INamedTypeSymbol, serviceName))
        {
            return;
        }

        var expected = serviceName switch
        {
            "AICopilot.Services.Contracts.ITransactionalExecutionService" =>
                "AICopilot.EntityFrameworkCore.Transactions.IdentityTransactionalExecutionService",
            "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard" =>
                "AICopilot.EntityFrameworkCore.Locking.PostgresIdentityEnabledAdminInvariantGuard",
            _ => null
        };
        if (expected is null ||
            (target.Name == "AddScoped" &&
             string.Equals(implementationName, expected, StringComparison.Ordinal) &&
             HasExpectedTypeIdentity(target.TypeArguments[1] as INamedTypeSymbol, implementationName)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EnabledAdminInvariantRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? "dependency injection",
            $"{serviceName} must have the exact final scoped binding '{expected}'"));
    }

    private static void AnalyzeIdentityPropertyAssignment(
        ISimpleAssignmentOperation assignment,
        OperationAnalysisContext context,
        CompilationState state)
    {
        foreach (var containingMethod in GetOperationOwners(assignment, context.ContainingSymbol))
        {
            CollectIdentityPropertyAssignment(assignment, containingMethod, state);
        }
    }

    private static void CollectIdentityPropertyAssignment(
        ISimpleAssignmentOperation assignment,
        IMethodSymbol containingMethod,
        CompilationState state)
    {
        if (IsIdentityRelationStateDeletion(assignment))
        {
            var relationOwner = Normalize(FindAnonymousOwner(assignment)?.Symbol ?? containingMethod);
            state.AddMethod(relationOwner);
            state.GetFacts(relationOwner).AddIdentityMutation(assignment.Syntax, completionObserved: true);
            return;
        }

        if (assignment.Target is not IPropertyReferenceOperation property ||
            property.Property.Name is not ("LockoutEnabled" or "LockoutEnd") ||
            !DerivesFromDefinition(
                property.Property.ContainingType,
                "Microsoft.AspNetCore.Identity",
                "IdentityUser"))
        {
            return;
        }

        var isReduction = property.Property.Name switch
        {
            "LockoutEnabled" => assignment.Value.ConstantValue is not { HasValue: true, Value: false },
            "LockoutEnd" => assignment.Value.ConstantValue is not { HasValue: true, Value: null },
            _ => false
        };
        if (!isReduction)
        {
            return;
        }

        if (containingMethod.Name == "MarkUserDisabled" &&
            containingMethod.ContainingType.ToDisplayString() ==
            "AICopilot.IdentityService.Authorization.IdentityGovernanceHelper")
        {
            // The formal helper is represented as an exact identity mutation at each call site.
            return;
        }

        var anonymousOwner = FindAnonymousOwner(assignment);
        var owner = Normalize(anonymousOwner?.Symbol ?? containingMethod);
        state.AddMethod(owner);
        state.GetFacts(owner).AddIdentityMutation(assignment.Syntax, completionObserved: true);
    }

    private static bool IsIdentityRelationStateDeletion(ISimpleAssignmentOperation assignment)
    {
        if (assignment.Target is not IPropertyReferenceOperation
            {
                Property.Name: "State",
                Instance.Type: INamedTypeSymbol entryType
            } ||
            !IsDefinition(
                entryType.OriginalDefinition,
                "Microsoft.EntityFrameworkCore.ChangeTracking",
                "EntityEntry") ||
            !entryType.TypeArguments.Any(IsIdentityRoleRelationType))
        {
            return false;
        }

        var value = UnwrapConversion(assignment.Value);
        return value is IFieldReferenceOperation
        {
            Field.Name: "Deleted",
            Field.ContainingType: INamedTypeSymbol entityStateType
        } && HasExpectedTypeIdentity(entityStateType, "Microsoft.EntityFrameworkCore.EntityState");
    }

    private static void AnalyzeRoleAuthorizationSymbol(
        ISymbol symbol,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        if (!IsProductionAssembly(state.AssemblyName))
        {
            return;
        }

        var roleAttribute = symbol.GetAttributes().FirstOrDefault(attribute =>
            HasExpectedTypeIdentity(attribute.AttributeClass, AuthorizeAttributeName) &&
            attribute.NamedArguments.Any(argument =>
                argument.Key == "Roles" &&
                argument.Value.Value is string roles &&
                !string.IsNullOrWhiteSpace(roles)));
        if (roleAttribute is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            roleAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? FirstSourceLocation(symbol),
            symbol.ToDisplayString(),
            "JWT role claims are not an authorization authority; use permission metadata"));
    }

    private static void AnalyzeRoleAuthorizationInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (!IsProductionAssembly(state.AssemblyName) ||
            target.Name != "RequireRole" ||
            target.ContainingNamespace?.ToDisplayString() != "Microsoft.AspNetCore.Authorization" ||
            target.ContainingAssembly?.Name != "Microsoft.AspNetCore.Authorization")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            "RequireRole is forbidden; authorization must use permission metadata"));
    }

    private static void AnalyzeRoleAuthorizationObjectCreation(
        IObjectCreationOperation objectCreation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (!IsProductionAssembly(state.AssemblyName) ||
            objectCreation.Syntax is AttributeSyntax ||
            !HasExpectedTypeIdentity(objectCreation.Type as INamedTypeSymbol, AuthorizeAttributeName) ||
            objectCreation.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>()
                .Any(assignment => assignment.Target is IPropertyReferenceOperation property &&
                    property.Property.Name == "Roles") != true)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            objectCreation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            "AuthorizeAttribute.Roles is forbidden; authorization must use permission metadata"));
    }

    private static void AnalyzeCloudReadObjectCreation(
        IObjectCreationOperation objectCreation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (!IsTrustedCloudReadType(objectCreation.Type))
        {
            return;
        }

        foreach (var containingMethod in GetOperationOwners(objectCreation, context.ContainingSymbol))
        {
            var owner = Normalize(containingMethod);
            state.AddMethod(owner);
            state.GetFacts(owner).UsesTrustedCloudReadType = true;
        }
    }

    private static void AnalyzeRoleClaimReference(
        IFieldReferenceOperation reference,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (!IsProductionAssembly(state.AssemblyName) ||
            reference.Field.ToDisplayString() != "System.Security.Claims.ClaimTypes.Role")
        {
            return;
        }

        var owner = context.ContainingSymbol?.ContainingType?.ToDisplayString();
        if (owner is "AICopilot.HttpApi.Infrastructure.CurrentUser" or
            "AICopilot.Infrastructure.Authentication.JwtTokenGenerator")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            reference.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            "ClaimTypes.Role is limited to token issuance and audit display"));
    }

    private static void AnalyzeAnonymousFunction(
        IAnonymousFunctionOperation anonymousFunction,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = Normalize(anonymousFunction.Symbol);
        state.AddMethod(target);

        if (context.ContainingSymbol is not IMethodSymbol containingMethod)
        {
            return;
        }

        var caller = Normalize(containingMethod);
        state.AddMethod(caller);
    }

    private static void AnalyzeDelegateMemberDefinition(
        OperationAnalysisContext context,
        CompilationState state)
    {
        switch (context.Operation)
        {
            case IFieldInitializerOperation fieldInitializer:
                foreach (var field in fieldInitializer.InitializedFields)
                {
                    state.AddDelegateMemberDefinition(field, fieldInitializer.Value);
                }

                break;
            case IPropertyInitializerOperation propertyInitializer:
                foreach (var initializedProperty in propertyInitializer.InitializedProperties)
                {
                    state.AddDelegateMemberDefinition(initializedProperty, propertyInitializer.Value);
                }

                break;
            case ISimpleAssignmentOperation assignment:
                AnalyzeIdentityPropertyAssignment(assignment, context, state);
                AddDelegateMemberDefinition(assignment.Target, assignment.Value, state);

                break;
            case ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Add
            } compoundAssignment:
                AddDelegateMemberDefinition(compoundAssignment.Target, compoundAssignment.Value, state);

                break;
            case IReturnOperation { ReturnedValue: not null } @return
                when context.ContainingSymbol is IMethodSymbol method:
                if (method.AssociatedSymbol is IPropertySymbol returnedProperty)
                {
                    state.AddDelegateMemberDefinition(returnedProperty, @return.ReturnedValue);
                }

                if (IsDelegateType(method.ReturnType))
                {
                    state.AddDelegateReturnDefinition(method, @return.ReturnedValue);
                }

                break;
        }
    }

    private static void AddDelegateMemberDefinition(
        IOperation target,
        IOperation value,
        CompilationState state)
    {
        switch (target)
        {
            case IFieldReferenceOperation field:
                state.AddDelegateMemberDefinition(field.Field, value);
                break;
            case IPropertyReferenceOperation property:
                state.AddDelegateMemberDefinition(property.Property, value);
                break;
        }
    }

    private static void AnalyzeDatabaseInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        AnalyzeDatabaseTarget(
            target,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol,
            state,
            context.ReportDiagnostic);
    }

    private static void AnalyzeDatabaseTarget(
        IMethodSymbol? target,
        Location location,
        ISymbol? containingSymbol,
        CompilationState state,
        Action<Diagnostic>? reportDiagnostic = null)
    {
        if (target is null || !IsDatabaseApi(target))
        {
            return;
        }

        if (IsDatabaseOwner(state.AssemblyName, containingSymbol?.ContainingType))
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            PersistenceOwnerRule,
            location,
            containingSymbol?.ToDisplayString() ?? state.AssemblyName,
            target.ToDisplayString());
        if (reportDiagnostic is not null)
        {
            reportDiagnostic(diagnostic);
        }
        else
        {
            state.AddDeferredDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeDynamicDatabaseOperation(
        IOperation operation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (IsDatabaseOwner(state.AssemblyName, context.ContainingSymbol?.ContainingType) ||
            operation is not IDynamicInvocationOperation dynamicInvocation ||
            dynamicInvocation.Operation is not IDynamicMemberReferenceOperation member ||
            !DynamicDatabaseMemberNames.Contains(member.MemberName) ||
            !IsProvablyDatabaseOperation(member.Instance))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PersistenceOwnerRule,
            operation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            $"dynamic database member '{member.MemberName}'"));
    }

    private static void AnalyzePluginRuntimeInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var isAssemblyScan = HasExpectedTypeIdentity(target.ContainingType, "System.Reflection.Assembly") &&
                             target.Name is "GetTypes" or "GetExportedTypes";
        var isDiActivation = HasExpectedTypeIdentity(
                                 target.ContainingType,
                                 "Microsoft.Extensions.DependencyInjection.ActivatorUtilities") &&
                             target.Name is "CreateInstance" or "GetServiceOrCreateInstance";
        if ((!isAssemblyScan && !isDiActivation) || state.AssemblyName == "AICopilot.AgentPlugin.Runtime")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            AgentPluginBoundaryRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            "assembly scanning and DI plugin activation belong only to AICopilot.AgentPlugin.Runtime"));
    }

    private static void AnalyzePluginRuntimePropertyReference(
        IPropertyReferenceOperation property,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (state.AssemblyName == "AICopilot.AgentPlugin.Runtime" ||
            property.Property.Name != "DefinedTypes" ||
            !HasExpectedTypeIdentity(property.Property.ContainingType, "System.Reflection.Assembly"))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            AgentPluginBoundaryRule,
            property.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            "assembly scanning and DI plugin activation belong only to AICopilot.AgentPlugin.Runtime"));
    }

    private static void AnalyzeToolSafetyMetadata(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (target.Name != "Create" ||
            !HasExpectedTypeIdentity(target.ContainingType, ToolSafetyDescriptorName))
        {
            return;
        }

        if (HasExpectedTypeIdentity(context.ContainingSymbol?.ContainingType, ToolSafetyPolicyName))
        {
            return;
        }

        var externalSystem = GetEnumArgumentName(invocation, "externalSystemType");
        if (externalSystem is not null &&
            !string.Equals(externalSystem, "CloudReadOnly", StringComparison.Ordinal))
        {
            return;
        }

        var readOnly = GetBooleanArgument(invocation, "readOnlyDeclared");
        var capability = GetEnumArgumentName(invocation, "capabilityKind");
        if (string.Equals(externalSystem, "CloudReadOnly", StringComparison.Ordinal) &&
            readOnly == true &&
            string.Equals(capability, "ReadOnlyQuery", StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? target.ToDisplayString(),
            externalSystem is null
                ? "dynamic tool metadata must use AiToolSafetyPolicy runtime evaluation because CloudReadOnly cannot be excluded statically"
                : "CloudReadOnly tools require the exact CloudReadOnly + ReadOnlyQuery + readOnlyDeclared=true tuple"));
    }

    private static void AnalyzeRepositoryInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        foreach (var typeArgument in invocation.TargetMethod.TypeArguments)
        {
            if (typeArgument is not INamedTypeSymbol repository ||
                !IsRepositoryType(repository) ||
                repository.TypeArguments.Length != 1)
            {
                continue;
            }

            var entity = repository.TypeArguments[0];
            if (entity is ITypeParameterSymbol ||
                ImplementsFullyQualified(entity, "AICopilot.SharedKernel.Domain.IAggregateRoot"))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                AggregateBoundaryRule,
                invocation.Syntax.GetLocation(),
                repository.ToDisplayString(),
                $"repository entity '{entity.ToDisplayString()}' is not an aggregate root"));
        }
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context, CompilationState state)
    {
        ResolveDeferredDelegateCalls(state);
        ResolveDeferredDapperCalls(state);
        AddStaticInitializerEdges(state);
        foreach (var diagnostic in state.GetDeferredDiagnosticsSnapshot())
        {
            context.ReportDiagnostic(diagnostic);
        }

        AnalyzeOwnEffectSummary(context, state);
        AnalyzeProjectReferences(context, state);
        AnalyzeReferencedEffectClosure(context, state);
        AnalyzeCallGraphs(context, state);
    }

    private static void AnalyzeReferencedEffectClosure(
        CompilationAnalysisContext context,
        CompilationState state)
    {
        var summaries = state.GetReferencedSummariesSnapshot()
            .Concat(BuildCrossProjectMethodEffects(state))
            .ToArray();
        if (summaries.Length == 0)
        {
            return;
        }

        var nodes = new Dictionary<string, ReferencedSummaryNode>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<ReferencedSummaryEdge>>(StringComparer.Ordinal);
        foreach (var summary in summaries)
        {
            var sourceKey = GetMethodEffectKey(summary.ContractAssembly, summary.MethodId);
            if ((summary.Flags & (CrossProjectNormalCallEdge | CrossProjectGuardedIdentityCallEdge)) != 0)
            {
                if (!TryParseMethodEffectKey(summary.Detail, out var targetAssembly, out var targetMethodId))
                {
                    continue;
                }

                if (!edges.TryGetValue(sourceKey, out var outgoing))
                {
                    outgoing = [];
                    edges.Add(sourceKey, outgoing);
                }

                outgoing.Add(new ReferencedSummaryEdge(
                    GetMethodEffectKey(targetAssembly, targetMethodId),
                    (summary.Flags & CrossProjectGuardedIdentityCallEdge) != 0,
                    summary.ProducerAssembly));
                continue;
            }

            if (!nodes.TryGetValue(sourceKey, out var node))
            {
                node = new ReferencedSummaryNode(summary.ContractAssembly, summary.MethodId);
                nodes.Add(sourceKey, node);
            }

            node.Flags |= summary.Flags;
            if ((summary.Flags & CrossProjectCloudRoot) != 0)
            {
                node.CloudRootProducers.Add(summary.ProducerAssembly);
            }

            if ((summary.Flags & CrossProjectIdentityRoot) != 0)
            {
                node.IdentityRootProducers.Add(summary.ProducerAssembly);
            }

            if ((summary.Flags & CrossProjectIdentityEffect) != 0)
            {
                node.IdentityEffectProducers.Add(summary.ProducerAssembly);
            }

            if ((summary.Flags & CrossProjectCloudSideEffect) != 0 &&
                (node.CloudSideEffect is null ||
                 string.CompareOrdinal(summary.Detail, node.CloudSideEffect) < 0))
            {
                node.CloudSideEffect = summary.Detail;
            }
            if ((summary.Flags & CrossProjectCloudSideEffect) != 0)
            {
                node.CloudEffectProducers.Add(summary.ProducerAssembly);
            }
        }

        foreach (var rootEntry in nodes.Where(item =>
                     (item.Value.Flags & CrossProjectCloudRoot) != 0))
        {
            var rootKey = rootEntry.Key;
            var root = rootEntry.Value;
            var sideEffect = FindReferencedSummaryEffect(
                rootKey,
                root.CloudRootProducers,
                nodes,
                edges,
                state.AssemblyName,
                cloudEffect: true);
            if (sideEffect is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CloudReadOnlyBoundaryRule,
                    FirstCompilationLocation(state.Compilation),
                    $"{root.ContractAssembly}:{root.MethodId}",
                    sideEffect));
            }
        }

        foreach (var rootEntry in nodes.Where(item =>
                     (item.Value.Flags & CrossProjectIdentityRoot) != 0))
        {
            var rootKey = rootEntry.Key;
            var root = rootEntry.Value;
            var reachesIdentityEffect = FindReferencedSummaryEffect(
                rootKey,
                root.IdentityRootProducers,
                nodes,
                edges,
                state.AssemblyName,
                cloudEffect: false) is not null;
            if (reachesIdentityEffect)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EnabledAdminInvariantRule,
                    FirstCompilationLocation(state.Compilation),
                    $"{root.ContractAssembly}:{root.MethodId}",
                    "a referenced project path reaches an enabled Admin reduction outside a proven caller transaction/guard"));
            }
        }
    }

    private static string? FindReferencedSummaryEffect(
        string root,
        IReadOnlyCollection<string> rootProducers,
        IReadOnlyDictionary<string, ReferencedSummaryNode> nodes,
        IReadOnlyDictionary<string, List<ReferencedSummaryEdge>> edges,
        string currentAssembly,
        bool cloudEffect)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<ReferencedTraversalState>();
        if (rootProducers.Count == 0)
        {
            pending.Push(new ReferencedTraversalState(root, null, null));
        }
        else
        {
            foreach (var producer in rootProducers)
            {
                pending.Push(new ReferencedTraversalState(root, null, null).AddProducer(
                    producer,
                    currentAssembly));
            }
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var visitKey = current.GetVisitKey();
            if (!visited.Add(visitKey))
            {
                continue;
            }

            if (nodes.TryGetValue(current.MethodKey, out var node))
            {
                var effectProducers = cloudEffect
                    ? node.CloudEffectProducers
                    : node.IdentityEffectProducers;
                foreach (var producer in effectProducers)
                {
                    if (current.AddProducer(producer, currentAssembly).HasTwoReferencedProducers)
                    {
                        return cloudEffect ? node.CloudSideEffect ?? "referenced side effect" : "identity effect";
                    }
                }
            }

            if (!edges.TryGetValue(current.MethodKey, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (cloudEffect || !edge.IsGuardedIdentityTransaction)
                {
                    var next = current.AddProducer(edge.ProducerAssembly, currentAssembly);
                    pending.Push(new ReferencedTraversalState(
                        edge.TargetKey,
                        next.FirstProducer,
                        next.SecondProducer));
                }
            }
        }

        return null;
    }

    private static void AnalyzeOwnEffectSummary(
        CompilationAnalysisContext context,
        CompilationState state)
    {
        if (!IsProductionAssembly(state.AssemblyName))
        {
            return;
        }

        if (!TryReadEffectSummary(state.Compilation.Assembly, out var actual, out var error))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ProjectBoundaryRule,
                FirstCompilationLocation(state.Compilation),
                state.AssemblyName,
                "<generated-effect-summary>",
                $"the versioned architecture effect summary is missing or invalid: {error}"));
            return;
        }

        var expected = BuildCrossProjectMethodEffects(state);
        if (!actual.SequenceEqual(expected, CrossProjectMethodEffectComparer.Instance))
        {
            var missing = expected
                .Except(actual, CrossProjectMethodEffectComparer.Instance)
                .Select(DescribeCrossProjectMethodEffect)
                .FirstOrDefault() ?? "<none>";
            var unexpected = actual
                .Except(expected, CrossProjectMethodEffectComparer.Instance)
                .Select(DescribeCrossProjectMethodEffect)
                .FirstOrDefault() ?? "<none>";
            context.ReportDiagnostic(Diagnostic.Create(
                ProjectBoundaryRule,
                FirstCompilationLocation(state.Compilation),
                state.AssemblyName,
                "<generated-effect-summary>",
                $"the generated architecture effect summary does not match the Analyzer call graph: missing={missing}; unexpected={unexpected}"));
        }
    }

    private static string DescribeCrossProjectMethodEffect(CrossProjectMethodEffect effect) =>
        $"{effect.ProducerAssembly}|{effect.ContractAssembly}|{effect.MethodId}|{effect.Flags}|{effect.Detail}";

    private static void ResolveDeferredDelegateCalls(CompilationState state)
    {
        foreach (var call in state.GetDeferredDelegateCallsSnapshot())
        {
            var targets = GetDelegateTargets(call.DelegateOperation, call.Invocation, state).ToArray();
            if (targets.Length == 0)
            {
                var callerFacts = state.GetFacts(call.Caller);
                if (state.TryGetReferencedDelegateReturnEffects(
                        call.DelegateOperation,
                        out var referencedEffects) &&
                    (referencedEffects.DelegateReturnIsResolved ||
                     referencedEffects.DelegateReturnHasIdentityEffect ||
                     referencedEffects.DelegateReturnCloudSideEffect is not null))
                {
                    if (referencedEffects.DelegateReturnHasIdentityEffect)
                    {
                        callerFacts.AddIdentityMutation(
                            call.Invocation.Syntax,
                            IsCompletionObserved(call.Invocation));
                    }

                    callerFacts.ForbiddenSideEffect ??=
                        referencedEffects.DelegateReturnCloudSideEffect;
                }
                else
                {
                    callerFacts.UnresolvedDelegateInvocationDetail ??=
                        $"delegate invocation target '{call.DelegateOperation.Syntax}' in '{call.Caller.ToDisplayString()}' cannot be resolved statically";
                }
            }

            foreach (var target in targets)
            {
                state.AddMethod(target);
                var callerFacts = state.GetFacts(call.Caller);
                var sideEffect = GetForbiddenSideEffect(target) ??
                                 (IsDapperWriteExecution(target) ? target.ToDisplayString() : null);
                if (sideEffect is not null && !IsAllowedCloudReadOnlyOperationalWrite(target))
                {
                    callerFacts.ForbiddenSideEffect = sideEffect;
                }

                if (call.IsTransactionScope)
                {
                    callerFacts.AddTransactionDelegateCall(target);
                    state.GetFacts(target).IsTransactionScope = true;
                }
                else
                {
                    callerFacts.AddInvocation(
                        target,
                        call.Invocation.Syntax,
                        state.HasReferencedIdentityEffect(target),
                        IsCompletionObserved(call.Invocation));
                }

                AnalyzeDatabaseTarget(
                    target,
                    call.Invocation.Syntax.GetLocation(),
                    call.Caller,
                    state);
            }
        }
    }

    private static void ResolveDeferredDapperCalls(CompilationState state)
    {
        foreach (var call in state.GetDeferredDapperCallsSnapshot())
        {
            if (IsProvablyNonDataDapperExecution(call.Invocation, call.Caller, state))
            {
                continue;
            }

            var target = call.Invocation.TargetMethod.ReducedFrom ?? call.Invocation.TargetMethod;
            state.GetFacts(call.Caller).ForbiddenSideEffect ??= target.ToDisplayString();
        }
    }

    private static void AddStaticInitializerEdges(CompilationState state)
    {
        var methods = state.GetMethodsSnapshot();
        foreach (var group in methods.GroupBy(
                     method => method.ContainingType,
                     SymbolEqualityComparer.Default))
        {
            var staticConstructor = group.FirstOrDefault(method =>
                method.MethodKind == MethodKind.StaticConstructor);
            if (staticConstructor is null)
            {
                continue;
            }

            foreach (var method in group.Where(method =>
                         method.MethodKind != MethodKind.StaticConstructor))
            {
                state.GetFacts(method).AddCall(staticConstructor);
            }
        }
    }

    private static void AnalyzeProjectReferences(CompilationAnalysisContext context, CompilationState state)
    {
        foreach (var error in state.GetReferencedSummaryErrorsSnapshot())
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ProjectBoundaryRule,
                FirstCompilationLocation(state.Compilation),
                state.AssemblyName,
                "<referenced-effect-summary>",
                error));
        }

        if (IsProductionAssembly(state.AssemblyName) && GetLayer(state.AssemblyName) == Layer.Unknown)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ProjectBoundaryRule,
                FirstCompilationLocation(state.Compilation),
                state.AssemblyName,
                "<unclassified>",
                "AICopilot production projects must have an explicit AIARCH001 layer classification"));
            return;
        }

        var projectReferences = context.Options.AdditionalFiles
            .Select(file => Path.GetFileNameWithoutExtension(file.Path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !name.EndsWith(".Analyzers", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (projectReferences.Length == 0)
        {
            projectReferences = state.Compilation.ReferencedAssemblyNames
                .Select(reference => reference.Name)
                .Where(name => ContainsTestAssemblyMarker(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        foreach (var reference in projectReferences)
        {
            var reason = GetProjectReferenceViolation(state.AssemblyName, reference);
            if (reason is null)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ProjectBoundaryRule,
                FirstCompilationLocation(state.Compilation),
                state.AssemblyName,
                reference,
                reason));
        }
    }

    private static void AnalyzeCallGraphs(CompilationAnalysisContext context, CompilationState state)
    {
        var sourceMethods = state.GetMethodsSnapshot()
            .Where(IsUserSourceMethod)
            .ToArray();
        var methodsWithIncomingEdges = GetMethodsWithIncomingEdges(sourceMethods, state);

        foreach (var method in sourceMethods.Where(method =>
                     IsIdentityAnalysisRoot(method, methodsWithIncomingEdges)))
        {
            var reachable = Traverse(method, state, sourceMethods);
            var hasDynamicIdentityPath = reachable.Any(candidate =>
                IsIdentityContextMethod(candidate) &&
                (state.GetFacts(candidate).HasDynamicInvocation ||
                 state.GetFacts(candidate).UnresolvedDelegateInvocationDetail is not null &&
                 !IsApprovedIdentityTransactionImplementation(candidate)));
            var hasIdentityDecrease = hasDynamicIdentityPath || reachable.Any(candidate =>
                state.GetFacts(candidate).GetIdentityMutationsSnapshot().Count != 0);
            if (hasIdentityDecrease)
            {
                var transactionScopes = reachable
                    .Where(candidate => state.GetFacts(candidate).IsTransactionScope)
                    .Where(candidate => HasReachableIdentityDecrease(candidate, state, sourceMethods))
                    .ToArray();
                var hasMutationOutsideTransaction = HasIdentityDecreaseOutsideTransaction(
                    method,
                    state,
                    sourceMethods,
                    new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
                var hasInvalidTransactionScope = transactionScopes.Any(scope =>
                    !HasOnlyGuardedIdentityDecrease(
                        scope,
                        inheritedGuard: false,
                        state,
                        sourceMethods,
                        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
                var hasUnobservedTransactionBoundary = reachable.Any(candidate =>
                    state.GetFacts(candidate).HasUnobservedTransactionBoundary);
                if (hasMutationOutsideTransaction ||
                    transactionScopes.Length == 0 ||
                    hasInvalidTransactionScope ||
                    hasUnobservedTransactionBoundary ||
                    hasDynamicIdentityPath)
                {
                    var reason = hasDynamicIdentityPath
                        ? "dynamic invocation cannot prove the identity invariant"
                        : hasUnobservedTransactionBoundary
                            ? "the transaction task is not awaited or returned"
                        : hasMutationOutsideTransaction
                        ? "a reduction is reachable outside the transaction delegate"
                        : transactionScopes.Length == 0
                            ? "no transaction delegate owns the reduction"
                            : "the invariant guard does not dominate every reduction inside the transaction delegate";
                    context.ReportDiagnostic(Diagnostic.Create(
                        EnabledAdminInvariantRule,
                        FirstSourceLocation(method),
                        method.ToDisplayString(),
                        reason));
                }
            }

        }

        foreach (var method in sourceMethods.Where(candidate =>
                     IsCloudReadOnlyOperation(candidate, state.GetFacts(candidate)) &&
                     (IsExternallyReachable(candidate) ||
                      !methodsWithIncomingEdges.Contains(Normalize(candidate)))))
        {
            var reachable = Traverse(method, state, sourceMethods);
            var sideEffect = reachable
                .Select(candidate => state.GetFacts(candidate).ForbiddenSideEffect ??
                    state.GetFacts(candidate).UnresolvedDelegateInvocationDetail)
                .FirstOrDefault(value => value is not null);
            if (sideEffect is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CloudReadOnlyBoundaryRule,
                    FirstSourceLocation(method),
                    method.ToDisplayString(),
                    sideEffect));
            }
        }
    }

    private static ISet<IMethodSymbol> GetMethodsWithIncomingEdges(
        IReadOnlyCollection<IMethodSymbol> sourceMethods,
        CompilationState state)
    {
        var incoming = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var method in sourceMethods)
        {
            foreach (var call in state.GetFacts(method).GetAllCallsSnapshot())
            {
                foreach (var target in ResolveDispatchTargets(call, sourceMethods).Where(IsUserSourceMethod))
                {
                    incoming.Add(Normalize(target));
                }
            }
        }

        return incoming;
    }

    private static bool IsIdentityAnalysisRoot(
        IMethodSymbol method,
        ISet<IMethodSymbol> methodsWithIncomingEdges) =>
        method.MethodKind == MethodKind.Ordinary &&
        (IsExternallyReachable(method) || !methodsWithIncomingEdges.Contains(Normalize(method)));

    private static IReadOnlyCollection<IMethodSymbol> Traverse(
        IMethodSymbol root,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods)
    {
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>();
        pending.Push(Normalize(root));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var call in state.GetFacts(current).GetAllCallsSnapshot())
            {
                foreach (var target in ResolveDispatchTargets(call, sourceMethods))
                {
                    if (!visited.Contains(target))
                    {
                        pending.Push(target);
                    }
                }
            }
        }

        return visited;
    }

    private static bool HasReachableIdentityDecrease(
        IMethodSymbol root,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods) =>
        Traverse(root, state, sourceMethods).Any(method =>
            state.GetFacts(method).GetIdentityMutationsSnapshot().Count != 0 ||
            state.GetFacts(method).HasDynamicInvocation ||
            state.GetFacts(method).UnresolvedDelegateInvocationDetail is not null);

    private static bool HasIdentityDecreaseOutsideTransaction(
        IMethodSymbol method,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods,
        ISet<IMethodSymbol> visiting)
    {
        method = Normalize(method);
        if (!visiting.Add(method))
        {
            return false;
        }

        try
        {
            if (state.GetFacts(method).GetIdentityMutationsSnapshot().Count != 0 ||
                IsIdentityContextMethod(method) &&
                (state.GetFacts(method).HasDynamicInvocation ||
                 state.GetFacts(method).UnresolvedDelegateInvocationDetail is not null &&
                 !IsApprovedIdentityTransactionImplementation(method)))
            {
                return true;
            }

            return state.GetFacts(method).GetCallsSnapshot()
                .SelectMany(call => ResolveDispatchTargets(call, sourceMethods))
                .Where(IsUserSourceMethod)
                .Any(target => HasIdentityDecreaseOutsideTransaction(
                    target,
                    state,
                    sourceMethods,
                    visiting));
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    private static bool HasOnlyGuardedIdentityDecrease(
        IMethodSymbol method,
        bool inheritedGuard,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods,
        ISet<IMethodSymbol> visiting)
    {
        method = Normalize(method);
        if (!visiting.Add(method))
        {
            return false;
        }

        try
        {
            var facts = state.GetFacts(method);
            var invocations = facts.GetInvocationsSnapshot()
                .OrderBy(invocation => invocation.Syntax.SpanStart)
                .ToArray();

            foreach (var mutation in facts.GetIdentityMutationsSnapshot())
            {
                var guarded = inheritedGuard || invocations.Any(candidate =>
                    IsEnabledAdminInvariantAcquire(candidate.Target) &&
                    candidate.CompletionObserved &&
                    Dominates(candidate.Syntax, mutation.Syntax));
                if (!guarded || !mutation.CompletionObserved)
                {
                    return false;
                }
            }

            foreach (var invocation in invocations)
            {
                var guarded = inheritedGuard || invocations.Any(candidate =>
                    IsEnabledAdminInvariantAcquire(candidate.Target) &&
                    candidate.CompletionObserved &&
                    Dominates(candidate.Syntax, invocation.Syntax));

                foreach (var target in ResolveDispatchTargets(invocation.Target, sourceMethods)
                             .Where(IsUserSourceMethod)
                             .Where(target => HasReachableIdentityDecrease(target, state, sourceMethods)))
                {
                    if (!invocation.CompletionObserved ||
                        !HasOnlyGuardedIdentityDecrease(
                            target,
                            guarded,
                            state,
                            sourceMethods,
                            visiting))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            visiting.Remove(method);
        }
    }

    private static bool Dominates(SyntaxNode guardSyntax, SyntaxNode guardedSyntax)
    {
        var guardStatement = guardSyntax.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (guardStatement?.Parent is null)
        {
            return false;
        }

        return guardedSyntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .Any(statement =>
                ReferenceEquals(statement.Parent, guardStatement.Parent) &&
                guardStatement.Span.End <= statement.SpanStart);
    }

    private static IEnumerable<IMethodSymbol> GetDelegateTargets(
        IOperation delegateOperation,
        IOperation resolutionPoint,
        CompilationState state)
    {
        var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        CollectDelegateTargets(
            delegateOperation,
            resolutionPoint,
            state,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            targets);
        return targets;
    }

    private static void CollectDelegateTargets(
        IOperation operation,
        IOperation resolutionPoint,
        CompilationState state,
        ISet<ILocalSymbol> resolvingLocals,
        ISet<ISymbol> resolvingMembers,
        ISet<IMethodSymbol> targets)
    {
        switch (operation)
        {
            case IAnonymousFunctionOperation anonymousFunction:
                targets.Add(Normalize(anonymousFunction.Symbol));
                return;
            case IMethodReferenceOperation methodReference:
                targets.Add(Normalize(methodReference.Method));
                return;
            case ILocalReferenceOperation localReference:
                CollectLocalDelegateTargets(
                    localReference.Local,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IFieldReferenceOperation fieldReference:
                CollectMemberDelegateTargets(
                    fieldReference.Field,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IPropertyReferenceOperation propertyReference:
                CollectMemberDelegateTargets(
                    propertyReference.Property,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IParameterReferenceOperation parameterReference:
                CollectParameterDelegateTargets(
                    parameterReference.Parameter,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IConditionalAccessInstanceOperation:
                for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
                {
                    if (parent is not IConditionalAccessOperation conditionalAccess)
                    {
                        continue;
                    }

                    CollectDelegateTargets(
                        conditionalAccess.Operation,
                        resolutionPoint,
                        state,
                        resolvingLocals,
                        resolvingMembers,
                        targets);
                    return;
                }

                return;
            case IInvocationOperation invocation when IsDelegateType(invocation.TargetMethod.ReturnType):
                CollectDelegateFactoryTargets(
                    invocation.TargetMethod,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectDelegateTargets(
                child,
                resolutionPoint,
                state,
                resolvingLocals,
                resolvingMembers,
                targets);
        }
    }

    private static void CollectLocalDelegateTargets(
        ILocalSymbol local,
        IOperation resolutionPoint,
        CompilationState state,
        ISet<ILocalSymbol> resolvingLocals,
        ISet<ISymbol> resolvingMembers,
        ISet<IMethodSymbol> targets)
    {
        if (!resolvingLocals.Add(local))
        {
            return;
        }

        try
        {
            var root = resolutionPoint;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            var definitions = root.DescendantsAndSelf()
                .Where(candidate => candidate.Syntax.SpanStart < resolutionPoint.Syntax.SpanStart)
                .Select(candidate => candidate switch
                {
                    IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) =>
                        declarator.Initializer?.Value,
                    ISimpleAssignmentOperation assignment
                        when assignment.Target is ILocalReferenceOperation target &&
                             SymbolEqualityComparer.Default.Equals(target.Local, local) =>
                        assignment.Value,
                    ICompoundAssignmentOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add
                    } assignment
                        when assignment.Target is ILocalReferenceOperation target &&
                             SymbolEqualityComparer.Default.Equals(target.Local, local) =>
                        assignment.Value,
                    _ => null
                })
                .Where(value => value is not null)
                .Cast<IOperation>()
                .ToArray();

            foreach (var definition in definitions)
            {
                CollectDelegateTargets(
                    definition,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
            }
        }
        finally
        {
            resolvingLocals.Remove(local);
        }
    }

    private static void CollectMemberDelegateTargets(
        ISymbol member,
        IOperation resolutionPoint,
        CompilationState state,
        ISet<ILocalSymbol> resolvingLocals,
        ISet<ISymbol> resolvingMembers,
        ISet<IMethodSymbol> targets)
    {
        if (!resolvingMembers.Add(member))
        {
            return;
        }

        try
        {
            foreach (var operation in state.GetDelegateMemberDefinitionsSnapshot(member))
            {
                CollectDelegateTargets(
                    operation,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
            }
        }
        finally
        {
            resolvingMembers.Remove(member);
        }
    }

    private static void CollectParameterDelegateTargets(
        IParameterSymbol parameter,
        IOperation resolutionPoint,
        CompilationState state,
        ISet<ILocalSymbol> resolvingLocals,
        ISet<ISymbol> resolvingMembers,
        ISet<IMethodSymbol> targets)
    {
        if (!resolvingMembers.Add(parameter))
        {
            return;
        }

        try
        {
            foreach (var operation in state.GetDelegateParameterDefinitionsSnapshot(parameter))
            {
                CollectDelegateTargets(
                    operation,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
            }
        }
        finally
        {
            resolvingMembers.Remove(parameter);
        }
    }

    private static void CollectDelegateFactoryTargets(
        IMethodSymbol method,
        IOperation resolutionPoint,
        CompilationState state,
        ISet<ILocalSymbol> resolvingLocals,
        ISet<ISymbol> resolvingMembers,
        ISet<IMethodSymbol> targets)
    {
        method = Normalize(method);
        if (!resolvingMembers.Add(method))
        {
            return;
        }

        try
        {
            foreach (var operation in state.GetDelegateReturnDefinitionsSnapshot(method))
            {
                CollectDelegateTargets(
                    operation,
                    resolutionPoint,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
            }
        }
        finally
        {
            resolvingMembers.Remove(method);
        }
    }

    private static IAnonymousFunctionOperation? FindAnonymousOwner(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation anonymousFunction)
            {
                return anonymousFunction;
            }
        }

        return null;
    }

    private static IReadOnlyCollection<IMethodSymbol> GetOperationOwners(
        IOperation operation,
        ISymbol? containingSymbol)
    {
        var anonymousOwner = FindAnonymousOwner(operation);
        if (anonymousOwner is not null)
        {
            return [Normalize(anonymousOwner.Symbol)];
        }

        if (containingSymbol is IMethodSymbol method)
        {
            return [Normalize(method)];
        }

        var isStatic = containingSymbol switch
        {
            IFieldSymbol field => field.IsStatic,
            IPropertySymbol property => property.IsStatic,
            IEventSymbol @event => @event.IsStatic,
            _ => false
        };
        var containingType = containingSymbol as INamedTypeSymbol ?? containingSymbol?.ContainingType;
        if (containingType is null)
        {
            return [];
        }

        var constructors = isStatic
            ? containingType.StaticConstructors
            : containingType.InstanceConstructors;
        return constructors.Select(Normalize).ToArray();
    }

    private static IEnumerable<IMethodSymbol> ResolveDispatchTargets(
        IMethodSymbol target,
        IReadOnlyCollection<IMethodSymbol> sourceMethods)
    {
        var normalized = Normalize(target);
        yield return normalized;

        foreach (var candidate in sourceMethods)
        {
            if (normalized.ContainingType?.TypeKind == TypeKind.Interface &&
                candidate.ContainingType?.FindImplementationForInterfaceMember(normalized) is IMethodSymbol implementation &&
                SymbolEqualityComparer.Default.Equals(Normalize(implementation), Normalize(candidate)))
            {
                yield return Normalize(candidate);
                continue;
            }

            for (var overridden = candidate.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(Normalize(overridden), normalized))
                {
                    yield return Normalize(candidate);
                    break;
                }
            }
        }
    }

    private static string? GetProjectReferenceViolation(string source, string target)
    {
        if (target.StartsWith("IIoT.CloudPlatform", StringComparison.Ordinal) ||
            target.StartsWith("IIoT.Cloud", StringComparison.Ordinal))
        {
            return "AICopilot production code may consume Cloud only through the approved AI read-only contract, never through Cloud implementation projects";
        }

        if (ContainsTestAssemblyMarker(target))
        {
            return "production projects may not reference Tests, Testing, TestKit, Fakes, or Mocks assemblies";
        }

        var sourceLayer = GetLayer(source);
        var targetLayer = GetLayer(target);
        if (source == "AICopilot.AgentPlugin" && target == "AICopilot.AgentPlugin.Runtime")
        {
            return "plugin abstractions may not depend on the runtime loader";
        }

        if (targetLayer == Layer.Unknown && IsProductionAssembly(target))
        {
            return "references to unclassified AICopilot production projects are forbidden until AIARCH001 assigns an explicit layer";
        }

        if (sourceLayer == Layer.Unknown || targetLayer == Layer.Unknown || sourceLayer == Layer.Host)
        {
            return null;
        }

        if (sourceLayer == Layer.Shared && targetLayer != Layer.Shared)
        {
            return "shared projects may reference shared projects only";
        }

        if (sourceLayer == Layer.Core && targetLayer != Layer.Shared)
        {
            return "core projects may reference shared projects only";
        }

        if (sourceLayer == Layer.Service && (targetLayer == Layer.Infrastructure || targetLayer == Layer.Host))
        {
            return "service projects may not reference infrastructure or hosts";
        }

        if (sourceLayer == Layer.Infrastructure && targetLayer == Layer.Host)
        {
            return "infrastructure projects may not reference hosts";
        }

        var ownedCore = GetOwnedCore(source);
        if (sourceLayer == Layer.Service && targetLayer == Layer.Core &&
            ownedCore is not null && !string.Equals(target, ownedCore, StringComparison.Ordinal))
        {
            return $"service bounded context owns only '{ownedCore}' and must collaborate through Services.Contracts";
        }

        if (source == "AICopilot.IdentityService" && targetLayer == Layer.Core)
        {
            return "IdentityService has no owned Core project";
        }

        return null;
    }

    private static string? GetOwnedCore(string assemblyName) => assemblyName switch
    {
        "AICopilot.AiGatewayService" => "AICopilot.Core.AiGateway",
        "AICopilot.DataAnalysisService" => "AICopilot.Core.DataAnalysis",
        "AICopilot.McpService" => "AICopilot.Core.McpServer",
        "AICopilot.RagService" => "AICopilot.Core.Rag",
        _ => null
    };

    private static Layer GetLayer(string assemblyName)
    {
        if (IsHostAssembly(assemblyName))
        {
            return Layer.Host;
        }

        if (assemblyName is "AICopilot.Core.AiGateway" or "AICopilot.Core.DataAnalysis" or
            "AICopilot.Core.McpServer" or "AICopilot.Core.Rag")
        {
            return Layer.Core;
        }

        if (assemblyName is "AICopilot.AiGatewayService" or "AICopilot.DataAnalysisService" or
            "AICopilot.IdentityService" or "AICopilot.McpService" or "AICopilot.RagService" or
            "AICopilot.Services.Contracts" or "AICopilot.Services.CrossCutting")
        {
            return Layer.Service;
        }

        if (assemblyName is "AICopilot.AiRuntime" or "AICopilot.Dapper" or
            "AICopilot.Embedding" or "AICopilot.EntityFrameworkCore" or
            "AICopilot.EventBus" or "AICopilot.Infrastructure")
        {
            return Layer.Infrastructure;
        }

        if (assemblyName is "AICopilot.AgentPlugin" or "AICopilot.AgentPlugin.Runtime" or
            "AICopilot.SharedKernel" or "AICopilot.Visualization")
        {
            return Layer.Shared;
        }

        return Layer.Unknown;
    }

    private static bool IsHostAssembly(string assemblyName) =>
        assemblyName is "AICopilot.AppHost" or "AICopilot.DataWorker" or "AICopilot.HttpApi" or
            "AICopilot.MigrationWorkApp" or "AICopilot.RagWorker" or "AICopilot.ServiceDefaults";

    private static bool IsHttpApiAssembly(string assemblyName) => assemblyName == "AICopilot.HttpApi";

    private static bool IsDatabaseOwner(string assemblyName, INamedTypeSymbol? containingType) =>
        IsEfOwner(assemblyName) ||
        assemblyName == "AICopilot.Dapper" ||
        assemblyName == "AICopilot.MigrationWorkApp" ||
        (assemblyName == "AICopilot.Infrastructure" &&
         IsNestedWithin(
             containingType,
             "AICopilot.Infrastructure.AiGateway.PostgreSqlSessionExecutionLock"));

    private static bool IsEfOwner(string assemblyName) => assemblyName == "AICopilot.EntityFrameworkCore";

    private static bool IsNestedWithin(INamedTypeSymbol? type, string fullyQualifiedOwnerName)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.ToDisplayString() == fullyQualifiedOwnerName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDatabaseApi(IMethodSymbol method)
    {
        var namespaceName = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var assemblyName = method.ContainingAssembly?.Name;
        if ((namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) &&
             string.Equals(assemblyName, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal)) ||
            (namespaceName.StartsWith("Npgsql", StringComparison.Ordinal) &&
             string.Equals(assemblyName, "Npgsql", StringComparison.Ordinal)) ||
            (namespaceName == "Dapper" &&
             string.Equals(assemblyName, "Dapper", StringComparison.Ordinal)))
        {
            return true;
        }

        return IsDatabaseType(method.ContainingType);
    }

    private static bool IsDatabaseType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (DerivesFromFullyQualified(named, "Microsoft.EntityFrameworkCore.DbContext") ||
            DerivesFromFullyQualified(named, "System.Data.Common.DbConnection") ||
            DerivesFromFullyQualified(named, "System.Data.Common.DbCommand") ||
            ImplementsFullyQualified(named, "System.Data.IDbConnection") ||
            ImplementsFullyQualified(named, "System.Data.IDbCommand"))
        {
            return true;
        }

        var namespaceName = named.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return namespaceName.StartsWith("Npgsql", StringComparison.Ordinal) &&
               string.Equals(named.ContainingAssembly?.Name, "Npgsql", StringComparison.Ordinal);
    }

    private static bool IsProvablyDatabaseOperation(IOperation? operation)
    {
        if (operation is null)
        {
            return false;
        }

        if (IsDatabaseType(operation.Type))
        {
            return true;
        }

        return operation switch
        {
            IConversionOperation conversion => IsProvablyDatabaseOperation(conversion.Operand),
            IParenthesizedOperation parenthesized => IsProvablyDatabaseOperation(parenthesized.Operand),
            IDynamicMemberReferenceOperation member => IsProvablyDatabaseOperation(member.Instance),
            ILocalReferenceOperation local => IsDatabaseType(local.Local.Type),
            IFieldReferenceOperation field => IsDatabaseType(field.Field.Type),
            IPropertyReferenceOperation property => IsDatabaseType(property.Property.Type),
            IParameterReferenceOperation parameter => IsDatabaseType(parameter.Parameter.Type),
            IObjectCreationOperation creation => IsDatabaseType(creation.Type),
            IInvocationOperation invocation => IsDatabaseType(invocation.TargetMethod.ReturnType),
            _ => operation.ChildOperations.Any(IsProvablyDatabaseOperation)
        };
    }

    private static bool IsIdentityDecreaseCall(IMethodSymbol method)
    {
        if (!IdentityDecreaseMethodNames.Contains(method.Name))
        {
            return false;
        }

        if (method.Name == "MarkUserDisabled")
        {
            return method.ContainingType.ToDisplayString() ==
                   "AICopilot.IdentityService.Authorization.IdentityGovernanceHelper";
        }

        return DerivesFromDefinition(
            method.ContainingType,
            "Microsoft.AspNetCore.Identity",
            "UserManager");
    }

    private static bool IsIdentityDecreaseInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name == "SetLockoutEnabledAsync" && IsIdentityDecreaseCall(method))
        {
            var enabled = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "enabled" or "lockoutEnabled");
            return enabled?.Value.ConstantValue is not { HasValue: true, Value: false };
        }

        if (method.Name == "SetLockoutEndDateAsync" && IsIdentityDecreaseCall(method))
        {
            var end = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "lockoutEnd" or "lockoutEndDate");
            return end?.Value.ConstantValue is not { HasValue: true, Value: null };
        }

        if (IsIdentityDecreaseCall(method))
        {
            return true;
        }

        if (method.Name is not (
                "Remove" or
                "RemoveAsync" or
                "RemoveRange" or
                "RemoveAll" or
                "RemoveAt" or
                "Clear" or
                "Delete" or
                "DeleteAsync" or
                "ExecuteDelete" or
                "ExecuteDeleteAsync"))
        {
            return false;
        }

        return IsIdentityRoleRelationType(method.ContainingType) ||
               invocation.Arguments.Any(argument => IsIdentityRoleRelationType(argument.Value.Type));
    }

    private static bool IsIdentityRoleRelationType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        return IsDefinition(
                   named.OriginalDefinition,
                   "Microsoft.AspNetCore.Identity",
                   "IdentityUserRole") ||
               named.TypeArguments.Any(IsIdentityRoleRelationType);
    }

    private static bool IsIdentityContextMethod(IMethodSymbol method)
    {
        static bool IsIdentityType(ITypeSymbol? type) =>
            DerivesFromDefinition(type, "Microsoft.AspNetCore.Identity", "UserManager") ||
            DerivesFromDefinition(type, "Microsoft.AspNetCore.Identity", "RoleManager") ||
            ImplementsFullyQualified(type, "AICopilot.Services.Contracts.ITransactionalExecutionService") ||
            ImplementsFullyQualified(type, "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard") ||
            HasExpectedTypeIdentity(
                type as INamedTypeSymbol,
                "AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy");

        if (method.Parameters.Any(parameter => IsIdentityType(parameter.Type)) ||
            IsIdentityType(method.ReturnType))
        {
            return true;
        }

        var type = method.ContainingType;
        return type is not null &&
               (type.InstanceConstructors.Any(constructor =>
                    constructor.Parameters.Any(parameter => IsIdentityType(parameter.Type))) ||
                type.GetMembers().Any(member => member switch
                {
                    IFieldSymbol field => IsIdentityType(field.Type),
                    IPropertySymbol property => IsIdentityType(property.Type),
                    _ => false
                }));
    }

    private static bool IsApprovedIdentityTransactionImplementation(IMethodSymbol method) =>
        string.Equals(
            method.ContainingAssembly?.Name,
            "AICopilot.EntityFrameworkCore",
            StringComparison.Ordinal) &&
        string.Equals(
            method.ContainingType?.ToDisplayString(),
            "AICopilot.EntityFrameworkCore.Transactions.IdentityTransactionalExecutionService",
            StringComparison.Ordinal);

    private static bool IsDelegateType(ITypeSymbol? type) =>
        type?.TypeKind == TypeKind.Delegate;

    private static bool IsCompletionObserved(IOperation operation)
    {
        IOperation current = operation;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
        {
            current = current.Parent;
        }

        return current.Parent is IAwaitOperation or IReturnOperation;
    }

    private static bool IsTransactionalBoundary(IMethodSymbol method) =>
        (method.Name == "ExecuteAsync" || method.Name == "ExecuteResultAsync") &&
        ImplementsFullyQualified(
            method.ContainingType,
            "AICopilot.Services.Contracts.ITransactionalExecutionService");

    private static bool IsEnabledAdminInvariantAcquire(IMethodSymbol method) =>
        method.Name == "AcquireAsync" &&
        (HasExpectedTypeIdentity(
             method.ContainingType,
             "AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy") ||
         ImplementsFullyQualified(
             method.ContainingType,
             "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard"));

    private static string? GetForbiddenSideEffect(IMethodSymbol method)
    {
        if (IsEntityFrameworkSaveChanges(method) ||
            IsAdoNonQueryExecution(method) ||
            IsEntityFrameworkRawWrite(method) ||
            IsEntityFrameworkBulkWrite(method) ||
            IsFormalMcpToolExecution(method))
        {
            return method.ToDisplayString();
        }

        if (IsRepositoryType(method.ContainingType) &&
            method.Name is "Add" or "AddAsync" or "Update" or "UpdateAsync" or "Delete" or "DeleteAsync" or "Remove" or "RemoveAsync")
        {
            return method.ToDisplayString();
        }

        if (method.Name == "Send" && method.Parameters.Length > 0 &&
            ImplementsDefinition(
                method.Parameters[0].Type,
                "AICopilot.SharedKernel.Messaging",
                "ICommand"))
        {
            return method.ToDisplayString();
        }

        return null;
    }

    private static string? GetInvocationSideEffect(
        IInvocationOperation invocation,
        IMethodSymbol caller,
        CompilationState state)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (IsDapperWriteExecution(method))
        {
            state.AddDeferredDapperCall(caller, invocation);
            return null;
        }

        if (method.Name == "Send" && invocation.Arguments.Length > 0 &&
            ImplementsDefinition(
                invocation.Arguments[0].Value.Type,
                "AICopilot.SharedKernel.Messaging",
                "ICommand"))
        {
            return method.ToDisplayString();
        }

        var isHttpMessageInvoker = DerivesFromFullyQualified(
            method.ContainingType,
            "System.Net.Http.HttpMessageInvoker");
        var isJsonWriteExtension = HasExpectedTypeIdentity(
                                       method.ContainingType,
                                       "System.Net.Http.Json.HttpClientJsonExtensions") &&
                                   method.Name is
                                       "PostAsJsonAsync" or
                                       "PutAsJsonAsync" or
                                       "PatchAsJsonAsync" or
                                       "DeleteFromJsonAsync";
        if (isHttpMessageInvoker || isJsonWriteExtension)
        {
            var isWriteVerb = method.Name is "PostAsync" or "PutAsync" or "PatchAsync" or "DeleteAsync";
            var isSend = method.Name is "Send" or "SendAsync";
            if (isWriteVerb || isSend || isJsonWriteExtension)
            {
                var isFormalGetTransport = caller.ContainingType?.ToDisplayString() ==
                                           "AICopilot.Infrastructure.CloudRead.CloudAiReadHttpTransport" &&
                                           isSend &&
                                           IsProvablyGetRequest(invocation);
                if (!isFormalGetTransport)
                {
                    return method.ToDisplayString();
                }
            }
        }

        return null;
    }

    private static bool IsProvablyGetRequest(IInvocationOperation invocation)
    {
        var request = invocation.Arguments.FirstOrDefault(argument =>
            HasExpectedTypeIdentity(
                argument.Parameter?.Type as INamedTypeSymbol,
                "System.Net.Http.HttpRequestMessage"))?.Value;
        return request is not null && IsProvablyGetRequestValue(
            request,
            invocation,
            invocation,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsProvablyGetRequestValue(
        IOperation operation,
        IInvocationOperation invocation,
        IOperation referencePoint,
        ISet<ILocalSymbol> resolvingLocals)
    {
        operation = UnwrapConversion(operation) ?? operation;
        if (operation is IObjectCreationOperation creation &&
            HasExpectedTypeIdentity(
                creation.Type as INamedTypeSymbol,
                "System.Net.Http.HttpRequestMessage"))
        {
            var methodArgument = creation.Arguments.FirstOrDefault(argument =>
                HasExpectedTypeIdentity(
                    argument.Parameter?.Type as INamedTypeSymbol,
                    "System.Net.Http.HttpMethod"))?.Value;
            return IsHttpGetValue(methodArgument) ||
                   creation.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>().Any(assignment =>
                       assignment.Target is IPropertyReferenceOperation property &&
                       property.Property.Name == "Method" &&
                       IsHttpGetValue(assignment.Value)) == true;
        }

        if (operation is not ILocalReferenceOperation localReference ||
            !resolvingLocals.Add(localReference.Local))
        {
            return false;
        }

        try
        {
            var root = (IOperation)invocation;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            if (HasRequestLocalAliasBefore(root, localReference.Local, referencePoint))
            {
                return false;
            }

            var stateEvents = new List<RequestStateEvent>();
            foreach (var candidate in root.DescendantsAndSelf()
                         .Where(candidate => candidate.Syntax.SpanStart < referencePoint.Syntax.SpanStart))
            {
                if (candidate is IVariableDeclaratorOperation declarator &&
                    SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local) &&
                    declarator.Initializer?.Value is { } initializer)
                {
                    stateEvents.Add(new RequestStateEvent(declarator, initializer, isMethodAssignment: false));
                    continue;
                }

                if (candidate is not ISimpleAssignmentOperation assignment)
                {
                    continue;
                }

                if (assignment.Target is ILocalReferenceOperation target &&
                    SymbolEqualityComparer.Default.Equals(target.Local, localReference.Local))
                {
                    stateEvents.Add(new RequestStateEvent(assignment, assignment.Value, isMethodAssignment: false));
                    continue;
                }

                if (assignment.Target is IPropertyReferenceOperation property &&
                    property.Property.Name == "Method" &&
                    property.Instance is ILocalReferenceOperation instance &&
                    SymbolEqualityComparer.Default.Equals(instance.Local, localReference.Local))
                {
                    stateEvents.Add(new RequestStateEvent(assignment, assignment.Value, isMethodAssignment: true));
                }
            }

            var guaranteedEvents = stateEvents
                .Where(item => IsGuaranteedBefore(item.Operation, referencePoint))
                .OrderBy(item => item.Operation.Syntax.SpanStart)
                .ToArray();
            if (guaranteedEvents.Length == 0)
            {
                return false;
            }

            var last = guaranteedEvents[guaranteedEvents.Length - 1];
            if (stateEvents.Any(item =>
                    item.Operation.Syntax.SpanStart > last.Operation.Syntax.SpanStart &&
                    !IsGuaranteedBefore(item.Operation, referencePoint)))
            {
                return false;
            }

            return last.IsMethodAssignment
                ? IsHttpGetValue(last.Value)
                : IsProvablyGetRequestValue(
                    last.Value,
                    invocation,
                    last.Operation,
                    resolvingLocals);
        }
        finally
        {
            resolvingLocals.Remove(localReference.Local);
        }
    }

    private static bool HasRequestLocalAliasBefore(
        IOperation root,
        ILocalSymbol requestLocal,
        IOperation referencePoint)
    {
        foreach (var candidate in root.DescendantsAndSelf()
                     .Where(candidate => candidate.Syntax.SpanStart < referencePoint.Syntax.SpanStart))
        {
            if (candidate is IVariableDeclaratorOperation
                {
                    Symbol: var declaredLocal,
                    Initializer.Value: { } initializer
                } &&
                UnwrapConversion(initializer) is ILocalReferenceOperation initializerLocal &&
                !SymbolEqualityComparer.Default.Equals(declaredLocal, initializerLocal.Local) &&
                (SymbolEqualityComparer.Default.Equals(declaredLocal, requestLocal) ||
                 SymbolEqualityComparer.Default.Equals(initializerLocal.Local, requestLocal)))
            {
                return true;
            }

            if (candidate is ISimpleAssignmentOperation
                {
                    Target: ILocalReferenceOperation targetLocal
                } assignment &&
                UnwrapConversion(assignment.Value) is ILocalReferenceOperation valueLocal &&
                !SymbolEqualityComparer.Default.Equals(targetLocal.Local, valueLocal.Local) &&
                (SymbolEqualityComparer.Default.Equals(targetLocal.Local, requestLocal) ||
                 SymbolEqualityComparer.Default.Equals(valueLocal.Local, requestLocal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGuaranteedBefore(IOperation operation, IOperation referencePoint)
    {
        var operationBlock = operation.Syntax.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        var referenceBlock = referencePoint.Syntax.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault();
        if (operationBlock is null || referenceBlock is null)
        {
            return false;
        }

        if (operationBlock == referenceBlock)
        {
            return operation.Syntax.SpanStart < referencePoint.Syntax.SpanStart;
        }

        if (!operationBlock.Span.Contains(referencePoint.Syntax.Span))
        {
            return false;
        }

        var containingStatement = referencePoint.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(statement => statement.Parent == operationBlock);
        return containingStatement is not null &&
               operation.Syntax.SpanStart < containingStatement.SpanStart;
    }

    private sealed class RequestStateEvent
    {
        public RequestStateEvent(IOperation operation, IOperation value, bool isMethodAssignment)
        {
            Operation = operation;
            Value = value;
            IsMethodAssignment = isMethodAssignment;
        }

        public IOperation Operation { get; }

        public IOperation Value { get; }

        public bool IsMethodAssignment { get; }
    }

    private static bool IsHttpGetValue(IOperation? operation)
    {
        operation = UnwrapConversion(operation);
        return operation is IPropertyReferenceOperation property &&
               property.Property.Name == "Get" &&
               HasExpectedTypeIdentity(
                   property.Property.ContainingType,
                   "System.Net.Http.HttpMethod");
    }

    private static bool IsDapperWriteExecution(IMethodSymbol method) =>
        HasExpectedTypeIdentity(method.ContainingType, "Dapper.SqlMapper") &&
        method.Name is "Execute" or "ExecuteAsync";

    private static bool IsEntityFrameworkSaveChanges(IMethodSymbol method) =>
        method.Name is "SaveChanges" or "SaveChangesAsync" &&
        DerivesFromFullyQualified(method.ContainingType, "Microsoft.EntityFrameworkCore.DbContext");

    private static bool IsAdoNonQueryExecution(IMethodSymbol method) =>
        method.Name is "ExecuteNonQuery" or "ExecuteNonQueryAsync" &&
        (DerivesFromFullyQualified(method.ContainingType, "System.Data.Common.DbCommand") ||
         ImplementsFullyQualified(method.ContainingType, "System.Data.IDbCommand"));

    private static bool IsEntityFrameworkRawWrite(IMethodSymbol method) =>
        method.Name is "ExecuteSqlRaw" or "ExecuteSqlRawAsync" or
            "ExecuteSqlInterpolated" or "ExecuteSqlInterpolatedAsync" &&
        string.Equals(
            method.ContainingAssembly?.Name,
            "Microsoft.EntityFrameworkCore.Relational",
            StringComparison.Ordinal);

    private static bool IsProvablyNonDataDapperExecution(
        IInvocationOperation invocation,
        IMethodSymbol caller,
        CompilationState state)
    {
        var commandTextValues = invocation.Arguments
            .Where(argument =>
                !argument.IsImplicit &&
                argument.Parameter is not null &&
                (string.Equals(argument.Parameter.Name, "sql", StringComparison.Ordinal) &&
                 argument.Parameter.Type.SpecialType == SpecialType.System_String ||
                 string.Equals(argument.Parameter.Name, "command", StringComparison.Ordinal) &&
                 HasExpectedTypeIdentity(
                     argument.Parameter.Type as INamedTypeSymbol,
                     "Dapper.CommandDefinition")))
            .Select(argument => TryResolveDapperCommandTextValue(argument.Value, invocation))
            .Where(value => value is not null)
            .Cast<IOperation>()
            .ToArray();
        return commandTextValues.Length == 1 &&
               IsProvablyNonDataCommandText(commandTextValues[0], caller, state);
    }

    private static IOperation? TryResolveDapperCommandTextValue(
        IOperation? value,
        IInvocationOperation invocation)
    {
        value = UnwrapConversion(value);
        if (value is null)
        {
            return null;
        }

        if (value.ConstantValue is { HasValue: true, Value: string } ||
            value is IParameterReferenceOperation parameter &&
            parameter.Parameter.Type.SpecialType == SpecialType.System_String)
        {
            return value;
        }

        if (value is IObjectCreationOperation creation &&
            HasExpectedTypeIdentity(creation.Type as INamedTypeSymbol, "Dapper.CommandDefinition"))
        {
            var commandText = creation.Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Parameter?.Name, "commandText", StringComparison.Ordinal) &&
                argument.Parameter?.Type.SpecialType == SpecialType.System_String);
            return commandText is null
                ? null
                : TryResolveDapperCommandTextValue(commandText.Value, invocation);
        }

        if (value is not ILocalReferenceOperation local)
        {
            return null;
        }

        IOperation root = invocation;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var definitions = root.DescendantsAndSelf()
            .Where(candidate => candidate.Syntax.SpanStart < invocation.Syntax.SpanStart)
            .Select(candidate => candidate switch
            {
                IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local.Local) =>
                    declarator.Initializer?.Value,
                ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation target &&
                         SymbolEqualityComparer.Default.Equals(target.Local, local.Local) =>
                    assignment.Value,
                _ => null
            })
            .Where(definition => definition is not null)
            .Cast<IOperation>()
            .ToArray();
        return definitions.Length == 1
            ? TryResolveDapperCommandTextValue(definitions[0], invocation)
            : null;
    }

    private static bool IsProvablyNonDataCommandText(
        IOperation value,
        IMethodSymbol caller,
        CompilationState state)
    {
        value = UnwrapConversion(value) ?? value;
        if (value.ConstantValue is { HasValue: true, Value: string commandText })
        {
            return IsNonDataSessionStatement(commandText);
        }

        if (value is not IParameterReferenceOperation parameterReference ||
            caller.DeclaredAccessibility != Accessibility.Private ||
            state.HasDelegateMethodReference(caller) ||
            !SymbolEqualityComparer.Default.Equals(
                Normalize(parameterReference.Parameter.ContainingSymbol as IMethodSymbol ?? caller),
                Normalize(caller)))
        {
            return false;
        }

        var directCalls = 0;
        foreach (var observed in state.GetObservedInvocationsSnapshot())
        {
            var callTarget = observed.Invocation.TargetMethod.ReducedFrom ??
                             observed.Invocation.TargetMethod;
            if (!SymbolEqualityComparer.Default.Equals(Normalize(callTarget), Normalize(caller)))
            {
                continue;
            }

            var argument = observed.Invocation.Arguments.FirstOrDefault(candidate =>
                candidate.Parameter?.Ordinal == parameterReference.Parameter.Ordinal);
            var argumentValue = UnwrapConversion(argument?.Value);
            if (argumentValue?.ConstantValue is not { HasValue: true, Value: string text } ||
                !IsNonDataSessionStatement(text))
            {
                return false;
            }

            directCalls++;
        }

        return directCalls > 0;
    }

    private static bool IsNonDataSessionStatement(string commandText) =>
        commandText.Trim() is
            "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY" or
            "SET TRANSACTION READ ONLY" or
            "SET SESSION CHARACTERISTICS AS TRANSACTION READ WRITE";

    private static bool IsEntityFrameworkBulkWrite(IMethodSymbol method) =>
        HasExpectedTypeIdentity(
            method.ContainingType,
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions") &&
        method.Name is "ExecuteDelete" or "ExecuteDeleteAsync" or "ExecuteUpdate" or "ExecuteUpdateAsync";

    private static bool IsFormalMcpToolExecution(IMethodSymbol method)
    {
        if (method.Name != "ExecuteAsync")
        {
            return false;
        }

        var containingType = method.ContainingType.OriginalDefinition.ToDisplayString();
        return containingType is
                   "AICopilot.AiGatewayService.AgentTasks.IAgentToolExecutor" or
                   "AICopilot.AiGatewayService.AgentTasks.McpAgentToolExecutor" &&
               HasExpectedAssemblyIdentity(method.ContainingType.OriginalDefinition, containingType);
    }

    private static bool IsAuditWrite(IMethodSymbol method)
    {
        method = Normalize(method);
        if (IsFormalAuditWriteContractMethod(method))
        {
            return true;
        }

        if (!HasExpectedTypeIdentity(method.ContainingType, FormalAuditLogWriterName))
        {
            return false;
        }

        return ImplementsFormalContractMethod(
            method,
            "AICopilot.Services.Contracts.IAuditLogWriter",
            IsFormalAuditWriteContractMethod);
    }

    private static bool IsFormalAuditWriteContractMethod(IMethodSymbol method)
    {
        method = Normalize(method);
        if (!HasExpectedTypeIdentity(
                method.ContainingType,
                "AICopilot.Services.Contracts.IAuditLogWriter"))
        {
            return false;
        }

        return method.Name switch
        {
            "WriteAsync" =>
                method.Parameters.Length == 2 &&
                HasExpectedTypeIdentity(
                    method.Parameters[0].Type as INamedTypeSymbol,
                    "AICopilot.Services.Contracts.AuditLogWriteRequest") &&
                method.Parameters[1].Type.ToDisplayString() == "System.Threading.CancellationToken",
            "SaveChangesAsync" =>
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.ToDisplayString() == "System.Threading.CancellationToken",
            _ => false
        };
    }

    private static bool ImplementsFormalContractMethod(
        IMethodSymbol method,
        string contractTypeName,
        Func<IMethodSymbol, bool> isFormalContractMethod)
    {
        foreach (var contract in method.ContainingType.AllInterfaces.Where(@interface =>
                     HasExpectedTypeIdentity(@interface, contractTypeName)))
        {
            foreach (var contractMethod in contract.GetMembers().OfType<IMethodSymbol>().Where(isFormalContractMethod))
            {
                if (method.ContainingType.FindImplementationForInterfaceMember(contractMethod) is IMethodSymbol implementation &&
                    SymbolEqualityComparer.Default.Equals(Normalize(implementation), method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAllowedCloudReadOnlyOperationalWrite(IMethodSymbol method) =>
        IsAuditWrite(method) || IsModelQuotaOperationalWrite(method);

    private static bool IsModelQuotaOperationalWrite(IMethodSymbol method)
    {
        method = Normalize(method);
        if (IsFormalModelQuotaOperationalContractMethod(method))
        {
            return true;
        }

        if (!HasExpectedTypeIdentity(method.ContainingType, FormalQuotaTypes.Store))
        {
            return false;
        }

        return ImplementsFormalContractMethod(
            method,
            FormalQuotaTypes.Contract,
            IsFormalModelQuotaOperationalContractMethod);
    }

    private static bool IsFormalModelQuotaOperationalContractMethod(IMethodSymbol method)
    {
        method = Normalize(method);
        if (!HasExpectedTypeIdentity(method.ContainingType, FormalQuotaTypes.Contract))
        {
            return false;
        }

        return method.Name switch
        {
            "TryReserveAsync" => MatchesFormalQuotaOperation(
                method,
                "AICopilot.Services.Contracts.ModelQuotaReservationRequest",
                "AICopilot.Services.Contracts.ModelQuotaReservationOutcome"),
            "SettleAsync" => MatchesFormalQuotaOperation(
                method,
                "AICopilot.Services.Contracts.ModelQuotaSettlement",
                "AICopilot.Services.Contracts.ModelQuotaReservationResult"),
            "ReclaimExpiredAsync" =>
                method.Parameters.Length == 3 &&
                method.Parameters[0].Type.ToDisplayString() == "System.DateTimeOffset" &&
                method.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
                method.Parameters[2].Type.ToDisplayString() == "System.Threading.CancellationToken" &&
                method.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task<int>",
            _ => false
        };
    }

    private static bool MatchesFormalQuotaOperation(
        IMethodSymbol method,
        string requestType,
        string resultType)
    {
        return method.Parameters.Length == 2 &&
               HasExpectedTypeIdentity(method.Parameters[0].Type as INamedTypeSymbol, requestType) &&
               method.Parameters[1].Type.ToDisplayString() == "System.Threading.CancellationToken" &&
               method.ReturnType.ToDisplayString() == $"System.Threading.Tasks.Task<{resultType}>";
    }

    private static bool IsCloudReadOnlyOperation(IMethodSymbol method, MethodFacts facts)
    {
        if (facts.UsesTrustedCloudReadType ||
            IsTrustedCloudReadType(method.ContainingType) ||
            method.Parameters.Any(parameter => IsCloudReadClientType(parameter.Type)) ||
            IsCloudReadClientType(method.ReturnType) ||
            method.TypeArguments.Any(IsCloudReadClientType))
        {
            return true;
        }

        var containingType = method.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        if (containingType.AllInterfaces.Any(IsTrustedCloudReadType) ||
            containingType.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Any(parameter => IsCloudReadClientType(parameter.Type))) ||
            containingType.GetMembers().Any(member => member switch
            {
                IFieldSymbol field => IsCloudReadClientType(field.Type),
                IPropertySymbol property => IsCloudReadClientType(property.Type),
                _ => false
            }))
        {
            return true;
        }

        return IsFormalCloudReadOnlyWorkflowSymbol(method) ||
               IsFormalCloudReadOnlyWorkflowSymbol(containingType);
    }

    private static bool InvocationUsesTrustedCloudReadType(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (target.Name.StartsWith("Add", StringComparison.Ordinal) &&
            string.Equals(
                target.ContainingNamespace?.ToDisplayString(),
                "Microsoft.Extensions.DependencyInjection",
                StringComparison.Ordinal))
        {
            return false;
        }

        return IsTrustedCloudReadType(invocation.Type) ||
               IsTrustedCloudReadType(invocation.Instance?.Type) ||
               IsTrustedCloudReadType(target.ContainingType) ||
               IsTrustedCloudReadType(target.ReturnType) ||
               target.TypeArguments.Any(IsTrustedCloudReadType) ||
               target.Parameters.Any(parameter => IsTrustedCloudReadType(parameter.Type)) ||
               invocation.Arguments.Any(argument => IsTrustedCloudReadType(argument.Value.Type));
    }

    private static bool IsTrustedCloudReadType(ITypeSymbol? type) =>
        IsTrustedCloudReadType(
            type,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static bool IsTrustedCloudReadType(
        ITypeSymbol? type,
        ISet<ITypeSymbol> visited)
    {
        if (type is null || !visited.Add(type))
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsTrustedCloudReadType(array.ElementType, visited);
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(constraint =>
                IsTrustedCloudReadType(constraint, visited));
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if ((TrustedCloudReadTypeNames.Contains(named.OriginalDefinition.ToDisplayString()) ||
             FormalCloudReadOnlyWorkflowTypeNames.Contains(named.OriginalDefinition.ToDisplayString())) &&
            HasExpectedAssemblyIdentity(named.OriginalDefinition, named.OriginalDefinition.ToDisplayString()))
        {
            return true;
        }

        return named.TypeArguments.Any(argument =>
            IsTrustedCloudReadType(argument, visited));
    }

    private static bool IsCloudReadClientType(ITypeSymbol? type) =>
        IsCloudReadClientType(
            type,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static bool IsCloudReadClientType(
        ITypeSymbol? type,
        ISet<ITypeSymbol> visited)
    {
        if (type is null || !visited.Add(type))
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsCloudReadClientType(array.ElementType, visited);
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(constraint =>
                IsCloudReadClientType(constraint, visited));
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        if (definition is "AICopilot.Services.Contracts.ICloudAiReadClient" or
                "AICopilot.Services.Contracts.IBusinessTextToSqlGenerator" or
                "AICopilot.Infrastructure.CloudRead.CloudAiReadClient" &&
            HasExpectedAssemblyIdentity(named.OriginalDefinition, definition))
        {
            return true;
        }

        return named.TypeArguments.Any(argument =>
            IsCloudReadClientType(argument, visited));
    }

    private static bool IsFormalCloudReadOnlyWorkflowSymbol(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        return type is not null &&
               FormalCloudReadOnlyWorkflowTypeNames.Contains(type.OriginalDefinition.ToDisplayString()) &&
               HasExpectedAssemblyIdentity(type.OriginalDefinition, type.OriginalDefinition.ToDisplayString());
    }

    private static bool IsExternallyReachable(IMethodSymbol method) =>
        IsCrossProjectCallableMethod(method) &&
        ((method.DeclaredAccessibility == Accessibility.Public && IsExternallyVisible(method.ContainingType)) ||
         (method.ExplicitInterfaceImplementations.Length > 0 && IsExternallyVisible(method.ContainingType)));

    private static bool IsExternallyVisible(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return type is not null;
    }

    private static bool IsServiceRequest(INamedTypeSymbol type) =>
        ImplementsDefinition(type, "AICopilot.SharedKernel.Messaging", "ICommand") ||
        ImplementsDefinition(type, "AICopilot.SharedKernel.Messaging", "IQuery") ||
        ImplementsDefinition(type, "MediatR", "IStreamRequest");

    private static bool IsControllerAction(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary &&
        !method.IsStatic &&
        method.DeclaredAccessibility == Accessibility.Public &&
        method.OverriddenMethod?.ContainingType.SpecialType != SpecialType.System_Object &&
        !HasAttribute(method, "Microsoft.AspNetCore.Mvc.NonActionAttribute");

    private static bool HasAuthorizationMetadata(ISymbol symbol) =>
        HasAttribute(symbol, AuthorizeAttributeName) || HasAttribute(symbol, AllowAnonymousAttributeName);

    private static bool TryGetResourceAuthorizationOwner(INamedTypeSymbol type, out string? ownerTypeName)
    {
        var attribute = type.GetAttributes().FirstOrDefault(candidate =>
            HasExpectedTypeIdentity(
                candidate.AttributeClass,
                "AICopilot.Services.CrossCutting.Attributes.ResourceAuthorizationOwnerAttribute"));
        if (attribute is null)
        {
            ownerTypeName = null;
            return false;
        }

        ownerTypeName = attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is INamedTypeSymbol ownerType &&
                        HasExpectedAssemblyIdentity(ownerType, ownerType.ToDisplayString())
            ? ownerType.ToDisplayString()
            : null;
        return true;
    }

    private static bool HasAttribute(ISymbol symbol, string fullyQualifiedAttributeName) =>
        symbol.GetAttributes().Any(attribute =>
            HasExpectedTypeIdentity(attribute.AttributeClass, fullyQualifiedAttributeName));

    private static bool IsRepositoryType(INamedTypeSymbol type) =>
        IsRepositoryDefinition(type.OriginalDefinition) ||
        type.AllInterfaces.Any(@interface => IsRepositoryDefinition(@interface.OriginalDefinition));

    private static bool IsRepositoryDefinition(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "AICopilot.SharedKernel.Repository" &&
        type.Name is "IRepository" or "IReadRepository" &&
        string.Equals(type.ContainingAssembly?.Name, "AICopilot.SharedKernel", StringComparison.Ordinal);

    private static bool Implements(ITypeSymbol? type, string interfaceName)
    {
        if (type is null)
        {
            return false;
        }

        if (type.Name == interfaceName)
        {
            return true;
        }

        return type is INamedTypeSymbol named && named.AllInterfaces.Any(@interface => @interface.Name == interfaceName);
    }

    private static bool ImplementsFullyQualified(ITypeSymbol? type, string fullyQualifiedInterfaceName)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        return HasExpectedTypeIdentity(named, fullyQualifiedInterfaceName) ||
               named.AllInterfaces.Any(@interface =>
                   HasExpectedTypeIdentity(@interface, fullyQualifiedInterfaceName));
    }

    private static bool ImplementsDefinition(
        ITypeSymbol? type,
        string fullyQualifiedNamespace,
        string typeName)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        return IsDefinition(named, fullyQualifiedNamespace, typeName) ||
               named.AllInterfaces.Any(@interface =>
                   IsDefinition(@interface, fullyQualifiedNamespace, typeName));
    }

    private static bool IsDefinition(
        INamedTypeSymbol type,
        string fullyQualifiedNamespace,
        string typeName) =>
        type.Name == typeName &&
        type.ContainingNamespace?.ToDisplayString() == fullyQualifiedNamespace &&
        HasExpectedAssemblyIdentity(type, string.Concat(fullyQualifiedNamespace, ".", typeName));

    private static bool DerivesFrom(ITypeSymbol? type, string baseTypeName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFromFullyQualified(ITypeSymbol? type, string fullyQualifiedBaseTypeName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (HasExpectedTypeIdentity(current, fullyQualifiedBaseTypeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasExpectedTypeIdentity(
        INamedTypeSymbol? type,
        string fullyQualifiedTypeName) =>
        type is not null &&
        string.Equals(type.OriginalDefinition.ToDisplayString(), fullyQualifiedTypeName, StringComparison.Ordinal) &&
        HasExpectedAssemblyIdentity(type.OriginalDefinition, fullyQualifiedTypeName);

    private static bool HasExpectedAssemblyIdentity(
        INamedTypeSymbol type,
        string fullyQualifiedTypeName)
    {
        var expectedAssembly = GetExpectedAICopilotAssembly(fullyQualifiedTypeName) ??
                               GetExpectedExternalAssembly(fullyQualifiedTypeName);
        return expectedAssembly is null ||
               string.Equals(type.ContainingAssembly?.Name, expectedAssembly, StringComparison.Ordinal);
    }

    private static string? GetExpectedExternalAssembly(string fullyQualifiedTypeName)
    {
        if (fullyQualifiedTypeName.StartsWith("Microsoft.AspNetCore.Authorization.", StringComparison.Ordinal))
        {
            return "Microsoft.AspNetCore.Authorization";
        }

        if (fullyQualifiedTypeName is "Microsoft.AspNetCore.Mvc.ControllerBase" or
            "Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute" or
            "Microsoft.AspNetCore.Mvc.NonActionAttribute")
        {
            return "Microsoft.AspNetCore.Mvc.Core";
        }

        if (fullyQualifiedTypeName is "System.Reflection.Assembly")
        {
            return "System.Private.CoreLib";
        }

        if (fullyQualifiedTypeName is "Microsoft.Extensions.DependencyInjection.ActivatorUtilities")
        {
            return "Microsoft.Extensions.DependencyInjection.Abstractions";
        }

        if (fullyQualifiedTypeName is "Microsoft.AspNetCore.Identity.UserManager" or
            "Microsoft.AspNetCore.Identity.RoleManager")
        {
            return "Microsoft.Extensions.Identity.Core";
        }

        if (fullyQualifiedTypeName.StartsWith("Microsoft.AspNetCore.Identity.Identity", StringComparison.Ordinal))
        {
            return "Microsoft.Extensions.Identity.Stores";
        }

        if (fullyQualifiedTypeName.StartsWith("System.Net.Http.Json.", StringComparison.Ordinal))
        {
            return "System.Net.Http.Json";
        }

        if (fullyQualifiedTypeName.StartsWith("System.Net.Http.", StringComparison.Ordinal))
        {
            return "System.Net.Http";
        }

        if (fullyQualifiedTypeName is "System.ComponentModel.DescriptionAttribute")
        {
            return "System.ComponentModel.Primitives";
        }

        if (fullyQualifiedTypeName is "System.Data.IDbConnection" or "System.Data.IDbCommand" or
            "System.Data.Common.DbConnection" or "System.Data.Common.DbCommand")
        {
            return "System.Data.Common";
        }

        if (fullyQualifiedTypeName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal))
        {
            return "Microsoft.EntityFrameworkCore";
        }

        if (fullyQualifiedTypeName.StartsWith("Dapper.", StringComparison.Ordinal))
        {
            return "Dapper";
        }

        if (fullyQualifiedTypeName.StartsWith("Npgsql.", StringComparison.Ordinal))
        {
            return "Npgsql";
        }

        return null;
    }

    private static string? GetExpectedAICopilotAssembly(string fullyQualifiedTypeName)
    {
        foreach (var assemblyName in ExplicitProductionAssemblyNamesByDescendingLength)
        {
            if (fullyQualifiedTypeName.StartsWith(assemblyName + ".", StringComparison.Ordinal))
            {
                return assemblyName;
            }
        }

        return null;
    }

    private static bool DerivesFromDefinition(
        ITypeSymbol? type,
        string fullyQualifiedNamespace,
        string typeName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (IsDefinition(current, fullyQualifiedNamespace, typeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeTestDouble(string name) =>
        name.StartsWith("Mock", StringComparison.Ordinal) ||
        name.StartsWith("Fake", StringComparison.Ordinal) ||
        name.StartsWith("Stub", StringComparison.Ordinal) ||
        name.StartsWith("Test", StringComparison.Ordinal);

    private static bool IsApprovedDevelopmentMock(INamedTypeSymbol type, string assemblyName) =>
        assemblyName == "AICopilot.AiGatewayService" &&
        type.ToDisplayString() == "AICopilot.AiGatewayService.AgentTasks.MockMcpAgentToolExecutor" &&
        type.DeclaredAccessibility == Accessibility.Internal;

    private static void ReportPlugin(SymbolAnalysisContext context, INamedTypeSymbol type, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(
            AgentPluginBoundaryRule,
            FirstSourceLocation(type),
            type.ToDisplayString(),
            reason));

    private static bool IsProductionAssembly(string assemblyName) =>
        assemblyName.StartsWith("AICopilot.", StringComparison.Ordinal) &&
        assemblyName != "AICopilot.Architecture.Analyzers";

    private static bool ContainsTestAssemblyMarker(string name)
    {
        var segments = name.Split('.');
        return segments.Any(segment =>
            segment.EndsWith("Tests", StringComparison.Ordinal) ||
            segment is "Testing" or "TestKit" or "Fakes" or "Mocks");
    }

    private static void AddType(ITypeSymbol? type, ISet<ITypeSymbol> observed)
    {
        if (type is null || !observed.Add(type))
        {
            return;
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                AddType(argument, observed);
            }
        }

        if (type is IArrayTypeSymbol array)
        {
            AddType(array.ElementType, observed);
        }
    }

    private static bool? GetBooleanArgument(IInvocationOperation invocation, string parameterName)
    {
        var argument = invocation.Arguments.FirstOrDefault(item => item.Parameter?.Name == parameterName);
        return argument?.Value.ConstantValue is { HasValue: true, Value: bool value } ? value : null;
    }

    private static string? GetEnumArgumentName(IInvocationOperation invocation, string parameterName)
    {
        var argument = invocation.Arguments.FirstOrDefault(item => item.Parameter?.Name == parameterName);
        var value = UnwrapConversion(argument?.Value);
        return value is IFieldReferenceOperation field ? field.Field.Name : null;
    }

    private static IOperation? UnwrapConversion(IOperation? operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static Location FirstSourceLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource) ?? Location.None;

    private static Location FirstCompilationLocation(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.FirstOrDefault();
        return tree is null ? Location.None : tree.GetRoot().GetLocation();
    }

    private static IMethodSymbol Normalize(IMethodSymbol method)
    {
        var normalized = method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
        return normalized.PartialDefinitionPart?.OriginalDefinition ?? normalized;
    }

    private static string GetMethodEffectKey(string assemblyName, string methodId) =>
        string.Concat(assemblyName, "\u001f", methodId);

    private static bool TryParseMethodEffectKey(
        string value,
        out string assemblyName,
        out string methodId)
    {
        var separator = value.IndexOf('\u001f');
        if (separator <= 0 || separator != value.LastIndexOf('\u001f') || separator == value.Length - 1)
        {
            assemblyName = string.Empty;
            methodId = string.Empty;
            return false;
        }

        assemblyName = value.Substring(0, separator);
        methodId = value.Substring(separator + 1);
        return true;
    }

    internal static string? GenerateCrossProjectEffectSource(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var assemblyName = compilation.AssemblyName ?? string.Empty;
        if (!IsProductionAssembly(assemblyName))
        {
            return null;
        }

        var state = CollectCompilationStateForEffectSummary(compilation, cancellationToken);
        var effects = BuildCrossProjectMethodEffects(state);
        return RenderEffectSummarySource(effects);
    }

    private static IReadOnlyCollection<CrossProjectMethodEffect> BuildCrossProjectMethodEffects(
        CompilationState state)
    {
        var sourceMethods = state.GetMethodsSnapshot()
            .Where(IsUserSourceMethod)
            .ToArray();
        var methodsWithIncomingEdges = GetMethodsWithIncomingEdges(sourceMethods, state);
        var contracts = new HashSet<IMethodSymbol>(
            GetCrossProjectContracts(sourceMethods),
            SymbolEqualityComparer.Default);
        foreach (var method in sourceMethods.Where(IsExecutableSourceMethod))
        {
            if (IsCloudReadOnlyOperation(method, state.GetFacts(method)) ||
                IsIdentityAnalysisRoot(method, methodsWithIncomingEdges))
            {
                contracts.Add(Normalize(method));
            }
        }

        var effects = new List<CrossProjectMethodEffect>();

        foreach (var contract in contracts)
        {
            var contractAssembly = contract.ContainingAssembly?.Name;
            var methodId = contract.GetDocumentationCommentId();
            if (contractAssembly is null || methodId is null)
            {
                continue;
            }

            var dispatchTargets = new HashSet<IMethodSymbol>(
                    ResolveDispatchTargets(contract, sourceMethods),
                    SymbolEqualityComparer.Default)
                .Where(IsUserSourceMethod)
                .ToArray();
            var crossProjectEdges = GetCrossProjectCallEdges(contract, state, sourceMethods);
            var hasIdentityEffect = dispatchTargets.Any(target =>
                HasIdentityDecreaseOutsideTransaction(
                    target,
                    state,
                    sourceMethods,
                    new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)));
            var flags = 0;
            var detail = string.Empty;

            if (IsUserSourceMethod(contract) &&
                IsExecutableSourceMethod(contract))
            {
                if (IsCloudReadOnlyOperation(contract, state.GetFacts(contract)))
                {
                    flags |= CrossProjectCloudRoot;
                }

                if (IsIdentityAnalysisRoot(contract, methodsWithIncomingEdges) &&
                    (hasIdentityEffect || crossProjectEdges.Count != 0))
                {
                    flags |= CrossProjectIdentityRoot;
                }
            }

            if (hasIdentityEffect)
            {
                flags |= CrossProjectIdentityEffect;
                detail = "enabled Admin reduction requires the caller transaction and invariant guard";
            }

            var cloudSideEffect = dispatchTargets
                .SelectMany(target => Traverse(target, state, sourceMethods))
                .Select(target => state.GetFacts(target).ForbiddenSideEffect ??
                    state.GetFacts(target).UnresolvedDelegateInvocationDetail)
                .Where(value => value is not null)
                .Cast<string>()
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            if (cloudSideEffect is not null)
            {
                flags |= CrossProjectCloudSideEffect;
                detail = cloudSideEffect;
            }

            if (flags != 0)
            {
                if (string.IsNullOrEmpty(detail))
                {
                    detail = "analysis root";
                }

                effects.Add(new CrossProjectMethodEffect(contractAssembly, methodId, flags, detail));
            }

            if (IsDelegateType(contract.ReturnType) ||
                dispatchTargets.Any(target => IsDelegateType(target.ReturnType)))
            {
                var delegateTargets = dispatchTargets
                    .SelectMany(target => state.GetDelegateReturnDefinitionsSnapshot(target))
                    .SelectMany(definition => GetDelegateTargets(definition, definition, state))
                    .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                    .ToArray();
                effects.Add(new CrossProjectMethodEffect(
                    contractAssembly,
                    methodId,
                    CrossProjectDelegateReturnResolved,
                    "resolved delegate return"));

                if (delegateTargets.Length == 0 || delegateTargets.Any(target =>
                        HasIdentityDecreaseOutsideTransaction(
                            target,
                            state,
                            sourceMethods,
                            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)) ||
                        state.HasReferencedIdentityEffect(target)))
                {
                    effects.Add(new CrossProjectMethodEffect(
                        contractAssembly,
                        methodId,
                        CrossProjectDelegateReturnIdentityEffect,
                        delegateTargets.Length == 0
                            ? "delegate return target cannot be resolved statically"
                            : "delegate return reaches an enabled Admin reduction"));
                }

                var delegateCloudEffect = delegateTargets.Length == 0
                    ? "delegate return target cannot be resolved statically"
                    : delegateTargets
                        .SelectMany(target => IsUserSourceMethod(target)
                            ? Traverse(target, state, sourceMethods)
                            : [target])
                        .Select(target => GetForbiddenSideEffect(target) ??
                            (IsDapperWriteExecution(target) ? target.ToDisplayString() : null) ??
                            state.GetFacts(target).ForbiddenSideEffect ??
                            state.GetReferencedCloudSideEffect(target))
                        .Where(value => value is not null)
                        .Cast<string>()
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .FirstOrDefault();
                if (delegateCloudEffect is not null)
                {
                    effects.Add(new CrossProjectMethodEffect(
                        contractAssembly,
                        methodId,
                        CrossProjectDelegateReturnCloudSideEffect,
                        delegateCloudEffect));
                }
            }

            foreach (var edge in crossProjectEdges)
            {
                var targetAssembly = edge.Target.ContainingAssembly?.Name;
                var targetMethodId = edge.Target.GetDocumentationCommentId();
                if (targetAssembly is null || targetMethodId is null)
                {
                    continue;
                }

                effects.Add(new CrossProjectMethodEffect(
                    contractAssembly,
                    methodId,
                    edge.IsGuardedIdentityTransaction
                        ? CrossProjectGuardedIdentityCallEdge
                        : CrossProjectNormalCallEdge,
                    GetMethodEffectKey(targetAssembly, targetMethodId)));
            }
        }

        var result = effects
            .Distinct(CrossProjectMethodEffectComparer.Instance)
            .OrderBy(effect => effect.ContractAssembly, StringComparer.Ordinal)
            .ThenBy(effect => effect.MethodId, StringComparer.Ordinal)
            .ThenBy(effect => effect.Flags)
            .ThenBy(effect => effect.Detail, StringComparer.Ordinal)
            .ToArray();
        foreach (var effect in result)
        {
            effect.ProducerAssembly = state.AssemblyName;
        }

        return result;
    }

    private static bool IsExecutableSourceMethod(IMethodSymbol method) =>
        IsCrossProjectCallableMethod(method) &&
        !method.IsAbstract;

    private static bool IsUserSourceMethod(IMethodSymbol method) =>
        method.Locations.Any(location =>
        {
            if (!location.IsInSource)
            {
                return false;
            }

            var path = location.SourceTree?.FilePath ?? string.Empty;
            return !string.Equals(
                Path.GetFileName(path),
                "AICopilot.Architecture.EffectSummary.g.cs",
                StringComparison.Ordinal);
        });

    private static bool IsCrossProjectCallableMethod(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary or
            MethodKind.Constructor or
            MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove or
            MethodKind.UserDefinedOperator or
            MethodKind.Conversion;

    private static IReadOnlyCollection<IMethodSymbol> GetCrossProjectContracts(
        IReadOnlyCollection<IMethodSymbol> sourceMethods)
    {
        var contracts = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var method in sourceMethods.Where(IsCrossProjectCallableMethod))
        {
            if (IsExternallyReachable(method))
            {
                contracts.Add(Normalize(method));
            }

            foreach (var explicitImplementation in method.ExplicitInterfaceImplementations)
            {
                contracts.Add(Normalize(explicitImplementation));
            }

            foreach (var @interface in method.ContainingType.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.ContainingType.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation &&
                        SymbolEqualityComparer.Default.Equals(Normalize(implementation), Normalize(method)))
                    {
                        contracts.Add(Normalize(member));
                    }
                }
            }

            for (var overridden = method.OverriddenMethod;
                 overridden is not null;
                 overridden = overridden.OverriddenMethod)
            {
                contracts.Add(Normalize(overridden));
            }
        }

        return contracts;
    }

    private static IReadOnlyCollection<CrossProjectCallEdge> GetCrossProjectCallEdges(
        IMethodSymbol root,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods)
    {
        var edges = new HashSet<CrossProjectCallEdge>(CrossProjectCallEdgeComparer.Instance);
        var normalVisited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var guardedVisited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var unguardedTransactionVisited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var dispatchTarget in ResolveDispatchTargets(root, sourceMethods))
        {
            if (IsUserSourceMethod(dispatchTarget))
            {
                CollectNormalCrossProjectEdges(
                    dispatchTarget,
                    state,
                    sourceMethods,
                    edges,
                    normalVisited,
                    guardedVisited,
                    unguardedTransactionVisited);
            }
        }

        return edges;
    }

    private static void CollectNormalCrossProjectEdges(
        IMethodSymbol method,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods,
        ISet<CrossProjectCallEdge> edges,
        ISet<IMethodSymbol> normalVisited,
        ISet<IMethodSymbol> guardedVisited,
        ISet<IMethodSymbol> unguardedTransactionVisited)
    {
        method = Normalize(method);
        if (!normalVisited.Add(method))
        {
            return;
        }

        var facts = state.GetFacts(method);
        foreach (var call in facts.GetCallsSnapshot())
        {
            foreach (var target in ResolveDispatchTargets(call, sourceMethods))
            {
                if (IsUserSourceMethod(target))
                {
                    CollectNormalCrossProjectEdges(
                        target,
                        state,
                        sourceMethods,
                        edges,
                        normalVisited,
                        guardedVisited,
                        unguardedTransactionVisited);
                }
                else
                {
                    AddCrossProjectEdge(edges, target, isGuardedIdentityTransaction: false, state);
                }
            }
        }

        foreach (var transactionTarget in facts.GetTransactionDelegateCallsSnapshot())
        {
            foreach (var target in ResolveDispatchTargets(transactionTarget, sourceMethods))
            {
                if (!IsUserSourceMethod(target))
                {
                    AddCrossProjectEdge(edges, target, isGuardedIdentityTransaction: false, state);
                    continue;
                }

                CollectTransactionCrossProjectEdges(
                    target,
                    inheritedGuard: false,
                    transactionObserved: !facts.HasUnobservedTransactionBoundary,
                    state,
                    sourceMethods,
                    edges,
                    normalVisited,
                    guardedVisited,
                    unguardedTransactionVisited);
            }
        }
    }

    private static void CollectTransactionCrossProjectEdges(
        IMethodSymbol method,
        bool inheritedGuard,
        bool transactionObserved,
        CompilationState state,
        IReadOnlyCollection<IMethodSymbol> sourceMethods,
        ISet<CrossProjectCallEdge> edges,
        ISet<IMethodSymbol> normalVisited,
        ISet<IMethodSymbol> guardedVisited,
        ISet<IMethodSymbol> unguardedTransactionVisited)
    {
        method = Normalize(method);
        var visited = inheritedGuard && transactionObserved
            ? guardedVisited
            : unguardedTransactionVisited;
        if (!visited.Add(method))
        {
            return;
        }

        var facts = state.GetFacts(method);
        var invocations = facts.GetInvocationsSnapshot()
            .OrderBy(invocation => invocation.Syntax.SpanStart)
            .ToArray();
        var representedCalls = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var invocation in invocations)
        {
            representedCalls.Add(Normalize(invocation.Target));
            var guarded = transactionObserved &&
                          (inheritedGuard || invocations.Any(candidate =>
                              IsEnabledAdminInvariantAcquire(candidate.Target) &&
                              candidate.CompletionObserved &&
                              Dominates(candidate.Syntax, invocation.Syntax)));
            foreach (var target in ResolveDispatchTargets(invocation.Target, sourceMethods))
            {
                if (IsUserSourceMethod(target))
                {
                    CollectTransactionCrossProjectEdges(
                        target,
                        guarded,
                        transactionObserved && invocation.CompletionObserved,
                        state,
                        sourceMethods,
                        edges,
                        normalVisited,
                        guardedVisited,
                        unguardedTransactionVisited);
                }
                else
                {
                    AddCrossProjectEdge(
                        edges,
                        target,
                        guarded && invocation.CompletionObserved,
                        state);
                }
            }
        }

        foreach (var call in facts.GetCallsSnapshot().Where(call => !representedCalls.Contains(Normalize(call))))
        {
            foreach (var target in ResolveDispatchTargets(call, sourceMethods))
            {
                if (IsUserSourceMethod(target))
                {
                    CollectNormalCrossProjectEdges(
                        target,
                        state,
                        sourceMethods,
                        edges,
                        normalVisited,
                        guardedVisited,
                        unguardedTransactionVisited);
                }
                else
                {
                    AddCrossProjectEdge(edges, target, isGuardedIdentityTransaction: false, state);
                }
            }
        }
    }

    private static void AddCrossProjectEdge(
        ISet<CrossProjectCallEdge> edges,
        IMethodSymbol target,
        bool isGuardedIdentityTransaction,
        CompilationState state)
    {
        target = Normalize(target);
        if (IsAllowedCloudReadOnlyOperationalWrite(target))
        {
            return;
        }

        var assemblyName = target.ContainingAssembly?.Name;
        if (assemblyName is null ||
            !IsProductionAssembly(assemblyName) ||
            ContainsTestAssemblyMarker(assemblyName) ||
            !IsSummaryRelevantCrossProjectTarget(target, state) ||
            target.GetDocumentationCommentId() is null)
        {
            return;
        }

        edges.Add(new CrossProjectCallEdge(target, isGuardedIdentityTransaction));
    }

    private static bool IsSummaryRelevantCrossProjectTarget(
        IMethodSymbol target,
        CompilationState state) =>
        target.IsAbstract ||
        target.ContainingType.TypeKind == TypeKind.Interface ||
        state.HasReferencedSummary(target);

    private static string RenderEffectSummarySource(
        IReadOnlyCollection<CrossProjectMethodEffect> effects)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.Append("[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"")
            .Append(EffectSummarySchemaKey)
            .Append("\", \"")
            .Append(EffectSummarySchemaVersion)
            .AppendLine("\")]");
        source.Append("[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"")
            .Append(EffectSummaryCountKey)
            .Append("\", \"")
            .Append(effects.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine("\")]");

        var index = 0;
        foreach (var effect in effects)
        {
            source.Append("[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"")
                .Append(EffectSummaryEntryPrefix)
                .Append(index.ToString("D6", CultureInfo.InvariantCulture))
                .Append("\", \"")
                .Append(EncodeEffectSummary(effect))
                .AppendLine("\")]");
            index++;
        }

        return source.ToString();
    }

    private static string EncodeEffectSummary(CrossProjectMethodEffect effect)
    {
        var payload = string.Join(
            "\n",
            effect.ProducerAssembly,
            effect.ContractAssembly,
            effect.MethodId,
            effect.Flags.ToString(CultureInfo.InvariantCulture),
            effect.Detail);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadEffectSummary(
        IAssemblySymbol assembly,
        out IReadOnlyCollection<CrossProjectMethodEffect> effects,
        out string? error)
    {
        var metadata = assembly.GetAttributes()
            .Where(attribute =>
                attribute.AttributeClass?.ToDisplayString() == AssemblyMetadataAttributeName &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value is string key &&
                key.StartsWith(EffectSummaryMetadataPrefix, StringComparison.Ordinal))
            .Select(attribute => new
            {
                Key = (string)attribute.ConstructorArguments[0].Value!,
                Value = attribute.ConstructorArguments[1].Value as string
            })
            .ToArray();
        var duplicate = metadata.GroupBy(item => item.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            effects = [];
            error = $"effect summary metadata key '{duplicate.Key}' is duplicated";
            return false;
        }

        var values = metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!values.TryGetValue(EffectSummarySchemaKey, out var schema) ||
            !string.Equals(schema, EffectSummarySchemaVersion, StringComparison.Ordinal))
        {
            effects = [];
            error = $"effect summary schema must be exactly '{EffectSummarySchemaVersion}'";
            return false;
        }

        if (!values.TryGetValue(EffectSummaryCountKey, out var countValue) ||
            !int.TryParse(countValue, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count < 0 ||
            count > MaximumEffectSummaryEntries)
        {
            effects = [];
            error = $"effect summary count must be between 0 and {MaximumEffectSummaryEntries}";
            return false;
        }

        var expectedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            EffectSummarySchemaKey,
            EffectSummaryCountKey
        };
        var parsed = new List<CrossProjectMethodEffect>(count);
        for (var index = 0; index < count; index++)
        {
            var key = EffectSummaryEntryPrefix + index.ToString("D6", CultureInfo.InvariantCulture);
            expectedKeys.Add(key);
            if (!values.TryGetValue(key, out var encoded) || encoded is null ||
                !TryDecodeEffectSummary(encoded, assembly.Name, out var effect))
            {
                effects = [];
                error = $"effect summary entry '{key}' is missing or malformed";
                return false;
            }

            parsed.Add(effect!);
        }

        var unexpected = values.Keys.FirstOrDefault(key => !expectedKeys.Contains(key));
        if (unexpected is not null)
        {
            effects = [];
            error = $"effect summary metadata key '{unexpected}' is not part of the declared entry set";
            return false;
        }

        var canonical = parsed
            .OrderBy(effect => effect.ContractAssembly, StringComparer.Ordinal)
            .ThenBy(effect => effect.MethodId, StringComparer.Ordinal)
            .ThenBy(effect => effect.Flags)
            .ThenBy(effect => effect.Detail, StringComparer.Ordinal)
            .ToArray();
        if (!parsed.SequenceEqual(canonical, CrossProjectMethodEffectComparer.Instance) ||
            parsed.Distinct(CrossProjectMethodEffectComparer.Instance).Count() != parsed.Count)
        {
            effects = [];
            error = "effect summary entries must be unique and canonically ordered";
            return false;
        }

        effects = canonical;
        error = null;
        return true;
    }

    private static bool TryDecodeEffectSummary(
        string encoded,
        string expectedProducerAssembly,
        out CrossProjectMethodEffect? effect)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var fields = payload.Split(new[] { '\n' }, 5);
            if (fields.Length != 5 ||
                string.IsNullOrWhiteSpace(fields[0]) ||
                !string.Equals(fields[0], expectedProducerAssembly, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(fields[1]) ||
                string.IsNullOrWhiteSpace(fields[2]) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var flags) ||
                flags <= 0 ||
                (flags & ~(CrossProjectIdentityEffect |
                           CrossProjectCloudSideEffect |
                           CrossProjectCloudRoot |
                           CrossProjectIdentityRoot |
                           CrossProjectNormalCallEdge |
                           CrossProjectGuardedIdentityCallEdge |
                           CrossProjectDelegateReturnIdentityEffect |
                           CrossProjectDelegateReturnCloudSideEffect |
                           CrossProjectDelegateReturnResolved)) != 0 ||
                string.IsNullOrWhiteSpace(fields[4]))
            {
                effect = null;
                return false;
            }

            var edgeFlags = flags & (CrossProjectNormalCallEdge | CrossProjectGuardedIdentityCallEdge);
            if ((edgeFlags != 0 &&
                 (flags != CrossProjectNormalCallEdge &&
                  flags != CrossProjectGuardedIdentityCallEdge ||
                  !TryParseMethodEffectKey(fields[4], out _, out _))) ||
                (edgeFlags == 0 &&
                 (flags & (CrossProjectIdentityEffect |
                           CrossProjectCloudSideEffect |
                           CrossProjectCloudRoot |
                           CrossProjectIdentityRoot |
                           CrossProjectDelegateReturnIdentityEffect |
                           CrossProjectDelegateReturnCloudSideEffect |
                           CrossProjectDelegateReturnResolved)) == 0))
            {
                effect = null;
                return false;
            }

            effect = new CrossProjectMethodEffect(fields[1], fields[2], flags, fields[4])
            {
                ProducerAssembly = fields[0]
            };
            return true;
        }
        catch (FormatException)
        {
            effect = null;
            return false;
        }
    }

    private static CompilationState CollectCompilationStateForEffectSummary(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var state = new CompilationState(compilation);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = syntaxTree.GetRoot(cancellationToken);
#pragma warning disable RS1030 // This path is executed only by the source generator.
            var model = compilation.GetSemanticModel(syntaxTree);
#pragma warning restore RS1030

            foreach (var declaration in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
                {
                    state.AddMethod(method);
                    if (IsDelegateType(method.ReturnType) &&
                        declaration is MethodDeclarationSyntax
                        {
                            ExpressionBody.Expression: { } expression
                        } &&
                        model.GetOperation(expression, cancellationToken) is IOperation returnedValue)
                    {
                        state.AddDelegateReturnDefinition(method, returnedValue);
                    }
                }
            }

            foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type)
                {
                    foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                    {
                        state.AddMethod(method);
                    }
                }
            }

            foreach (var declaration in root.DescendantNodes().OfType<AccessorDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
                {
                    state.AddMethod(method);
                }
            }

            foreach (var declaration in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
                {
                    state.AddMethod(method);
                }
            }

            foreach (var anonymous in root.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
            {
                if (model.GetOperation(anonymous, cancellationToken) is IAnonymousFunctionOperation operation)
                {
                    state.AddMethod(operation.Symbol);
                }
            }

            foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer is not null &&
                    model.GetDeclaredSymbol(variable, cancellationToken) is IFieldSymbol field &&
                    model.GetOperation(variable.Initializer.Value, cancellationToken) is IOperation definition)
                {
                    state.AddDelegateMemberDefinition(field, definition);
                }
            }

            foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(property, cancellationToken) is not IPropertySymbol symbol)
                {
                    continue;
                }

                var value = property.Initializer?.Value ?? property.ExpressionBody?.Expression;
                if (value is not null &&
                    model.GetOperation(value, cancellationToken) is IOperation definition)
                {
                    state.AddDelegateMemberDefinition(symbol, definition);
                }
            }

            foreach (var assignmentSyntax in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                var assignment = model.GetOperation(assignmentSyntax, cancellationToken);
                var assignedTarget = assignment switch
                {
                    ISimpleAssignmentOperation simpleTargetAssignment => simpleTargetAssignment.Target,
                    ICompoundAssignmentOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add
                    } compoundTargetAssignment => compoundTargetAssignment.Target,
                    _ => null
                };
                var assignedValue = assignment switch
                {
                    ISimpleAssignmentOperation simpleValueAssignment => simpleValueAssignment.Value,
                    ICompoundAssignmentOperation
                    {
                        OperatorKind: BinaryOperatorKind.Add
                    } compoundValueAssignment => compoundValueAssignment.Value,
                    _ => null
                };
                if (assignedTarget is null || assignedValue is null)
                {
                    continue;
                }

                switch (assignedTarget)
                {
                    case IFieldReferenceOperation field:
                        state.AddDelegateMemberDefinition(field.Field, assignedValue);
                        break;
                    case IPropertyReferenceOperation property:
                        state.AddDelegateMemberDefinition(property.Property, assignedValue);
                        break;
                }

                if (assignment is not ISimpleAssignmentOperation simpleAssignment)
                {
                    continue;
                }

                foreach (var method in GetOperationOwnersForEffectSummary(
                             simpleAssignment,
                             model,
                             cancellationToken))
                {
                    CollectIdentityPropertyAssignment(simpleAssignment, method, state);
                }
            }

            foreach (var returnSyntax in root.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                if (model.GetOperation(returnSyntax, cancellationToken) is IReturnOperation
                    {
                        ReturnedValue: not null
                    } @return)
                {
                    foreach (var method in GetOperationOwnersForEffectSummary(
                                 @return,
                                 model,
                                 cancellationToken))
                    {
                        if (method.AssociatedSymbol is IPropertySymbol property)
                        {
                            state.AddDelegateMemberDefinition(property, @return.ReturnedValue);
                        }

                        if (IsDelegateType(method.ReturnType))
                        {
                            state.AddDelegateReturnDefinition(method, @return.ReturnedValue);
                        }
                    }
                }
            }

            var operationRootKeys = new HashSet<(OperationKind Kind, int Start, int Length)>();
            foreach (var syntax in root.DescendantNodesAndSelf())
            {
                var operation = model.GetOperation(syntax, cancellationToken);
                if (operation is null)
                {
                    continue;
                }

                while (operation.Parent is not null)
                {
                    operation = operation.Parent;
                }

                var operationRootKey = (
                    operation.Kind,
                    operation.Syntax.SpanStart,
                    operation.Syntax.Span.Length);
                if (!operationRootKeys.Add(operationRootKey))
                {
                    continue;
                }

                foreach (var semanticOperation in operation.DescendantsAndSelf())
                {
                    if (semanticOperation.IsImplicit &&
                        semanticOperation is IInvocationOperation or IObjectCreationOperation)
                    {
                        continue;
                    }

                    if (semanticOperation is not (IInvocationOperation or
                        IDynamicInvocationOperation or
                        IDynamicObjectCreationOperation or
                        IDynamicIndexerAccessOperation or
                        IObjectCreationOperation or
                        IMethodReferenceOperation or
                        IPropertyReferenceOperation or
                        IConversionOperation or
                        IBinaryOperation or
                        IUnaryOperation or
                        ICompoundAssignmentOperation or
                        IIncrementOrDecrementOperation or
                        IEventAssignmentOperation))
                    {
                        continue;
                    }

                    foreach (var containingMethod in GetOperationOwnersForEffectSummary(
                                 semanticOperation,
                                 model,
                                 cancellationToken))
                    {
                        switch (semanticOperation)
                        {
                            case IInvocationOperation invocation:
                                CollectInvocationFacts(invocation, containingMethod, state);
                                break;
                            case IDynamicInvocationOperation dynamicInvocation:
                                CollectDynamicOperationFacts(dynamicInvocation, containingMethod, state);
                                break;
                            case IDynamicObjectCreationOperation dynamicObjectCreation:
                                CollectDynamicOperationFacts(dynamicObjectCreation, containingMethod, state);
                                break;
                            case IDynamicIndexerAccessOperation dynamicIndexer:
                                CollectDynamicOperationFacts(dynamicIndexer, containingMethod, state);
                                break;
                            case IObjectCreationOperation objectCreation:
                                foreach (var argument in objectCreation.Arguments.Where(argument =>
                                             !argument.IsImplicit &&
                                             argument.Parameter is not null &&
                                             IsDelegateType(argument.Parameter.Type)))
                                {
                                    state.AddDelegateParameterDefinition(argument.Parameter!, argument.Value);
                                }

                                CollectImplicitMethodCallFacts(
                                    objectCreation,
                                    objectCreation.Constructor,
                                    containingMethod,
                                    state);
                                break;
                            case IMethodReferenceOperation methodReference:
                                state.AddDelegateMethodReference(methodReference.Method);
                                break;
                            case IPropertyReferenceOperation:
                            case IConversionOperation:
                            case IBinaryOperation:
                            case IUnaryOperation:
                            case ICompoundAssignmentOperation:
                            case IIncrementOrDecrementOperation:
                                foreach (var target in GetImplicitCallTargets(semanticOperation))
                                {
                                    CollectImplicitMethodCallFacts(
                                        semanticOperation,
                                        target,
                                        containingMethod,
                                        state);
                                }

                                break;
                            case IEventAssignmentOperation eventAssignment:
                                foreach (var target in GetImplicitCallTargets(eventAssignment))
                                {
                                    CollectImplicitMethodCallFacts(
                                        eventAssignment,
                                        target,
                                        containingMethod,
                                        state);
                                }

                                break;
                        }
                    }
                }
            }
        }

        ResolveDeferredDelegateCalls(state);
        ResolveDeferredDapperCalls(state);
        AddStaticInitializerEdges(state);
        return state;
    }

    private static IReadOnlyCollection<IMethodSymbol> GetOperationOwnersForEffectSummary(
        IOperation operation,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var anonymousOwner = FindAnonymousOwner(operation);
        if (anonymousOwner is not null)
        {
            return [Normalize(anonymousOwner.Symbol)];
        }

        for (var symbol = model.GetEnclosingSymbol(operation.Syntax.SpanStart, cancellationToken);
             symbol is not null;
             symbol = symbol.ContainingSymbol)
        {
            if (symbol is IMethodSymbol method)
            {
                return [Normalize(method)];
            }

            if (symbol is IFieldSymbol or IPropertySymbol or IEventSymbol or INamedTypeSymbol)
            {
                return GetOperationOwners(operation, symbol);
            }
        }

        return [];
    }

    private sealed class CrossProjectMethodEffect(
        string contractAssembly,
        string methodId,
        int flags,
        string detail)
    {
        public string ContractAssembly { get; } = contractAssembly;

        public string MethodId { get; } = methodId;

        public int Flags { get; set; } = flags;

        public string Detail { get; set; } = detail;

        public string ProducerAssembly { get; set; } = string.Empty;
    }

    private sealed class CrossProjectMethodEffectComparer : IEqualityComparer<CrossProjectMethodEffect>
    {
        public static CrossProjectMethodEffectComparer Instance { get; } = new();

        public bool Equals(CrossProjectMethodEffect? x, CrossProjectMethodEffect? y) =>
            ReferenceEquals(x, y) ||
            (x is not null &&
             y is not null &&
             string.Equals(x.ProducerAssembly, y.ProducerAssembly, StringComparison.Ordinal) &&
             string.Equals(x.ContractAssembly, y.ContractAssembly, StringComparison.Ordinal) &&
             string.Equals(x.MethodId, y.MethodId, StringComparison.Ordinal) &&
             x.Flags == y.Flags &&
             string.Equals(x.Detail, y.Detail, StringComparison.Ordinal));

        public int GetHashCode(CrossProjectMethodEffect value)
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(value.ProducerAssembly);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(value.ContractAssembly);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(value.MethodId);
                hash = (hash * 397) ^ value.Flags;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(value.Detail);
                return hash;
            }
        }
    }

    private sealed class CrossProjectCallEdge(
        IMethodSymbol target,
        bool isGuardedIdentityTransaction)
    {
        public IMethodSymbol Target { get; } = Normalize(target);

        public bool IsGuardedIdentityTransaction { get; } = isGuardedIdentityTransaction;
    }

    private sealed class CrossProjectCallEdgeComparer : IEqualityComparer<CrossProjectCallEdge>
    {
        public static CrossProjectCallEdgeComparer Instance { get; } = new();

        public bool Equals(CrossProjectCallEdge? x, CrossProjectCallEdge? y) =>
            ReferenceEquals(x, y) ||
            (x is not null &&
             y is not null &&
             x.IsGuardedIdentityTransaction == y.IsGuardedIdentityTransaction &&
             SymbolEqualityComparer.Default.Equals(x.Target, y.Target));

        public int GetHashCode(CrossProjectCallEdge value)
        {
            unchecked
            {
                return (SymbolEqualityComparer.Default.GetHashCode(value.Target) * 397) ^
                       value.IsGuardedIdentityTransaction.GetHashCode();
            }
        }
    }

    private sealed class CompilationState
    {
        private readonly object sync = new();
        private readonly Dictionary<IMethodSymbol, MethodFacts> facts =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> methods = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, List<IOperation>> delegateMemberDefinitions =
            new(SymbolEqualityComparer.Default);
        private readonly Dictionary<IParameterSymbol, List<IOperation>> delegateParameterDefinitions =
            new(SymbolEqualityComparer.Default);
        private readonly Dictionary<IMethodSymbol, List<IOperation>> delegateReturnDefinitions =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> delegateMethodReferences =
            new(SymbolEqualityComparer.Default);
        private readonly List<DeferredDelegateCall> deferredDelegateCalls = [];
        private readonly List<DeferredDapperCall> deferredDapperCalls = [];
        private readonly List<ObservedInvocation> observedInvocations = [];
        private readonly Dictionary<string, ReferencedMethodEffects> referencedMethodEffects =
            new(StringComparer.Ordinal);
        private readonly List<CrossProjectMethodEffect> referencedSummaries = [];
        private readonly List<string> referencedSummaryErrors = [];
        private readonly List<Diagnostic> deferredDiagnostics = [];

        public CompilationState(Compilation compilation)
        {
            Compilation = compilation;
            AssemblyName = compilation.AssemblyName ?? string.Empty;
            LoadReferencedMethodEffects(compilation);
        }

        public Compilation Compilation { get; }

        public string AssemblyName { get; }

        public bool HasReferencedIdentityEffect(IMethodSymbol method) =>
            TryGetReferencedMethodEffects(method, out var effects) && effects.HasIdentityEffect;

        public string? GetReferencedCloudSideEffect(IMethodSymbol method) =>
            TryGetReferencedMethodEffects(method, out var effects)
                ? effects.CloudSideEffect
                : null;

        public bool HasReferencedSummary(IMethodSymbol method) =>
            TryGetReferencedMethodEffects(method, out _);

        public IReadOnlyCollection<string> GetReferencedSummaryErrorsSnapshot() =>
            referencedSummaryErrors.ToArray();

        public IReadOnlyCollection<CrossProjectMethodEffect> GetReferencedSummariesSnapshot() =>
            referencedSummaries.ToArray();

        public void AddDeferredDiagnostic(Diagnostic diagnostic)
        {
            lock (sync)
            {
                deferredDiagnostics.Add(diagnostic);
            }
        }

        public IReadOnlyCollection<Diagnostic> GetDeferredDiagnosticsSnapshot()
        {
            lock (sync)
            {
                return deferredDiagnostics.ToArray();
            }
        }

        public void AddMethod(IMethodSymbol method)
        {
            lock (sync)
            {
                methods.Add(Normalize(method));
            }
        }

        public IReadOnlyCollection<IMethodSymbol> GetMethodsSnapshot()
        {
            lock (sync)
            {
                return methods.ToArray();
            }
        }

        public MethodFacts GetFacts(IMethodSymbol method)
        {
            method = Normalize(method);
            lock (sync)
            {
                if (!facts.TryGetValue(method, out var value))
                {
                    value = new MethodFacts();
                    facts.Add(method, value);
                }

                return value;
            }
        }

        public void AddDelegateMemberDefinition(ISymbol member, IOperation definition)
        {
            lock (sync)
            {
                if (!delegateMemberDefinitions.TryGetValue(member, out var definitions))
                {
                    definitions = [];
                    delegateMemberDefinitions.Add(member, definitions);
                }

                definitions.Add(definition);
            }
        }

        public IReadOnlyCollection<IOperation> GetDelegateMemberDefinitionsSnapshot(ISymbol member)
        {
            lock (sync)
            {
                return delegateMemberDefinitions.TryGetValue(member, out var definitions)
                    ? definitions.ToArray()
                    : [];
            }
        }

        public void AddDelegateParameterDefinition(
            IParameterSymbol parameter,
            IOperation definition)
        {
            parameter = (IParameterSymbol)parameter.OriginalDefinition;
            lock (sync)
            {
                if (!delegateParameterDefinitions.TryGetValue(parameter, out var definitions))
                {
                    definitions = [];
                    delegateParameterDefinitions.Add(parameter, definitions);
                }

                definitions.Add(definition);
            }
        }

        public IReadOnlyCollection<IOperation> GetDelegateParameterDefinitionsSnapshot(
            IParameterSymbol parameter)
        {
            parameter = (IParameterSymbol)parameter.OriginalDefinition;
            lock (sync)
            {
                return delegateParameterDefinitions.TryGetValue(parameter, out var definitions)
                    ? definitions.ToArray()
                    : [];
            }
        }

        public void AddDelegateReturnDefinition(IMethodSymbol method, IOperation returnedValue)
        {
            method = Normalize(method);
            lock (sync)
            {
                if (!delegateReturnDefinitions.TryGetValue(method, out var definitions))
                {
                    definitions = [];
                    delegateReturnDefinitions.Add(method, definitions);
                }

                definitions.Add(returnedValue);
            }
        }

        public IReadOnlyCollection<IOperation> GetDelegateReturnDefinitionsSnapshot(IMethodSymbol method)
        {
            method = Normalize(method);
            lock (sync)
            {
                return delegateReturnDefinitions.TryGetValue(method, out var definitions)
                    ? definitions.ToArray()
                    : [];
            }
        }

        public IReadOnlyCollection<DelegateReturnDefinition> GetDelegateReturnDefinitionsSnapshot()
        {
            lock (sync)
            {
                return delegateReturnDefinitions
                    .SelectMany(item => item.Value.Select(value =>
                        new DelegateReturnDefinition(item.Key, value)))
                    .ToArray();
            }
        }

        public void AddDelegateMethodReference(IMethodSymbol method)
        {
            lock (sync)
            {
                delegateMethodReferences.Add(Normalize(method));
            }
        }

        public bool HasDelegateMethodReference(IMethodSymbol method)
        {
            lock (sync)
            {
                return delegateMethodReferences.Contains(Normalize(method));
            }
        }

        public void AddDeferredDelegateCall(
            IMethodSymbol caller,
            IOperation delegateOperation,
            IInvocationOperation invocation,
            bool isTransactionScope)
        {
            lock (sync)
            {
                deferredDelegateCalls.Add(new DeferredDelegateCall(
                    Normalize(caller),
                    delegateOperation,
                    invocation,
                    isTransactionScope));
            }
        }

        public IReadOnlyCollection<DeferredDelegateCall> GetDeferredDelegateCallsSnapshot()
        {
            lock (sync)
            {
                return deferredDelegateCalls.ToArray();
            }
        }

        public void AddObservedInvocation(IMethodSymbol caller, IInvocationOperation invocation)
        {
            lock (sync)
            {
                observedInvocations.Add(new ObservedInvocation(Normalize(caller), invocation));
            }
        }

        public IReadOnlyCollection<ObservedInvocation> GetObservedInvocationsSnapshot()
        {
            lock (sync)
            {
                return observedInvocations.ToArray();
            }
        }

        public void AddDeferredDapperCall(IMethodSymbol caller, IInvocationOperation invocation)
        {
            lock (sync)
            {
                deferredDapperCalls.Add(new DeferredDapperCall(Normalize(caller), invocation));
            }
        }

        public IReadOnlyCollection<DeferredDapperCall> GetDeferredDapperCallsSnapshot()
        {
            lock (sync)
            {
                return deferredDapperCalls.ToArray();
            }
        }

        private void LoadReferencedMethodEffects(Compilation compilation)
        {
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                {
                    continue;
                }

                var assemblyName = assembly.Name;
                if (!IsProductionAssembly(assemblyName) || ContainsTestAssemblyMarker(assemblyName))
                {
                    continue;
                }

                if (!TryReadEffectSummary(assembly, out var summaries, out var error))
                {
                    referencedSummaryErrors.Add($"reference '{assemblyName}' has invalid generated effect metadata: {error}");
                    continue;
                }

                foreach (var summary in summaries)
                {
                    summary.ProducerAssembly = assemblyName;
                    referencedSummaries.Add(summary);
                    var key = GetMethodEffectKey(summary.ContractAssembly, summary.MethodId);
                    if (!referencedMethodEffects.TryGetValue(key, out var effects))
                    {
                        effects = new ReferencedMethodEffects();
                        referencedMethodEffects.Add(key, effects);
                    }

                    effects.HasIdentityEffect |= (summary.Flags & CrossProjectIdentityEffect) != 0;
                    effects.DelegateReturnHasIdentityEffect |=
                        (summary.Flags & CrossProjectDelegateReturnIdentityEffect) != 0;
                    effects.DelegateReturnIsResolved |=
                        (summary.Flags & CrossProjectDelegateReturnResolved) != 0;
                    if ((summary.Flags & CrossProjectDelegateReturnCloudSideEffect) != 0)
                    {
                        if (effects.DelegateReturnCloudSideEffect is null ||
                            string.CompareOrdinal(summary.Detail, effects.DelegateReturnCloudSideEffect) < 0)
                        {
                            effects.DelegateReturnCloudSideEffect = summary.Detail;
                        }
                    }

                    if ((summary.Flags & CrossProjectCloudSideEffect) != 0)
                    {
                        if (effects.CloudSideEffect is null ||
                            string.CompareOrdinal(summary.Detail, effects.CloudSideEffect) < 0)
                        {
                            effects.CloudSideEffect = summary.Detail;
                        }
                    }
                }
            }
        }

        private bool TryGetReferencedMethodEffects(
            IMethodSymbol method,
            out ReferencedMethodEffects effects)
        {
            method = Normalize(method);
            var methodId = method.GetDocumentationCommentId();
            var assemblyName = method.ContainingAssembly?.Name;
            if (methodId is null || assemblyName is null)
            {
                effects = null!;
                return false;
            }

            return referencedMethodEffects.TryGetValue(
                GetMethodEffectKey(assemblyName, methodId),
                out effects!);
        }

        public bool TryGetReferencedDelegateReturnEffects(
            IOperation operation,
            out ReferencedMethodEffects effects)
        {
            operation = UnwrapConversion(operation) ?? operation;
            if (operation is IInvocationOperation invocation)
            {
                return TryGetReferencedMethodEffects(
                    invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod,
                    out effects);
            }

            effects = null!;
            return false;
        }
    }

    private sealed class ReferencedMethodEffects
    {
        public bool HasIdentityEffect { get; set; }

        public string? CloudSideEffect { get; set; }

        public bool DelegateReturnHasIdentityEffect { get; set; }

        public string? DelegateReturnCloudSideEffect { get; set; }

        public bool DelegateReturnIsResolved { get; set; }
    }

    private sealed class ReferencedSummaryNode(string contractAssembly, string methodId)
    {
        public string ContractAssembly { get; } = contractAssembly;

        public string MethodId { get; } = methodId;

        public int Flags { get; set; }

        public string? CloudSideEffect { get; set; }

        public HashSet<string> CloudRootProducers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> IdentityRootProducers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> CloudEffectProducers { get; } = new(StringComparer.Ordinal);

        public HashSet<string> IdentityEffectProducers { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ReferencedSummaryEdge(
        string targetKey,
        bool isGuardedIdentityTransaction,
        string producerAssembly)
    {
        public string TargetKey { get; } = targetKey;

        public bool IsGuardedIdentityTransaction { get; } = isGuardedIdentityTransaction;

        public string ProducerAssembly { get; } = producerAssembly;
    }

    private sealed class ReferencedTraversalState(
        string methodKey,
        string? firstProducer,
        string? secondProducer)
    {
        public string MethodKey { get; } = methodKey;

        public string? FirstProducer { get; } = firstProducer;

        public string? SecondProducer { get; } = secondProducer;

        public bool HasTwoReferencedProducers => SecondProducer is not null;

        public ReferencedTraversalState AddProducer(string producer, string currentAssembly)
        {
            if (string.IsNullOrEmpty(producer) ||
                string.Equals(producer, currentAssembly, StringComparison.Ordinal) ||
                string.Equals(producer, FirstProducer, StringComparison.Ordinal) ||
                string.Equals(producer, SecondProducer, StringComparison.Ordinal))
            {
                return this;
            }

            if (FirstProducer is null)
            {
                return new ReferencedTraversalState(MethodKey, producer, null);
            }

            if (SecondProducer is null)
            {
                var first = string.CompareOrdinal(FirstProducer, producer) <= 0
                    ? FirstProducer
                    : producer;
                var second = string.CompareOrdinal(FirstProducer, producer) <= 0
                    ? producer
                    : FirstProducer;
                return new ReferencedTraversalState(MethodKey, first, second);
            }

            return this;
        }

        public string GetVisitKey() =>
            string.Concat(MethodKey, "\u001e", FirstProducer, "\u001e", SecondProducer);
    }

    private sealed class DeferredDelegateCall(
        IMethodSymbol caller,
        IOperation delegateOperation,
        IInvocationOperation invocation,
        bool isTransactionScope)
    {
        public IMethodSymbol Caller { get; } = caller;

        public IOperation DelegateOperation { get; } = delegateOperation;

        public IInvocationOperation Invocation { get; } = invocation;

        public bool IsTransactionScope { get; } = isTransactionScope;
    }

    private sealed class DeferredDapperCall(
        IMethodSymbol caller,
        IInvocationOperation invocation)
    {
        public IMethodSymbol Caller { get; } = caller;

        public IInvocationOperation Invocation { get; } = invocation;
    }

    private sealed class ObservedInvocation(
        IMethodSymbol caller,
        IInvocationOperation invocation)
    {
        public IMethodSymbol Caller { get; } = caller;

        public IInvocationOperation Invocation { get; } = invocation;
    }

    private sealed class DelegateReturnDefinition(
        IMethodSymbol method,
        IOperation returnedValue)
    {
        public IMethodSymbol Method { get; } = method;

        public IOperation ReturnedValue { get; } = returnedValue;
    }

    private sealed class MethodFacts
    {
        private readonly object sync = new();
        private readonly HashSet<IMethodSymbol> calls = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> transactionDelegateCalls =
            new(SymbolEqualityComparer.Default);
        private readonly List<InvocationFact> invocations = [];
        private readonly List<IdentityMutationFact> identityMutations = [];

        public string? UnresolvedDelegateInvocationDetail { get; set; }

        public void AddCall(IMethodSymbol method)
        {
            lock (sync)
            {
                calls.Add(Normalize(method));
            }
        }

        public void AddTransactionDelegateCall(IMethodSymbol method)
        {
            lock (sync)
            {
                transactionDelegateCalls.Add(Normalize(method));
            }
        }

        public void AddInvocation(
            IMethodSymbol method,
            SyntaxNode syntax,
            bool isIdentityDecrease,
            bool completionObserved)
        {
            method = Normalize(method);
            lock (sync)
            {
                calls.Add(method);
                invocations.Add(new InvocationFact(method, syntax, completionObserved));
                if (isIdentityDecrease)
                {
                    identityMutations.Add(new IdentityMutationFact(syntax, completionObserved));
                }
            }
        }

        public void AddIdentityMutation(SyntaxNode syntax, bool completionObserved)
        {
            lock (sync)
            {
                identityMutations.Add(new IdentityMutationFact(syntax, completionObserved));
            }
        }

        public IReadOnlyCollection<IMethodSymbol> GetCallsSnapshot()
        {
            lock (sync)
            {
                return calls.ToArray();
            }
        }

        public IReadOnlyCollection<IMethodSymbol> GetAllCallsSnapshot()
        {
            lock (sync)
            {
                var result = new HashSet<IMethodSymbol>(calls, SymbolEqualityComparer.Default);
                result.UnionWith(transactionDelegateCalls);
                return result.ToArray();
            }
        }

        public IReadOnlyCollection<IMethodSymbol> GetTransactionDelegateCallsSnapshot()
        {
            lock (sync)
            {
                return transactionDelegateCalls.ToArray();
            }
        }

        public IReadOnlyCollection<InvocationFact> GetInvocationsSnapshot()
        {
            lock (sync)
            {
                return invocations.ToArray();
            }
        }

        public IReadOnlyCollection<IdentityMutationFact> GetIdentityMutationsSnapshot()
        {
            lock (sync)
            {
                return identityMutations.ToArray();
            }
        }

        public bool IsTransactionScope { get; set; }

        public bool UsesTrustedCloudReadType { get; set; }

        public bool HasDynamicInvocation { get; set; }

        public bool HasUnobservedTransactionBoundary { get; set; }

        public string? ForbiddenSideEffect { get; set; }
    }

    private sealed class InvocationFact
    {
        public InvocationFact(IMethodSymbol target, SyntaxNode syntax, bool completionObserved)
        {
            Target = target;
            Syntax = syntax;
            CompletionObserved = completionObserved;
        }

        public IMethodSymbol Target { get; }

        public SyntaxNode Syntax { get; }

        public bool CompletionObserved { get; }
    }

    private sealed class IdentityMutationFact
    {
        public IdentityMutationFact(SyntaxNode syntax, bool completionObserved)
        {
            Syntax = syntax;
            CompletionObserved = completionObserved;
        }

        public SyntaxNode Syntax { get; }

        public bool CompletionObserved { get; }
    }

    private enum Layer
    {
        Unknown,
        Shared,
        Core,
        Service,
        Infrastructure,
        Host
    }
}
