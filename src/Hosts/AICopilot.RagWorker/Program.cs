using AICopilot.EntityFrameworkCore;
using AICopilot.EventBus;
using AICopilot.Infrastructure.Storage;
using AICopilot.RagWorker;
using AICopilot.RagWorker.Services;
using AICopilot.RagWorker.Services.Embeddings;
using AICopilot.RagWorker.Services.Parsers;
using AICopilot.RagWorker.Services.TokenCounter;
using AICopilot.Services.Common.Contracts;
using Microsoft.SemanticKernel.Services;
using AICopilot.Embedding;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// 1. 添加 Aspire 服务默认配置
builder.AddServiceDefaults();

// 2. 注册数据库上下文 (PostgreSQL)
// 这里的连接字符串名称需与 AppHost 中定义的一致
builder.AddNpgsqlDbContext<AiCopilotDbContext>("ai-copilot");

// 3. 注册文件存储服务
// 必须与 HttpApi 使用相同的存储实现，确保能读取到上传的文件
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

// 4. 注册事件总线 (RabbitMQ)
// 将自动扫描当前程序集下的 Consumer
builder.AddEventBus(typeof(Program).Assembly);

// 注册解析器
builder.Services.AddSingleton<IDocumentParser, PdfDocumentParser>();
builder.Services.AddSingleton<IDocumentParser, TextDocumentParser>();

// 注册工厂
builder.Services.AddSingleton<DocumentParserFactory>();

builder.Services.AddScoped<RagAppService>();

// 注册Token计数器
builder.Services.AddSingleton<ITokenCounter, SharpTokenCounter>();
// 文本分割
builder.Services.AddSingleton<TextSplitterService>();

builder.AddEmbedding();

var host = builder.Build();
host.Run();