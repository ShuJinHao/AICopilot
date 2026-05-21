using AICopilot.AiGatewayService.Agents;
using AICopilot.AiGatewayService.AgentTasks;
using AICopilot.AiGatewayService.ApprovalPolicies;
using AICopilot.AiGatewayService.Commands.ConversationTemplates;
using AICopilot.AiGatewayService.Commands.LanguageModels;
using AICopilot.AiGatewayService.Commands.Sessions;
using AICopilot.AiGatewayService.CloudReadiness;
using AICopilot.AiGatewayService.Queries.ConversationTemplates;
using AICopilot.AiGatewayService.Queries.LanguageModels;
using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.AiGatewayService.PromptPolicies;
using AICopilot.AiGatewayService.RoutingModels;
using AICopilot.AiGatewayService.Runtime;
using AICopilot.AiGatewayService.Tools;
using AICopilot.AiGatewayService.TrialOperations;
using AICopilot.AiGatewayService.Uploads;
using AICopilot.AiGatewayService.Workspaces;
using AICopilot.HttpApi.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AICopilot.HttpApi.Controllers;

[Route("/api/aigateway")]
[Authorize]
public class AiGatewayController(ISender sender) : ApiControllerBase(sender)
{
    private const long MaxAiGatewayUploadBytes = 50_000_000;

    [HttpPost("language-model")]
    public async Task<IActionResult> CreateLanguageModel(CreateLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("language-model")]
    public async Task<IActionResult> UpdateLanguageModel(UpdateLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("language-model")]
    public async Task<IActionResult> DeleteLanguageModel(DeleteLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("language-model")]
    public async Task<IActionResult> GetLanguageModel([FromQuery] GetLanguageModelQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("language-model/list")]
    public async Task<IActionResult> GetListLanguageModels()
    {
        return ReturnResult(await Sender.Send(new GetListLanguageModelsQuery()));
    }

    [HttpGet("language-model/chat-options")]
    public async Task<IActionResult> GetSelectableChatModels()
    {
        return ReturnResult(await Sender.Send(new GetSelectableChatModelsQuery()));
    }

    [HttpPost("language-model/test")]
    public async Task<IActionResult> TestLanguageModel(TestLanguageModelCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("routing-model")]
    public async Task<IActionResult> CreateRoutingModel(CreateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("routing-model")]
    public async Task<IActionResult> UpdateRoutingModel(UpdateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("routing-model")]
    public async Task<IActionResult> DeleteRoutingModel(DeleteRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("routing-model/activate")]
    public async Task<IActionResult> ActivateRoutingModel(ActivateRoutingModelConfigurationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("routing-model")]
    public async Task<IActionResult> GetRoutingModel([FromQuery] GetRoutingModelConfigurationQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("routing-model/list")]
    public async Task<IActionResult> GetListRoutingModels()
    {
        return ReturnResult(await Sender.Send(new GetListRoutingModelConfigurationsQuery()));
    }

    [HttpGet("provider-reliability")]
    public async Task<IActionResult> GetProviderReliability()
    {
        return ReturnResult(await Sender.Send(new GetProviderReliabilityQuery()));
    }

    [HttpGet("model-pools")]
    public async Task<IActionResult> GetModelPools()
    {
        return ReturnResult(await Sender.Send(new GetModelPoolsQuery()));
    }

    [HttpGet("runtime-settings")]
    public async Task<IActionResult> GetRuntimeSettings()
    {
        return ReturnResult(await Sender.Send(new GetChatRuntimeSettingsQuery()));
    }

    [HttpPut("runtime-settings")]
    public async Task<IActionResult> UpdateRuntimeSettings(UpdateChatRuntimeSettingsCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("prompt-policy")]
    public async Task<IActionResult> UpsertPromptPolicy(UpsertPromptPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("prompt-policy/activate")]
    public async Task<IActionResult> ActivatePromptPolicyVersion(ActivatePromptPolicyVersionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("prompt-policy")]
    public async Task<IActionResult> GetPromptPolicy([FromQuery] GetPromptPolicyQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("prompt-policy/active")]
    public async Task<IActionResult> GetActivePromptPolicy([FromQuery] GetActivePromptPolicyQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("prompt-policy/list")]
    public async Task<IActionResult> GetListPromptPolicies()
    {
        return ReturnResult(await Sender.Send(new GetListPromptPoliciesQuery()));
    }

