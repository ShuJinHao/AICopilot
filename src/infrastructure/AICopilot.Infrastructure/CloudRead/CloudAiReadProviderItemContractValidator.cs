using System.Globalization;
using System.Text.Json;
using AICopilot.Services.Contracts;

namespace AICopilot.Infrastructure.CloudRead;

internal static class CloudAiReadProviderItemContractValidator
{
    private static readonly ProviderItemContract ProductionFieldSchemaContract = new(
        Required("key", ProviderItemValueKind.String),
        Required("label", ProviderItemValueKind.String),
        Required("type", ProviderItemValueKind.String),
        RequiredNullable("unit", ProviderItemValueKind.String),
        RequiredNullable("precision", ProviderItemValueKind.Integer),
        Required("required", ProviderItemValueKind.Boolean));

    private static readonly IReadOnlyDictionary<CloudAiReadOperation, ProviderItemContract> Contracts =
        new Dictionary<CloudAiReadOperation, ProviderItemContract>
        {
            [CloudAiReadOperation.Device] = new(
                Required("id", ProviderItemValueKind.Guid),
                Required("deviceCode", ProviderItemValueKind.String),
                Required("deviceName", ProviderItemValueKind.String),
                Required("processId", ProviderItemValueKind.Guid)),
            [CloudAiReadOperation.Process] = new(
                Required("id", ProviderItemValueKind.Guid),
                Required("processCode", ProviderItemValueKind.String),
                Required("processName", ProviderItemValueKind.String)),
            [CloudAiReadOperation.ClientRelease] = new(
                Required("id", ProviderItemValueKind.Guid),
                Required("componentKind", ProviderItemValueKind.String),
                Required("componentKey", ProviderItemValueKind.String),
                Required("displayName", ProviderItemValueKind.String),
                Required("channel", ProviderItemValueKind.String),
                Required("targetRuntime", ProviderItemValueKind.String),
                Required("version", ProviderItemValueKind.String),
                Required("status", ProviderItemValueKind.String),
                RequiredNullable("releaseNotes", ProviderItemValueKind.String),
                Required("createdAtUtc", ProviderItemValueKind.DateTime),
                RequiredNullable("publishedAtUtc", ProviderItemValueKind.DateTime),
                RequiredNullable("deletedAtUtc", ProviderItemValueKind.DateTime)),
            [CloudAiReadOperation.DeviceClientState] = new(
                Required("deviceId", ProviderItemValueKind.Guid),
                Required("deviceName", ProviderItemValueKind.String),
                Required("clientCode", ProviderItemValueKind.String),
                RequiredNullable("primaryIp", ProviderItemValueKind.String),
                RequiredNullable("channel", ProviderItemValueKind.String),
                RequiredNullable("hostVersion", ProviderItemValueKind.String),
                RequiredNullable("hostApiVersion", ProviderItemValueKind.String),
                RequiredNullable("versionReportedAtUtc", ProviderItemValueKind.DateTime),
                RequiredNullable("versionReceivedAtUtc", ProviderItemValueKind.DateTime),
                Required("softwareStatus", ProviderItemValueKind.String),
                RequiredNullable("runtimeStatus", ProviderItemValueKind.String),
                RequiredNullable("runtimeStartedAtUtc", ProviderItemValueKind.DateTime),
                RequiredNullable("lastRuntimeHeartbeatAtUtc", ProviderItemValueKind.DateTime),
                RequiredNullable("updatedAtUtc", ProviderItemValueKind.DateTime)),
            [CloudAiReadOperation.CapacitySummary] = new(
                Required("date", ProviderItemValueKind.Date),
                Required("totalCount", ProviderItemValueKind.Integer),
                Required("okCount", ProviderItemValueKind.Integer),
                Required("ngCount", ProviderItemValueKind.Integer),
                Required("dayShiftTotal", ProviderItemValueKind.Integer),
                Required("nightShiftTotal", ProviderItemValueKind.Integer)),
            [CloudAiReadOperation.CapacityHourly] = new(
                Required("time", ProviderItemValueKind.DateTime),
                Required("date", ProviderItemValueKind.Date),
                Required("hour", ProviderItemValueKind.Integer),
                Required("minute", ProviderItemValueKind.Integer),
                Required("timeLabel", ProviderItemValueKind.String),
                Required("shiftCode", ProviderItemValueKind.String),
                Required("totalCount", ProviderItemValueKind.Integer),
                Required("okCount", ProviderItemValueKind.Integer),
                Required("ngCount", ProviderItemValueKind.Integer),
                Required("okRate", ProviderItemValueKind.Number)),
            [CloudAiReadOperation.DeviceLog] = new(
                Required("id", ProviderItemValueKind.Guid),
                Required("deviceId", ProviderItemValueKind.Guid),
                Required("deviceName", ProviderItemValueKind.String),
                Required("level", ProviderItemValueKind.String),
                Required("message", ProviderItemValueKind.String),
                Required("logTime", ProviderItemValueKind.DateTime),
                Required("receivedAt", ProviderItemValueKind.DateTime)),
            [CloudAiReadOperation.ProductionRecord] = new(
                Required("recordId", ProviderItemValueKind.Guid),
                Required("typeKey", ProviderItemValueKind.String),
                Required("typeName", ProviderItemValueKind.String),
                Required("deviceId", ProviderItemValueKind.Guid),
                Required("deviceName", ProviderItemValueKind.String),
                RequiredNullable("barcode", ProviderItemValueKind.String),
                RequiredNullable("result", ProviderItemValueKind.String),
                RequiredNullable("completedAt", ProviderItemValueKind.DateTime),
                RequiredNullable("receivedAt", ProviderItemValueKind.DateTime),
                Required("fields", ProviderItemValueKind.ScalarObject),
                Required(
                    "fieldSchema",
                    ProviderItemValueKind.ObjectArray,
                    ProductionFieldSchemaContract))
        };

