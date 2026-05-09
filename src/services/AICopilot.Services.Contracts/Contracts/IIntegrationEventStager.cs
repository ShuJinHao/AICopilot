namespace AICopilot.Services.Contracts;

public interface IIntegrationEventStager
{
    void Stage<TEvent>(TEvent message)
        where TEvent : class;

    void Stage<TEvent>(Func<TEvent> messageFactory)
        where TEvent : class;
}
