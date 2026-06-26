export const FORM_DEFAULTS = {
  languageModel: {
    provider: 'DeepSeek',
    protocolType: 'OpenAICompatible',
    contextWindowTokens: 128000,
    maxOutputTokens: 4096,
    temperature: 0.7
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
