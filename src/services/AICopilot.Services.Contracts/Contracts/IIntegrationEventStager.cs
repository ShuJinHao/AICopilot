namespace AICopilot.Services.Contracts;

public interface IIntegrationEventStager
{
    void Stage<TEvent>(Func<TEvent> messageFactory)
        where TEvent : class;
}
