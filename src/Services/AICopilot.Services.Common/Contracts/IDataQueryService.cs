using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using AICopilot.Core.AiGateway.Aggregates.LanguageModel;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.DataAnalysis.Aggregates.BusinessDatabase;
using AICopilot.Core.Rag.Aggregates.EmbeddingModel;
using AICopilot.Core.Rag.Aggregates.KnowledgeBase;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Document = AICopilot.Core.Rag.Aggregates.KnowledgeBase.Document;

namespace AICopilot.Services.Common.Contracts;

public interface IDataQueryService
{
    public IQueryable<ConversationTemplate> ConversationTemplates { get; }

    public IQueryable<LanguageModel> LanguageModels { get; }

    public IQueryable<Session> Sessions { get; }

    public IQueryable<Message> Messages { get; }

    public IQueryable<EmbeddingModel> EmbeddingModels { get; }
    public IQueryable<KnowledgeBase> KnowledgeBases { get; }
    public IQueryable<Document> Documents { get; }
    public IQueryable<DocumentChunk> DocumentChunks { get; }

    public IQueryable<BusinessDatabase> BusinessDatabases { get; }

    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> queryable) where T : class;

    Task<IList<T>> ToListAsync<T>(IQueryable<T> queryable) where T : class;

    Task<bool> AnyAsync<T>(IQueryable<T> queryable) where T : class;
}