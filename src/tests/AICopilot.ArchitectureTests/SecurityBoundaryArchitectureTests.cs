using System.Reflection;
using AICopilot.AiGatewayService.Queries.Runtime;
using AICopilot.AiGatewayService.Queries.Sessions;
using AICopilot.EntityFrameworkCore;
using AICopilot.EntityFrameworkCore.AuditLogs;
using AICopilot.EntityFrameworkCore.Outbox;
using AICopilot.HttpApi.Controllers;
using AICopilot.RagService.Queries.KnowledgeBases;
using AICopilot.Services.Contracts;
using AICopilot.Services.Contracts.Uploads;
using AICopilot.Services.CrossCutting.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AICopilot.ArchitectureTests;

public sealed class SecurityBoundaryArchitectureTests
{
    [Fact]
    public void ManagementControllers_ShouldRequireHttpAuthentication()
    {
        Type[] aiGatewayControllers =
        [
            typeof(AiGatewayController),
            typeof(AiGatewayToolController),
            typeof(AiGatewaySessionController),
            typeof(AiGatewayAgentTaskController),
            typeof(AiGatewayWorkspaceArtifactController)
        ];

        foreach (var controller in aiGatewayControllers)
        {
            controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull(controller.Name);
        }

        typeof(DataAnalysisController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(McpController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        typeof(RagController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Theory]
    [InlineData(typeof(GetProviderReliabilityQuery), "AiGateway.GetProviderReliability")]
    [InlineData(typeof(SearchKnowledgeBaseQuery), "Rag.SearchKnowledgeBase")]
    [InlineData(typeof(GetListChatMessagesQuery), "AiGateway.GetSession")]
    public void DedicatedQueries_ShouldDeclareExactReadPermission(Type queryType, string expectedPermission)
    {
        var attribute = queryType.GetCustomAttribute<AuthorizeRequirementAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Permission.Should().Be(expectedPermission);
    }

    [Fact]
    public void IdentityManagementWrites_ShouldRequireAuthAndManagementRateLimit()
    {
        AssertIdentityManagementEndpoint(nameof(IdentityController.CreateRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.UpdateRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.DeleteRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.CreateUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.UpdateUserRole));
        AssertIdentityManagementEndpoint(nameof(IdentityController.DisableUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.EnableUser));
        AssertIdentityManagementEndpoint(nameof(IdentityController.ResetUserPassword));

        typeof(IdentityController)
            .GetMethod(nameof(IdentityController.Login))!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!
            .PolicyName.Should().Be("login");
        typeof(IdentityController)
            .GetMethod(nameof(IdentityController.GetInitializationStatus))!
            .GetCustomAttribute<AuthorizeAttribute>()
            .Should().BeNull();
    }

    [Fact]
    public void UploadDocument_ShouldDeclareRequestSizeLimits()
    {
        var method = typeof(RagController).GetMethod(nameof(RagController.UploadDocument));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<RequestSizeLimitAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<RequestFormLimitsAttribute>()?.MultipartBodyLengthLimit
            .Should().Be(DocumentUploadRequestPolicy.MaxUploadBytes);
    }

    [Fact]
    public void AuditRuntimeServices_ShouldUseDedicatedAuditDbContext()
    {
        var writerParameters = typeof(AuditLogWriter).GetConstructors().Single().GetParameters();
        writerParameters.Should().Contain(parameter => parameter.ParameterType == typeof(AuditDbContext));
        writerParameters.Should().NotContain(parameter => parameter.ParameterType == typeof(AiCopilotDbContext));

        var queryParameters = typeof(AuditLogQueryService).GetConstructors().Single().GetParameters();
        queryParameters.Should().ContainSingle()
            .Which.ParameterType.Should().Be(typeof(AuditDbContext));

        using var auditDbContext = new AuditDbContext(
            new DbContextOptionsBuilder<AuditDbContext>()
                .UseNpgsql("Host=localhost;Database=security;Username=test;Password=test")
                .Options);
        auditDbContext.Model.FindEntityType(typeof(AuditLogEntry)).Should().NotBeNull();
    }

    [Fact]
    public void IntegrationEventStager_ShouldExposeOnlyFactoryPath()
    {
        var stageMethod = typeof(IIntegrationEventStager).GetMethods().Should().ContainSingle().Subject;
        var stageParameter = stageMethod.GetParameters().Should().ContainSingle().Subject.ParameterType;
        stageMethod.Name.Should().Be(nameof(IIntegrationEventStager.Stage));
        stageMethod.IsGenericMethodDefinition.Should().BeTrue();
        stageParameter.IsGenericType.Should().BeTrue();
        stageParameter.GetGenericTypeDefinition().Should().Be(typeof(Func<>));

        typeof(RagIntegrationEventBuffer).Should().BeAssignableTo<IIntegrationEventStager>();
        typeof(RagIntegrationEventBuffer).GetMethod(nameof(IIntegrationEventStager.Stage))
            .Should().NotBeNull();
        typeof(RagDbContext).GetMethod("StageIntegrationEvent").Should().BeNull();
        typeof(IIntegrationEventStager).Assembly
            .GetType("AICopilot.Services.Contracts.IIntegrationEventPublisher")
            .Should().BeNull();
        typeof(OutboxDbContext).Assembly
            .GetType("AICopilot.EntityFrameworkCore.Outbox.OutboxIntegrationEventPublisher")
            .Should().BeNull();
    }

    [Fact]
    public void OutboxRuntimeContext_ShouldOwnOutboxModel()
    {
        using var outboxContext = new OutboxDbContext(
            new DbContextOptionsBuilder<OutboxDbContext>()
                .UseNpgsql("Host=localhost;Database=aicopilot_security;Username=test;Password=test")
                .Options);
        var outboxEntity = outboxContext.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(OutboxMessage));

        outboxEntity.Should().NotBeNull();
        outboxEntity!.GetSchema().Should().Be("outbox");
        outboxEntity.GetTableName().Should().Be("outbox_messages");
        outboxEntity.IsTableExcludedFromMigrations().Should().BeFalse();
    }

    private static void AssertIdentityManagementEndpoint(string actionName)
    {
        var method = typeof(IdentityController).GetMethod(actionName);
        method.Should().NotBeNull();
        method!.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            .Should().Be("identity-management");
    }
}
