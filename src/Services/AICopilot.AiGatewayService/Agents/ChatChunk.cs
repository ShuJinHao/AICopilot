using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AICopilot.AiGatewayService.Agents;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChunkType
{
    Error,
    Text,
    Intent,
    FunctionCall,
    FunctionResult,
    Widget
}

public record ChatChunk(string Source, ChunkType Type, string Content);