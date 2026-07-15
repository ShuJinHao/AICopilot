using System.Net;

namespace AICopilot.EndToEndTests;

[Collection(CloudSemanticSimulationBackendTestCollection.Name)]
public sealed class PolicyChatEndToEndTests(
    CloudSemanticSimulationAICopilotAppFixture fixture)
    : EndToEndScenarioTestBase(fixture)
{
    [Fact]
    public async Task EmployeeAuthorizationPolicy_ShouldRequireAssignmentAndFunctionalPermission()
    {
        await RunPolicyContractAsync(
            (
                "员工修改机台参数需要什么权限？",
                "Policy.EmployeeAuthorization",
                ["业务结论", "双重", "禁止放宽"]),
            (
                "Can an operator change recipe settings without device assignment?",
                "Policy.EmployeeAuthorization",
                ["业务结论", "功能权限", "设备分配"]));
    }

    [Fact]
    public async Task DeviceRegistrationPolicy_ShouldRequireAdministratorRole()
    {
        await RunPolicyContractAsync(
            (
                "谁可以注册新设备？",
                "Policy.DeviceRegistration",
                ["管理员", "业务结论", "禁止放宽"]),
            (
                "Can a normal user register a new device?",
                "Policy.DeviceRegistration",
                ["管理员", "禁止放宽", "ClientCode"]));
    }

    [Fact]
    public async Task DeviceLifecyclePolicy_ShouldPreserveHistoryAndImmutableCode()
    {
        await RunPolicyContractAsync(
            (
                "设备删除前要检查什么？",
                "Policy.DeviceLifecycle",
                ["历史依赖", "禁止放宽", "配方"]),
            (
                "Can the device code be edited or the device be hard deleted?",
                "Policy.DeviceLifecycle",
                ["寻址码", "硬删除", "历史依赖"]));
    }

    [Fact]
    public async Task BootstrapIdentityPolicy_ShouldRequireClientCodeToDeviceIdChain()
    {
        await RunPolicyContractAsync(
            (
                "ClientCode 和 DeviceId 是什么关系？",
                "Policy.BootstrapIdentity",
                ["ClientCode", "DeviceId", "bootstrap"]),
            (
                "Can the client upload production data directly by device name?",
                "Policy.BootstrapIdentity",
                ["不能", "DeviceId", "bootstrap"]));
    }

    [Fact]
    public async Task RecipeVersioningPolicy_ShouldCreateNewArchivedVersion()
    {
        await RunPolicyContractAsync(
            (
                "配方修改是覆盖还是新建版本？",
                "Policy.RecipeVersioning",
                ["版本化", "V1.0", "禁止放宽"]),
            (
                "Does recipe editing overwrite the active version?",
                "Policy.RecipeVersioning",
                ["版本化", "归档", "禁止放宽"]));
    }

    private async Task RunPolicyContractAsync(
        params (string Message, string Intent, string[] ExpectedFragments)[] cases)
    {
        await AuthenticateAsAdminAsync();

        Guid languageModelId = Guid.Empty;
        Guid routingConfigurationId = Guid.Empty;
        Guid generalTemplateId = Guid.Empty;
        Guid intentRoutingTemplateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            languageModelId = await CreateLanguageModelAsync(
                $"policy-lm-{Guid.NewGuid():N}",
                BuildFakeAiBaseUrl(),
                "sk-policy",
                usages: ["Chat", "Routing"]);

            generalTemplateId = await CreateConversationTemplateAsync(
                $"PolicyAgent-{Guid.NewGuid():N}",
                languageModelId,
                "policy assistant",
                "You are a manufacturing policy assistant.");

            await DeleteConversationTemplateIfExistsAsync("IntentRoutingAgent");
            intentRoutingTemplateId = await CreateConversationTemplateAsync(
                "IntentRoutingAgent",
                languageModelId,
                "intent routing",
                "Choose the best intent and return a JSON array. {{$IntentList}}");
            routingConfigurationId = await CreateActiveRoutingModelAsync(languageModelId);
            sessionId = await CreateSessionAsync(generalTemplateId);

            foreach (var testCase in cases)
            {
                await AssertPolicyChatAsync(
                    sessionId,
                    testCase.Message,
                    testCase.Intent,
                    testCase.ExpectedFragments);
            }
        }
        finally
        {
            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/session",
                    new { id = sessionId },
                    HttpStatusCode.NoContent);
            }

            if (intentRoutingTemplateId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/conversation-template",
                    new { id = intentRoutingTemplateId },
                    HttpStatusCode.NoContent);
            }

            if (generalTemplateId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/conversation-template",
                    new { id = generalTemplateId },
                    HttpStatusCode.NoContent);
            }

            if (routingConfigurationId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/routing-model",
                    new { id = routingConfigurationId },
                    HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(
                    HttpMethod.Delete,
                    "/api/aigateway/language-model",
                    new { id = languageModelId },
                    HttpStatusCode.NoContent);
            }
        }
    }
}
