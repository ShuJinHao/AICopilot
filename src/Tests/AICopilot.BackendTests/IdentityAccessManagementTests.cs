using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Transactions;
using AICopilot.Services.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AICopilot.BackendTests;

[Collection(CoreBackendTestCollection.Name)]
[Trait("Suite", "Phase38Acceptance")]
[Trait("Runtime", "DockerRequired")]
public sealed class IdentityAccessManagementTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AICopilotAppFixture _fixture;

    public IdentityAccessManagementTests(CoreAICopilotAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Admin_ShouldManageRolesUsers_AndCurrentProfile()
    {
        await AuthenticateAsAdminAsync();

        var roleName = $"ConfigReader-{Guid.NewGuid():N}";
        var userName = $"operator-{Guid.NewGuid():N}";
        string? roleId = null;
        string? userId = null;

        try
        {
            var profile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");
            profile.UserName.Should().Be(_fixture.BootstrapAdminUserName);
            profile.RoleName.Should().Be("Admin");
            profile.Permissions.Should().Contain("Identity.CreateRole");

            var permissionDefinitions = await GetJsonAsync<List<PermissionDefinitionDto>>("/api/identity/permission/list");
            permissionDefinitions.Should().Contain(item => item.Code == "Identity.GetListUsers");
            permissionDefinitions.Should().Contain(item => item.Code == "Identity.DisableUser");
            permissionDefinitions.Should().Contain(item => item.Code == "Identity.ResetUserPassword");
            permissionDefinitions.Should().Contain(item => item.Code == "Rag.SearchKnowledgeBase");

            var createdRole = await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
            {
                roleName,
                permissions = new[]
                {
                    "AiGateway.GetListLanguageModels",
                    "DataAnalysis.GetListBusinessDatabases"
                }
            });

            roleId = createdRole.RoleId;
            createdRole.IsSystemRole.Should().BeFalse();
            createdRole.AssignedUserCount.Should().Be(0);

            var updatedRole = await PutJsonAsync<RoleSummaryDto>("/api/identity/role", new
            {
                roleId,
                permissions = new[]
                {
                    "AiGateway.GetListLanguageModels",
                    "AiGateway.GetLanguageModel",
                    "DataAnalysis.GetListBusinessDatabases"
                }
            });

            updatedRole.Permissions.Should().Contain("AiGateway.GetLanguageModel");
            updatedRole.IsSystemRole.Should().BeFalse();

            var createdUser = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
            {
                userName,
                password = "Password123!",
                roleName = "User"
            });

            userId = createdUser.UserId;
            createdUser.RoleName.Should().Be("User");
            createdUser.IsEnabled.Should().BeTrue();
            createdUser.Status.Should().Be("Enabled");

            var updatedUser = await PutJsonAsync<UserSummaryDto>("/api/identity/user/role", new
            {
                userId,
                roleName
            });

            updatedUser.RoleName.Should().Be(roleName);
            updatedUser.IsEnabled.Should().BeTrue();
            updatedUser.Status.Should().Be("Enabled");

            var roles = await GetJsonAsync<List<RoleSummaryDto>>("/api/identity/role/list");
            roles.Should().Contain(item =>
                item.RoleId == roleId &&
                item.RoleName == roleName &&
                item.AssignedUserCount == 1 &&
                item.Permissions.Contains("AiGateway.GetLanguageModel"));

            var users = await GetJsonAsync<List<UserSummaryDto>>("/api/identity/user/list");
            users.Should().Contain(item =>
                item.UserId == userId &&
                item.RoleName == roleName &&
                item.IsEnabled &&
                item.Status == "Enabled");

            var auditLogs = await GetJsonAsync<AuditLogListDto>("/api/identity/audit-log/list?page=1&pageSize=20&actionGroup=Identity");
            auditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.CreateRole" &&
                item.TargetName == roleName &&
                item.Result == "Succeeded");
            auditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.UpdateRole" &&
                item.TargetName == roleName &&
                item.ChangedFields.Contains("permissions"));
            auditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.CreateUser" &&
                item.TargetName == userName);
            auditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.UpdateUserRole" &&
                item.TargetName == userName);

            var filteredAuditLogs = await GetJsonAsync<AuditLogListDto>(
                $"/api/identity/audit-log/list?page=1&pageSize=5&actionGroup=Identity&actionCode=Identity.UpdateUserRole&targetName={Uri.EscapeDataString(userName)}&result=Succeeded");
            filteredAuditLogs.Page.Should().Be(1);
            filteredAuditLogs.PageSize.Should().Be(5);
            filteredAuditLogs.TotalCount.Should().BeGreaterThan(0);
            filteredAuditLogs.Items.Should().OnlyContain(item =>
                item.ActionCode == "Identity.UpdateUserRole" &&
                item.TargetName == userName &&
                item.Result == "Succeeded");

            await AuthenticateAsync(userName, "Password123!");
            var operatorProfile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");

            operatorProfile.UserName.Should().Be(userName);
            operatorProfile.RoleName.Should().Be(roleName);
            operatorProfile.Permissions.Should().Contain("AiGateway.GetLanguageModel");
            operatorProfile.Permissions.Should().NotContain("Identity.CreateRole");
        }
        finally
        {
            await AuthenticateAsAdminAsync();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await DeleteUserIfExistsAsync(userId);
            }

            if (!string.IsNullOrWhiteSpace(roleId))
            {
                await DeleteRoleIfExistsAsync(roleId);
            }
        }
    }

    [Fact]
    public async Task UserRole_ShouldKeepChatAccess_AndForbiddenResponsesShouldIncludeMissingPermissions()
    {
        await AuthenticateAsAdminAsync();

        var userName = $"chat-user-{Guid.NewGuid():N}";
        string? userId = null;
        Guid languageModelId = Guid.Empty;
        Guid templateId = Guid.Empty;
        Guid sessionId = Guid.Empty;

        try
        {
            var createdUser = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
            {
                userName,
                password = "Password123!",
                roleName = "User"
            });
            userId = createdUser.UserId;

            languageModelId = await CreateLanguageModelAsync($"chat-lm-{Guid.NewGuid():N}");
            templateId = await CreateConversationTemplateAsync(
                $"chat-template-{Guid.NewGuid():N}",
                languageModelId,
                "chat template",
                "You are a concise assistant.");

            await AuthenticateAsync(userName, "Password123!");

            var profile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");
            profile.RoleName.Should().Be("User");
            profile.Permissions.Should().BeEquivalentTo(
                "AiGateway.CreateSession",
                "AiGateway.GetSession",
                "AiGateway.GetListSessions",
                "AiGateway.Chat");

            var createdSession = await PostJsonAsync<CreatedSessionDto>("/api/aigateway/session", new
            {
                templateId
            });

            sessionId = createdSession.Id;

            using var sessionListResponse = await _fixture.HttpClient.GetAsync("/api/aigateway/session/list");
            sessionListResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var forbiddenResponse = await _fixture.HttpClient.GetAsync("/api/identity/user/list");
            forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var forbiddenProblem = await ReadJsonAsync<ProblemDetailsDto>(forbiddenResponse);
            forbiddenProblem.Code.Should().Be("missing_permission");
            forbiddenProblem.MissingPermissions.Should().Contain("Identity.GetListUsers");

            await AssertForbiddenAsync("/api/data-analysis/business-database/list");
            await AssertForbiddenAsync("/api/identity/role/list");
            await AssertForbiddenAsync("/api/identity/permission/list");
            await AssertForbiddenAsync("/api/identity/audit-log/list");
        }
        finally
        {
            await AuthenticateAsAdminAsync();

            if (sessionId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/session", new { id = sessionId }, HttpStatusCode.NoContent);
            }

            if (templateId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/conversation-template", new { id = templateId }, HttpStatusCode.NoContent);
            }

            if (languageModelId != Guid.Empty)
            {
                await SendJsonAsync(HttpMethod.Delete, "/api/aigateway/language-model", new { id = languageModelId }, HttpStatusCode.NoContent);
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                await DeleteUserIfExistsAsync(userId);
            }
        }
    }

    [Fact]
    public async Task DisableEnableUser_ShouldRevokeExistingSession_AndAllowLoginAfterRecovery()
    {
        await AuthenticateAsAdminAsync();

        var userName = $"disable-user-{Guid.NewGuid():N}";
        string? userId = null;

        try
        {
            var createdUser = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
            {
                userName,
                password = "Password123!",
                roleName = "User"
            });
            userId = createdUser.UserId;

            var initialLogin = await LoginAsync(userName, "Password123!");

            await AuthenticateAsAdminAsync();
            var disabledUser = await PutJsonAsync<UserSummaryDto>("/api/identity/user/disable", new
            {
                userId
            });

            disabledUser.IsEnabled.Should().BeFalse();
            disabledUser.Status.Should().Be("Disabled");

            _fixture.SetAuthToken(initialLogin.Token);
            using var staleTokenResponse = await _fixture.HttpClient.GetAsync("/api/identity/me");
            staleTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var disabledProblem = await ReadJsonAsync<ProblemDetailsDto>(staleTokenResponse);
            disabledProblem.Code.Should().Be("account_disabled");

            var blockedLoginProblem = await LoginExpectingUnauthorizedAsync(userName, "Password123!");
            blockedLoginProblem.Code.Should().Be("account_disabled");

            await AuthenticateAsAdminAsync();
            var enabledUser = await PutJsonAsync<UserSummaryDto>("/api/identity/user/enable", new
            {
                userId
            });

            enabledUser.IsEnabled.Should().BeTrue();
            enabledUser.Status.Should().Be("Enabled");

            await AuthenticateAsync(userName, "Password123!");
            var restoredProfile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");
            restoredProfile.UserName.Should().Be(userName);
        }
        finally
        {
            await AuthenticateAsAdminAsync();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await DeleteUserIfExistsAsync(userId);
            }
        }
    }

    [Fact]
    public async Task ResetPassword_ShouldInvalidateOldPassword_AndExistingSession()
    {
        await AuthenticateAsAdminAsync();

        var userName = $"reset-user-{Guid.NewGuid():N}";
        const string oldPassword = "Password123!";
        const string newPassword = "Password456!";
        string? userId = null;

        try
        {
            var createdUser = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
            {
                userName,
                password = oldPassword,
                roleName = "User"
            });
            userId = createdUser.UserId;

            var initialLogin = await LoginAsync(userName, oldPassword);

            await AuthenticateAsAdminAsync();
            await SendJsonAsync(HttpMethod.Put, "/api/identity/user/password/reset", new
            {
                userId,
                newPassword
            }, HttpStatusCode.NoContent);

            _fixture.SetAuthToken(initialLogin.Token);
            using var staleTokenResponse = await _fixture.HttpClient.GetAsync("/api/identity/me");
            staleTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var revokedProblem = await ReadJsonAsync<ProblemDetailsDto>(staleTokenResponse);
            revokedProblem.Code.Should().Be("session_revoked");

            var oldPasswordProblem = await LoginExpectingUnauthorizedAsync(userName, oldPassword);
            oldPasswordProblem.Code.Should().Be("invalid_credentials");

            await AuthenticateAsync(userName, newPassword);
            var restoredProfile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");
            restoredProfile.UserName.Should().Be(userName);
        }
        finally
        {
            await AuthenticateAsAdminAsync();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await DeleteUserIfExistsAsync(userId);
            }
        }
    }

    [Fact]
    public async Task DeleteRole_ShouldRespectSystemAndAssignmentRules_AndAuditFilters()
    {
        await AuthenticateAsAdminAsync();

        var freeRoleName = $"free-role-{Guid.NewGuid():N}";
        var boundRoleName = $"bound-role-{Guid.NewGuid():N}";
        var boundUserName = $"bound-user-{Guid.NewGuid():N}";
        string? freeRoleId = null;
        string? boundRoleId = null;
        string? boundUserId = null;

        try
        {
            var freeRole = await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
            {
                roleName = freeRoleName,
                permissions = new[]
                {
                    "AiGateway.GetListSessions"
                }
            });
            freeRoleId = freeRole.RoleId;

            var boundRole = await PostJsonAsync<CreatedRoleDto>("/api/identity/role", new
            {
                roleName = boundRoleName,
                permissions = new[]
                {
                    "AiGateway.GetListSessions"
                }
            });
            boundRoleId = boundRole.RoleId;

            var boundUser = await PostJsonAsync<CreatedUserDto>("/api/identity/user", new
            {
                userName = boundUserName,
                password = "Password123!",
                roleName = boundRoleName
            });
            boundUserId = boundUser.UserId;

            await SendJsonAsync(HttpMethod.Delete, "/api/identity/role", new
            {
                roleId = freeRoleId
            }, HttpStatusCode.NoContent);
            freeRoleId = null;

            var adminRole = (await GetJsonAsync<List<RoleSummaryDto>>("/api/identity/role/list"))
                .Single(item => item.RoleName == "Admin");

            using var deleteSystemRoleResponse = await SendJsonRawAsync(HttpMethod.Delete, "/api/identity/role", new
            {
                roleId = adminRole.RoleId
            });
            deleteSystemRoleResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await deleteSystemRoleResponse.Content.ReadAsStringAsync()).Should().Contain("系统基线角色");

            using var deleteBoundRoleResponse = await SendJsonRawAsync(HttpMethod.Delete, "/api/identity/role", new
            {
                roleId = boundRoleId
            });
            deleteBoundRoleResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await deleteBoundRoleResponse.Content.ReadAsStringAsync()).Should().Contain("绑定用户");

            var rejectedAuditLogs = await GetJsonAsync<AuditLogListDto>(
                $"/api/identity/audit-log/list?page=1&pageSize=20&actionGroup=Identity&actionCode=Identity.DeleteRole&targetName={Uri.EscapeDataString(boundRoleName)}&result=Rejected");
            rejectedAuditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.DeleteRole" &&
                item.TargetName == boundRoleName &&
                item.Result == "Rejected");

            var succeededAuditLogs = await GetJsonAsync<AuditLogListDto>(
                $"/api/identity/audit-log/list?page=1&pageSize=20&actionGroup=Identity&actionCode=Identity.DeleteRole&targetName={Uri.EscapeDataString(freeRoleName)}&result=Succeeded");
            succeededAuditLogs.Items.Should().Contain(item =>
                item.ActionCode == "Identity.DeleteRole" &&
                item.TargetName == freeRoleName &&
                item.Result == "Succeeded");
        }
        finally
        {
            await AuthenticateAsAdminAsync();

            if (!string.IsNullOrWhiteSpace(boundUserId))
            {
                await DeleteUserIfExistsAsync(boundUserId);
            }

            if (!string.IsNullOrWhiteSpace(boundRoleId))
            {
                await DeleteRoleIfExistsAsync(boundRoleId);
            }

            if (!string.IsNullOrWhiteSpace(freeRoleId))
            {
                await DeleteRoleIfExistsAsync(freeRoleId);
            }
        }
    }

    [Fact]
    public async Task LastEnabledAdmin_ShouldNotBeDisabled()
    {
        await AuthenticateAsAdminAsync();

        var adminProfile = await GetJsonAsync<CurrentUserProfileDto>("/api/identity/me");

        using var disableResponse = await SendJsonRawAsync(HttpMethod.Put, "/api/identity/user/disable", new
        {
            userId = adminProfile.UserId
        });

        disableResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await disableResponse.Content.ReadAsStringAsync()).Should().Contain("至少保留 1 个启用状态的管理员");

        var rejectedAuditLogs = await GetJsonAsync<AuditLogListDto>(
            $"/api/identity/audit-log/list?page=1&pageSize=20&actionGroup=Identity&actionCode=Identity.DisableUser&targetName={Uri.EscapeDataString(adminProfile.UserName)}&result=Rejected");
        rejectedAuditLogs.Items.Should().Contain(item =>
            item.ActionCode == "Identity.DisableUser" &&
            item.TargetName == adminProfile.UserName &&
            item.Result == "Rejected");
    }

    [Fact]
    public async Task IdentityTransaction_ShouldRollbackAuditRows_WhenOperationFails()
    {
        var actionCode = $"Identity.RollbackProbe.{Guid.NewGuid():N}";
        var targetName = $"rollback-probe-{Guid.NewGuid():N}";

        await using (var dbContext = await CreateIdentityStoreDbContextAsync())
        {
            var transactionalExecutionService = new EfTransactionalExecutionService(dbContext);
            var auditLogWriter = new IdentityAuditLogWriter(dbContext);

            Func<Task> action = () => transactionalExecutionService.ExecuteAsync<int>(async cancellationToken =>
            {
                await auditLogWriter.WriteAsync(
                    new AuditLogWriteRequest(
                        AuditActionGroups.Identity,
                        actionCode,
                        "RollbackProbe",
                        null,
                        targetName,
                        AuditResults.Succeeded,
                        "rollback probe"),
                    cancellationToken);

                throw new InvalidOperationException("rollback probe failure");
            });

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("rollback probe failure");
        }

        await using var verificationDbContext = await CreateIdentityStoreDbContextAsync();
        var hasResidualAudit = await verificationDbContext.AuditLogs.AnyAsync(log =>
            log.ActionCode == actionCode &&
            log.TargetName == targetName);

        hasResidualAudit.Should().BeFalse(
            "identity audit rows must be enlisted in the same transaction as identity management operations");
    }

    private async Task AssertForbiddenAsync(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        await AuthenticateAsync(_fixture.BootstrapAdminUserName, _fixture.BootstrapAdminPassword);
    }

    private async Task AuthenticateAsync(string userName, string password)
    {
        var result = await LoginAsync(userName, password);
        _fixture.SetAuthToken(result.Token);
    }

    private async Task<LoginUserDto> LoginAsync(string userName, string password)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/identity/login", new
        {
            username = userName,
            password
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<LoginUserDto>(response);
    }

    private async Task<ProblemDetailsDto> LoginExpectingUnauthorizedAsync(string userName, string password)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/identity/login", new
        {
            username = userName,
            password
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        return await ReadJsonAsync<ProblemDetailsDto>(response);
    }

    private async Task<Guid> CreateLanguageModelAsync(string name)
    {
        var created = await PostJsonAsync<CreatedLanguageModelDto>("/api/aigateway/language-model", new
        {
            provider = "OpenAI",
            name,
            baseUrl = new Uri(_fixture.FakeAiBaseUri, "/v1").ToString().TrimEnd('/'),
            apiKey = "sk-test",
            maxTokens = 1024,
            temperature = 0.2
        });

        return created.Id;
    }

    private async Task<Guid> CreateConversationTemplateAsync(
        string templateName,
        Guid modelId,
        string description,
        string prompt)
    {
        var created = await PostJsonAsync<CreatedConversationTemplateDto>("/api/aigateway/conversation-template", new
        {
            name = templateName,
            description,
            systemPrompt = prompt,
            modelId,
            maxTokens = 512,
            temperature = 0.1
        });

        return created.Id;
    }

    private async Task DeleteUserIfExistsAsync(string userId)
    {
        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            return;
        }

        await using var dbContext = await CreateIdentityStoreDbContextAsync();
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == parsedUserId);
        if (user is null)
        {
            return;
        }

        var userRoles = await dbContext.UserRoles.Where(item => item.UserId == parsedUserId).ToListAsync();
        dbContext.UserRoles.RemoveRange(userRoles);
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();
    }

    private async Task DeleteRoleIfExistsAsync(string roleId)
    {
        if (!Guid.TryParse(roleId, out var parsedRoleId))
        {
            return;
        }

        await using var dbContext = await CreateIdentityStoreDbContextAsync();
        var role = await dbContext.Roles.SingleOrDefaultAsync(item => item.Id == parsedRoleId);
        if (role is null)
        {
            return;
        }

        var claims = await dbContext.RoleClaims.Where(item => item.RoleId == parsedRoleId).ToListAsync();
        dbContext.RoleClaims.RemoveRange(claims);
        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync();
    }

    private async Task<T> GetJsonAsync<T>(string uri)
    {
        using var response = await _fixture.HttpClient.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PostJsonAsync<T>(string uri, object payload)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(uri, payload, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task<T> PutJsonAsync<T>(string uri, object payload)
    {
        using var response = await SendJsonRawAsync(HttpMethod.Put, uri, payload);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadJsonAsync<T>(response);
    }

    private async Task SendJsonAsync(HttpMethod method, string uri, object payload, HttpStatusCode expectedStatusCode)
    {
        using var response = await SendJsonRawAsync(method, uri, payload);
        response.StatusCode.Should().Be(expectedStatusCode);
    }

    private async Task<HttpResponseMessage> SendJsonRawAsync(HttpMethod method, string uri, object payload)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(payload)
        };

        return await _fixture.HttpClient.SendAsync(request);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<IdentityStoreDbContext> CreateIdentityStoreDbContextAsync(AICopilotAppFixture? fixture = null)
    {
        var connectionString = await (fixture ?? _fixture).GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IdentityStoreDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityStoreDbContext(options);
    }

    private sealed record LoginUserDto(string UserName, string Token);

    private sealed record CurrentUserProfileDto(
        string UserId,
        string UserName,
        string? RoleName,
        IReadOnlyCollection<string> Permissions);

    private sealed record PermissionDefinitionDto(
        string Code,
        string Group,
        string DisplayName,
        string Description);

    private sealed record RoleSummaryDto(
        string RoleId,
        string RoleName,
        IReadOnlyCollection<string> Permissions,
        bool IsSystemRole,
        int AssignedUserCount);

    private sealed record CreatedRoleDto(
        string RoleId,
        string RoleName,
        IReadOnlyCollection<string> Permissions,
        bool IsSystemRole,
        int AssignedUserCount);

    private sealed record UserSummaryDto(
        string UserId,
        string UserName,
        string? RoleName,
        bool IsEnabled,
        string Status);

    private sealed record CreatedUserDto(
        string UserId,
        string UserName,
        string RoleName,
        bool IsEnabled,
        string Status);

    private sealed record ProblemDetailsDto(
        string? Title,
        string? Detail,
        int? Status,
        string? Code,
        IReadOnlyCollection<string> MissingPermissions);

    private sealed record CreatedLanguageModelDto(Guid Id, string Provider, string Name);

    private sealed record CreatedConversationTemplateDto(Guid Id, string Name);

    private sealed record CreatedSessionDto(Guid Id, string Title);

    private sealed record AuditLogListDto(
        IReadOnlyCollection<AuditLogSummaryDto> Items,
        int Page,
        int PageSize,
        int TotalCount);

    private sealed record AuditLogSummaryDto(
        Guid Id,
        string ActionGroup,
        string ActionCode,
        string TargetType,
        string? TargetId,
        string? TargetName,
        string? OperatorUserName,
        string? OperatorRoleName,
        string Result,
        string Summary,
        IReadOnlyCollection<string> ChangedFields,
        DateTime CreatedAt);
}
