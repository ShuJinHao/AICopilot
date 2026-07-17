using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AICopilot.AiGatewayService.AgentTasks;

internal static class CanonicalJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
        return Canonicalize(element);
    }

    public static string Canonicalize(string json, IReadOnlySet<string>? excludedRootProperties = null)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 128
        });
        EnsureNoDuplicateProperties(document.RootElement, "$", depth: 0);
        return Canonicalize(document.RootElement, excludedRootProperties);
    }

    public static string Canonicalize(JsonElement element, IReadOnlySet<string>? excludedRootProperties = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Encoder = JavaScriptEncoder.Default,
                   Indented = false,
                   SkipValidation = false
               }))
        {
            WriteElement(writer, element, excludedRootProperties, depth: 0);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ComputeSha256(string canonicalJson)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlySet<string>? excludedRootProperties,
        int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .Where(property => depth != 0 || excludedRootProperties?.Contains(property.Name) != true)
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, excludedRootProperties, depth + 1);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, excludedRootProperties, depth + 1);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON token '{element.ValueKind}'.");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string path, int depth)
    {
        if (depth > 128)
        {
            throw new JsonException("JSON nesting exceeds the canonical contract depth limit.");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"Duplicate JSON property '{property.Name}' at '{path}'.");
                }

                EnsureNoDuplicateProperties(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item, $"{path}[{index}]", depth + 1);
                index++;
            }
        }
    }
}
