using System.Linq.Expressions;
using System.Text;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.KnowledgeBases;
using AICopilot.RagService.Queries.KnowledgeBases;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Events;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.Logging.Abstractions;

namespace AICopilot.BackendTests;

[Trait("Suite", "RagPermission")]
public sealed class RagPermissionTests
{
    private static readonly Guid UserAId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid UserBId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    [Fact]
    public async Task ListAndGet_ShouldHideForeignPrivateKnowledgeBases()
    {
        var userPrivate = CreateKnowledgeBase("user-a-private", UserAId);
        var shared = CreateKnowledgeBase("shared", UserBId, KnowledgeBaseAccessScope.AuthenticatedUsers);
        var foreignPrivate = CreateKnowledgeBase("foreign-private", UserBId);
        var repository = new TestRepository<KnowledgeBase>(userPrivate, shared, foreignPrivate);
        var currentUser = new TestCurrentUser(UserAId);

        var list = await new GetListKnowledgeBasesQueryHandler(repository, currentUser)
            .Handle(new GetListKnowledgeBasesQuery(), CancellationToken.None);
        var foreignGet = await new GetKnowledgeBaseQueryHandler(repository, currentUser)
            .Handle(new GetKnowledgeBaseQuery(foreignPrivate.Id), CancellationToken.None);

        list.IsSuccess.Should().BeTrue();
        list.Value!.Select(item => item.Name)
            .Should()
            .BeEquivalentTo("user-a-private", "shared");
        list.Value!.Select(item => item.Name).Should().NotContain("foreign-private");
        foreignGet.Status.Should().Be(ResultStatus.NotFound);
        foreignGet.Value.Should().BeNull();
    }

