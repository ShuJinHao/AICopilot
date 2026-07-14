using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
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

    private static readonly DiagnosticDescriptor ProjectBoundaryRule = CreateRule(
        ProjectBoundaryId,
        "Project dependency violates the AICopilot layer graph",
        "Project '{0}' must not reference '{1}': {2}");

    private static readonly DiagnosticDescriptor AggregateBoundaryRule = CreateRule(
        AggregateBoundaryId,
        "Aggregate and repository ownership must be explicit",
        "Symbol '{0}' violates the approved aggregate/repository boundary: {1}");

    private static readonly DiagnosticDescriptor PersistenceOwnerRule = CreateRule(
        PersistenceOwnerId,
        "Database technology must stay with its approved owner",
        "'{0}' uses '{1}', which is owned by AICopilot.EntityFrameworkCore, AICopilot.Dapper, or the explicit migration/lock composition boundary");

    private static readonly DiagnosticDescriptor EnabledAdminInvariantRule = CreateRule(
        EnabledAdminInvariantId,
        "Enabled Admin reduction requires the shared invariant transaction",
        "'{0}' can reduce enabled Admin membership but does not reach both ITransactionalExecutionService and the enabled-admin invariant guard");

    private static readonly DiagnosticDescriptor AgentPluginBoundaryRule = CreateRule(
        AgentPluginBoundaryId,
        "Agent plugin capability and host boundaries must be explicit",
        "Plugin symbol '{0}' violates the plugin boundary: {1}");

    private static readonly DiagnosticDescriptor CloudReadOnlyBoundaryRule = CreateRule(
        CloudReadOnlyBoundaryId,
        "Cloud read-only call graphs must not reach side effects",
        "Cloud read-only entry '{0}' can reach forbidden side effect '{1}'");

    private static readonly DiagnosticDescriptor SecurityMetadataRule = CreateRule(
        SecurityMetadataId,
        "Authorization and read-only metadata must not be bypassed",
        "'{0}' must declare valid authorization/read-only metadata: {1}");

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
            "MarkUserDisabled");

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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var state = new CompilationState(startContext.Compilation);

            startContext.RegisterSymbolAction(
                symbolContext => AnalyzeNamedType((INamedTypeSymbol)symbolContext.Symbol, symbolContext, state),
                SymbolKind.NamedType);
            startContext.RegisterSymbolAction(
                symbolContext => state.AddMethod(Normalize((IMethodSymbol)symbolContext.Symbol)),
                SymbolKind.Method);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation((IInvocationOperation)operationContext.Operation, operationContext, state),
                OperationKind.Invocation);
            startContext.RegisterOperationAction(
                operationContext => AnalyzeAnonymousFunction((IAnonymousFunctionOperation)operationContext.Operation, operationContext, state),
                OperationKind.AnonymousFunction);
            startContext.RegisterCompilationEndAction(endContext => AnalyzeCompilation(endContext, state));
        });
    }

    private static DiagnosticDescriptor CreateRule(string id, string title, string message) =>
        new(
            id,
            title,
            message,
            "Architecture",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "AICopilot architecture diagnostics are stable build errors and may only be changed with the corresponding formal contract and executable fixtures.");

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
            .Where(method => HasAttribute(method, "DescriptionAttribute"))
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
            var isStream = Implements(type, "IStreamRequest");
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
                     !HasAttribute(type, "AuthorizeRequirementAttribute"))
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
            !DerivesFrom(type, "ControllerBase") ||
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
        if (context.ContainingSymbol is not IMethodSymbol containingMethod)
        {
            return;
        }

        var caller = Normalize(containingMethod);
        var target = Normalize(invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod);
        state.AddMethod(caller);
        state.GetFacts(caller).AddCall(target);

        if (IsIdentityDecreaseCall(target))
        {
            state.GetFacts(caller).IdentityDecreaseTarget = target.ToDisplayString();
        }

        if (IsTransactionalBoundary(target))
        {
            state.GetFacts(caller).HasTransactionBoundary = true;
        }

        if (IsEnabledAdminInvariantAcquire(target))
        {
            state.GetFacts(caller).HasInvariantAcquire = true;
        }

        var sideEffect = GetForbiddenSideEffect(target) ?? GetInvocationSideEffect(invocation);
        if (sideEffect is not null && !IsAuditWrite(target))
        {
            state.GetFacts(caller).ForbiddenSideEffect = sideEffect;
        }

        AnalyzeDatabaseInvocation(invocation, context, state);
        AnalyzePluginRuntimeInvocation(invocation, context, state);
        AnalyzeToolSafetyMetadata(invocation, context);
        AnalyzeRepositoryInvocation(invocation, context);
    }

    private static void AnalyzeAnonymousFunction(
        IAnonymousFunctionOperation anonymousFunction,
        OperationAnalysisContext context,
        CompilationState state)
    {
        if (context.ContainingSymbol is not IMethodSymbol containingMethod)
        {
            return;
        }

        var caller = Normalize(containingMethod);
        var target = Normalize(anonymousFunction.Symbol);
        state.AddMethod(caller);
        state.AddMethod(target);
        state.GetFacts(caller).AddCall(target);
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
        if (target.Name != "Create" || target.ContainingType.Name != "AiToolSafetyDescriptor")
        {
            return;
        }

        var externalSystem = GetEnumArgumentName(invocation, "externalSystemType");
        if (!string.Equals(externalSystem, "CloudReadOnly", StringComparison.Ordinal))
        {
            return;
        }

        var readOnly = GetBooleanArgument(invocation, "readOnlyDeclared");
        var capability = GetEnumArgumentName(invocation, "capabilityKind");
        if (readOnly == true && !string.Equals(capability, "SideEffecting", StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            SecurityMetadataRule,
            invocation.Syntax.GetLocation(),
            context.ContainingSymbol?.ToDisplayString() ?? target.ToDisplayString(),
            "CloudReadOnly tools must set readOnlyDeclared=true and must not declare SideEffecting capability"));
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
        AnalyzeProjectReferences(context, state);
        AnalyzeCallGraphs(context, state);
    }

    private static void AnalyzeProjectReferences(CompilationAnalysisContext context, CompilationState state)
    {
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

        foreach (var method in sourceMethods.Where(IsExternallyReachable))
        {
            var reachable = Traverse(method, state, sourceMethods);
            var identityDecrease = reachable
                .Select(candidate => state.GetFacts(candidate).IdentityDecreaseTarget)
                .FirstOrDefault(value => value is not null);
            if (identityDecrease is not null)
            {
                var hasTransaction = reachable.Any(candidate => state.GetFacts(candidate).HasTransactionBoundary);
                var hasInvariant = reachable.Any(candidate => state.GetFacts(candidate).HasInvariantAcquire);
                if (!hasTransaction || !hasInvariant)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        EnabledAdminInvariantRule,
                        FirstSourceLocation(method),
                        method.ToDisplayString()));
                }
            }

            if (!IsCloudReadOnlyEntry(method))
            {
                continue;
            }

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

            foreach (var call in state.GetFacts(current).GetCallsSnapshot())
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

        if (assemblyName is "AICopilot.AiRuntime" or "AICopilot.Dapper" or "AICopilot.Embedding" or
            "AICopilot.EntityFrameworkCore" or "AICopilot.EventBus" or "AICopilot.Infrastructure")
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

        return method.Name == "MarkUserDisabled"
            ? method.ContainingType.Name == "IdentityGovernanceHelper"
            : DerivesFrom(method.ContainingType, "UserManager") ||
              method.ContainingType.Name == "UserManager";
    }

    private static bool IsTransactionalBoundary(IMethodSymbol method) =>
        (method.Name == "ExecuteAsync" || method.Name == "ExecuteResultAsync") &&
        (method.ContainingType.Name == "ITransactionalExecutionService" ||
         Implements(method.ContainingType, "ITransactionalExecutionService"));

    private static bool IsEnabledAdminInvariantAcquire(IMethodSymbol method) =>
        method.Name == "AcquireAsync" &&
        (method.ContainingType.Name is "EnabledAdminInvariantPolicy" or "IIdentityEnabledAdminInvariantGuard" ||
         Implements(method.ContainingType, "IIdentityEnabledAdminInvariantGuard"));

    private static string? GetForbiddenSideEffect(IMethodSymbol method)
    {
        if (method.Name.StartsWith("SaveChanges", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteNonQuery", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteSqlRaw", StringComparison.Ordinal) ||
            method.Name.StartsWith("ExecuteSqlInterpolated", StringComparison.Ordinal))
        {
            return method.ToDisplayString();
        }

        if (IsRepositoryType(method.ContainingType) &&
            method.Name is "Add" or "AddAsync" or "Update" or "UpdateAsync" or "Delete" or "DeleteAsync" or "Remove" or "RemoveAsync")
        {
            return method.ToDisplayString();
        }

        if (method.Name == "Send" && method.Parameters.Length > 0 &&
            Implements(method.Parameters[0].Type, "ICommand"))
        {
            return method.ToDisplayString();
        }

        return null;
    }

    private static string? GetInvocationSideEffect(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        if (method.Name == "Send" && invocation.Arguments.Length > 0 &&
            Implements(invocation.Arguments[0].Value.Type, "ICommand"))
        {
            return method.ToDisplayString();
        }

        var containingName = method.ContainingType?.Name ?? string.Empty;
        if (containingName.IndexOf("Mcp", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (method.Name.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0 ||
             method.Name.IndexOf("Create", StringComparison.OrdinalIgnoreCase) >= 0 ||
             method.Name.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
             method.Name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0 ||
             method.Name.IndexOf("Remove", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return method.ToDisplayString();
        }

        return null;
    }

    private static bool IsAuditWrite(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "AICopilot.Services.Contracts.IAuditLogWriter" ||
        ImplementsFullyQualified(method.ContainingType, "AICopilot.Services.Contracts.IAuditLogWriter");

    private static bool IsCloudReadOnlyEntry(IMethodSymbol method)
    {
        if (ContainsCloudReadName(method.Name) || ContainsCloudReadName(method.ContainingType?.Name))
        {
            return true;
        }

        if (method.Parameters.Any(parameter => ContainsCloudReadType(parameter.Type)) ||
            ContainsCloudReadType(method.ReturnType))
        {
            return true;
        }

        var containingType = method.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        if (containingType.AllInterfaces.Any(ContainsCloudReadType))
        {
            return true;
        }

        return containingType.GetMembers()
            .Any(member => member switch
            {
                IFieldSymbol field => ContainsCloudReadType(field.Type),
                IPropertySymbol property => ContainsCloudReadType(property.Type),
                _ => false
            });
    }

    private static bool ContainsCloudReadType(ITypeSymbol? type) =>
        type is not null &&
        (ContainsCloudReadName(type.Name) ||
         ContainsCloudReadName(type.ContainingNamespace?.ToDisplayString()));

    private static bool ContainsCloudReadName(string? value) =>
        value is not null &&
        (value.IndexOf("CloudAiRead", StringComparison.OrdinalIgnoreCase) >= 0 ||
         value.IndexOf("CloudReadonly", StringComparison.OrdinalIgnoreCase) >= 0 ||
         value.IndexOf("CloudReadOnly", StringComparison.OrdinalIgnoreCase) >= 0);

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
        Implements(type, "ICommand") || Implements(type, "IQuery") || Implements(type, "IStreamRequest");

    private static bool IsControllerAction(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary &&
        !method.IsStatic &&
        method.DeclaredAccessibility == Accessibility.Public &&
        method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name.StartsWith("Http", StringComparison.Ordinal) == true &&
            attribute.AttributeClass.Name.EndsWith("Attribute", StringComparison.Ordinal));

    private static bool HasAuthorizationMetadata(ISymbol symbol) =>
        HasAttribute(symbol, "AuthorizeAttribute") || HasAttribute(symbol, "AllowAnonymousAttribute");

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

    private static bool HasAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.Name, attributeName, StringComparison.Ordinal) ||
            string.Equals(attribute.AttributeClass?.Name, attributeName.Replace("Attribute", string.Empty), StringComparison.Ordinal));

    private static bool IsRepositoryType(INamedTypeSymbol type) =>
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
    }

    private sealed class MethodFacts
    {
        private readonly object sync = new();
        private readonly HashSet<IMethodSymbol> calls = new(SymbolEqualityComparer.Default);

        public void AddCall(IMethodSymbol method)
        {
            lock (sync)
            {
                calls.Add(Normalize(method));
            }
        }

        public IReadOnlyCollection<IMethodSymbol> GetCallsSnapshot()
        {
            lock (sync)
            {
                return calls.ToArray();
            }
        }

        public string? IdentityDecreaseTarget { get; set; }

        public bool HasTransactionBoundary { get; set; }

        public bool HasInvariantAcquire { get; set; }

        public string? ForbiddenSideEffect { get; set; }
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
