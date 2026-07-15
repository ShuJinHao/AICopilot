using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
            "AICopilot.Core.AiGateway.Aggregates.Skills.SkillDefinition",
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
            "UpdateAsync",
            "SetLockoutEndDateAsync",
            "SetLockoutEnabledAsync",
            "MarkUserDisabled");

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
    private static readonly ImmutableHashSet<string> TrustedCloudReadTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.Services.Contracts.ICloudAiReadClient",
            "AICopilot.Services.Contracts.ICloudReadOnlyTextToSqlGenerator",
            "AICopilot.CloudReadClient.CloudAiReadClient",
            "AICopilot.AiGatewayService.AgentTasks.ICloudReadonlyAgentToolExecutor",
            "AICopilot.AiGatewayService.AgentTasks.ICloudReadonlyDataProvider");
    private static readonly ImmutableHashSet<string> FormalCloudReadOnlyWorkflowTypeNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "AICopilot.AiGatewayService.Workflows.Executors.CloudReadOnlyTextToSqlFallbackRunner",
            "AICopilot.AiGatewayService.AgentTasks.CloudReadonlyAgentToolExecutor",
            "AICopilot.AiGatewayService.AgentTasks.RealCloudReadonlyDataProvider");

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
                operationContext => AnalyzeRoleClaimReference(
                    (IFieldReferenceOperation)operationContext.Operation,
                    operationContext,
                    state),
                OperationKind.FieldReference);
            startContext.RegisterOperationAction(
                operationContext =>
                {
                    var objectCreation = (IObjectCreationOperation)operationContext.Operation;
                    AnalyzeRoleAuthorizationObjectCreation(objectCreation, operationContext, state);
                    AnalyzeCloudReadObjectCreation(objectCreation, operationContext, state);
                },
                OperationKind.ObjectCreation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeAnonymousFunction((IAnonymousFunctionOperation)operationContext.Operation, operationContext, state),
                OperationKind.AnonymousFunction);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeDelegateMemberDefinition(operationContext, state),
                OperationKind.FieldInitializer,
                OperationKind.PropertyInitializer,
                OperationKind.SimpleAssignment,
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

        if (type.TypeKind == TypeKind.Class &&
            Implements(type, "IAggregateRoot") &&
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
            if (entity is ITypeParameterSymbol || Implements(entity, "IAggregateRoot"))
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
        if (DerivesFrom(type, "DbContext") && !IsEfOwner(state.AssemblyName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PersistenceOwnerRule,
                FirstSourceLocation(type),
                type.ToDisplayString(),
                "Microsoft.EntityFrameworkCore.DbContext"));
        }
    }

    private static void AnalyzePluginType(
        INamedTypeSymbol type,
        SymbolAnalysisContext context,
        CompilationState state)
    {
        var isPlugin = Implements(type, "IAgentPlugin");
        var isAgentExecutor = Implements(type, "IAgentToolExecutor");
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
            var isPublicException = !isStream && ExplicitPublicRequestNames.Contains(type.ToDisplayString());
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
        var anonymousOwner = FindAnonymousOwner(invocation);
        var containingMethod = anonymousOwner?.Symbol ?? context.ContainingSymbol as IMethodSymbol;
        if (containingMethod is null)
        {
            return;
        }

        var caller = Normalize(containingMethod);
        var invokedTarget = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var target = Normalize(invokedTarget);
        state.AddMethod(caller);
        state.GetFacts(caller).AddInvocation(
            target,
            invocation.Syntax,
            IsIdentityDecreaseInvocation(invocation),
            IsCompletionObserved(invocation));

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
                if (IsDelegateType(argument.Parameter?.Type ?? argument.Value.Type))
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
                         IsDelegateType(argument.Parameter?.Type ?? argument.Value.Type)))
            {
                state.AddDeferredDelegateCall(caller, argument.Value, invocation, isTransactionScope: false);
            }
        }

        var sideEffect = GetForbiddenSideEffect(invokedTarget) ?? GetInvocationSideEffect(invocation, caller);
        if (sideEffect is not null && !IsAuditWrite(invokedTarget))
        {
            state.GetFacts(caller).ForbiddenSideEffect = sideEffect;
        }

        AnalyzeDatabaseInvocation(invocation, context, state);
        AnalyzePluginRuntimeInvocation(invocation, context, state);
        AnalyzeToolSafetyMetadata(invocation, context);
        AnalyzeRepositoryInvocation(invocation, context);
        AnalyzeRoleAuthorizationInvocation(invocation, context, state);
        AnalyzeInvariantServiceRegistration(invocation, context);
    }

    private static void AnalyzeDynamicInvocation(OperationAnalysisContext context, CompilationState state)
    {
        var operation = context.Operation;
        var anonymousOwner = FindAnonymousOwner(operation);
        var containingMethod = anonymousOwner?.Symbol ?? context.ContainingSymbol as IMethodSymbol;
        if (containingMethod is null)
        {
            return;
        }

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
        var expected = serviceName switch
        {
            "AICopilot.Services.Contracts.ITransactionalExecutionService" =>
                "AICopilot.EntityFrameworkCore.Transactions.IdentityTransactionalExecutionService",
            "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard" =>
                "AICopilot.EntityFrameworkCore.Locking.PostgresIdentityEnabledAdminInvariantGuard",
            _ => null
        };
        if (expected is null ||
            (target.Name == "AddScoped" && string.Equals(implementationName, expected, StringComparison.Ordinal)))
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
        if (!isReduction || context.ContainingSymbol is not IMethodSymbol containingMethod)
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
            attribute.AttributeClass?.ToDisplayString() ==
            "Microsoft.AspNetCore.Authorization.AuthorizeAttribute" &&
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
            target.ContainingNamespace?.ToDisplayString() != "Microsoft.AspNetCore.Authorization")
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
            objectCreation.Type?.ToDisplayString() !=
            "Microsoft.AspNetCore.Authorization.AuthorizeAttribute" ||
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
        if (!IsTrustedCloudReadType(objectCreation.Type) ||
            context.ContainingSymbol is not IMethodSymbol containingMethod)
        {
            return;
        }

        var anonymousOwner = FindAnonymousOwner(objectCreation);
        var owner = Normalize(anonymousOwner?.Symbol ?? containingMethod);
        state.AddMethod(owner);
        state.GetFacts(owner).UsesTrustedCloudReadType = true;
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
                foreach (var property in propertyInitializer.InitializedProperties)
                {
                    state.AddDelegateMemberDefinition(property, propertyInitializer.Value);
                }

                break;
            case ISimpleAssignmentOperation assignment:
                AnalyzeIdentityPropertyAssignment(assignment, context, state);
                switch (assignment.Target)
                {
                    case IFieldReferenceOperation field:
                        state.AddDelegateMemberDefinition(field.Field, assignment.Value);
                        break;
                    case IPropertyReferenceOperation property:
                        state.AddDelegateMemberDefinition(property.Property, assignment.Value);
                        break;
                }

                break;
            case IReturnOperation { ReturnedValue: not null } @return
                when context.ContainingSymbol is IMethodSymbol
                {
                    AssociatedSymbol: IPropertySymbol property
                }:
                state.AddDelegateMemberDefinition(property, @return.ReturnedValue);
                break;
        }
    }

    private static void AnalyzeDatabaseInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (!IsDatabaseApi(target))
        {
            return;
        }

        if (IsDatabaseOwner(state.AssemblyName, context.ContainingSymbol?.ContainingType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PersistenceOwnerRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? state.AssemblyName,
            target.ToDisplayString()));
    }

    private static void AnalyzePluginRuntimeInvocation(
        IInvocationOperation invocation,
        OperationAnalysisContext context,
        CompilationState state)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        var isAssemblyScan = target.Name == "GetTypes" && target.ContainingType.Name == "Assembly";
        var isDiActivation = target.Name == "CreateInstance" && target.ContainingType.Name == "ActivatorUtilities";
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

    private static void AnalyzeToolSafetyMetadata(
        IInvocationOperation invocation,
        OperationAnalysisContext context)
    {
        var target = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (target.Name != "Create" || target.ContainingType.ToDisplayString() != ToolSafetyDescriptorName)
        {
            return;
        }

        if (context.ContainingSymbol?.ContainingType?.ToDisplayString() == ToolSafetyPolicyName)
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
            if (entity is ITypeParameterSymbol || Implements(entity, "IAggregateRoot"))
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
        AnalyzeProjectReferences(context, state);
        AnalyzeCallGraphs(context, state);
    }

    private static void ResolveDeferredDelegateCalls(CompilationState state)
    {
        foreach (var call in state.GetDeferredDelegateCallsSnapshot())
        {
            foreach (var target in GetDelegateTargets(call.DelegateOperation, call.Invocation, state))
            {
                state.AddMethod(target);
                var callerFacts = state.GetFacts(call.Caller);
                var sideEffect = GetForbiddenSideEffect(target);
                if (sideEffect is not null && !IsAuditWrite(target))
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
                    callerFacts.AddCall(target);
                }
            }
        }
    }

    private static void AnalyzeProjectReferences(CompilationAnalysisContext context, CompilationState state)
    {
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
            .Where(method => method.Locations.Any(location => location.IsInSource))
            .ToArray();
        var methodsWithIncomingEdges = GetMethodsWithIncomingEdges(sourceMethods, state);

        foreach (var method in sourceMethods.Where(method =>
                     IsIdentityAnalysisRoot(method, methodsWithIncomingEdges)))
        {
            var reachable = Traverse(method, state, sourceMethods);
            var hasDynamicIdentityPath = reachable.Any(candidate =>
                state.GetFacts(candidate).HasDynamicInvocation && IsIdentityContextMethod(candidate));
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
                     IsCloudReadOnlyOperation(candidate, state.GetFacts(candidate))))
        {
            var reachable = Traverse(method, state, sourceMethods);
            var sideEffect = reachable
                .Select(candidate => state.GetFacts(candidate).ForbiddenSideEffect)
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
                foreach (var target in ResolveDispatchTargets(call, sourceMethods).Where(candidate =>
                             candidate.Locations.Any(location => location.IsInSource)))
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
            state.GetFacts(method).HasDynamicInvocation);

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
                state.GetFacts(method).HasDynamicInvocation)
            {
                return true;
            }

            return state.GetFacts(method).GetCallsSnapshot()
                .SelectMany(call => ResolveDispatchTargets(call, sourceMethods))
                .Where(target => target.Locations.Any(location => location.IsInSource))
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
                             .Where(target => target.Locations.Any(location => location.IsInSource))
                             .Where(target => HasReachableIdentityDecrease(target, state, sourceMethods)))
                {
                    if (!HasOnlyGuardedIdentityDecrease(
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
        IInvocationOperation invocation,
        CompilationState state)
    {
        var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        CollectDelegateTargets(
            delegateOperation,
            invocation,
            state,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            targets);
        return targets;
    }

    private static void CollectDelegateTargets(
        IOperation operation,
        IInvocationOperation transactionInvocation,
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
                    transactionInvocation,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IFieldReferenceOperation fieldReference:
                CollectMemberDelegateTargets(
                    fieldReference.Field,
                    transactionInvocation,
                    state,
                    resolvingLocals,
                    resolvingMembers,
                    targets);
                return;
            case IPropertyReferenceOperation propertyReference:
                CollectMemberDelegateTargets(
                    propertyReference.Property,
                    transactionInvocation,
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
                transactionInvocation,
                state,
                resolvingLocals,
                resolvingMembers,
                targets);
        }
    }

    private static void CollectLocalDelegateTargets(
        ILocalSymbol local,
        IInvocationOperation transactionInvocation,
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
            var root = (IOperation)transactionInvocation;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            var definitions = root.DescendantsAndSelf()
                .Where(candidate => candidate.Syntax.SpanStart < transactionInvocation.Syntax.SpanStart)
                .Select(candidate => candidate switch
                {
                    IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) =>
                        declarator.Initializer?.Value,
                    ISimpleAssignmentOperation assignment
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
                    transactionInvocation,
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
        IInvocationOperation invocation,
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
                    invocation,
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

        if (assemblyName.StartsWith("AICopilot.Core.", StringComparison.Ordinal))
        {
            return Layer.Core;
        }

        if (assemblyName.Contains("Service", StringComparison.Ordinal))
        {
            return Layer.Service;
        }

        if (assemblyName is "AICopilot.AiRuntime" or "AICopilot.ArtifactGeneration" or
            "AICopilot.CloudReadClient" or "AICopilot.Dapper" or "AICopilot.Embedding" or
            "AICopilot.EmbeddingClient" or "AICopilot.EntityFrameworkCore" or
            "AICopilot.EventBus" or "AICopilot.Infrastructure" or "AICopilot.McpRuntime" or
            "AICopilot.SecretProtection" or "AICopilot.SqlSafety")
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
        if (namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Npgsql", StringComparison.Ordinal) ||
            namespaceName == "Dapper" ||
            method.ContainingAssembly?.Name == "Dapper")
        {
            return true;
        }

        return DerivesFrom(method.ContainingType, "DbContext") ||
               DerivesFrom(method.ContainingType, "DbCommand");
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
                   "UserManager") ||
               (method.Name == "DeleteAsync" && DerivesFromDefinition(
                   method.ContainingType,
                   "Microsoft.AspNetCore.Identity",
                   "RoleManager"));
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

        if (method.Name is not ("Remove" or "RemoveAsync" or "Delete" or "DeleteAsync" or "ExecuteDelete" or "ExecuteDeleteAsync"))
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

        return named.OriginalDefinition.ToDisplayString().StartsWith(
                   "Microsoft.AspNetCore.Identity.IdentityUserRole<",
                   StringComparison.Ordinal) ||
               named.TypeArguments.Any(IsIdentityRoleRelationType);
    }

    private static bool IsIdentityContextMethod(IMethodSymbol method)
    {
        static bool IsIdentityType(ITypeSymbol? type) =>
            DerivesFromDefinition(type, "Microsoft.AspNetCore.Identity", "UserManager") ||
            DerivesFromDefinition(type, "Microsoft.AspNetCore.Identity", "RoleManager") ||
            ImplementsFullyQualified(type, "AICopilot.Services.Contracts.ITransactionalExecutionService") ||
            ImplementsFullyQualified(type, "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard") ||
            type?.ToDisplayString() ==
            "AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy";

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

    private static bool IsDelegateType(ITypeSymbol? type) =>
        type?.TypeKind == TypeKind.Delegate;

    private static bool IsCompletionObserved(IOperation operation)
    {
        IOperation current = operation;
        while (current.Parent is IConversionOperation or IParenthesizedOperation)
        {
            current = current.Parent;
        }

        if (current.Parent is IAwaitOperation or IReturnOperation)
        {
            return true;
        }

        return current.Syntax.AncestorsAndSelf().Any(node =>
            node is AwaitExpressionSyntax or ReturnStatementSyntax or ArrowExpressionClauseSyntax);
    }

    private static bool IsTransactionalBoundary(IMethodSymbol method) =>
        (method.Name == "ExecuteAsync" || method.Name == "ExecuteResultAsync") &&
        ImplementsFullyQualified(
            method.ContainingType,
            "AICopilot.Services.Contracts.ITransactionalExecutionService");

    private static bool IsEnabledAdminInvariantAcquire(IMethodSymbol method) =>
        method.Name == "AcquireAsync" &&
        (method.ContainingType.ToDisplayString() ==
             "AICopilot.IdentityService.Authorization.EnabledAdminInvariantPolicy" ||
         ImplementsFullyQualified(
             method.ContainingType,
             "AICopilot.Services.Contracts.IIdentityEnabledAdminInvariantGuard"));

    private static string? GetForbiddenSideEffect(IMethodSymbol method)
    {
        if (method.Name.StartsWith("SaveChanges", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteNonQuery", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteSqlRaw", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteSqlInterpolated", StringComparison.Ordinal) ||
            IsDapperWriteExecution(method) ||
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
        IMethodSymbol caller)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name == "Send" && invocation.Arguments.Length > 0 &&
            ImplementsDefinition(
                invocation.Arguments[0].Value.Type,
                "AICopilot.SharedKernel.Messaging",
                "ICommand"))
        {
            return method.ToDisplayString();
        }

        if (DerivesFromFullyQualified(method.ContainingType, "System.Net.Http.HttpClient"))
        {
            var isWriteVerb = method.Name is "PostAsync" or "PutAsync" or "PatchAsync" or "DeleteAsync";
            var isSend = method.Name == "SendAsync";
            if (isWriteVerb || isSend)
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
            argument.Parameter?.Type.ToDisplayString() == "System.Net.Http.HttpRequestMessage")?.Value;
        return request is not null && IsProvablyGetRequestValue(
            request,
            invocation,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsProvablyGetRequestValue(
        IOperation operation,
        IInvocationOperation invocation,
        ISet<ILocalSymbol> resolvingLocals)
    {
        operation = UnwrapConversion(operation) ?? operation;
        if (operation is IObjectCreationOperation creation &&
            creation.Type?.ToDisplayString() == "System.Net.Http.HttpRequestMessage")
        {
            var methodArgument = creation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Type.ToDisplayString() == "System.Net.Http.HttpMethod")?.Value;
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

            return root.DescendantsAndSelf()
                .Where(candidate => candidate.Syntax.SpanStart < invocation.Syntax.SpanStart)
                .Select(candidate => candidate switch
                {
                    IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local) =>
                        declarator.Initializer?.Value,
                    ISimpleAssignmentOperation assignment
                        when assignment.Target is ILocalReferenceOperation target &&
                             SymbolEqualityComparer.Default.Equals(target.Local, localReference.Local) =>
                        assignment.Value,
                    _ => null
                })
                .Where(value => value is not null)
                .Cast<IOperation>()
                .Any(value => IsProvablyGetRequestValue(value, invocation, resolvingLocals));
        }
        finally
        {
            resolvingLocals.Remove(localReference.Local);
        }
    }

    private static bool IsHttpGetValue(IOperation? operation)
    {
        operation = UnwrapConversion(operation);
        return operation is IPropertyReferenceOperation property &&
               property.Property.Name == "Get" &&
               property.Property.ContainingType.ToDisplayString() == "System.Net.Http.HttpMethod";
    }

    private static bool IsDapperWriteExecution(IMethodSymbol method) =>
        method.ContainingType.OriginalDefinition.ToDisplayString() == "Dapper.SqlMapper" &&
        method.Name is "Execute" or "ExecuteAsync";

    private static bool IsEntityFrameworkBulkWrite(IMethodSymbol method) =>
        method.ContainingType.OriginalDefinition.ToDisplayString() ==
            "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions" &&
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
            "AICopilot.AiGatewayService.AgentTasks.McpAgentToolExecutor";
    }

    private static bool IsAuditWrite(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "AICopilot.Services.Contracts.IAuditLogWriter" ||
        ImplementsFullyQualified(method.ContainingType, "AICopilot.Services.Contracts.IAuditLogWriter");

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
        return IsTrustedCloudReadType(invocation.Type) ||
               IsTrustedCloudReadType(invocation.Instance?.Type) ||
               IsTrustedCloudReadType(target.ContainingType) ||
               IsTrustedCloudReadType(target.ReturnType) ||
               target.TypeArguments.Any(IsTrustedCloudReadType) ||
               target.Parameters.Any(parameter => IsTrustedCloudReadType(parameter.Type)) ||
               invocation.Arguments.Any(argument => IsTrustedCloudReadType(argument.Value.Type));
    }

    private static bool IsTrustedCloudReadType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsTrustedCloudReadType(array.ElementType);
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(IsTrustedCloudReadType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (TrustedCloudReadTypeNames.Contains(named.OriginalDefinition.ToDisplayString()) ||
            FormalCloudReadOnlyWorkflowTypeNames.Contains(named.OriginalDefinition.ToDisplayString()))
        {
            return true;
        }

        return named.TypeArguments.Any(IsTrustedCloudReadType);
    }

    private static bool IsCloudReadClientType(ITypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type is IArrayTypeSymbol array)
        {
            return IsCloudReadClientType(array.ElementType);
        }

        if (type is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.ConstraintTypes.Any(IsCloudReadClientType);
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition.ToDisplayString();
        if (definition is "AICopilot.Services.Contracts.ICloudAiReadClient" or
            "AICopilot.Services.Contracts.ICloudReadOnlyTextToSqlGenerator" or
            "AICopilot.CloudReadClient.CloudAiReadClient")
        {
            return true;
        }

        return named.TypeArguments.Any(IsCloudReadClientType);
    }

    private static bool IsFormalCloudReadOnlyWorkflowSymbol(ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        return type is not null &&
               FormalCloudReadOnlyWorkflowTypeNames.Contains(type.OriginalDefinition.ToDisplayString());
    }

    private static bool IsExternallyReachable(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary &&
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
        method.GetAttributes().Any(attribute =>
            DerivesFromFullyQualified(
                attribute.AttributeClass,
                "Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute"));

    private static bool HasAuthorizationMetadata(ISymbol symbol) =>
        HasAttribute(symbol, AuthorizeAttributeName) || HasAttribute(symbol, AllowAnonymousAttributeName);

    private static bool TryGetResourceAuthorizationOwner(INamedTypeSymbol type, out string? ownerTypeName)
    {
        var attribute = type.GetAttributes().FirstOrDefault(candidate =>
            candidate.AttributeClass?.ToDisplayString() ==
            "AICopilot.Services.CrossCutting.Attributes.ResourceAuthorizationOwnerAttribute");
        if (attribute is null)
        {
            ownerTypeName = null;
            return false;
        }

        ownerTypeName = attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is INamedTypeSymbol ownerType
            ? ownerType.ToDisplayString()
            : null;
        return true;
    }

    private static bool HasAttribute(ISymbol symbol, string fullyQualifiedAttributeName) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == fullyQualifiedAttributeName);

    private static bool IsRepositoryType(INamedTypeSymbol type) =>
        IsRepositoryDefinition(type.OriginalDefinition) ||
        type.AllInterfaces.Any(@interface => IsRepositoryDefinition(@interface.OriginalDefinition));

    private static bool IsRepositoryDefinition(INamedTypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "AICopilot.SharedKernel.Repository" &&
        type.Name is "IRepository" or "IReadRepository";

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

        return named.ToDisplayString() == fullyQualifiedInterfaceName ||
               named.AllInterfaces.Any(@interface => @interface.ToDisplayString() == fullyQualifiedInterfaceName);
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
        type.ContainingNamespace?.ToDisplayString() == fullyQualifiedNamespace;

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
            if (current.ToDisplayString() == fullyQualifiedBaseTypeName)
            {
                return true;
            }
        }

        return false;
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
        !ContainsTestAssemblyMarker(assemblyName);

    private static bool ContainsTestAssemblyMarker(string name)
    {
        var segments = name.Split('.');
        return segments.Any(segment => segment is "Tests" or "Testing" or "TestKit" or "Fakes" or "Mocks");
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

    private static IMethodSymbol Normalize(IMethodSymbol method) =>
        method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;

    private sealed class CompilationState
    {
        private readonly object sync = new();
        private readonly Dictionary<IMethodSymbol, MethodFacts> facts =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> methods = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, List<IOperation>> delegateMemberDefinitions =
            new(SymbolEqualityComparer.Default);
        private readonly List<DeferredDelegateCall> deferredDelegateCalls = [];

        public CompilationState(Compilation compilation)
        {
            Compilation = compilation;
            AssemblyName = compilation.AssemblyName ?? string.Empty;
        }

        public Compilation Compilation { get; }

        public string AssemblyName { get; }

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

    private sealed class MethodFacts
    {
        private readonly object sync = new();
        private readonly HashSet<IMethodSymbol> calls = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> transactionDelegateCalls =
            new(SymbolEqualityComparer.Default);
        private readonly List<InvocationFact> invocations = [];
        private readonly List<IdentityMutationFact> identityMutations = [];

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