    [HttpPost("conversation-template")]
    public async Task<IActionResult> CreateConversationTemplate(CreateConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("conversation-template")]
    public async Task<IActionResult> UpdateConversationTemplate(UpdateConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("conversation-template")]
    public async Task<IActionResult> DeleteConversationTemplate(DeleteConversationTemplateCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("conversation-template")]
    public async Task<IActionResult> GetConversationTemplate([FromQuery] GetConversationTemplateQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("conversation-template/list")]
    public async Task<IActionResult> GetListConversationTemplates()
    {
        return ReturnResult(await Sender.Send(new GetListConversationTemplatesQuery()));
    }

    [HttpPost("conversation-template/reset-builtins")]
    public async Task<IActionResult> ResetBuiltInConversationTemplates(ResetBuiltInConversationTemplatesCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("approval-policy")]
    public async Task<IActionResult> CreateApprovalPolicy(CreateApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("approval-policy")]
    public async Task<IActionResult> UpdateApprovalPolicy(UpdateApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("approval-policy")]
    public async Task<IActionResult> DeleteApprovalPolicy(DeleteApprovalPolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("approval-policy")]
    public async Task<IActionResult> GetApprovalPolicy([FromQuery] GetApprovalPolicyQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("approval-policy/list")]
    public async Task<IActionResult> GetListApprovalPolicies()
    {
        return ReturnResult(await Sender.Send(new GetListApprovalPoliciesQuery()));
    }

    [HttpGet("tools")]
    public async Task<IActionResult> GetToolRegistrations()
    {
        return ReturnResult(await Sender.Send(new GetListToolRegistrationsQuery()));
    }

    [HttpGet("tools/catalog")]
    public async Task<IActionResult> GetToolCatalog([FromQuery] GetToolCatalogQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("tools/{toolCode}")]
    public async Task<IActionResult> GetToolRegistration(string toolCode)
    {
        return ReturnResult(await Sender.Send(new GetToolRegistrationQuery(toolCode)));
    }

