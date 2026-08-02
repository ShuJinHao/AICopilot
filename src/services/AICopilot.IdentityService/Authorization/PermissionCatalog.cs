using AICopilot.Services.Contracts;

namespace AICopilot.IdentityService.Authorization;

public static class IdentityPermissionConstants
{
    public const string PermissionClaimType = "permission";
}

public sealed class PermissionCatalog : IPermissionCatalog
{
    private static readonly PermissionDefinition[] Definitions =
    [
        new("Identity.GetListPermissions", "Identity", "View Permission Catalog", "View the built-in permission catalog and descriptions."),
        new("Identity.GetListRoles", "Identity", "View Roles", "View roles and their assigned permissions."),
        new("Identity.CreateRole", "Identity", "Create Role", "Create a new role and assign permissions."),
        new("Identity.UpdateRole", "Identity", "Update Role Permissions", "Change permissions assigned to an existing role."),
        new("Identity.GetListUsers", "Identity", "View Users", "View users and their current role."),
        new("Identity.CreateUser", "Identity", "Create User", "Create a new user and assign an initial role."),
        new("Identity.UpdateUserRole", "Identity", "Update User Role", "Reassign the single effective role of a user."),
        new("Identity.DisableUser", "Identity", "Disable User", "Disable a user account and revoke active sessions."),
        new("Identity.EnableUser", "Identity", "Enable User", "Re-enable a disabled user account."),
        new("Identity.ResetUserPassword", "Identity", "Reset User Password", "Reset a user password without exposing the previous password."),
        new("Identity.DeleteRole", "Identity", "Delete Role", "Delete a custom role that is no longer assigned to users."),
        new("Identity.GetListAuditLogs", "Identity", "View Audit Logs", "View governance audit logs for identity, config, and approval actions."),

        new("AiGateway.Chat", "AiGateway", "Chat", "Use the Harness main chat and receive streaming responses."),
        new("AiGateway.CreateSession", "AiGateway", "Create Session", "Create a new chat session."),
        new("AiGateway.GetSession", "AiGateway", "View Session", "View a single session and its history."),
        new("AiGateway.GetListSessions", "AiGateway", "View Session List", "View the current account's visible sessions."),
        new("AiGateway.RenameSession", "AiGateway", "Rename Session", "Rename a chat session owned by the current account."),
        new("AiGateway.DeleteSession", "AiGateway", "Delete Session", "Delete a chat session."),
        new("AiGateway.GetLanguageModel", "AiGateway", "View Language Model", "View language model configuration details."),
        new("AiGateway.GetListLanguageModels", "AiGateway", "View Language Models", "View the language model configuration list."),
        new("AiGateway.GetProviderReliability", "AiGateway", "View Provider Reliability", "View model provider fallback, circuit breaker, and output-token budget configuration."),
        new("AiGateway.GetModelPools", "AiGateway", "View Model Pools", "View model endpoint pool routing, limits, health, and aggregate runtime statistics."),
        new("AiGateway.GetCloudReadonlyStatus", "AiGateway", "View Cloud Readonly Status", "View the effective governed Cloud read-only runtime status."),
        new("AiGateway.CreateLanguageModel", "AiGateway", "Create Language Model", "Create a new language model configuration."),
        new("AiGateway.UpdateLanguageModel", "AiGateway", "Update Language Model", "Update an existing language model configuration."),
        new("AiGateway.DeleteLanguageModel", "AiGateway", "Delete Language Model", "Delete a language model configuration."),
        new("AiGateway.GetConversationTemplate", "AiGateway", "View Conversation Template", "View conversation template details."),
        new("AiGateway.GetConversationTemplateByName", "AiGateway", "View Template By Name", "View a conversation template by its name."),
        new("AiGateway.GetListConversationTemplates", "AiGateway", "View Conversation Templates", "View the conversation template list."),
        new("AiGateway.CreateConversationTemplate", "AiGateway", "Create Conversation Template", "Create a new conversation template."),
        new("AiGateway.UpdateConversationTemplate", "AiGateway", "Update Conversation Template", "Update an existing conversation template."),
        new("AiGateway.DeleteConversationTemplate", "AiGateway", "Delete Conversation Template", "Delete a conversation template."),
        new("AiGateway.ToolRegistry.Read", "AiGateway", "View Tool Registry", "View controlled Agent tool registrations and policy metadata."),
        new("AiGateway.ToolRegistry.Manage", "AiGateway", "Manage Tool Registry", "Update controlled Agent tool enablement, approval, risk, and permission policy."),

        new("DataAnalysis.GetBusinessDatabase", "DataAnalysis", "View Business Database", "View business database configuration details."),
        new("DataAnalysis.GetListBusinessDatabases", "DataAnalysis", "View Business Databases", "View the business database configuration list."),
        new("DataAnalysis.CreateBusinessDatabase", "DataAnalysis", "Create Business Database", "Create a new business database configuration."),
        new("DataAnalysis.UpdateBusinessDatabase", "DataAnalysis", "Update Business Database", "Update an existing business database configuration."),
        new("DataAnalysis.DeleteBusinessDatabase", "DataAnalysis", "Delete Business Database", "Delete a business database configuration."),
        new("DataSource.Read", "DataSource", "View Data Sources", "View governed business data sources after permission trimming."),
        new("DataSource.TextToSql", "DataSource", "Use Text-to-SQL Data Sources", "Generate governed Text-to-SQL drafts and execute them by draft id against authorized data sources."),
        new("DataSource.QueryGovernedSql", "DataSource", "Query Governed SQL", "Run high-permission governed readonly SQL against authorized data sources for data stewardship and operations."),
        new("DataSource.Manage", "DataSource", "Manage Data Sources", "Create and update governed read-only business data source configurations."),
        new("DataSource.SchemaView", "DataSource", "View Data Source Schema", "View schema metadata for authorized business data sources."),
        new("DataSource.AuditView", "DataSource", "View Data Source Audit", "View query audit summaries for governed business data sources."),

        new("Rag.GetEmbeddingModel", "Rag", "View Embedding Model", "View embedding model configuration details."),
        new("Rag.GetListEmbeddingModels", "Rag", "View Embedding Models", "View the embedding model configuration list."),
        new("Rag.CreateEmbeddingModel", "Rag", "Create Embedding Model", "Create a new embedding model configuration."),
        new("Rag.UpdateEmbeddingModel", "Rag", "Update Embedding Model", "Update an existing embedding model configuration."),
        new("Rag.DeleteEmbeddingModel", "Rag", "Delete Embedding Model", "Delete an embedding model configuration."),
        new("Rag.GetKnowledgeBase", "Rag", "View Knowledge Base", "View knowledge base details."),
        new("Rag.GetListKnowledgeBases", "Rag", "View Knowledge Bases", "View the knowledge base list."),
        new("Rag.CreateKnowledgeBase", "Rag", "Create Knowledge Base", "Create a new knowledge base."),
        new("Rag.UpdateKnowledgeBase", "Rag", "Update Knowledge Base", "Update an existing knowledge base."),
        new("Rag.DeleteKnowledgeBase", "Rag", "Delete Knowledge Base", "Delete a knowledge base."),
        new("Rag.GetListDocuments", "Rag", "View Documents", "View the document list under a knowledge base."),
        new("Rag.UploadDocument", "Rag", "Upload Document", "Upload a knowledge base document."),
        new("Rag.UpdateDocumentGovernance", "Rag", "Update Document Governance", "Update governance metadata for a knowledge base document."),
        new("Rag.DeleteDocument", "Rag", "Delete Document", "Delete a knowledge base document."),
        new("Rag.SearchKnowledgeBase", "Rag", "Search Knowledge Base", "Search indexed knowledge base documents."),
        new("Rag.GetKnowledgeGovernance", "Rag", "View Knowledge Governance", "View knowledge categories, versions, and supplements."),
        new("Rag.ManageKnowledgeGovernance", "Rag", "Manage Knowledge Governance", "Create and update knowledge categories, document governance, and supplements."),

        new("Mcp.GetServer", "Mcp", "View MCP Server", "View MCP server configuration details."),
        new("Mcp.GetListServers", "Mcp", "View MCP Servers", "View the MCP server configuration list."),
        new("Mcp.CreateServer", "Mcp", "Create MCP Server", "Create a new MCP server configuration."),
        new("Mcp.UpdateServer", "Mcp", "Update MCP Server", "Update an existing MCP server configuration."),
        new("Mcp.DeleteServer", "Mcp", "Delete MCP Server", "Delete an MCP server configuration.")
    ];