    public static void Validate(CloudAiReadOperation operation, IReadOnlyList<JsonElement> records)
    {
        if (!Contracts.TryGetValue(operation, out var contract))
        {
            throw CloudAiReadJsonValueReader.InvalidProviderContract();
        }

        foreach (var record in records)
        {
            ValidateObject(record, contract);
            if (operation == CloudAiReadOperation.ProductionRecord)
            {
                ValidateProductionRecord(record);
            }
        }
    }

    private static void ValidateProductionRecord(JsonElement record)
    {
        var schemaByKey = new Dictionary<string, ProductionFieldContract>(StringComparer.Ordinal);
        var caseInsensitiveSchemaKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schemaItem in record.GetProperty("fieldSchema").EnumerateArray())
        {
            var key = schemaItem.GetProperty("key").GetString()!;
            var type = schemaItem.GetProperty("type").GetString()!;
            var required = schemaItem.GetProperty("required").GetBoolean();
            if (!IsSafeProductionFieldKey(key) ||
                !IsSupportedProductionFieldType(type) ||
                !caseInsensitiveSchemaKeys.Add(key) ||
                !schemaByKey.TryAdd(key, new ProductionFieldContract(type, required)))
            {
                throw CloudAiReadJsonValueReader.InvalidProviderContract();
            }
        }