    [HttpPatch("tools/{toolCode}")]
    public async Task<IActionResult> UpdateToolRegistration(string toolCode, UpdateToolRegistrationRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateToolRegistrationCommand(
            toolCode,
            request.DisplayName,
            request.Description,
            request.InputSchemaJson,
            request.OutputSchemaJson,
            request.RiskLevel,
            request.RequiredPermission,
            request.RequiresApproval,
            request.IsEnabled,
            request.TimeoutSeconds,
            request.AuditLevel,
            request.Category,
            request.BusinessDomains,
            request.DataBoundary,
            request.IsVisibleToPlanner,
            request.IsExecutableByAgent,
            request.SchemaVersion,
            request.CatalogVersion,
            request.ApprovalPolicy)));
    }

    [HttpPost("tools/definition")]
    public async Task<IActionResult> UpsertToolDefinition(UpsertToolDefinitionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("tools/definition/activate")]
    public async Task<IActionResult> ActivateToolDefinitionVersion(ActivateToolDefinitionVersionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("tools/definition/disable")]
    public async Task<IActionResult> DisableToolDefinition(DisableToolDefinitionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(CreateSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpDelete("session")]
    public async Task<IActionResult> DeleteSession(DeleteSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("session/title")]
    public async Task<IActionResult> RenameSession(RenameSessionCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("session")]
    public async Task<IActionResult> GetSession([FromQuery] GetSessionQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("session/list")]
    public async Task<IActionResult> GetListSessions()
    {
        return ReturnResult(await Sender.Send(new GetListSessionsQuery()));
    }

    [HttpGet("agent/trial-scenarios")]
    public async Task<IActionResult> GetAgentTrialScenarios()
    {
        return ReturnResult(await Sender.Send(new GetAgentTrialScenariosQuery()));
    }

    [HttpPost("agent/trial-scenarios/create-task")]
    public async Task<IActionResult> CreateAgentTaskFromTrialScenario(
        CreateAgentTaskFromTrialScenarioCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("trial-operations/campaigns")]
    public async Task<IActionResult> GetTrialCampaigns()
    {
        return ReturnResult(await Sender.Send(new GetTrialCampaignsQuery()));
    }

    [HttpGet("trial-operations/campaigns/{id:guid}")]
    public async Task<IActionResult> GetTrialCampaignDetail(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetTrialCampaignDetailQuery(id)));
    }

    [HttpPost("trial-operations/campaigns")]
    public async Task<IActionResult> CreateTrialCampaign(CreateTrialCampaignCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPatch("trial-operations/campaigns/{id:guid}/status")]
    public async Task<IActionResult> UpdateTrialCampaignStatus(Guid id, UpdateTrialCampaignStatusRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateTrialCampaignStatusCommand(id, request.Status)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/attach-task")]
    public async Task<IActionResult> AttachAgentTaskToTrialCampaign(Guid id, AttachAgentTaskToTrialCampaignRequest request)
    {
        return ReturnResult(await Sender.Send(new AttachAgentTaskToTrialCampaignCommand(
            id,
            request.TaskId,
            request.ScenarioId,
            request.TrialMode)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/risks")]
    public async Task<IActionResult> UpsertTrialRiskIssue(Guid id, UpsertTrialRiskIssueRequest request)
    {
        return ReturnResult(await Sender.Send(new UpsertTrialRiskIssueCommand(
            id,
            request.IssueId,
            request.Severity,
            request.Category,
            request.Status,
            request.Owner,
            request.SourceRef,
            request.ResolutionHash)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/readiness")]
    public async Task<IActionResult> RunPilotReadinessEvaluation(Guid id)
    {
        return ReturnResult(await Sender.Send(new RunPilotReadinessEvaluationCommand(id)));
    }

    [HttpPost("trial-operations/campaigns/{id:guid}/evidence-package")]
    public async Task<IActionResult> GenerateTrialEvidencePackage(Guid id)
    {
        return ReturnResult(await Sender.Send(new GenerateTrialEvidencePackageCommand(id)));
    }

    [HttpPost("agent/task/plan")]
    public async Task<IActionResult> PlanAgentTask(PlanAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/cloud-sandbox-controlled-trial/plan")]
    public async Task<IActionResult> CreateCloudReadonlySandboxControlledPlan(
        CreateCloudReadonlySandboxControlledPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/cloud-production-controlled-pilot/plan")]
    public async Task<IActionResult> CreateCloudReadonlyProductionControlledPlan(
        CreateCloudReadonlyProductionControlledPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/approve-plan")]
    public async Task<IActionResult> ApproveAgentTaskPlan(ApproveAgentTaskPlanCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/run")]
    public async Task<IActionResult> RunAgentTask(RunAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/retry")]
    public async Task<IActionResult> RetryAgentTask(RetryAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("agent/task/cancel")]
    public async Task<IActionResult> CancelAgentTask(CancelAgentTaskCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("agent/task")]
    public async Task<IActionResult> GetAgentTask([FromQuery] GetAgentTaskQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("agent/task/by-session")]
    public async Task<IActionResult> GetAgentTasksBySession([FromQuery] GetListAgentTasksBySessionQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("agent/approval/pending")]
    public async Task<IActionResult> GetPendingAgentApprovals()
    {
        return ReturnResult(await Sender.Send(new GetPendingAgentApprovalsQuery()));
    }

    [HttpGet("agent/task/{id:guid}/approvals")]
    public async Task<IActionResult> GetAgentTaskApprovals(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskApprovalsQuery(id)));
    }

    [HttpGet("agent/task/{id:guid}/audit-summary")]
    public async Task<IActionResult> GetAgentTaskAuditSummary(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskAuditSummaryQuery(id)));
    }

    [HttpGet("agent/task/{id:guid}/tool-executions")]
    public async Task<IActionResult> GetAgentTaskToolExecutions(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? toolCode = null)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskToolExecutionsQuery(
            id,
            pageIndex,
            pageSize,
            status,
            toolCode)));
    }

    [HttpGet("agent/task/{id:guid}/run-attempts")]
    public async Task<IActionResult> GetAgentTaskRunAttempts(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskRunAttemptsQuery(
            id,
            pageIndex,
            pageSize)));
    }

    [HttpGet("agent/task/{id:guid}/run-queue")]
    public async Task<IActionResult> GetAgentTaskRunQueue(
        Guid id,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20)
    {
        return ReturnResult(await Sender.Send(new GetAgentTaskRunQueueQuery(
            id,
            pageIndex,
            pageSize)));
    }

    [HttpGet("agent/run-queue")]
    public async Task<IActionResult> GetAgentRunQueue(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? triggerType = null,
        [FromQuery] Guid? taskId = null,
        [FromQuery] Guid? requestedBy = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        return ReturnResult(await Sender.Send(new GetAgentRunQueueQuery(
            pageIndex,
            pageSize,
            status,
            triggerType,
            taskId,
            requestedBy,
            from,
            to)));
    }

    [HttpGet("agent/run-queue/summary")]
    public async Task<IActionResult> GetAgentRunQueueSummary()
    {
        return ReturnResult(await Sender.Send(new GetAgentRunQueueSummaryQuery()));
    }

    [HttpGet("agent/worker/status")]
    public async Task<IActionResult> GetAgentWorkerStatus()
    {
        return ReturnResult(await Sender.Send(new GetAgentWorkerStatusQuery()));
    }

    [HttpPost("agent/run-queue/{id:guid}/dead-letter")]
    public async Task<IActionResult> DeadLetterAgentRunQueueItem(
        Guid id,
        [FromBody] DeadLetterAgentRunQueueItemRequest? request = null)
    {
        return ReturnResult(await Sender.Send(new DeadLetterAgentRunQueueItemCommand(id, request?.Reason)));
    }

    [HttpPost("agent/approval/{id:guid}/approve")]
    public async Task<IActionResult> ApproveAgentApproval(Guid id, AgentApprovalDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new ApproveAgentApprovalCommand(id, request.Comment)));
    }

    [HttpPost("agent/approval/{id:guid}/reject")]
    public async Task<IActionResult> RejectAgentApproval(Guid id, AgentApprovalDecisionRequest request)
    {
        return ReturnResult(await Sender.Send(new RejectAgentApprovalCommand(id, request.Comment)));
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxAiGatewayUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAiGatewayUploadBytes)]
    public async Task<IActionResult> Upload(
        [FromForm] string scope,
        IFormFile? file,
        [FromForm] Guid? sessionId = null,
        [FromForm] Guid? agentTaskId = null,
        [FromForm] Guid? knowledgeBaseId = null)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        if (file.Length > MaxAiGatewayUploadBytes)
        {
            return BadRequest(new { error = "File exceeds the 50 MB upload limit." });
        }

        await using var stream = file.OpenReadStream();
        var command = new UploadRecordCommand(
            scope,
            new AiGatewayUploadStream(
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                file.Length,
                stream),
            sessionId,
            agentTaskId,
            knowledgeBaseId);
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("upload/list")]
    public async Task<IActionResult> GetUploadRecords([FromQuery] GetListUploadRecordsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("workspace/{code}")]
    public async Task<IActionResult> GetWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceQuery(code)));
    }

    [HttpGet("workspace-settings")]
    public async Task<IActionResult> GetWorkspaceSettings()
    {
        return ReturnResult(await Sender.Send(new GetArtifactWorkspaceSettingsQuery()));
    }

    [HttpPost("workspace/{code}/submit-final-review")]
    public async Task<IActionResult> SubmitFinalReview(string code)
    {
        return ReturnResult(await Sender.Send(new SubmitFinalReviewCommand(code)));
    }

    [HttpPost("workspace/{code}/finalize")]
    public async Task<IActionResult> FinalizeWorkspace(string code)
    {
        return ReturnResult(await Sender.Send(new FinalizeArtifactWorkspaceCommand(code)));
    }

    [HttpGet("artifact/{id:guid}/download")]
    public async Task<IActionResult> DownloadArtifact(Guid id)
    {
        var result = await Sender.Send(new DownloadArtifactQuery(id));
        if (!result.IsSuccess || result.Value is null)
        {
            return ReturnResult(result);
        }

        return File(result.Value.Stream, result.Value.MimeType, result.Value.FileName);
    }

    [HttpGet("artifact/{id:guid}/content")]
    public async Task<IActionResult> GetArtifactContent(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetArtifactContentQuery(id)));
    }

    [HttpGet("artifact/{id:guid}/preview")]
    public async Task<IActionResult> GetAgentArtifactPreview(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetAgentArtifactPreviewQuery(id)));
    }

    [HttpPut("artifact/{id:guid}/content")]
    public async Task<IActionResult> UpdateArtifactContent(Guid id, UpdateArtifactContentRequest request)
    {
        return ReturnResult(await Sender.Send(new UpdateArtifactContentCommand(
            id,
            request.Content,
            request.ExpectedVersion,
            request.Comment)));
    }

    [HttpPost("artifact/{id:guid}/revision-comment")]
    public async Task<IActionResult> CreateArtifactRevisionComment(Guid id, CreateArtifactRevisionCommentRequest request)
    {
        return ReturnResult(await Sender.Send(new CreateArtifactRevisionCommentCommand(
            id,
            request.Comment,
            request.ExpectedVersion)));
    }

    [HttpPost("artifact/{id:guid}/regenerate-draft")]
    public async Task<IActionResult> RegenerateDraftArtifact(Guid id, RegenerateDraftArtifactRequest request)
    {
        return ReturnResult(await Sender.Send(new RegenerateDraftArtifactCommand(
            id,
            request.Content,
            request.ExpectedVersion,
            request.Comment)));
    }

    [HttpPost("artifact/{id:guid}/submit-final-approval")]
    public async Task<IActionResult> SubmitArtifactForFinalApproval(Guid id)
    {
        return ReturnResult(await Sender.Send(new SubmitArtifactForFinalApprovalCommand(id)));
    }

    [HttpGet("artifact/{id:guid}/versions")]
    public async Task<IActionResult> GetArtifactVersions(Guid id)
    {
        return ReturnResult(await Sender.Send(new GetArtifactVersionsQuery(id)));
    }

    [HttpGet("artifact/{id:guid}/versions/{version:int}/download")]
    public async Task<IActionResult> DownloadArtifactVersion(Guid id, int version)
    {
        var result = await Sender.Send(new DownloadArtifactVersionQuery(id, version));
        if (!result.IsSuccess || result.Value is null)
        {
            return ReturnResult(result);
        }

        return File(result.Value.Stream, result.Value.MimeType, result.Value.FileName);
    }

    [HttpGet("artifact/{id:guid}/versions/{fromVersion:int}/diff/{toVersion:int}")]
    public async Task<IActionResult> GetArtifactVersionDiff(Guid id, int fromVersion, int toVersion)
    {
        return ReturnResult(await Sender.Send(new GetArtifactVersionDiffQuery(id, fromVersion, toVersion)));
    }

    [HttpPost("artifact/{id:guid}/versions/{version:int}/restore")]
    public async Task<IActionResult> RestoreArtifactVersion(Guid id, int version, RestoreArtifactVersionRequest request)
    {
        return ReturnResult(await Sender.Send(new RestoreArtifactVersionCommand(
            id,
            version,
            request.ExpectedVersion,
            request.Comment)));
    }

    [HttpPut("session/safety-attestation")]
    public async Task<IActionResult> UpdateSessionSafetyAttestation(UpdateSessionSafetyAttestationCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("chat-message/list")]
    public async Task<IActionResult> GetListChatMessages([FromQuery] GetListChatMessageHistoryQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpPost("chat")]
    [EnableRateLimiting("chat")]
    public IResult Chat(ChatStreamRequest request)
    {
        var stream = Sender.CreateStream(request);
        return Results.ServerSentEvents(stream);
    }

    [HttpPost("approval/decision")]
    [EnableRateLimiting("chat")]
    public IResult DecideApproval(ApprovalDecisionStreamRequest request)
    {
        var stream = Sender.CreateStream(request);
        return Results.ServerSentEvents(stream);
    }

    [HttpGet("approval/pending")]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] GetPendingApprovalsQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }
}

