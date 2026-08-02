using AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;
using System.Text.Json;
using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.Core.AiGateway.Aggregates.Sessions;
using AICopilot.Core.AiGateway.Ids;
using AICopilot.Core.AiGateway.Runtime.AgentSessions;
using AICopilot.Core.AiGateway.Specifications.ConversationTemplate;
using AICopilot.Services.CrossCutting.Attributes;
using AICopilot.Services.Contracts;
using AICopilot.SharedKernel.Messaging;
using AICopilot.SharedKernel.Repository;
using AICopilot.SharedKernel.Result;

namespace AICopilot.AiGatewayService.Commands.Sessions;

public record CreatedSessionDto(
    Guid Id,
    string Title,
    string AgentMode,
    long AgentSessionVersion,
    string AgentSessionStatus);

[AuthorizeRequirement("AiGateway.CreateSession")]
public record CreateSessionCommand(Guid? TemplateId) : ICommand<Result<CreatedSessionDto>>;

public class CreateSessionCommandHandler(
    IRepository<Session> repo,
    IReadRepository<ConversationTemplate> templateRepository,
    ConfiguredAgentRuntimeFactory configuredAgentRuntimeFactory,
    IAgentSessionStateStore agentSessionStateStore,
    ICurrentUser user)
    : ICommandHandler<CreateSessionCommand, Result<CreatedSessionDto>>
{
    public async Task<Result<CreatedSessionDto>> Handle(CreateSessionCommand request, CancellationToken ct)
    {
        if (user.Id is not { } userId)
        {
            return Result.Unauthorized(new ApiProblemDescriptor(
                AuthProblemCodes.Unauthorized,
                "Current user id is missing or invalid."));
        }

        var templateId = request.TemplateId;

        if (templateId == null)
        {
            var template = await templateRepository.FirstOrDefaultAsync(
                new ConversationTemplateByCodeSpec("chat_answer"),
                ct);
            if (template is null || !template.IsEnabled)
            {
                return Result.NotFound();
            }

            templateId = template.Id;
        }
        else
        {
            var template = await templateRepository.FirstOrDefaultAsync(
                new ConversationTemplateByIdSpec(new ConversationTemplateId(templateId.Value)),
                ct);
            if (template is null)
            {
                return Result.NotFound();
            }

            if (!template.IsEnabled || !IsMainChatTemplate(template))
            {
                return Result.Failure(new ApiProblemDescriptor(
                    AppProblemCodes.ChatConfigurationMissing,
                    "The selected conversation template is disabled or is reserved for an internal agent."));
            }
        }

        var session = new Session(userId, new ConversationTemplateId(templateId.Value));
        repo.Add(session);
        await using (var runtime = await configuredAgentRuntimeFactory.CreateHarnessAgentAsync(
                         session.TemplateId,
                         [],
                         cancellationToken: ct))
        {
            var harnessAgent = runtime.Agent as IHarnessRuntimeChatAgent
                ?? throw new InvalidOperationException(
                    "Configured main chat runtime did not create a Harness agent.");
            var agentSession = await harnessAgent.CreateSessionAsync(ct);
            var mode = await harnessAgent.GetModeAsync(agentSession, ct);
            if (mode != RuntimeAgentMode.Plan)
            {
                throw new InvalidOperationException(
                    "New Harness sessions must initialize in Plan mode.");
            }

            var serialized = await harnessAgent.SerializeSessionAsync(
                agentSession,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
            agentSessionStateStore.AddNew(
                session.Id.Value,
                userId,
                user.CloudTenantId,
                serialized);
        }

        await repo.SaveChangesAsync(ct);

        return Result.Success(new CreatedSessionDto(
            session.Id,
            session.Title,
            "plan",
            1,
            AgentSessionRuntimeStatus.Ready.ToString()));
    }

    private static bool IsMainChatTemplate(ConversationTemplate template) =>
        template.Scope is ConversationTemplateScope.General or ConversationTemplateScope.ChatAnswer;
}
