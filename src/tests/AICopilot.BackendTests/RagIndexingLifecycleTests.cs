using System.Linq.Expressions;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using AICopilot.Core.Rag.Ids;
using AICopilot.RagService.Commands.Documents;
using AICopilot.RagService.Documents;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;
using AICopilot.SharedKernel.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AICopilot.BackendTests;

public sealed class RagIndexingLifecycleTests
{
    [Fact]
    public async Task UploadDocument_ShouldRejectUnsupportedExtensionBeforeSaving()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        var fileStorage = new CapturingFileStorage();
        var eventPublisher = new CapturingEventPublisher();
        var handler = new UploadDocumentCommandHandler(
            repository,
            fileStorage,
            new FixedDocumentFormatPolicy([".txt", ".md", ".pdf"]),
            eventPublisher);

        var result = await handler.Handle(
            new UploadDocumentCommand(
                knowledgeBase.Id.Value,
                new FileUploadStream("unsupported.exe", new MemoryStream([1, 2, 3]))),
            CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        knowledgeBase.Documents.Should().BeEmpty();
        fileStorage.SaveCount.Should().Be(0);
        eventPublisher.PublishedCount.Should().Be(0);
    }

    [Fact]
    public async Task ParsingDocument_ShouldRecoverAndFinishIndexing()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        document.StartParsing();

