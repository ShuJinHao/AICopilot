using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICopilot.SharedKernel.Ai;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class CanonicalJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        return Canonicalize(element);
    }

    public static string Canonicalize(string json, IReadOnlySet<string>? excludedRootProperties = null)
    {
        return AgentCanonicalJsonV1.Canonicalize(json, excludedRootProperties);
    }

    public static string Canonicalize(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        return AgentCanonicalJsonV1.Canonicalize(json, maxUtf8Bytes, excludedRootProperties);
    }

    public static string Canonicalize(JsonElement element, IReadOnlySet<string>? excludedRootProperties = null)
    {
        return AgentCanonicalJsonV1.Canonicalize(element, excludedRootProperties);
    }

    public static int MeasureCanonicalUtf8Bytes(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        return AgentCanonicalJsonV1.MeasureCanonicalUtf8Bytes(
            json,
            maxUtf8Bytes,
            excludedRootProperties);
    }

    public static string ComputeSha256(string canonicalJson)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
    }

}
