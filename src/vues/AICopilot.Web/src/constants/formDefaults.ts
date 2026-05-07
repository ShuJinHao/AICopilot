export const FORM_DEFAULTS = {
  languageModel: {
    provider: 'OpenAI',
    maxTokens: 2048,
    temperature: 0.7
  },
  businessDatabase: {
    provider: 1,
    externalSystemType: 0
  },
  mcpServer: {
    transportType: 1,
    command: 'dotnet',
    chatExposureMode: 0,
    externalSystemType: 0,
    capabilityKind: 1,
    riskLevel: 1
  },
  embeddingModel: {
    provider: 'OpenAI',
    baseUrl: 'https://api.openai.com/v1',
    modelName: 'text-embedding-3-small',
    dimensions: 1536,
    maxTokens: 8191
  },
  knowledgeDocument: {
    classification: 'Internal',
    sourceType: 'UserUploaded'
  }
} as const
