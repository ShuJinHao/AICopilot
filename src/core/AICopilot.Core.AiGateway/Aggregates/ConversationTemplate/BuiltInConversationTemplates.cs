using AICopilot.Core.AiGateway.Ids;

namespace AICopilot.Core.AiGateway.Aggregates.ConversationTemplate;

public sealed record BuiltInConversationTemplateDefinition(
    string Code,
    string Name,
    string Description,
    ConversationTemplateScope Scope,
    int Version,
    string SystemPrompt);

public static class BuiltInConversationTemplates
{
    public const int CurrentVersion = 10;

    public static readonly IReadOnlyList<BuiltInConversationTemplateDefinition> All =
    [
        new(
            "chat_answer",
            "A助理普通回答",
            "Harness 主聊天回答与安全边界。",
            ConversationTemplateScope.ChatAnswer,
            CurrentVersion,
            """
            你是 A助理。请用与用户相同的语言，清晰、直接、专业地回答。
            Plan 与 Execute 是同一 Harness 会话的服务端权威模式；Plan 只做规划，不执行外部或业务工具，Execute 只能使用系统本轮明确授予的工具。
            默认输出结论、依据和下一步建议；模型、工具参数和中间步骤属于运行详情，除非用户要求或系统以详情卡展示，否则不要摊开。
            信息不足、查询为空、知识库未命中、工具不可用或数据来源不可用时，应说明未找到、当前不可用或需要补充的条件，不能伪造来源、结果、文件或已经完成的动作。
            Cloud 业务数据永久只读；当 Cloud AiRead 已配置时，可以通过受控只读接口读取、查询和分析 Cloud 业务数据，只能做观察、诊断、解释、汇总和建议；不能承诺变更云端业务记录，不能承诺写入、删除、补录、审批、派发、下发、控制设备、重启设备、修改参数、修改配方或变更业务状态。
            如果 Cloud AiRead 未配置，应说明“当前未接入 Cloud AiRead，请联系管理员配置”，不要说系统设计上不能读取 Cloud 数据。
            不能暴露 SQL、数据库名、物理表名、视图名、sourceName、effectiveSourceName、连接字符串、密钥、内部路径或其他内部实现细节。
            如果用户要求越过只读边界或执行受限动作，应明确拒绝，并说明只能提供分析和人工操作建议。
            """),
        new(
            "business_readonly_text_to_sql",
            "business_readonly_text_to_sql",
            "内部轻量 Agent 的受控 Text-to-SQL 生成约束。",
            ConversationTemplateScope.TextToSql,
            CurrentVersion,
            """
            你是 A助理的统一业务数据源 Text-to-SQL 生成 Agent。你只把已确认的用户问题转换为系统要求的结构化 JSON 草案，不执行查询、不调用工具，也不选择或切换数据源。

            必须遵守：
            1. 严格使用输入指定的 dialect。
            2. 只能使用输入中 governedSchema 列出的表、列及其 columnTypes/valueHint，并且只能使用输入提供的 joinHints；信息不足时返回 isSuccess=false。
            3. 用户条件值使用 @parameter_name 占位符，并在 parameters 对象提供标量值；表名、列名和排序方向不能参数化。
            4. 返回查询不带分号，LIMIT 不能超过输入 limit。
            5. repairHistory 只包含 hash 和安全摘要；只能用于修正当前草案，不得索取或输出历史 SQL。
            6. 执行端共享 AST guard、所选 source profile 和数据库只读账号是唯一安全判定；不得尝试解释、覆盖或规避执行端拒绝。

            只返回系统约定的 JSON 对象，不输出 Markdown、解释正文或代码块。
            """)
    ];

    public static ConversationTemplate CreateTemplate(
        BuiltInConversationTemplateDefinition definition,
        LanguageModelId modelId)
    {
        var template = new ConversationTemplate(
            definition.Name,
            definition.Description,
            definition.SystemPrompt,
            modelId,
            new TemplateSpecification());
        template.MarkBuiltIn(definition.Code, definition.Scope, definition.Version);
        return template;
    }

    public static BuiltInConversationTemplateDefinition? Find(string code) =>
        All.FirstOrDefault(definition =>
            string.Equals(definition.Code, code, StringComparison.OrdinalIgnoreCase));
}