public sealed record UpdateToolRegistrationRequest(
    string? DisplayName = null,
    string? Description = null,
    string? InputSchemaJson = null,
    string? OutputSchemaJson = null,
    AICopilot.SharedKernel.Ai.AiToolRiskLevel? RiskLevel = null,
    string? RequiredPermission = null,
    bool? RequiresApproval = null,
    bool? IsEnabled = null,
    int? TimeoutSeconds = null,
    string? AuditLevel = null,
    string? Category = null,
    IReadOnlyCollection<string>? BusinessDomains = null,
    string? DataBoundary = null,
    bool? IsVisibleToPlanner = null,
    bool? IsExecutableByAgent = null,
    int? SchemaVersion = null,
    int? CatalogVersion = null,
    string? ApprovalPolicy = null);

public sealed record DeadLetterAgentRunQueueItemRequest(string? Reason = null);

public sealed record UpdateTrialCampaignStatusRequest(string Status);

public sealed record AttachAgentTaskToTrialCampaignRequest(
    Guid TaskId,
    string? ScenarioId = null,
    string? TrialMode = null);

public sealed record UpsertTrialRiskIssueRequest(
    Guid? IssueId,
    string Severity,
    string Category,
    string Status,
    string? Owner = null,
    string? SourceRef = null,
    string? ResolutionHash = null);
