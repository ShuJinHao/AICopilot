using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AICopilot.AiGatewayService.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChunkType
{
    Error,
    Text,
    Metadata,
    Intent,
    FunctionCall,
    FunctionResult,
    Widget,
    ApprovalRequest,
    AgentEvent,
    AgentTask
}

public record ChatChunk(string Source, ChunkType Type, string Content);