        foreach (var field in record.GetProperty("fields").EnumerateObject())
        {
            if (!schemaByKey.TryGetValue(field.Name, out var fieldContract) ||
                !MatchesProductionFieldContract(field.Value, fieldContract))
            {
                throw CloudAiReadJsonValueReader.InvalidProviderContract();
            }
        }
    }

    private static bool IsSafeProductionFieldKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (var index = 1; index < key.Length; index++)
        {
            var character = key[index];
            if (character is not (>= 'a' and <= 'z') and
                not (>= 'A' and <= 'Z') and
                not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedProductionFieldType(string type)
    {
        return type is "string" or "number" or "integer" or "boolean" or "datetime" or "enum";
    }

    private static bool MatchesProductionFieldContract(
        JsonElement value,
        ProductionFieldContract contract)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return !contract.Required;
        }

        return contract.Type switch
        {
            "string" or "enum" =>
                value.ValueKind == JsonValueKind.String &&
                (!contract.Required || !string.IsNullOrWhiteSpace(value.GetString())),
            "datetime" =>
                value.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(value.GetString(), out _),
            "integer" =>
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDecimal(out var integer) &&
                decimal.Truncate(integer) == integer,
            "number" =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => false
        };
    }

    private static void ValidateObject(JsonElement value, ProviderItemContract contract)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw CloudAiReadJsonValueReader.InvalidProviderContract();
        }

        var exactPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitivePropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!exactPropertyNames.Add(property.Name) ||
                !caseInsensitivePropertyNames.Add(property.Name) ||
                !contract.Fields.ContainsKey(property.Name))
            {
                throw CloudAiReadJsonValueReader.InvalidProviderContract();
            }
        }

        foreach (var field in contract.Fields.Values)
        {
            if (!value.TryGetProperty(field.Name, out var property))
            {
                if (field.Required)
                {
                    throw CloudAiReadJsonValueReader.InvalidProviderContract();
                }

                continue;
            }

            if (property.ValueKind == JsonValueKind.Null)
            {
                if (!field.Nullable)
                {
                    throw CloudAiReadJsonValueReader.InvalidProviderContract();
                }

                continue;
            }

            ValidateValue(property, field);
        }
    }

    private static void ValidateValue(JsonElement value, ProviderItemField field)
    {
        var valid = field.ValueKind switch
        {
            ProviderItemValueKind.String => value.ValueKind == JsonValueKind.String,
            ProviderItemValueKind.Guid =>
                value.ValueKind == JsonValueKind.String &&
                value.TryGetGuid(out var id) &&
                id != Guid.Empty,
            ProviderItemValueKind.Date =>
                value.ValueKind == JsonValueKind.String &&
                DateOnly.TryParseExact(
                    value.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _),
            ProviderItemValueKind.DateTime =>
                value.ValueKind == JsonValueKind.String &&
                value.TryGetDateTime(out _),
            ProviderItemValueKind.Integer =>
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            ProviderItemValueKind.Number =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            ProviderItemValueKind.Boolean =>
                value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ProviderItemValueKind.ScalarObject => IsScalarObject(value),
            ProviderItemValueKind.ObjectArray => ValidateObjectArray(value, field.NestedContract),
            _ => false
        };

        if (!valid)
        {
            throw CloudAiReadJsonValueReader.InvalidProviderContract();
        }
    }

    private static bool IsScalarObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var exactPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitivePropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return value.EnumerateObject().All(property =>
               exactPropertyNames.Add(property.Name) &&
               caseInsensitivePropertyNames.Add(property.Name) &&
               (property.Value.ValueKind switch
               {
                   JsonValueKind.String or
                   JsonValueKind.True or
                   JsonValueKind.False or
                   JsonValueKind.Null => true,
                   JsonValueKind.Number =>
                       property.Value.TryGetInt64(out _) || property.Value.TryGetDecimal(out _),
                   _ => false
               }));
    }

    private static bool ValidateObjectArray(
        JsonElement value,
        ProviderItemContract? nestedContract)
    {
        if (value.ValueKind != JsonValueKind.Array || nestedContract is null)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            ValidateObject(item, nestedContract);
        }

        return true;
    }

    private static ProviderItemField Required(
        string name,
        ProviderItemValueKind valueKind,
        ProviderItemContract? nestedContract = null)
    {
        return new ProviderItemField(
            name,
            valueKind,
            Required: true,
            Nullable: false,
            NestedContract: nestedContract);
    }

    private static ProviderItemField RequiredNullable(string name, ProviderItemValueKind valueKind)
    {
        return new ProviderItemField(name, valueKind, Required: true, Nullable: true, NestedContract: null);
    }

    private sealed class ProviderItemContract(params ProviderItemField[] fields)
    {
        public IReadOnlyDictionary<string, ProviderItemField> Fields { get; } =
            fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
    }

    private sealed record ProviderItemField(
        string Name,
        ProviderItemValueKind ValueKind,
        bool Required,
        bool Nullable,
        ProviderItemContract? NestedContract);

    private sealed record ProductionFieldContract(string Type, bool Required);

    private enum ProviderItemValueKind
    {
        String,
        Guid,
        Date,
        DateTime,
        Integer,
        Number,
        Boolean,
        ScalarObject,
        ObjectArray
    }
}
