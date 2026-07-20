using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AICopilot.SharedKernel.Ai;

/// <summary>Single recursive canonical JSON owner for all agent contracts.</summary>
public static class AgentCanonicalJsonV1
{
    public const string PolicyVersion = "agent-canonical-json-policy:v1";
    public const int MaxDepth = 128;
    public const int MaxCanonicalJsonUtf8Bytes = 262_144;
    public const int MaxObjectProperties = 512;
    public const int MaxArrayItems = 4_096;
    public const int MaxTotalTokens = 32_768;
    public const int MaxPropertyNameUtf8Bytes = 256;
    public const int MaxStringUtf8Bytes = 65_536;

    public static string Canonicalize(
        string json,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        return Canonicalize(json, MaxCanonicalJsonUtf8Bytes, excludedRootProperties);
    }

    public static string Canonicalize(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        Preflight(json, maxUtf8Bytes);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaxDepth
        });
        EnsureNoDuplicateProperties(document.RootElement, "$", depth: 0);
        return CanonicalizeValidatedElement(document.RootElement, excludedRootProperties);
    }

    /// <summary>
    /// Performs a bounded streaming validation before any JSON DOM is created. The raw
    /// UTF-8 limit is owned by the calling contract; structural limits are shared by all
    /// agent JSON contracts so whitespace, huge collections, strings, and numbers cannot
    /// amplify memory or CPU use during canonicalization.
    /// </summary>
    public static int Preflight(string json, int maxUtf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maxUtf8Bytes <= 0 || maxUtf8Bytes > MaxCanonicalJsonUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
        }

        var utf8ByteCount = Encoding.UTF8.GetByteCount(json);
        if (utf8ByteCount > maxUtf8Bytes)
        {
            throw new JsonException(
                $"JSON input is {utf8ByteCount} UTF-8 bytes; maximum is {maxUtf8Bytes}.");
        }

        var utf8 = Encoding.UTF8.GetBytes(json);
        ValidateStructure(utf8);
        return utf8ByteCount;
    }

    /// <summary>
    /// Measures the canonical JSON produced by this owner without materializing the
    /// canonical payload. The returned count is capped at <paramref name="maxUtf8Bytes"/>
    /// plus one so callers can distinguish an exact boundary from overflow while all
    /// shared token, collection, string, number, duplicate-property, ordering, and
    /// escaping rules remain authoritative here. Measurement flushes each emitted
    /// canonical token to the bounded stream; normal canonicalization remains buffered.
    /// </summary>
    public static int MeasureCanonicalUtf8Bytes(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        return MeasureCanonicalUtf8BytesCore(json, maxUtf8Bytes, excludedRootProperties)
            .Utf8ByteCount;
    }

    internal static CanonicalMeasurementDiagnostics MeasureCanonicalUtf8BytesForDiagnostics(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        return MeasureCanonicalUtf8BytesCore(json, maxUtf8Bytes, excludedRootProperties);
    }

    private static CanonicalMeasurementDiagnostics MeasureCanonicalUtf8BytesCore(
        string json,
        int maxUtf8Bytes,
        IReadOnlySet<string>? excludedRootProperties)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maxUtf8Bytes <= 0 || maxUtf8Bytes > MaxCanonicalJsonUtf8Bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUtf8Bytes));
        }

        var utf8 = Encoding.UTF8.GetBytes(json);
        ValidateStructure(utf8);
        using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaxDepth
        });
        EnsureNoDuplicateProperties(document.RootElement, "$", depth: 0);

        using var stream = new BoundedCountingStream(maxUtf8Bytes);
        var measurement = new CanonicalMeasurementState();
        var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = false,
            SkipValidation = false
        });
        try
        {
            WriteElement(
                writer,
                document.RootElement,
                excludedRootProperties,
                depth: 0,
                measurement);
            measurement.CanonicalWriteTraversalCompleted = true;
            writer.Flush();
            writer.Dispose();
        }
        catch (CanonicalMeasurementLimitExceededException)
        {
            try
            {
                writer.Dispose();
            }
            catch (CanonicalMeasurementLimitExceededException)
            {
                // The first overflow is the only signal this bounded measure maps.
                // Dispose can flush the same buffered segment and repeat that signal.
            }

            return measurement.Snapshot(maxUtf8Bytes + 1, stream.SuccessfulWriteCount);
        }
        catch
        {
            try
            {
                writer.Dispose();
            }
            catch (CanonicalMeasurementLimitExceededException)
            {
                // Preserve the real structural/canonical failure that occurred first.
            }

            throw;
        }

        return measurement.Snapshot(checked((int)stream.Length), stream.SuccessfulWriteCount);
    }

    private static void ValidateStructure(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaxDepth
        });
        var containers = new Stack<PreflightContainer>();
        var tokenCount = 0;
        var rootValueSeen = false;

        while (reader.Read())
        {
            tokenCount++;
            if (tokenCount > MaxTotalTokens)
            {
                throw new JsonException($"JSON token count exceeds {MaxTotalTokens}.");
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    RegisterValue(containers, ref rootValueSeen);
                    containers.Push(PreflightContainer.Object());
                    break;
                case JsonTokenType.StartArray:
                    RegisterValue(containers, ref rootValueSeen);
                    containers.Push(PreflightContainer.Array());
                    break;
                case JsonTokenType.EndObject:
                    RequireAndPop(containers, isObject: true);
                    break;
                case JsonTokenType.EndArray:
                    RequireAndPop(containers, isObject: false);
                    break;
                case JsonTokenType.PropertyName:
                    RegisterProperty(containers, reader.GetString());
                    break;
                case JsonTokenType.String:
                    RegisterValue(containers, ref rootValueSeen);
                    EnsureUtf8Length(reader.GetString(), MaxStringUtf8Bytes, "JSON string");
                    break;
                case JsonTokenType.Number:
                    RegisterValue(containers, ref rootValueSeen);
                    if (reader.ValueSpan.Length > AgentCanonicalNumberPolicyV1.MaxLexicalCharacters)
                    {
                        throw new JsonException(
                            $"JSON number exceeds {AgentCanonicalNumberPolicyV1.MaxLexicalCharacters} lexical characters.");
                    }

                    _ = AgentCanonicalNumberPolicyV1.Normalize(Encoding.UTF8.GetString(reader.ValueSpan));
                    break;
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    RegisterValue(containers, ref rootValueSeen);
                    break;
                default:
                    throw new JsonException($"Unsupported JSON token '{reader.TokenType}'.");
            }
        }

        if (!rootValueSeen || containers.Count != 0)
        {
            throw new JsonException("JSON input must contain exactly one complete value.");
        }
    }

    public static string Canonicalize(
        JsonElement element,
        IReadOnlySet<string>? excludedRootProperties = null)
    {
        Preflight(element.GetRawText(), MaxCanonicalJsonUtf8Bytes);
        EnsureNoDuplicateProperties(element, "$", depth: 0);
        return CanonicalizeValidatedElement(element, excludedRootProperties);
    }

    private static string CanonicalizeValidatedElement(
        JsonElement element,
        IReadOnlySet<string>? excludedRootProperties)
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

    private static void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        IReadOnlySet<string>? excludedRootProperties,
        int depth,
        CanonicalMeasurementState? measurement = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                CommitMeasurementToken(writer, measurement);
                foreach (var property in element
                             .EnumerateObject()
                             .Where(property => depth != 0 || excludedRootProperties?.Contains(property.Name) != true)
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    CommitMeasurementToken(writer, measurement);
                    WriteElement(writer, property.Value, excludedRootProperties, depth + 1, measurement);
                }

                writer.WriteEndObject();
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                CommitMeasurementToken(writer, measurement);
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, excludedRootProperties, depth + 1, measurement);
                }

                writer.WriteEndArray();
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(
                    AgentCanonicalNumberPolicyV1.Normalize(element.GetRawText()),
                    skipInputValidation: false);
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                CommitMeasurementToken(writer, measurement);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                CommitMeasurementToken(writer, measurement);
                break;
            default:
                throw new JsonException($"Unsupported JSON token '{element.ValueKind}'.");
        }
    }

    private static void CommitMeasurementToken(
        Utf8JsonWriter writer,
        CanonicalMeasurementState? measurement)
    {
        if (measurement is null)
        {
            return;
        }

        measurement.CanonicalWriteTokenCount++;
        measurement.CanonicalTokenFlushAttemptCount++;
        writer.Flush();
    }

    private static void EnsureNoDuplicateProperties(JsonElement element, string path, int depth)
    {
        if (depth > MaxDepth)
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

    private static void RegisterValue(Stack<PreflightContainer> containers, ref bool rootValueSeen)
    {
        if (containers.Count == 0)
        {
            if (rootValueSeen)
            {
                throw new JsonException("JSON input contains more than one root value.");
            }

            rootValueSeen = true;
            return;
        }

        var parent = containers.Peek();
        if (!parent.IsObject)
        {
            parent.ItemCount++;
            if (parent.ItemCount > MaxArrayItems)
            {
                throw new JsonException($"JSON array contains more than {MaxArrayItems} items.");
            }
        }
    }

    private static void RegisterProperty(Stack<PreflightContainer> containers, string? propertyName)
    {
        if (containers.Count == 0 || !containers.Peek().IsObject || propertyName is null)
        {
            throw new JsonException("JSON property is outside an object.");
        }

        EnsureUtf8Length(propertyName, MaxPropertyNameUtf8Bytes, "JSON property name");
        var parent = containers.Peek();
        parent.ItemCount++;
        if (parent.ItemCount > MaxObjectProperties)
        {
            throw new JsonException($"JSON object contains more than {MaxObjectProperties} properties.");
        }

        if (!parent.PropertyNames!.Add(propertyName))
        {
            throw new JsonException($"Duplicate JSON property '{propertyName}'.");
        }

        if (!parent.CaseFoldedPropertyNames!.Add(propertyName))
        {
            throw new JsonException(
                $"JSON property '{propertyName}' has a case-fold collision in the same object.");
        }
    }

    private static void RequireAndPop(Stack<PreflightContainer> containers, bool isObject)
    {
        if (containers.Count == 0 || containers.Peek().IsObject != isObject)
        {
            throw new JsonException("JSON container boundaries are invalid.");
        }

        containers.Pop();
    }

    private static void EnsureUtf8Length(string? value, int maximum, string label)
    {
        if (value is null || Encoding.UTF8.GetByteCount(value) > maximum)
        {
            throw new JsonException($"{label} exceeds {maximum} UTF-8 bytes.");
        }
    }

    private sealed class PreflightContainer
    {
        private PreflightContainer(bool isObject)
        {
            IsObject = isObject;
            PropertyNames = isObject ? new HashSet<string>(StringComparer.Ordinal) : null;
            CaseFoldedPropertyNames = isObject ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        }

        public bool IsObject { get; }

        public int ItemCount { get; set; }

        public HashSet<string>? PropertyNames { get; }

        public HashSet<string>? CaseFoldedPropertyNames { get; }

        public static PreflightContainer Object() => new(true);

        public static PreflightContainer Array() => new(false);
    }

    private sealed class BoundedCountingStream(long maximum) : Stream
    {
        private long count;

        public int SuccessfulWriteCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => count;

        public override long Position
        {
            get => count;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int readCount) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int writeCount)
        {
            Count(writeCount);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Count(buffer.Length);
        }

        public override void WriteByte(byte value)
        {
            Count(1);
        }

        private void Count(int writeCount)
        {
            if (writeCount <= 0)
            {
                return;
            }

            if (count > maximum - writeCount)
            {
                count = maximum + 1;
                throw new CanonicalMeasurementLimitExceededException();
            }

            count += writeCount;
            SuccessfulWriteCount++;
        }
    }

    private sealed class CanonicalMeasurementState
    {
        public int CanonicalWriteTokenCount { get; set; }

        public int CanonicalTokenFlushAttemptCount { get; set; }

        public bool CanonicalWriteTraversalCompleted { get; set; }

        public CanonicalMeasurementDiagnostics Snapshot(
            int utf8ByteCount,
            int successfulStreamWriteCount)
        {
            return new CanonicalMeasurementDiagnostics(
                utf8ByteCount,
                CanonicalWriteTokenCount,
                CanonicalTokenFlushAttemptCount,
                successfulStreamWriteCount,
                CanonicalWriteTraversalCompleted);
        }
    }

    internal readonly record struct CanonicalMeasurementDiagnostics(
        int Utf8ByteCount,
        int CanonicalWriteTokenCount,
        int CanonicalTokenFlushAttemptCount,
        int SuccessfulStreamWriteCount,
        bool CanonicalWriteTraversalCompleted);

    private sealed class CanonicalMeasurementLimitExceededException : Exception
    {
    }
}
