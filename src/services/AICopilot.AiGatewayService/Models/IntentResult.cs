using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using AICopilot.Services.Contracts;

namespace AICopilot.AiGatewayService.Models;

/// <summary>
/// 意图识别的标准输出结果
/// </summary>
public record IntentResult
{
    /// <summary>
    /// 意图标识符
    /// 规范：
    /// - 工具类：Action.{PluginName}
    /// - 知识类：Knowledge.{KnowledgeBaseName}
    /// - 自由数据分析：Analysis.{BusinessDatabaseName}
    /// - 结构化业务语义分析：Analysis.Device.* / Analysis.DeviceLog.* / Analysis.Recipe.* / Analysis.Capacity.* / Analysis.ProductionData.*
    /// - 业务规则语义：Policy.*
    /// </summary>
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// 置信度 (0.0 - 1.0)
    /// 用于下游节点的“置信度门控”机制
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>
    /// 路由边界内部的简短诊断标记。
    /// 不接受模型输出，不序列化、不持久化、不返回前端；不得承载思维链。
    /// </summary>
    [JsonIgnore]
    public string? RoutingNote { get; set; }

    /// <summary>
    /// 由服务端确认流程写入的业务查询确认状态。
    /// 模型路由输出、会话存在或高置信度都不能设置或替代该状态。
    /// </summary>
    [JsonIgnore]
    public BusinessQueryConfirmation? ConfirmedBusinessQueryContext { get; set; }

    /// <summary>
    /// 仅当用户在本次任务中明确选择业务数据源时由服务端设置。
    /// </summary>
    [JsonIgnore]
    public bool BusinessDataSourceExplicitlySelected { get; set; }

    /// <summary>
    /// 服务端通过一次性确认 challenge 恢复的完整查询上下文。
    /// 该值不接受模型 JSON，也不持久化到路由输出。
    /// </summary>
    [JsonIgnore]
    public BusinessQueryContext? ConfirmedBusinessQuery { get; set; }

    /// <summary>
    /// 检索参数
    /// - Knowledge 意图：检索关键词
    /// - 语义数据分析意图：可承载结构化 JSON 查询载荷
    /// - Policy 意图：默认承载用户原始问题文本
    /// </summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}
