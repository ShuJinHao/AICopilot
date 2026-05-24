using System.Linq.Expressions;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.RagService.Queries.KnowledgeBases;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Specification;

namespace AICopilot.BackendTests;

[Trait("Suite", "AICopilotM4RagGovernanceLoop")]
public sealed class AICopilotM4RagGovernanceLoopTests
{
    private static readonly Guid UserAId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Search_ShouldExcludeGovernanceBlockedDocuments()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var disabledCategory = new KnowledgeCategory("disabled", "policy", "AuthenticatedUsers", "", 1, isEnabled: false);
        var now = DateTime.UtcNow;

        var allowed = AddIndexedDocument(knowledgeBase, 1, "allowed.txt");
        var softDeleted = AddIndexedDocument(knowledgeBase, 2, "soft-deleted.txt");
        softDeleted.SoftDelete();
        var superseded = AddIndexedDocument(knowledgeBase, 3, "superseded.txt");
        superseded.SupersedeBy(allowed.Id);
        AddIndexedDocument(knowledgeBase, 4, "expired.txt", expiredAt: now.AddDays(-1));
        AddIndexedDocument(knowledgeBase, 5, "future.txt", effectiveAt: now.AddDays(1));
        AddIndexedDocument(knowledgeBase, 6, "forbidden.txt", classification: DocumentClassification.Forbidden);
        AddIndexedDocument(knowledgeBase, 7, "blocked.txt", allowedForFinalPrompt: false);
        AddIndexedDocument(knowledgeBase, 8, "disabled-category.txt", categoryId: disabledCategory.Id);
        var vectorResults = knowledgeBase.Documents
            .Select(document => ToVectorResult(document, $"text from {document.Name}"))
            .ToArray();