    private static readonly Dictionary<string, PermissionDefinition> Index = Definitions
        .ToDictionary(item => item.Code, StringComparer.Ordinal);

    private static readonly IReadOnlyCollection<string> UserDefaultPermissions =
    [
        "AiGateway.CreateSession",
        "AiGateway.GetSession",
        "AiGateway.GetListSessions",
        "AiGateway.RenameSession",
        "AiGateway.DeleteSession",
        "AiGateway.Chat",
        "AiGateway.ToolRegistry.Read",
        "Rag.GetKnowledgeBase",
        "Rag.GetListKnowledgeBases",
        "Rag.GetListDocuments",
        "Rag.UploadDocument",
        "Rag.DeleteDocument",
        "Rag.SearchKnowledgeBase"
    ];

    public IReadOnlyCollection<PermissionDefinition> GetAll()
    {
        return Definitions;
    }

    public IReadOnlyCollection<string> GetDefaultPermissions(string roleName)
    {
        return roleName switch
        {
            IdentityRoleNames.Admin => Definitions.Select(item => item.Code).ToArray(),
            IdentityRoleNames.User => UserDefaultPermissions,
            _ => Array.Empty<string>()
        };
    }

    public bool Exists(string permissionCode)
    {
        return Index.ContainsKey(permissionCode);
    }
}
