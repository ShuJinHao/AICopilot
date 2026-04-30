using System.Text.Encodings.Web;
using System.Text.Json;

namespace AICopilot.Services.CrossCutting.Serialization;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default
    };

    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, DefaultOptions);
    }
}
