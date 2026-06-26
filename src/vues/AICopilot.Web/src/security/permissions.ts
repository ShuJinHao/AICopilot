export const CHAT_REQUIRED_PERMISSIONS = [
  'AiGateway.CreateSession',
  'AiGateway.GetSession',
  'AiGateway.GetListSessions',
  'AiGateway.Chat'
] as const

export const CONFIG_READ_PERMISSIONS = {
  languageModel: ['AiGateway.GetLanguageModel', 'AiGateway.GetListLanguageModels'],
  routingModel: ['AiGateway.GetRoutingModel', 'AiGateway.GetListRoutingModels'],
  conversationTemplate: [
    'AiGateway.GetConversationTemplate',
    'AiGateway.GetConversationTemplateByName',
    'AiGateway.GetListConversationTemplates'
  ]
} as const

export const CONFIG_WRITE_PERMISSIONS = {
  languageModel: {
    create: 'AiGateway.CreateLanguageModel',
    update: 'AiGateway.UpdateLanguageModel',
    delete: 'AiGateway.DeleteLanguageModel'
  },
  routingModel: {
    create: 'AiGateway.CreateRoutingModel',
    update: 'AiGateway.UpdateRoutingModel',
    delete: 'AiGateway.DeleteRoutingModel'
  },
  conversationTemplate: {
    create: 'AiGateway.CreateConversationTemplate',
    update: 'AiGateway.UpdateConversationTemplate',
    delete: 'AiGateway.DeleteConversationTemplate'
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

export const KNOWLEDGE_READ_PERMISSIONS = [
  'Rag.GetEmbeddingModel',
  'Rag.GetListEmbeddingModels',
  'Rag.GetKnowledgeBase',
  'Rag.GetListKnowledgeBases',
  'Rag.GetListDocuments'
] as const

export const KNOWLEDGE_WRITE_PERMISSIONS = {
  embeddingModel: {
    create: 'Rag.CreateEmbeddingModel',
    update: 'Rag.UpdateEmbeddingModel',
    delete: 'Rag.DeleteEmbeddingModel'
  },
  knowledgeBase: {
    create: 'Rag.CreateKnowledgeBase',
    update: 'Rag.UpdateKnowledgeBase',
    delete: 'Rag.DeleteKnowledgeBase'
  },
  document: {
    upload: 'Rag.UploadDocument',
    governance: 'Rag.UpdateDocumentGovernance',
    delete: 'Rag.DeleteDocument'
  },
  search: 'Rag.SearchKnowledgeBase'
} as const

export function collectConfigReadPermissions() {
  return Object.values(CONFIG_READ_PERMISSIONS).flat()
}

export function collectKnowledgeReadPermissions() {
  return [...KNOWLEDGE_READ_PERMISSIONS]
}
