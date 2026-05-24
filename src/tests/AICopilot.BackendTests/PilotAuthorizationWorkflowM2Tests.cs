using System.Linq.Expressions;
using System.Reflection;
using AICopilot.AiGatewayService.PilotAuthorization;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.Services.Contracts;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "PilotAuthorizationWorkflowM2")]
public sealed class PilotAuthorizationWorkflowM2Tests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ReviewerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset FutureExpiry = DateTimeOffset.UtcNow.AddDays(5);

    [Theory]
    [InlineData("Keep API key abc in the package")]
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz")]
    [InlineData("Connection String=Host=prod;Password=secret")]
    [InlineData("Attach raw payload rows for diagnostics")]
    [InlineData("select * from devices")]
    [InlineData("请保存连接串和原始业务行")]
    [InlineData("Endpoint is https://prod.example.test/api")]
    public async Task DraftGuard_ShouldRejectUnsafeCreate_AndNotPersist(string unsafeText)
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));

        var created = await fixture.CreateHandler.Handle(
            CreateCommand(businessPurpose: unsafeText),
            CancellationToken.None);

        created.Status.Should().Be(ResultStatus.Invalid);
        (await fixture.Repository.CountAsync(cancellationToken: CancellationToken.None)).Should().Be(0);
        fixture.Audit.Requests.Should().ContainSingle(request =>
            request.ActionCode == PilotAuthorizationAuditActions.UnsafeDraftRejected);
        fixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task DraftGuard_ShouldRejectUnsafeUpdate_AndKeepExistingDraft()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var created = await fixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);

        var updated = await fixture.UpdateHandler.Handle(
            UpdateCommand(created.Value!.SubmissionId, businessPurpose: "token=secret-value"),
            CancellationToken.None);

        var stored = await fixture.Repository.FirstOrDefaultAsync(cancellationToken: CancellationToken.None);
        updated.Status.Should().Be(ResultStatus.Invalid);
        stored!.BusinessPurpose.Should().Be("Plan a limited read-only Pilot authorization package.");
        fixture.Audit.Requests.Should().Contain(request =>
            request.ActionCode == PilotAuthorizationAuditActions.UnsafeDraftRejected);
        fixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task Submit_ShouldMachineReject_WhenOwnersAreMissing()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(CreateCommand(dataOwner: "", rollbackOwner: ""), CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.MachineRejected.ToString());
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains("data owner", StringComparison.OrdinalIgnoreCase));
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains("rollback owner", StringComparison.OrdinalIgnoreCase));
        submitted.Value.GateState.Should().Be(PilotAuthorizationGateState.BlockedMissingAuthorizationMaterials);
    }

    [Fact]
    public async Task Submit_ShouldMachineReject_WhenEndpointIsOutsideAllowlist()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(
            CreateCommand(endpointCodes: ["recipe_versions"]),
            CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.MachineRejected.ToString());
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains("Endpoint is not allowed", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("", "business scope")]
    [InlineData(null, "business scope")]
    public async Task Submit_ShouldMachineReject_WhenM7IntakeBusinessScopeIsMissing(
        string? businessScope,
        string expectedReason)
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(
            CreateCommand(businessScope: businessScope),
            CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.MachineRejected.ToString());
        submitted.Value.MachineRejectedReasons.Should().Contain(reason =>
            reason.Contains(expectedReason, StringComparison.OrdinalIgnoreCase));
        submitted.Value.GateState.Should().Be(PilotAuthorizationGateState.BlockedMissingAuthorizationMaterials);
    }

    [Fact]
    public async Task Submit_ShouldMachineReject_WhenCredentialOwnerOrSignedApprovalRefMissing()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(
            CreateCommand(credentialOwner: "", signedApprovalRef: ""),
            CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.MachineRejected.ToString());
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains("credential owner", StringComparison.OrdinalIgnoreCase));
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains("signed approval ref", StringComparison.OrdinalIgnoreCase));
        submitted.Value.GateState.Should().Be(PilotAuthorizationGateState.BlockedMissingAuthorizationMaterials);
    }

    [Fact]
    public async Task Submit_ShouldEnterReviewPending_WhenPackageIsSafe()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.ReviewPending.ToString());
        submitted.Value.MachineRejectedReasons.Should().BeEmpty();
        submitted.Value.GateState.Should().Be(PilotAuthorizationGateState.ReviewPending);
        fixture.Audit.Requests.Select(request => request.ActionCode).Should().Contain(PilotAuthorizationAuditActions.ReviewStarted);
    }

    [Fact]
    public async Task Reviewer_ShouldApprovePlanning_WithoutGrantingExecution()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var reviewerFixture = CreateFixture(Reviewer(ReviewerId), repository);
        var credential = await reviewerFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(draft.Value.SubmissionId, "planning approved", "credential window planning only"),
            CancellationToken.None);
        var limited = await reviewerFixture.ApproveLimitedHandler.Handle(
            new ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand(draft.Value.SubmissionId, "limited planning approved"),
            CancellationToken.None);

        credential.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.ApprovedForCredentialWindowPlanning.ToString());
        limited.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.ApprovedForLimitedPilotExecutionPlanning.ToString());
        limited.Value.GateState.Should().Be(PilotAuthorizationGateState.BlockedUntilExplicitM7Authorization);
        var forbiddenExecutionState = "Execution" + "Granted";
        limited.Value.Status.Should().NotBe(forbiddenExecutionState);
        Enum.GetNames<PilotAuthorizationSubmissionStatus>().Should().NotContain(forbiddenExecutionState);
    }

    [Fact]
    public async Task SelfReview_ShouldBeForbidden_ForApproveRejectRevokeAndExpire()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var selfReviewerFixture = CreateFixture(Reviewer(OwnerId), repository);

        var approve = await selfReviewerFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(draft.Value.SubmissionId),
            CancellationToken.None);
        var reject = await selfReviewerFixture.RejectHandler.Handle(
            new RejectPilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "self reject"),
            CancellationToken.None);
        var revoke = await selfReviewerFixture.RevokeHandler.Handle(
            new RevokePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "self revoke"),
            CancellationToken.None);
        var expire = await selfReviewerFixture.ExpireHandler.Handle(
            new ExpirePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "self expire"),
            CancellationToken.None);

        approve.Status.Should().Be(ResultStatus.Forbidden);
        reject.Status.Should().Be(ResultStatus.Forbidden);
        revoke.Status.Should().Be(ResultStatus.Forbidden);
        expire.Status.Should().Be(ResultStatus.Forbidden);
        ProblemCodeOf(approve).Should().Be("pilot_authorization_self_review_forbidden");
        var stored = await repository.FirstOrDefaultAsync(cancellationToken: CancellationToken.None);
        stored!.Status.Should().Be(PilotAuthorizationSubmissionStatus.ReviewPending);
        selfReviewerFixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task DecisionGuard_ShouldRejectUnsafeDecisionText_WithoutChangingState()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var reviewerFixture = CreateFixture(Reviewer(ReviewerId), repository);
        var result = await reviewerFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(
                draft.Value.SubmissionId,
                "token=unsafe",
                "credential window planning only"),
            CancellationToken.None);

        var stored = await repository.FirstOrDefaultAsync(cancellationToken: CancellationToken.None);
        result.Status.Should().Be(ResultStatus.Invalid);
        stored!.Status.Should().Be(PilotAuthorizationSubmissionStatus.ReviewPending);
        reviewerFixture.Audit.Requests.Should().Contain(request =>
            request.ActionCode == PilotAuthorizationAuditActions.UnsafeDecisionRejected);
        reviewerFixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task OrdinaryUser_ShouldNotApprovePlanning()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var ordinaryFixture = CreateFixture(DefaultUser(Guid.Parse("33333333-3333-4333-8333-333333333333")), repository);
        var result = await ordinaryFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(draft.Value.SubmissionId),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task Expire_ShouldMakeSubmissionTerminal_AndPreventReuse()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var reviewerFixture = CreateFixture(Reviewer(ReviewerId), repository);
        var expired = await reviewerFixture.ExpireHandler.Handle(
            new ExpirePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "authorization window elapsed"),
            CancellationToken.None);
        var repeatExpire = await reviewerFixture.ExpireHandler.Handle(
            new ExpirePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "repeat"),
            CancellationToken.None);
        var resubmit = await ownerFixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value.SubmissionId),
            CancellationToken.None);
        var approve = await reviewerFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(draft.Value.SubmissionId),
            CancellationToken.None);

        expired.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.Expired.ToString());
        repeatExpire.Status.Should().Be(ResultStatus.Invalid);
        resubmit.Status.Should().Be(ResultStatus.Invalid);
        approve.Status.Should().Be(ResultStatus.Invalid);
        reviewerFixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task ExpiryWorker_ShouldExpireDueSubmissions_WithSanitizedAudit()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var audit = new CapturingAuditLogWriter();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository, audit);
        var draft = await ownerFixture.CreateHandler.Handle(
            CreateCommand(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            CancellationToken.None);

        var processed = await PilotAuthorizationExpiryWorker.ExpireDueSubmissionsAsync(
            repository,
            audit,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var stored = await repository.FirstOrDefaultAsync(cancellationToken: CancellationToken.None);
        processed.Should().BeTrue();
        stored!.Id.Value.Should().Be(draft.Value!.SubmissionId);
        stored.Status.Should().Be(PilotAuthorizationSubmissionStatus.Expired);
        audit.Requests.Should().Contain(request => request.ActionCode == PilotAuthorizationAuditActions.Expired);
        audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task RevokedPlanningApproval_ShouldNotBeReused()
    {
        var repository = new MemoryPilotAuthorizationRepository();
        var ownerFixture = CreateFixture(DefaultUser(OwnerId), repository);
        var draft = await ownerFixture.CreateHandler.Handle(CreateCommand(), CancellationToken.None);
        await ownerFixture.SubmitHandler.Handle(new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId), CancellationToken.None);

        var reviewerFixture = CreateFixture(Reviewer(ReviewerId), repository);
        await reviewerFixture.ApproveCredentialHandler.Handle(
            new ApprovePilotAuthorizationCredentialWindowPlanningCommand(draft.Value.SubmissionId),
            CancellationToken.None);
        var revoked = await reviewerFixture.RevokeHandler.Handle(
            new RevokePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "planning window elapsed"),
            CancellationToken.None);
        var resubmit = await ownerFixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value.SubmissionId),
            CancellationToken.None);

        revoked.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.Revoked.ToString());
        resubmit.Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public void Commands_ShouldDeclareM2Permissions()
    {
        PermissionOf<CreatePilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Submit);
        PermissionOf<UpdatePilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Submit);
        PermissionOf<SubmitPilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Submit);
        PermissionOf<GetPilotAuthorizationSubmissionsQuery>().Should().Be(PilotAuthorizationPermissions.View);
        PermissionOf<GetPilotAuthorizationSubmissionQuery>().Should().Be(PilotAuthorizationPermissions.View);
        PermissionOf<ApprovePilotAuthorizationCredentialWindowPlanningCommand>().Should().Be(PilotAuthorizationPermissions.ApprovePlanning);
        PermissionOf<ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommand>().Should().Be(PilotAuthorizationPermissions.ApprovePlanning);
        PermissionOf<RejectPilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Reject);
        PermissionOf<RevokePilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Reject);
        PermissionOf<ExpirePilotAuthorizationSubmissionCommand>().Should().Be(PilotAuthorizationPermissions.Expire);
    }

    private static CreatePilotAuthorizationSubmissionCommand CreateCommand(
        string title = "M2 Pilot authorization",
        string businessPurpose = "Plan a limited read-only Pilot authorization package.",
        IReadOnlyCollection<string>? endpointCodes = null,
        string dataOwner = "DataOwner",
        string rollbackOwner = "RollbackOwner",
        string evidenceSummary = "Sanitized evidence summary only.",
        string rollbackSummary = "Rollback owner confirms stop procedure.",
        string? businessScope = "AICopilot M7 readiness package for read-only operational summaries.",
        string? department = "AI Governance",
        string? pilotOwner = "PilotOwner",
        string? credentialOwner = "CredentialOwner",
        string? signedApprovalRef = "signed-approval-ref-hash",
        DateTimeOffset? expiresAt = null) =>
        new(
            title,
            businessPurpose,
            endpointCodes ?? ["devices", "capacity_summary"],
            50,
            7,
            dataOwner,
            "ToolOwner",
            "FinalOwner",
            rollbackOwner,
            "EmergencyOwner",
            evidenceSummary,
            rollbackSummary,
            businessScope,
            department,
            pilotOwner,
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(3),
            DateTimeOffset.UtcNow.AddHours(3),
            DateTimeOffset.UtcNow.AddHours(4),
            credentialOwner,
            "external-vault-reference-only",
            "sha256-reference-name",
            "hash-ledger",
            signedApprovalRef,
            expiresAt ?? FutureExpiry);

    private static UpdatePilotAuthorizationSubmissionCommand UpdateCommand(
        Guid submissionId,
        string businessPurpose = "Plan a limited read-only Pilot authorization package.") =>
        new(
            submissionId,
            "M2 Pilot authorization",
            businessPurpose,
            ["devices", "capacity_summary"],
            50,
            7,
            "DataOwner",
            "ToolOwner",
            "FinalOwner",
            "RollbackOwner",
            "EmergencyOwner",
            "Sanitized evidence summary only.",
            "Rollback owner confirms stop procedure.",
            "AICopilot M7 readiness package for read-only operational summaries.",
            "AI Governance",
            "PilotOwner",
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow.AddHours(3),
            DateTimeOffset.UtcNow.AddHours(3),
            DateTimeOffset.UtcNow.AddHours(4),
            "CredentialOwner",
            "external-vault-reference-only",
            "sha256-reference-name",
            "hash-ledger",
            "signed-approval-ref-hash",
            FutureExpiry);

    private static PilotAuthorizationTestFixture CreateFixture(
        CurrentUserAccess access,
        MemoryPilotAuthorizationRepository? repository = null,
        CapturingAuditLogWriter? audit = null)
    {
        repository ??= new MemoryPilotAuthorizationRepository();
        audit ??= new CapturingAuditLogWriter();
        var currentUser = new TestCurrentUser(access.UserId, access.RoleName ?? "User", access.UserName);
        var identity = new TestIdentityAccessService(access);
        return new PilotAuthorizationTestFixture(
            repository,
            audit,
            new CreatePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit),
            new UpdatePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit),
            new SubmitPilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, new PilotAuthorizationMachineValidator(), audit),
            new ApprovePilotAuthorizationCredentialWindowPlanningCommandHandler(repository, currentUser, identity, audit),
            new ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommandHandler(repository, currentUser, identity, audit),
            new RejectPilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit),
            new RevokePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit),
            new ExpirePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit));
    }

    private static CurrentUserAccess DefaultUser(Guid userId) =>
        new(userId, $"user-{userId:N}"[..13], "User", [PilotAuthorizationPermissions.Submit, PilotAuthorizationPermissions.View]);

    private static CurrentUserAccess Reviewer(Guid userId) =>
        new(userId, $"reviewer-{userId:N}"[..17], "PilotReviewer",
            [
                PilotAuthorizationPermissions.View,
                PilotAuthorizationPermissions.Review,
                PilotAuthorizationPermissions.ApprovePlanning,
                PilotAuthorizationPermissions.Reject,
                PilotAuthorizationPermissions.Expire,
                PilotAuthorizationPermissions.Audit
            ]);

    private static bool AuditIsSanitized(AuditLogWriteRequest request)
    {
        var text = string.Join(
            "\n",
            request.ActionGroup,
            request.ActionCode,
            request.TargetType,
            request.TargetId,
            request.TargetName,
            request.Result,
            request.Summary,
            string.Join(",", request.ChangedFields ?? []),
            string.Join(",", request.Metadata?.Select(item => $"{item.Key}={item.Value}") ?? []));

        return !text.Contains("api key", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("token", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("sk-", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("raw payload", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("raw rows", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("raw business rows", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("connection string", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("full sql", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("连接串", StringComparison.OrdinalIgnoreCase)
               && !text.Contains("原始业务行", StringComparison.OrdinalIgnoreCase);
    }

    private static string PermissionOf<TRequest>()
    {
        return typeof(TRequest).GetCustomAttribute<AuthorizeRequirementAttribute>()?.Permission
               ?? throw new InvalidOperationException($"{typeof(TRequest).Name} is missing AuthorizeRequirementAttribute.");
    }

    private static string? ProblemCodeOf<T>(Result<T> result)
    {
        return result.Errors?.OfType<ApiProblemDescriptor>().FirstOrDefault()?.Code;
    }

    private sealed record PilotAuthorizationTestFixture(
        MemoryPilotAuthorizationRepository Repository,
        CapturingAuditLogWriter Audit,
        CreatePilotAuthorizationSubmissionCommandHandler CreateHandler,
        UpdatePilotAuthorizationSubmissionCommandHandler UpdateHandler,
        SubmitPilotAuthorizationSubmissionCommandHandler SubmitHandler,
        ApprovePilotAuthorizationCredentialWindowPlanningCommandHandler ApproveCredentialHandler,
        ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommandHandler ApproveLimitedHandler,
        RejectPilotAuthorizationSubmissionCommandHandler RejectHandler,
        RevokePilotAuthorizationSubmissionCommandHandler RevokeHandler,
        ExpirePilotAuthorizationSubmissionCommandHandler ExpireHandler);

    private sealed class TestIdentityAccessService(CurrentUserAccess access) : IIdentityAccessService
    {
        public Task<CurrentUserAccess?> GetCurrentUserAccessAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CurrentUserAccess?>(access.UserId == userId ? access : null);
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(
            string roleName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(access.Permissions);
        }

        public Task SyncRolePermissionsAsync(
            string roleName,
            IEnumerable<string> permissionCodes,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Requests.Count);
        }
    }

    private sealed class MemoryPilotAuthorizationRepository
        : IRepository<PilotAuthorizationSubmission>
    {
        private readonly List<PilotAuthorizationSubmission> items = [];

        public PilotAuthorizationSubmission Add(PilotAuthorizationSubmission entity)
        {
            items.Add(entity);
            return entity;
        }

        public void Update(PilotAuthorizationSubmission entity)
        {
        }

        public void Delete(PilotAuthorizationSubmission entity)
        {
            items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<PilotAuthorizationSubmission>> ListAsync(
            ISpecification<PilotAuthorizationSubmission>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<PilotAuthorizationSubmission?> FirstOrDefaultAsync(
            ISpecification<PilotAuthorizationSubmission>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<PilotAuthorizationSubmission>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<PilotAuthorizationSubmission>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<PilotAuthorizationSubmission?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(items.FirstOrDefault(item => Equals(item.Id, id)));
        }

        public Task<List<PilotAuthorizationSubmission>> GetListAsync(
            Expression<Func<PilotAuthorizationSubmission, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<PilotAuthorizationSubmission, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Count(expression));
        }

        public Task<PilotAuthorizationSubmission?> GetAsync(
            Expression<Func<PilotAuthorizationSubmission, bool>> expression,
            Expression<Func<PilotAuthorizationSubmission, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<PilotAuthorizationSubmission>> GetListAsync(
            Expression<Func<PilotAuthorizationSubmission, bool>> expression,
            Expression<Func<PilotAuthorizationSubmission, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<PilotAuthorizationSubmission> Apply(
            ISpecification<PilotAuthorizationSubmission>? specification)
        {
            return SpecificationEvaluator.GetQuery(items.AsQueryable(), specification);
        }
    }
}
