using System.Linq.Expressions;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.RagService.Commands.KnowledgeBases;
using AICopilot.RagService.EmbeddingModels;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Domain;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

public sealed class RagMcpAuditCommandTests
{
    [Fact]
    public async Task CreateKnowledgeBase_ShouldWriteConfigAuditBeforeSave()
    {
        var embeddingModel = CreateEmbeddingModel("kb-embedding");
        var knowledgeRepository = new InMemoryRepository<KnowledgeBase>();
        var embeddingRepository = new InMemoryRepository<EmbeddingModel>(embeddingModel);
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new CreateKnowledgeBaseCommandHandler(
            knowledgeRepository,
            embeddingRepository,
            auditLogWriter);

        var result = await handler.Handle(
            new CreateKnowledgeBaseCommand("line-docs", "Line runbooks", embeddingModel.Id.Value),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        knowledgeRepository.SaveCount.Should().Be(1);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.CreateKnowledgeBase");
        audit.TargetType.Should().Be("KnowledgeBase");
        audit.TargetName.Should().Be("line-docs");
        audit.ChangedFields.Should().Contain(["name", "description", "embeddingModelId"]);
    }

    [Fact]
    public async Task CreateEmbeddingModel_ShouldWriteRedactedAudit()
    {
        var repository = new InMemoryRepository<EmbeddingModel>();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new CreateEmbeddingModelCommandHandler(repository, auditLogWriter);
        const string secret = "secret-embedding-key";

        var result = await handler.Handle(
            new CreateEmbeddingModelCommand(
                "embedding",
                "OpenAI",
                "https://embedding.example",
                secret,
                "text-embedding-3-small",
                1536,
                8191),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.SaveCount.Should().Be(1);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.CreateEmbeddingModel");
        audit.ChangedFields.Should().Contain("apiKey");
        audit.Summary.Should().NotContain(secret);
        audit.Summary.Should().Contain("apiKey=provided and redacted");
    }

    [Fact]
    public async Task UpdateEmbeddingModel_ShouldRecordApiKeyChangeWithoutSecret()
    {
        var entity = CreateEmbeddingModel("embedding");
        var repository = new InMemoryRepository<EmbeddingModel>(entity);
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UpdateEmbeddingModelCommandHandler(repository, auditLogWriter);
        const string secret = "replacement-embedding-key";

        var result = await handler.Handle(
            new UpdateEmbeddingModelCommand(
                entity.Id.Value,
                "embedding-updated",
                "OpenAI",
                "https://embedding.example",
                secret,
                "text-embedding-3-large",
                3072,
                8191,
                true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.SaveCount.Should().Be(1);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.UpdateEmbeddingModel");
        audit.ChangedFields.Should().Contain(["name", "modelName", "dimensions", "apiKey"]);
        audit.Summary.Should().NotContain(secret);
        audit.Summary.Should().Contain("apiKey=changed and redacted");
    }

    private static EmbeddingModel CreateEmbeddingModel(string name)
    {
        return new EmbeddingModel(
            name,
            "OpenAI",
            "https://embedding.example",
            "text-embedding-3-small",
            1536,
            8191,
            "initial-key");
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

    private sealed class InMemoryRepository<T>(params T[] initialItems) : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        private readonly List<T> items = [..initialItems];

        public int SaveCount { get; private set; }

        public T Add(T entity)
        {
            items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task<List<T>> ListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<T?> FirstOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(items.FirstOrDefault(item => Equals(GetId(item), id)));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(items.AsQueryable().Where(expression).ToList());
        }

        private static object? GetId(T item)
        {
            return typeof(T).GetProperty("Id")?.GetValue(item);
        }

        private IQueryable<T> Apply(ISpecification<T>? specification)
        {
            var query = items.AsQueryable();
            return specification?.FilterCondition is null
                ? query
                : query.Where(specification.FilterCondition);
        }
    }
}
