using System.Text.Encodings.Web;
using System.Text.Json;

namespace AICopilot.Services.Common.Helper;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(this object obj)
    {
        return JsonSerializer.Serialize(obj, DefaultOptions);
    }
}