        var audit = new CapturingAuditLogWriter();
        var result = await CreateHandler(
                knowledgeBase,
                embedding,
                vectorResults,
                audit,
                categories: [disabledCategory])
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.DocumentId.Should().Be(allowed.Id.Value);
        item.DocumentName.Should().Be(allowed.Name);
        item.GovernanceEvidence!.Citations.Should().ContainSingle().Which.DocumentId.Should().Be(allowed.Id.Value);
        audit.Requests.Should().ContainSingle().Which.Metadata!["filteredVectorHitCount"].Should().Be("7");
    }

    [Fact]
    public async Task Search_ShouldOnlyRecallLatestEffectiveDocumentVersion()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var documentGroupId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var oldDocument = AddIndexedDocument(knowledgeBase, 1, "policy-v1.txt", documentGroupId: documentGroupId, versionNo: 1);
        var newDocument = AddIndexedDocument(knowledgeBase, 2, "policy-v2.txt", documentGroupId: documentGroupId, versionNo: 2);
        var audit = new CapturingAuditLogWriter();

        var result = await CreateHandler(
                knowledgeBase,
                embedding,
                [ToVectorResult(oldDocument, "old policy"), ToVectorResult(newDocument, "new policy")],
                audit)
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.DocumentId.Should().Be(newDocument.Id.Value);
        var metadata = audit.Requests.Should().ContainSingle().Subject.Metadata!;
        metadata["filteredVectorHitCount"].Should().Be("1");
        metadata["warningCodes"].Should().Contain("OUTDATED_DOCUMENT_SKIPPED");
    }

    [Fact]
    public async Task Search_ShouldPrioritizeCriticalSupplementAndExposeSafeEvidence()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var document = AddIndexedDocument(knowledgeBase, 1, "leave-policy.txt", versionNo: 3);
        var supplement = new KnowledgeSupplement(
            "婚假制度更新",
            "婚假 15 天，自 2026-05-01 起执行。",
            KnowledgeSupplementPriority.CriticalOverride,
            documentId: document.Id);
        var audit = new CapturingAuditLogWriter();

        var result = await CreateHandler(
                knowledgeBase,
                embedding,
                [ToVectorResult(document, "旧制度：婚假 3 天。")],
                audit,
                supplements: [supplement])
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "婚假几天"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.Text.IndexOf("婚假 15 天", StringComparison.Ordinal)
            .Should()
            .BeLessThan(item.Text.IndexOf("婚假 3 天", StringComparison.Ordinal));
        item.GovernanceEvidence!.HasGovernanceOverride.Should().BeTrue();
        item.GovernanceEvidence.WarningCodes.Should().Contain("SUPPLEMENT_OVERRIDE_APPLIED");
        var citation = item.GovernanceEvidence.Citations.Should().ContainSingle().Subject;
        citation.DocumentGroupId.Should().Be(document.DocumentGroupId);
        citation.VersionNo.Should().Be(3);
        citation.CitationHash.Should().StartWith("sha256:");
        var hit = item.SupplementHits.Should().ContainSingle().Subject;
        hit.ContentHash.Should().StartWith("sha256:");
        hit.SourceDocumentGroupId.Should().Be(document.DocumentGroupId);
        hit.SourceDocumentVersionNo.Should().Be(3);
        hit.WarningCode.Should().Be("SUPPLEMENT_OVERRIDE_APPLIED");
        audit.Requests.Single().Metadata!["supplementHashes"].Should().Contain(hit.ContentHash);
    }

    [Fact]
    public async Task Search_ShouldIgnoreDisabledAndExpiredSupplements()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var document = AddIndexedDocument(knowledgeBase, 1, "leave-policy.txt");
        var disabled = new KnowledgeSupplement(
            "disabled supplement",
            "disabled content",
            KnowledgeSupplementPriority.CriticalOverride,
            documentId: document.Id,
            isEnabled: false);
        var expired = new KnowledgeSupplement(
            "expired supplement",
            "expired content",
            KnowledgeSupplementPriority.High,
            expiredAt: DateTime.UtcNow.AddDays(-1),
            documentId: document.Id);

        var result = await CreateHandler(
                knowledgeBase,
                embedding,
                [ToVectorResult(document, "current document content")],
                new CapturingAuditLogWriter(),
                supplements: [disabled, expired])
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value.Should().ContainSingle().Subject;
        item.SupplementHits.Should().BeEmpty();
        item.Text.Should().NotContain("disabled content").And.NotContain("expired content");
    }

    [Fact]
    public async Task Search_ShouldEnforceCategoryVisibility()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var authenticatedCategory = new KnowledgeCategory("authenticated", "policy", "AuthenticatedUsers", "", 10);
        var departmentCategory = new KnowledgeCategory("department", "policy", "Department", "D-01", 9);
        var unknownCategory = new KnowledgeCategory("unknown", "policy", "OwnerOnly", "", 8);
        var authenticatedDocument = AddIndexedDocument(knowledgeBase, 1, "authenticated.txt", categoryId: authenticatedCategory.Id);
        var departmentDocument = AddIndexedDocument(knowledgeBase, 2, "department.txt", categoryId: departmentCategory.Id);
        var unknownDocument = AddIndexedDocument(knowledgeBase, 3, "unknown.txt", categoryId: unknownCategory.Id);
        var vectorResults = new[]
        {
            ToVectorResult(authenticatedDocument, "authenticated text"),
            ToVectorResult(departmentDocument, "department text"),
            ToVectorResult(unknownDocument, "unknown text")
        };
        var categories = new[] { authenticatedCategory, departmentCategory, unknownCategory };

        var departmentUserResult = await CreateHandler(
                knowledgeBase,
                embedding,
                vectorResults,
                new CapturingAuditLogWriter(),
                currentUser: new TestCurrentUser(UserAId, cloudDepartmentId: "D-01"),
                categories: categories)
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);
        var otherDepartmentResult = await CreateHandler(
                knowledgeBase,
                embedding,
                vectorResults,
                new CapturingAuditLogWriter(),
                currentUser: new TestCurrentUser(UserAId, cloudDepartmentId: "D-02"),
                categories: categories)
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);
        var adminResult = await CreateHandler(
                knowledgeBase,
                embedding,
                vectorResults,
                new CapturingAuditLogWriter(),
                currentUser: new TestCurrentUser(UserAId, role: "Admin"),
                categories: categories)
            .Handle(new SearchKnowledgeBaseQuery(knowledgeBase.Id, "policy"), CancellationToken.None);

        departmentUserResult.Value!.Select(item => item.DocumentId)
            .Should()
            .BeEquivalentTo([authenticatedDocument.Id.Value, departmentDocument.Id.Value]);
        otherDepartmentResult.Value!.Select(item => item.DocumentId)
            .Should()
            .BeEquivalentTo([authenticatedDocument.Id.Value]);
        adminResult.Value!.Select(item => item.DocumentId)
            .Should()
            .BeEquivalentTo([authenticatedDocument.Id.Value, departmentDocument.Id.Value, unknownDocument.Id.Value]);
    }

    [Fact]
    public async Task RecallAudit_ShouldOnlyPersistSafeSummaryFields()
    {
        var embedding = CreateEmbedding();
        var knowledgeBase = CreateKnowledgeBase(embedding);
        var document = AddIndexedDocument(knowledgeBase, 1, "safe-policy.txt");
        var supplement = new KnowledgeSupplement(
            "safe supplement",
            "raw supplement secret",
            KnowledgeSupplementPriority.CriticalOverride,
            documentId: document.Id);
        var audit = new CapturingAuditLogWriter();

        var result = await CreateHandler(
                knowledgeBase,
                embedding,
                [ToVectorResult(document, "raw payroll row 42 secret salary")],
                audit,
                supplements: [supplement])
            .Handle(
                new SearchKnowledgeBaseQuery(
                    knowledgeBase.Id,
                    "SELECT * FROM payroll where token='" + "s" + "k-testsecret123456' raw payload"),
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        audit.SaveChangesCount.Should().Be(1);
        var request = audit.Requests.Should().ContainSingle().Subject;
        request.ActionCode.Should().Be("Rag.SearchKnowledgeBaseRecall");
        request.Metadata!["queryHash"].Should().StartWith("sha256:");
        var auditText = string.Join(
            "\n",
            request.Summary,
            string.Join("\n", request.Metadata!.Select(item => $"{item.Key}={item.Value}")));
        auditText.Should().NotContain("SELECT * FROM payroll");
        auditText.Should().NotContain("raw payroll row");
        auditText.Should().NotContain("raw supplement secret");
        auditText.Should().NotContain("s" + "k-testsecret");
        auditText.Should().NotContain("raw payload");
        auditText.Should().NotContain("salary");
    }

    private static SearchKnowledgeBaseQueryHandler CreateHandler(
        KnowledgeBase knowledgeBase,
        EmbeddingModel embedding,
        IReadOnlyList<KnowledgeVectorSearchResult> vectorResults,
        CapturingAuditLogWriter auditLogWriter,
        ICurrentUser? currentUser = null,
        IReadOnlyCollection<KnowledgeSupplement>? supplements = null,
        IReadOnlyCollection<KnowledgeCategory>? categories = null)
    {
        return new SearchKnowledgeBaseQueryHandler(
            new TestRepository<KnowledgeBase>(knowledgeBase),
            new TestRepository<EmbeddingModel>(embedding),
            new TestRepository<KnowledgeSupplement>(supplements?.ToArray() ?? Array.Empty<KnowledgeSupplement>()),
            new TestRepository<KnowledgeCategory>(categories?.ToArray() ?? Array.Empty<KnowledgeCategory>()),
            new CapturingVectorSearchService(vectorResults),
            currentUser ?? new TestCurrentUser(UserAId),
            auditLogWriter);
    }

    private static EmbeddingModel CreateEmbedding()
    {
        return new EmbeddingModel(
            "embedding",
            "OpenAI",
            "https://example.invalid/v1",
            "text-embedding-test",
            1536,
            8191);
    }

    private static KnowledgeBase CreateKnowledgeBase(EmbeddingModel embedding)
    {
        return new KnowledgeBase(
            "employee-policy",
            "employee policy",
            embedding.Id,
            UserAId,
            KnowledgeBaseAccessScope.AuthenticatedUsers);
    }

    private static Document AddIndexedDocument(
        KnowledgeBase knowledgeBase,
        int id,
        string name,
        DocumentClassification classification = DocumentClassification.Internal,
        bool allowedForFinalPrompt = true,
        Guid? documentGroupId = null,
        int versionNo = 1,
        DateTime? effectiveAt = null,
        DateTime? expiredAt = null,
        KnowledgeCategoryId? categoryId = null)
    {
        var document = knowledgeBase.AddDocument(
            name,
            name,
            ".txt",
            $"hash-{id}",
            classification: classification,
            allowedForFinalPrompt: allowedForFinalPrompt,
            documentGroupId: documentGroupId,
            versionNo: versionNo,
            effectiveAt: effectiveAt,
            expiredAt: expiredAt,
            categoryId: categoryId);
        typeof(Document).GetProperty(nameof(Document.Id))!.SetValue(document, new DocumentId(id));
        document.MarkAsIndexed();
        return document;
    }

    private static KnowledgeVectorSearchResult ToVectorResult(Document document, string text)
    {
        return new KnowledgeVectorSearchResult(
            text,
            0.95,
            document.Id.Value,
            document.Name,
            0);
    }

    private sealed class CapturingVectorSearchService(
        IReadOnlyList<KnowledgeVectorSearchResult> results) : IKnowledgeVectorSearchService
    {
        public Task<IReadOnlyList<KnowledgeVectorSearchResult>> SearchAsync(
            KnowledgeBase knowledgeBase,
            EmbeddingModel embeddingModel,
            string queryText,
            int topK,
            double minScore,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(results);
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
}
