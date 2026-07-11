using AICopilot.Services.Contracts;

namespace AICopilot.EntityFrameworkCore.Outbox;

public sealed class RagIntegrationEventStager(RagDbContext dbContext) : IIntegrationEventStager
{
    public void Stage<TEvent>(Func<TEvent> messageFactory)
        where TEvent : class
    {
        dbContext.StageIntegrationEvent(messageFactory);
    }
}
