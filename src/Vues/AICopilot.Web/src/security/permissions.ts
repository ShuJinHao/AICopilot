export const CHAT_REQUIRED_PERMISSIONS = [
  'AiGateway.CreateSession',
  'AiGateway.GetSession',
  'AiGateway.GetListSessions',
  'AiGateway.Chat'
] as const

export const CONFIG_READ_PERMISSIONS = {
  languageModel: ['AiGateway.GetLanguageModel', 'AiGateway.GetListLanguageModels'],
  conversationTemplate: [
    'AiGateway.GetConversationTemplate',
    'AiGateway.GetConversationTemplateByName',
    'AiGateway.GetListConversationTemplates'
  ],
  approvalPolicy: ['AiGateway.GetApprovalPolicy', 'AiGateway.GetListApprovalPolicies'],
  businessDatabase: [
    'DataAnalysis.GetBusinessDatabase',
    'DataAnalysis.GetListBusinessDatabases'
  ],
  mcpServer: ['Mcp.GetServer', 'Mcp.GetListServers']
} as const

export const CONFIG_WRITE_PERMISSIONS = {
  languageModel: {
    create: 'AiGateway.CreateLanguageModel',
    update: 'AiGateway.UpdateLanguageModel',
    delete: 'AiGateway.DeleteLanguageModel'
  },
  conversationTemplate: {
    create: 'AiGateway.CreateConversationTemplate',
    update: 'AiGateway.UpdateConversationTemplate',
    delete: 'AiGateway.DeleteConversationTemplate'
  },
  approvalPolicy: {
    create: 'AiGateway.CreateApprovalPolicy',
    update: 'AiGateway.UpdateApprovalPolicy',
    delete: 'AiGateway.DeleteApprovalPolicy'
  },
  businessDatabase: {
    create: 'DataAnalysis.CreateBusinessDatabase',
    update: 'DataAnalysis.UpdateBusinessDatabase',
    delete: 'DataAnalysis.DeleteBusinessDatabase'
  },
  mcpServer: {
    create: 'Mcp.CreateServer',
    update: 'Mcp.UpdateServer',
    delete: 'Mcp.DeleteServer'
  }
} as const

export const ACCESS_MANAGEMENT_PERMISSIONS = [
  'Identity.GetListPermissions',
  'Identity.GetListRoles',
  'Identity.GetListAuditLogs',
  'Identity.CreateRole',
  'Identity.UpdateRole',
  'Identity.DeleteRole',
  'Identity.GetListUsers',
  'Identity.CreateUser',
  'Identity.UpdateUserRole',
  'Identity.DisableUser',
  'Identity.EnableUser',
  'Identity.ResetUserPassword'
] as const

export function collectConfigReadPermissions() {
  return Object.values(CONFIG_READ_PERMISSIONS).flat()
}
