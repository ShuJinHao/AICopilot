export interface CrudMessageSet {
  loadFailed: string
  loadForbidden: string
  saveFailed: string
  saveForbidden: string
  deleteFailed: string
  deleteForbidden: string
}

export const CONFIG_STORE_MESSAGES = {
  pageLoadFailed: '配置页面加载失败，请稍后重试。',
  pageLoadForbidden: '当前账号没有查看配置的权限。',
  languageModel: {
    loadFailed: '加载模型详情失败，请稍后重试。',
    loadForbidden: '当前账号没有查看模型详情的权限。',
    saveFailed: '保存模型失败，请稍后重试。',
    saveForbidden: '当前账号没有管理模型的权限。',
    deleteFailed: '删除模型失败，请稍后重试。',
    deleteForbidden: '当前账号没有删除模型的权限。'
  },
  routingModel: {
    loadFailed: '加载路由模型详情失败，请稍后重试。',
    loadForbidden: '当前账号没有查看路由模型详情的权限。',
    saveFailed: '保存路由模型失败，请稍后重试。',
    saveForbidden: '当前账号没有管理路由模型的权限。',
    deleteFailed: '删除路由模型失败，请稍后重试。',
    deleteForbidden: '当前账号没有删除路由模型的权限。'
  },
  conversationTemplate: {
    loadFailed: '加载模板详情失败，请稍后重试。',
    loadForbidden: '当前账号没有查看模板详情的权限。',
    saveFailed: '保存模板失败，请稍后重试。',
    saveForbidden: '当前账号没有管理模板的权限。',
    deleteFailed: '删除模板失败，请稍后重试。',
    deleteForbidden: '当前账号没有删除模板的权限。'
  }
} as const satisfies Record<string, unknown>

export const RAG_STORE_MESSAGES = {
  pageLoadFailed: '知识库页面加载失败，请稍后重试。',
  pageLoadForbidden: '当前账号没有查看知识库配置的权限。',
  selectKnowledgeBaseFirst: '请先选择一个知识库。',
  embeddingModel: {
    loadFailed: '加载嵌入模型详情失败，请稍后重试。',
    loadForbidden: '当前账号没有查看嵌入模型详情的权限。',
    saveFailed: '保存嵌入模型失败，请稍后重试。',
    saveForbidden: '当前账号没有管理嵌入模型的权限。',
    deleteFailed: '删除嵌入模型失败，请确认没有知识库仍在使用该模型。',
    deleteForbidden: '当前账号没有删除嵌入模型的权限。'
  },
  knowledgeBase: {
    loadFailed: '加载知识库详情失败，请稍后重试。',
    loadForbidden: '当前账号没有查看知识库详情的权限。',
    saveFailed: '保存知识库失败，请稍后重试。',
    saveForbidden: '当前账号没有管理知识库的权限。',
    deleteFailed: '删除知识库失败，请稍后重试。',
    deleteForbidden: '当前账号没有删除知识库的权限。'
  },
  document: {
    uploadFailed: '上传文档失败，请检查文件大小和格式后重试。',
    uploadForbidden: '当前账号没有上传知识库文档的权限。',
    deleteFailed: '删除文档失败，请稍后重试。',
    deleteForbidden: '当前账号没有删除知识库文档的权限。',
    governanceSaveFailed: '保存文档治理设置失败，请检查字段后重试。',
    governanceSaveForbidden: '当前账号没有编辑知识库文档治理设置的权限。'
  },
  search: {
    failed: '检索知识库失败，请稍后重试。',
    forbidden: '当前账号没有检索知识库的权限。'
  }
} as const