        var vectorWriter = new CapturingVectorWriter();
        var service = CreateService(
            knowledgeBase,
            new StubContentExtractor("可恢复的文档内容"),
            new StubTextSplitter(["chunk-1", "chunk-2"]),
            vectorWriter);

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Indexed);
        document.ErrorMessage.Should().BeNull();
        document.Chunks.Select(chunk => chunk.Content).Should().Equal("chunk-1", "chunk-2");
        vectorWriter.Requests.Should().ContainSingle().Which.PreviousChunkCount.Should().Be(0);
    }

    [Fact]
    public async Task EmbeddingDocument_ShouldRecoverAndPassPreviousChunkCountForCleanup()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        PutDocumentInEmbeddingState(document, ["old-0", "old-1", "old-2"]);

        var vectorWriter = new CapturingVectorWriter();
        var service = CreateService(
            knowledgeBase,
            new StubContentExtractor("重新索引后的文档内容"),
            new StubTextSplitter(["new-0"]),
            vectorWriter);

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Indexed);
        document.Chunks.Select(chunk => chunk.Content).Should().Equal("new-0");
        var request = vectorWriter.Requests.Should().ContainSingle().Subject;
        request.PreviousChunkCount.Should().Be(3);
        vectorWriter.WrittenChunks.Should().ContainSingle().Which.Should().Equal("new-0");
    }

    [Fact]
    public async Task ParsingTimeout_ShouldMarkFailedAndAllowRetry()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        var extractor = new StubContentExtractor(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "不会返回";
            },
            _ => Task.FromResult("重试后的文档内容"));

        var service = CreateService(
            knowledgeBase,
            extractor,
            new StubTextSplitter(["retry-chunk"]),
            new CapturingVectorWriter(),
            new RagIndexingOptions { ParsingTimeoutSeconds = 1 });

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Failed);
        document.ErrorMessage.Should().Be("文档解析超时，请稍后重试。");

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Indexed);
        document.ErrorMessage.Should().BeNull();
        document.Chunks.Select(chunk => chunk.Content).Should().Equal("retry-chunk");
    }

    [Fact]
    public async Task EmbeddingTimeout_ShouldMarkFailedAndAllowRetry()
    {
        var (knowledgeBase, document) = CreateKnowledgeBaseWithDocument();
        var vectorWriter = new CapturingVectorWriter(
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            (_, _, _) => Task.CompletedTask);
        var service = CreateService(
            knowledgeBase,
            new StubContentExtractor("向量化超时测试文档"),
            new StubTextSplitter(["chunk-after-timeout"]),
            vectorWriter,
            new RagIndexingOptions { EmbeddingTimeoutSeconds = 1 });

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Failed);
        document.ErrorMessage.Should().Be("文档向量化超时，请稍后重试。");

        await service.IndexAsync(document.Id);

        document.Status.Should().Be(DocumentStatus.Indexed);
        document.ErrorMessage.Should().BeNull();
        vectorWriter.Requests.Should().HaveCount(2);
        vectorWriter.Requests[1].PreviousChunkCount.Should().Be(1);
    }

    private static DocumentIndexingService CreateService(
        KnowledgeBase knowledgeBase,
        StubContentExtractor extractor,
        StubTextSplitter splitter,
        CapturingVectorWriter vectorWriter,
        RagIndexingOptions? options = null)
    {
        var repository = new MutableKnowledgeBaseRepository(knowledgeBase);
        return new DocumentIndexingService(
            repository,
            extractor,
            splitter,
            vectorWriter,
            Options.Create(options ?? new RagIndexingOptions()),
            NullLogger<DocumentIndexingService>.Instance);
    }

    private static (KnowledgeBase KnowledgeBase, Document Document) CreateKnowledgeBaseWithDocument()
    {
        var knowledgeBase = new KnowledgeBase("kb", "description", EmbeddingModelId.New());
        var document = knowledgeBase.AddDocument("doc.txt", "doc.txt", ".txt", "hash");

        return (knowledgeBase, document);
    }

    private static void PutDocumentInEmbeddingState(Document document, IReadOnlyList<string> chunks)
    {
        document.StartParsing();
        document.CompleteParsing();
        for (var index = 0; index < chunks.Count; index++)
        {
            document.AddChunk(index, chunks[index]);
        }

        document.StartEmbedding();
    }

    private sealed class StubContentExtractor(params Func<CancellationToken, Task<string>>[] behaviors) : IDocumentContentExtractor
    {
        private readonly Queue<Func<CancellationToken, Task<string>>> behaviors = new(behaviors);

        public StubContentExtractor(string text)
            : this(_ => Task.FromResult(text))
        {
        }

        public Task<string> ExtractAsync(DocumentContentSource source, CancellationToken cancellationToken = default)
        {
            var behavior = behaviors.Count > 1 ? behaviors.Dequeue() : behaviors.Peek();
            return behavior(cancellationToken);
        }
    }

    private sealed class StubTextSplitter(IReadOnlyList<string> chunks) : IDocumentTextSplitter
    {
        public IReadOnlyList<string> Split(string text)
        {
            return chunks;
        }
    }

    private sealed class CapturingVectorWriter(params Func<KnowledgeVectorIndexRequest, IReadOnlyList<string>, CancellationToken, Task>[] behaviors)
        : IKnowledgeVectorIndexWriter
    {
        private readonly Queue<Func<KnowledgeVectorIndexRequest, IReadOnlyList<string>, CancellationToken, Task>> behaviors = new(behaviors);

        public List<KnowledgeVectorIndexRequest> Requests { get; } = [];

        public List<IReadOnlyList<string>> WrittenChunks { get; } = [];

        public Task UpsertAsync(
            KnowledgeVectorIndexRequest request,
            IReadOnlyList<string> chunks,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            WrittenChunks.Add(chunks);

            if (behaviors.Count == 0)
            {
                return Task.CompletedTask;
            }

            var behavior = behaviors.Count > 1 ? behaviors.Dequeue() : behaviors.Peek();
            return behavior(request, chunks, cancellationToken);
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

    private sealed class CapturingEventPublisher : IIntegrationEventPublisher
    {
        public int PublishedCount { get; private set; }

        public Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
            where TEvent : class
        {
            PublishedCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableKnowledgeBaseRepository(params KnowledgeBase[] knowledgeBases) : IRepository<KnowledgeBase>
    {
        private readonly List<KnowledgeBase> knowledgeBases = [..knowledgeBases];

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
            return Task.FromResult(1);
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
