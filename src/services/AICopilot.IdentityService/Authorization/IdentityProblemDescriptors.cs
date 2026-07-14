using AICopilot.SharedKernel.Result;

namespace AICopilot.IdentityService.Authorization;

public static class IdentityProblemDescriptors
{
    public const string LastEnabledAdminDetail =
        "At least one enabled administrator account must remain.";

    public const string LastEnabledAdminUserFacingMessage =
        "至少保留 1 个启用状态的管理员，不能移除最后一个管理员账号的管理能力。";

    public static ApiProblemDescriptor LastEnabledAdminRequired()
    {
        return new ApiProblemDescriptor(
            AuthProblemCodes.LastEnabledAdminRequired,
            LastEnabledAdminDetail,
            new Dictionary<string, object?>
            {
                [ApiProblemExtensionKeys.UserFacingMessage] = LastEnabledAdminUserFacingMessage
            });
    }
}
