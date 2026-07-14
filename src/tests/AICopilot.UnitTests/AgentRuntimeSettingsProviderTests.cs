using System.Linq.Expressions;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.Core.AiGateway.Aggregates.RuntimeSettings;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.UnitTests;

public sealed class AgentRuntimeSettingsProviderTests
{
    [Fact]
    public async Task MissingSettings_ShouldReturnDefaults_WithoutPersistingFromReadPath()
    {
        var repository = new MissingSettingsRepository();
        var provider = new AgentRuntimeSettingsProvider(repository);

        var result = await provider.GetAsync(CancellationToken.None);

        result.Should().Be(new ChatRuntimeSettingsDto(
            RoutingHistoryCount: 4,
            AnswerHistoryCount: 10,
            RagRewriteHistoryCount: 4,
            AgentPlanningHistoryCount: 6,
            ContextTokenLimit: 24000));
        repository.FirstOrDefaultCount.Should().Be(1);
        repository.AddCount.Should().Be(0);
        repository.UpdateCount.Should().Be(0);
        repository.DeleteCount.Should().Be(0);
        repository.SaveChangesCount.Should().Be(0);
    }

    private sealed class MissingSettingsRepository : IRepository<ChatRuntimeSettings>
    {
        public int FirstOrDefaultCount { get; private set; }
        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int SaveChangesCount { get; private set; }

        public ChatRuntimeSettings Add(ChatRuntimeSettings entity)
        {
            AddCount++;
            return entity;
        }

        public void Update(ChatRuntimeSettings entity) => UpdateCount++;

        public void Delete(ChatRuntimeSettings entity) => DeleteCount++;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task<ChatRuntimeSettings?> FirstOrDefaultAsync(
            ISpecification<ChatRuntimeSettings>? specification = null,
            CancellationToken cancellationToken = default)
        {
            FirstOrDefaultCount++;
            return Task.FromResult<ChatRuntimeSettings?>(null);
        }

        public Task<List<ChatRuntimeSettings>> ListAsync(
            ISpecification<ChatRuntimeSettings>? specification = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ChatRuntimeSettings>());

        public Task<int> CountAsync(
            ISpecification<ChatRuntimeSettings>? specification = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> AnyAsync(
            ISpecification<ChatRuntimeSettings>? specification = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<ChatRuntimeSettings?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull => Task.FromResult<ChatRuntimeSettings?>(null);

        public Task<List<ChatRuntimeSettings>> GetListAsync(
            Expression<Func<ChatRuntimeSettings, bool>> expression,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ChatRuntimeSettings>());

        public Task<int> GetCountAsync(
            Expression<Func<ChatRuntimeSettings, bool>> expression,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<ChatRuntimeSettings?> GetAsync(
            Expression<Func<ChatRuntimeSettings, bool>> expression,
            Expression<Func<ChatRuntimeSettings, object>>[]? includes = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatRuntimeSettings?>(null);

        public Task<List<ChatRuntimeSettings>> GetListAsync(
            Expression<Func<ChatRuntimeSettings, bool>> expression,
            Expression<Func<ChatRuntimeSettings, object>>[]? includes = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<ChatRuntimeSettings>());
    }
}
