using System.Linq.Expressions;
using AICopilot.AiGatewayService.Approvals;
using AICopilot.Core.AiGateway.Aggregates.ApprovalPolicy;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.SharedKernel.Ai;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

public sealed class ToolSafetyAndApprovalIdentityTests
{
    [Fact]
    public void CloudReadOnlyToolSafety_ShouldRejectForbiddenWriteVerbs()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "createDevice",
            "Create device in Cloud");

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("forbidden write semantics");
    }

    [Fact]
    public void CloudReadOnlyToolSafety_ShouldAllowReadOnlyVerb()
    {
        var decision = AiToolSafetyPolicy.Evaluate(
            AiToolExternalSystemType.CloudReadOnly,
            AiToolCapabilityKind.ReadOnlyQuery,
            AiToolRiskLevel.Low,
            "queryDeviceLogs",
            "Read Cloud device logs");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovalResolver_ShouldScopeSameToolNameByTargetIdentity()
    {
        var serverAPolicy = new ApprovalPolicy(
            "server-a-query",
            null,
            ApprovalTargetType.McpServer,
            "server-a",
            ["query"],
            isEnabled: true,
            requiresOnsiteAttestation: true);
        var resolver = new ApprovalRequirementResolver(new ApprovalPolicyReadRepository(serverAPolicy));

        var serverA = await resolver.GetMergedRequirementByIdentityAsync(
            new AiToolIdentity(AiToolCallKind.Mcp, AiToolTargetType.McpServer, "server-a", "query"));
        var serverB = await resolver.GetMergedRequirementByIdentityAsync(
            new AiToolIdentity(AiToolCallKind.Mcp, AiToolTargetType.McpServer, "server-b", "query"));

        serverA.RequiresApproval.Should().BeTrue();
        serverA.RequiresOnsiteAttestation.Should().BeTrue();
        serverB.RequiresApproval.Should().BeFalse();
    }

    private sealed class ApprovalPolicyReadRepository(params ApprovalPolicy[] policies) : IReadRepository<ApprovalPolicy>
    {
        private readonly List<ApprovalPolicy> policies = [..policies];

        public Task<List<ApprovalPolicy>> ListAsync(
            ISpecification<ApprovalPolicy>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<ApprovalPolicy?> FirstOrDefaultAsync(
            ISpecification<ApprovalPolicy>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<ApprovalPolicy>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<ApprovalPolicy>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<ApprovalPolicy?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(policies.FirstOrDefault(policy => Equals(policy.Id, id)));
        }

        public Task<List<ApprovalPolicy>> GetListAsync(
            Expression<Func<ApprovalPolicy, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policies.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<ApprovalPolicy, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policies.AsQueryable().Count(expression));
        }

        public Task<ApprovalPolicy?> GetAsync(
            Expression<Func<ApprovalPolicy, bool>> expression,
            Expression<Func<ApprovalPolicy, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policies.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<ApprovalPolicy>> GetListAsync(
            Expression<Func<ApprovalPolicy, bool>> expression,
            Expression<Func<ApprovalPolicy, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(policies.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<ApprovalPolicy> Apply(ISpecification<ApprovalPolicy>? specification)
        {
            var query = policies.AsQueryable();
            return specification?.FilterCondition is null ? query : query.Where(specification.FilterCondition);
        }
    }
}
