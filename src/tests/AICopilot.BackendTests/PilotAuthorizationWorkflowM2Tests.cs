using System.Reflection;
using AICopilot.AiGatewayService.PilotAuthorization;
using AICopilot.Core.AiGateway.Aggregates.PilotAuthorization;
using AICopilot.EntityFrameworkCore.Specification;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using AICopilot.Services.CrossCutting.Attributes;
using System.Linq.Expressions;

namespace AICopilot.BackendTests;

[Trait("Suite", "PilotAuthorizationWorkflowM2")]
public sealed class PilotAuthorizationWorkflowM2Tests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ReviewerId = Guid.Parse("22222222-2222-4222-8222-222222222222");

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
    }

    [Theory]
    [InlineData("Keep API key abc in the package", "Sensitive or unrestricted execution wording")]
    [InlineData("Attach raw payload rows for diagnostics", "Sensitive or unrestricted execution wording")]
    [InlineData("Use recipe version endpoint", "Recipe/version scope")]
    public async Task Submit_ShouldMachineReject_WhenSensitiveOrBlockedWordingAppears(string purpose, string expectedReason)
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));
        var draft = await fixture.CreateHandler.Handle(CreateCommand(businessPurpose: purpose), CancellationToken.None);

        var submitted = await fixture.SubmitHandler.Handle(
            new SubmitPilotAuthorizationSubmissionCommand(draft.Value!.SubmissionId),
            CancellationToken.None);

        submitted.Value!.Status.Should().Be(PilotAuthorizationSubmissionStatus.MachineRejected.ToString());
        submitted.Value.MachineRejectedReasons.Should().Contain(reason => reason.Contains(expectedReason, StringComparison.OrdinalIgnoreCase));
        fixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
    }

    [Fact]
    public async Task DtoAndAudit_ShouldRedactSensitiveMaterial()
    {
        var fixture = CreateFixture(DefaultUser(OwnerId));

        var created = await fixture.CreateHandler.Handle(
            CreateCommand(
                title: "Token sk-sensitive-value",
                businessPurpose: "Keep API key abc in the package.",
                evidenceSummary: "Attach raw payload and connection string details.",
                rollbackSummary: "Include full SQL for rollback."),
            CancellationToken.None);

        created.Value!.Title.Should().Contain("[redacted]").And.NotContainEquivalentOf("token");
        created.Value.BusinessPurpose.Should().Contain("[redacted]").And.NotContainEquivalentOf("api key");
        created.Value.EvidenceSummary.Should().Contain("[redacted]").And.NotContainEquivalentOf("raw payload");
        created.Value.EvidenceSummary.Should().NotContainEquivalentOf("connection string");
        created.Value.RollbackSummary.Should().Contain("[redacted]").And.NotContainEquivalentOf("full sql");
        fixture.Audit.Requests.Should().OnlyContain(request => AuditIsSanitized(request));
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
        var forbiddenExecutionState = "Execution" + "Granted";
        limited.Value.Status.Should().NotBe(forbiddenExecutionState);
        Enum.GetNames<PilotAuthorizationSubmissionStatus>().Should().NotContain(forbiddenExecutionState);
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
            new RevokePilotAuthorizationSubmissionCommand(draft.Value.SubmissionId, "planning window expired"),
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
    }

    private static CreatePilotAuthorizationSubmissionCommand CreateCommand(
        string title = "M2 Pilot authorization",
        string businessPurpose = "Plan a limited read-only Pilot authorization package.",
        IReadOnlyCollection<string>? endpointCodes = null,
        string dataOwner = "DataOwner",
        string rollbackOwner = "RollbackOwner",
        string evidenceSummary = "Sanitized evidence summary only.",
        string rollbackSummary = "Rollback owner confirms stop procedure.") =>
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
            rollbackSummary);

    private static PilotAuthorizationTestFixture CreateFixture(
        CurrentUserAccess access,
        MemoryPilotAuthorizationRepository? repository = null)
    {
        repository ??= new MemoryPilotAuthorizationRepository();
        var currentUser = new TestCurrentUser(access.UserId, access.RoleName ?? "User", access.UserName);
        var identity = new TestIdentityAccessService(access);
        var audit = new CapturingAuditLogWriter();
        return new PilotAuthorizationTestFixture(
            repository,
            audit,
            new CreatePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit),
            new SubmitPilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, new PilotAuthorizationMachineValidator(), audit),
            new ApprovePilotAuthorizationCredentialWindowPlanningCommandHandler(repository, currentUser, identity, audit),
            new ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommandHandler(repository, currentUser, identity, audit),
            new RevokePilotAuthorizationSubmissionCommandHandler(repository, currentUser, identity, audit));
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
               && !text.Contains("full sql", StringComparison.OrdinalIgnoreCase);
    }

    private static string PermissionOf<TRequest>()
    {
        return typeof(TRequest).GetCustomAttribute<AuthorizeRequirementAttribute>()?.Permission
               ?? throw new InvalidOperationException($"{typeof(TRequest).Name} is missing AuthorizeRequirementAttribute.");
    }

    private sealed record PilotAuthorizationTestFixture(
        MemoryPilotAuthorizationRepository Repository,
        CapturingAuditLogWriter Audit,
        CreatePilotAuthorizationSubmissionCommandHandler CreateHandler,
        SubmitPilotAuthorizationSubmissionCommandHandler SubmitHandler,
        ApprovePilotAuthorizationCredentialWindowPlanningCommandHandler ApproveCredentialHandler,
        ApprovePilotAuthorizationLimitedPilotExecutionPlanningCommandHandler ApproveLimitedHandler,
        RevokePilotAuthorizationSubmissionCommandHandler RevokeHandler);

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
