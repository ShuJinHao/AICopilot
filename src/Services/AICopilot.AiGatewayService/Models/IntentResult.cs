using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

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
    /// 推理过程
    /// 强制模型输出思维链，提高分类准确度
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    /// <summary>
    /// 检索参数
    /// - Knowledge 意图：检索关键词
    /// - 语义数据分析意图：可承载结构化 JSON 查询载荷
    /// - Policy 意图：默认承载用户原始问题文本
    /// </summary>
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}