    [Fact]
    public async Task Search_ShouldReturnNotFoundWithoutVectorLookup_ForForeignPrivateKnowledgeBase()
    {
        var embedding = new EmbeddingModel(
            "embedding",
            "OpenAI",
            "https://example.invalid/v1",
            "text-embedding-test",
            1536,
            8191);
        var foreignPrivate = new KnowledgeBase(
            "foreign-private",
            "private data",
            embedding.Id,
            UserBId,
            KnowledgeBaseAccessScope.OwnerOnly);
        foreignPrivate.AddDocument("secret-doc.txt", "secret-doc.txt", ".txt", "hash");
        var vectorSearch = new CapturingVectorSearchService();

        var result = await new SearchKnowledgeBaseQueryHandler(
                new TestRepository<KnowledgeBase>(foreignPrivate),
                new TestRepository<EmbeddingModel>(embedding),
                new TestRepository<KnowledgeSupplement>(),
                vectorSearch,
                new TestCurrentUser(UserAId))
            .Handle(new SearchKnowledgeBaseQuery(foreignPrivate.Id, "secret"), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        result.Value.Should().BeNull();
        vectorSearch.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RagSupplement_ShouldOverrideOlderDocumentInDefaultRetrieval()
    {
        var embedding = new EmbeddingModel(
            "embedding",
            "OpenAI",
            "https://example.invalid/v1",
            "text-embedding-test",
            1536,
            8191);
        var knowledgeBase = new KnowledgeBase(
            "employee-policy",
            "employee policy",
            embedding.Id,
            UserAId,
            KnowledgeBaseAccessScope.AuthenticatedUsers);
        var document = knowledgeBase.AddDocument(
            "leave-policy.txt",
            "leave-policy.txt",
            ".txt",
            "hash");
        document.MarkAsIndexed();
        var supplement = new KnowledgeSupplement(
            "婚假制度更新",
            "婚假 15 天，自 2026-05-01 起执行。",
            KnowledgeSupplementPriority.CriticalOverride,
            documentId: document.Id);
        var vectorSearch = new CapturingVectorSearchService(
            [
                new KnowledgeVectorSearchResult(
                    "旧制度：婚假 3 天。",
                    0.97,
                    document.Id.Value,
                    document.Name,
                    0)
            ]);

        var result = await new SearchKnowledgeBaseQueryHandler(
                new TestRepository<KnowledgeBase>(knowledgeBase),
                new TestRepository<EmbeddingModel>(embedding),
                new TestRepository<KnowledgeSupplement>(supplement),
                vectorSearch,
                new TestCurrentUser(UserAId))
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "婚假几天", TopK: 3), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.Text.Should().Contain("婚假 15 天");
        item.Text.IndexOf("婚假 15 天", StringComparison.Ordinal)
            .Should()
            .BeLessThan(item.Text.IndexOf("婚假 3 天", StringComparison.Ordinal));
        var hit = item.SupplementHits.Should().ContainSingle().Subject;
        hit.Priority.Should().Be(KnowledgeSupplementPriority.CriticalOverride.ToString());
        hit.ContentHash.Should().StartWith("sha256:");
    }

    [Fact]
    public async Task Upload_ShouldReturnNotFoundBeforeSaving_ForForeignPrivateKnowledgeBase()
    {
        var foreignPrivate = CreateKnowledgeBase("foreign-private", UserBId);
        var fileStorage = new CapturingFileStorage();
        var eventStager = new CapturingEventStager();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UploadDocumentCommandHandler(
            new TestRepository<KnowledgeBase>(foreignPrivate),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt"]),
            eventStager,
            auditLogWriter,
            new TestCurrentUser(UserAId),
            NullLogger<UploadDocumentCommandHandler>.Instance);

        var content = Encoding.UTF8.GetBytes("safe text");
        var result = await handler.Handle(
            new UploadDocumentCommand(
                foreignPrivate.Id,
                new FileUploadStream("foreign.txt", new MemoryStream(content), "text/plain", content.Length)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        fileStorage.SaveCount.Should().Be(0);
        eventStager.StagedMessages.Should().BeEmpty();
        auditLogWriter.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ShouldRejectDangerousFilesAndWriteAudit()
    {
        var knowledgeBase = CreateKnowledgeBase("owned-private", UserAId);
        var fileStorage = new CapturingFileStorage();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UploadDocumentCommandHandler(
            new TestRepository<KnowledgeBase>(knowledgeBase),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt", ".pdf"]),
            new CapturingEventStager(),
            auditLogWriter,
            new TestCurrentUser(UserAId),
            NullLogger<UploadDocumentCommandHandler>.Instance);

        var content = Encoding.UTF8.GetBytes("MZ");
        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id,
                new FileUploadStream("payload.exe", new MemoryStream(content), "application/octet-stream", content.Length)),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        fileStorage.SaveCount.Should().Be(0);
        auditLogWriter.SaveChangesCount.Should().Be(1);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.UploadDocument");
        audit.Result.Should().Be(AuditResults.Rejected);
        audit.TargetName.Should().Be("payload.exe");
    }

    private static KnowledgeBase CreateKnowledgeBase(
        string name,
        Guid ownerUserId,
        KnowledgeBaseAccessScope accessScope = KnowledgeBaseAccessScope.OwnerOnly)
    {
        return new KnowledgeBase(
            name,
            "description",
            EmbeddingModelId.New(),
            ownerUserId,
            accessScope);
    }

    private sealed class TestRepository<T> : IRepository<T>
        where T : class, AICopilot.SharedKernel.Domain.IEntity, AICopilot.SharedKernel.Domain.IAggregateRoot
    {
        private readonly List<T> entities;

        public TestRepository(params T[] entities)
        {
            this.entities = [..entities];
        }

        public T Add(T entity)
        {
            entities.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            entities.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
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
            return Task.FromResult(entities.FirstOrDefault(entity => Equals(ReadId(entity), id)));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Count(expression));
        }

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(entities.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<T> Apply(ISpecification<T>? specification)
        {
            var query = entities.AsQueryable();
            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition);
            }

            if (specification?.OrderBy is not null)
            {
                query = query.OrderBy(specification.OrderBy);
            }

            if (specification?.OrderByDescending is not null)
            {
                query = query.OrderByDescending(specification.OrderByDescending);
            }

            return query;
        }

        private static object? ReadId(T entity)
        {
            return typeof(T).GetProperty("Id")?.GetValue(entity);
        }
    }

    private sealed class CapturingVectorSearchService(
        IReadOnlyList<KnowledgeVectorSearchResult>? results = null) : IKnowledgeVectorSearchService
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<KnowledgeVectorSearchResult>> SearchAsync(
            KnowledgeBase knowledgeBase,
            EmbeddingModel embeddingModel,
            string queryText,
            int topK,
            double minScore,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<KnowledgeVectorSearchResult> defaultResults =
            [
                new("secret fragment", 0.99, 1, "secret-doc.txt", 0)
            ];
            return Task.FromResult(results ?? defaultResults);
        }
    }

    private sealed class FixedDocumentFormatPolicy(IReadOnlyCollection<string> supportedExtensions) : IDocumentFormatPolicy
    {
        public IReadOnlyCollection<string> SupportedExtensions { get; } = supportedExtensions;

        public bool IsSupported(string extension)
        {
            return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class CapturingFileStorage : IFileStorageService
    {
        public int SaveCount { get; private set; }

        public Task<string> SaveAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(fileName);
        }

        public Task<Stream?> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(null);
        }

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingEventStager : IIntegrationEventStager
    {
        public List<object> StagedMessages { get; } = [];

        public void Stage<TEvent>(TEvent message)
            where TEvent : class
        {
            StagedMessages.Add(message);
        }

        public void Stage<TEvent>(Func<TEvent> messageFactory)
            where TEvent : class
        {
            StagedMessages.Add(messageFactory());
        }
    }

    private sealed class CapturingAuditLogWriter : IAuditLogWriter
    {
        public List<AuditLogWriteRequest> Requests { get; } = [];

        public int SaveChangesCount { get; private set; }

        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(Requests.Count);
        }
    }
}
