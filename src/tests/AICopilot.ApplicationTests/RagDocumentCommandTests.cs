using System.Linq.Expressions;
using System.Security.Cryptography;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.Documents;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using AICopilot.Services.Contracts.Events;
using AICopilot.Services.Contracts;

namespace AICopilot.ApplicationTests;

public sealed class RagDocumentCommandTests
{
    [Fact]
    public async Task UploadDocument_ShouldRejectUnsupportedExtensionBeforeSaving()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var fileStorage = new CapturingFileStorage();
        var eventStager = new CapturingEventStager();
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt", ".md", ".pdf"]),
            eventStager,
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("unsupported.exe", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        knowledgeBase.Documents.Should().BeEmpty();
        fileStorage.SaveCount.Should().Be(0);
        eventStager.StagedCount.Should().Be(0);
    }

    [Fact]
    public async Task UploadDocument_ShouldPersistGovernanceMetadata()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var eventStager = new CapturingEventStager();
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(),
            new CapturingFileStorage(),
            new FixedDocumentFormatPolicy([".txt"]),
            eventStager,
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("runbook.txt", new MemoryStream([1, 2, 3])),
                DocumentClassification.Sensitive.ToString(),
                DocumentSourceType.Runbook.ToString(),
                IsSanitized: true,
                AllowedForFinalPrompt: false,
                BlockedReason: " contains secrets "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var document = knowledgeBase.Documents.Should().ContainSingle().Subject;
        document.Classification.Should().Be(DocumentClassification.Sensitive);
        document.SourceType.Should().Be(DocumentSourceType.Runbook);
        document.IsSanitized.Should().BeTrue();
        document.AllowedForFinalPrompt.Should().BeFalse();
        document.BlockedReason.Should().Be("contains secrets");
        document.CanEnterFinalPrompt(DateTime.UtcNow).Should().BeFalse();
        eventStager.StagedMessages.Should().ContainSingle()
            .Which.Should().BeOfType<DocumentUploadedEvent>();
    }

    [Fact]
    public async Task UploadDocument_ShouldStageUploadedEventBeforeSavingAggregate()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var saveCountObservedAtStage = -1;
        var eventStager = new CapturingEventStager(_ => saveCountObservedAtStage = repository.SaveCount);
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(),
            new CapturingFileStorage(),
            new FixedDocumentFormatPolicy([".txt"]),
            eventStager,
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("runbook.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.SaveCount.Should().Be(1);
        saveCountObservedAtStage.Should().Be(0);
        var message = eventStager.StagedMessages.Should().ContainSingle().Subject.Should().BeOfType<DocumentUploadedEvent>().Subject;
        var document = knowledgeBase.Documents.Should().ContainSingle().Subject;
        message.DocumentId.Should().Be(document.Id.Value);
        message.KnowledgeBaseId.Should().Be(knowledgeBase.Id.Value);
        message.FilePath.Should().Be(document.FilePath);
        message.FileName.Should().Be("runbook.txt");
    }

    [Fact]
    public async Task UploadDocument_ShouldDeleteSavedFile_WhenStagingFails()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var fileStorage = new CapturingFileStorage();
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt"]),
            new ThrowingEventStager(new InvalidOperationException("stage failed")),
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        Func<Task> act = async () => await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("runbook.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("stage failed");
        repository.SaveCount.Should().Be(0);
        fileStorage.DeleteCount.Should().Be(1);
        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be("runbook.txt");
    }

    [Fact]
    public async Task UploadDocument_ShouldDeleteSavedFile_WhenRepositorySaveFails()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(
            _ => throw new InvalidOperationException("database save failed"),
            knowledgeBase);
        var fileStorage = new CapturingFileStorage();
        var eventStager = new CapturingEventStager();
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt"]),
            eventStager,
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        Func<Task> act = async () => await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("runbook.txt", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("database save failed");
        eventStager.StagedCount.Should().Be(1);
        repository.SaveCount.Should().Be(1);
        fileStorage.DeleteCount.Should().Be(1);
        fileStorage.DeletedPaths.Should().ContainSingle().Which.Should().Be("runbook.txt");
    }

    [Fact]
    public async Task UploadDocument_ShouldReturnExistingDocumentWithoutFileOrOutboxSideEffects_WhenHashAlreadyExists()
    {
        var content = new byte[] { 1, 2, 3 };
        var fileHash = Sha256Hex(content);
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var existingDocument = knowledgeBase.AddDocument(
            new DocumentId(1),
            "existing.txt",
            "existing.txt",
            ".txt",
            fileHash);
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var fileStorage = new CapturingFileStorage();
        var eventStager = new CapturingEventStager();
        var handler = new UploadDocumentCommandHandler(
            repository,
            new SequentialDocumentIdAllocator(2),
            fileStorage,
            new FixedDocumentFormatPolicy([".txt"]),
            eventStager,
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("duplicate.txt", new MemoryStream(content))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(existingDocument.Id.Value);
        knowledgeBase.Documents.Should().ContainSingle();
        repository.SaveCount.Should().Be(0);
        fileStorage.SaveCount.Should().Be(0);
        eventStager.StagedCount.Should().Be(0);
    }

    [Fact]
    public void DocumentGovernance_ShouldDefaultToCurrentBehaviorAndFilterUnsafeDocuments()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var document = knowledgeBase.AddDocument(
            new DocumentId(1),
            "doc.txt",
            "doc.txt",
            ".txt",
            "hash");
        var now = DateTime.UtcNow;

        document.Classification.Should().Be(DocumentClassification.Internal);
        document.SourceType.Should().Be(DocumentSourceType.UserUploaded);
        document.AllowedForFinalPrompt.Should().BeTrue();
        document.StartParsing();
        document.CompleteParsing();
        document.AddChunk(0, "chunk");
        document.StartEmbedding();
        document.MarkAsIndexed();
        document.CanEnterFinalPrompt(now).Should().BeTrue();

        document.ConfigureGovernance(
            DocumentClassification.Internal,
            DocumentSourceType.UserUploaded,
            isSanitized: false,
            reviewedBy: null,
            reviewedAt: null,
            effectiveFrom: null,
            effectiveTo: null,
            allowedForFinalPrompt: false,
            blockedReason: null);
        document.CanEnterFinalPrompt(now).Should().BeFalse();

        document.ConfigureGovernance(
            DocumentClassification.Forbidden,
            DocumentSourceType.UserUploaded,
            isSanitized: false,
            reviewedBy: null,
            reviewedAt: null,
            effectiveFrom: null,
            effectiveTo: null,
            allowedForFinalPrompt: true,
            blockedReason: null);
        document.CanEnterFinalPrompt(now).Should().BeFalse();

        document.ConfigureGovernance(
            DocumentClassification.Internal,
            DocumentSourceType.UserUploaded,
            isSanitized: false,
            reviewedBy: null,
            reviewedAt: null,
            effectiveFrom: now.AddMinutes(1),
            effectiveTo: null,
            allowedForFinalPrompt: true,
            blockedReason: null);
        document.CanEnterFinalPrompt(now).Should().BeFalse();

        document.ConfigureGovernance(
            DocumentClassification.Internal,
            DocumentSourceType.UserUploaded,
            isSanitized: false,
            reviewedBy: null,
            reviewedAt: null,
            effectiveFrom: null,
            effectiveTo: now.AddMinutes(-1),
            allowedForFinalPrompt: true,
            blockedReason: null);
        document.CanEnterFinalPrompt(now).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDocumentGovernance_ShouldOnlyChangeGovernanceMetadata()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        document.StartParsing();
        document.CompleteParsing();
        document.AddChunk(0, "chunk");
        document.StartEmbedding();
        document.MarkAsIndexed();
        var originalFilePath = document.FilePath;
        var originalFileHash = document.FileHash;
        var originalStatus = document.Status;
        var originalChunkCount = document.ChunkCount;
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new UpdateDocumentGovernanceCommandHandler(
            new MutableKnowledgeBaseRepository(knowledgeBase),
            auditLogWriter,
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UpdateDocumentGovernanceCommand(
                document.Id,
                DocumentClassification.Forbidden.ToString(),
                DocumentSourceType.External.ToString(),
                IsSanitized: true,
                EffectiveFrom: DateTime.UtcNow.AddDays(-1),
                EffectiveTo: DateTime.UtcNow.AddDays(1),
                AllowedForFinalPrompt: false,
                BlockedReason: " contains unsafe examples "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.Classification.Should().Be(DocumentClassification.Forbidden);
        document.SourceType.Should().Be(DocumentSourceType.External);
        document.IsSanitized.Should().BeTrue();
        document.AllowedForFinalPrompt.Should().BeFalse();
        document.BlockedReason.Should().Be("contains unsafe examples");
        document.FilePath.Should().Be(originalFilePath);
        document.FileHash.Should().Be(originalFileHash);
        document.Status.Should().Be(originalStatus);
        document.ChunkCount.Should().Be(originalChunkCount);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.UpdateDocumentGovernance");
        audit.TargetId.Should().Be(document.Id.ToString());
        audit.ChangedFields.Should().Contain(["classification", "sourceType", "isSanitized", "effectiveFrom", "effectiveTo", "allowedForFinalPrompt", "blockedReason"]);
    }

    [Fact]
    public async Task UpdateDocumentGovernance_ShouldRejectInvalidEffectiveRange()
    {
        var (_, document) = CreateKnowledgeBaseWithDocument();
        var handler = new UpdateDocumentGovernanceCommandHandler(
            new MutableKnowledgeBaseRepository(),
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UpdateDocumentGovernanceCommand(
                document.Id,
                DocumentClassification.Internal.ToString(),
                DocumentSourceType.UserUploaded.ToString(),
                IsSanitized: false,
                EffectiveFrom: DateTime.UtcNow.AddDays(1),
                EffectiveTo: DateTime.UtcNow.AddDays(-1),
                AllowedForFinalPrompt: true,
                BlockedReason: null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task UpdateDocumentGovernance_ShouldRejectInvalidClassification()
    {
        var (_, document) = CreateKnowledgeBaseWithDocument();
        var handler = new UpdateDocumentGovernanceCommandHandler(
            new MutableKnowledgeBaseRepository(),
            new CapturingAuditLogWriter(),
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(
            new UpdateDocumentGovernanceCommand(
                document.Id,
                "UnknownClassification",
                DocumentSourceType.UserUploaded.ToString(),
                IsSanitized: false,
                EffectiveFrom: null,
                EffectiveTo: null,
                AllowedForFinalPrompt: true,
                BlockedReason: null),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task DeleteDocument_ShouldRemoveAggregateDocumentAndStageFileDeletionEvent()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        var eventStager = new CapturingEventStager();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new DeleteDocumentCommandHandler(
            new MutableKnowledgeBaseRepository(knowledgeBase),
            eventStager,
            auditLogWriter,
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(new DeleteDocumentCommand(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        knowledgeBase.Documents.Should().ContainSingle().Which.Status.Should().Be(DocumentStatus.SoftDeleted);
        var message = eventStager.StagedMessages.Should().ContainSingle().Subject
            .Should().BeOfType<DocumentFileDeletionRequestedEvent>().Subject;
        message.DocumentId.Should().Be(document.Id.Value);
        message.KnowledgeBaseId.Should().Be(knowledgeBase.Id.Value);
        message.FilePath.Should().Be(document.FilePath);
        message.FileName.Should().Be(document.Name);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.DeleteDocument");
        audit.TargetId.Should().Be(document.Id.ToString());
        audit.TargetName.Should().Be(document.Name);
    }

    [Fact]
    public async Task DeleteDocument_ShouldSucceedWithoutStagingCleanup_WhenDocumentDoesNotExist()
    {
        var eventStager = new CapturingEventStager();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new DeleteDocumentCommandHandler(
            new MutableKnowledgeBaseRepository(),
            eventStager,
            auditLogWriter,
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(new DeleteDocumentCommand(404), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        eventStager.StagedMessages.Should().BeEmpty();
        auditLogWriter.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RetryDocumentIndexing_ShouldStageIndexingEventForFailedDocument()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        document.MarkAsFailed("parse failed");
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var eventStager = new CapturingEventStager();
        var auditLogWriter = new CapturingAuditLogWriter();
        var handler = new RetryDocumentIndexingCommandHandler(
            repository,
            eventStager,
            auditLogWriter,
            new TestCurrentUser(role: "Admin"));

        var result = await handler.Handle(new RetryDocumentIndexingCommand(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        document.Status.Should().Be(DocumentStatus.Failed);
        repository.SaveCount.Should().Be(1);
        var message = eventStager.StagedMessages.Should().ContainSingle().Subject
            .Should().BeOfType<DocumentUploadedEvent>().Subject;
        message.DocumentId.Should().Be(document.Id.Value);
        message.KnowledgeBaseId.Should().Be(knowledgeBase.Id.Value);
        message.FilePath.Should().Be(document.FilePath);
        message.FileName.Should().Be(document.Name);
        var audit = auditLogWriter.Requests.Should().ContainSingle().Subject;
        audit.ActionCode.Should().Be("Rag.RetryDocumentIndexing");
        audit.TargetId.Should().Be(document.Id.ToString());
    }

    private static (KnowledgeBase KnowledgeBase, Document Document) CreateKnowledgeBaseWithDocument()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var document = knowledgeBase.AddDocument(
            new DocumentId(1),
            "doc.txt",
            "doc.txt",
            ".txt",
            "hash");

        return (knowledgeBase, document);
    }
    private static string Sha256Hex(byte[] content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(content);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
    private sealed class CapturingEventStager(Action<object>? onStage = null) : IIntegrationEventStager
    {
        public int StagedCount => StagedMessages.Count;

        public List<object> StagedMessages { get; } = [];

        public void Stage<TEvent>(Func<TEvent> messageFactory)
            where TEvent : class
        {
            var message = messageFactory();
            StagedMessages.Add(message);
            onStage?.Invoke(message);
        }
    }

    private sealed class ThrowingEventStager(Exception exception) : IIntegrationEventStager
    {
        public void Stage<TEvent>(Func<TEvent> messageFactory)
            where TEvent : class
        {
            throw exception;
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

    private sealed class MutableKnowledgeBaseRepository : IRepository<KnowledgeBase>
    {
        private readonly List<KnowledgeBase> knowledgeBases;
        private readonly Func<CancellationToken, Task<int>> saveChanges;

        public MutableKnowledgeBaseRepository(params KnowledgeBase[] knowledgeBases)
            : this(_ => Task.FromResult(1), knowledgeBases)
        {
        }

        public MutableKnowledgeBaseRepository(
            Func<CancellationToken, Task<int>> saveChanges,
            params KnowledgeBase[] knowledgeBases)
        {
            this.knowledgeBases = [.. knowledgeBases];
            this.saveChanges = saveChanges;
        }

        public int SaveCount { get; private set; }

        public KnowledgeBase Add(KnowledgeBase entity)
        {
            knowledgeBases.Add(entity);
            return entity;
        }

        public void Update(KnowledgeBase entity)
        {
        }

        public void Delete(KnowledgeBase entity)
        {
            knowledgeBases.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return saveChanges(cancellationToken);
        }

        public Task<List<KnowledgeBase>> ListAsync(
            ISpecification<KnowledgeBase>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).ToList());
        }

        public Task<KnowledgeBase?> FirstOrDefaultAsync(
            ISpecification<KnowledgeBase>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).FirstOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<KnowledgeBase>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<KnowledgeBase>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Apply(specification).Any());
        }

        public Task<KnowledgeBase?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
        {
            return Task.FromResult(knowledgeBases.FirstOrDefault(knowledgeBase => Equals(knowledgeBase.Id, id)));
        }

        public Task<List<KnowledgeBase>> GetListAsync(
            Expression<Func<KnowledgeBase, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(knowledgeBases.AsQueryable().Where(expression).ToList());
        }

        public Task<int> GetCountAsync(
            Expression<Func<KnowledgeBase, bool>> expression,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(knowledgeBases.AsQueryable().Count(expression));
        }

        public Task<KnowledgeBase?> GetAsync(
            Expression<Func<KnowledgeBase, bool>> expression,
            Expression<Func<KnowledgeBase, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(knowledgeBases.AsQueryable().FirstOrDefault(expression));
        }

        public Task<List<KnowledgeBase>> GetListAsync(
            Expression<Func<KnowledgeBase, bool>> expression,
            Expression<Func<KnowledgeBase, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(knowledgeBases.AsQueryable().Where(expression).ToList());
        }

        private IQueryable<KnowledgeBase> Apply(ISpecification<KnowledgeBase>? specification)
        {
            var query = knowledgeBases.AsQueryable();
            if (specification?.FilterCondition is null)
            {
                return query;
            }

            return query.Where(specification.FilterCondition);
        }
    }
}
