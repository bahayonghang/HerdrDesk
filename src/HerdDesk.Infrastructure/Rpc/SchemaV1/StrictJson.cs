using System.Collections.Frozen;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

internal sealed class DecodeFail : Exception
{
    public DecodeFail(string code) : base(code)
    {
    }
}

internal static class StrictJson
{
    public static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = 64,
        AllowDuplicateProperties = false
    };

    public static void RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        CheckDuplicateKeys(element);
    }

    public static void CheckDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(GetName(property)))
                    throw new DecodeFail(ProjectionCodes.DuplicateJsonKey);
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
                CheckDuplicateKeys(child);
        }
    }

    public static FrozenDictionary<string, JsonElement> Partition(
        JsonElement element,
        FrozenSet<string> known,
        Dictionary<string, JsonElement> fields)
    {
        RequireObject(element);
        Dictionary<string, JsonElement>? extras = null;
        foreach (var property in element.EnumerateObject())
        {
            var name = GetName(property);
            if (known.Contains(name))
            {
                fields[name] = property.Value;
                continue;
            }
            extras ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            extras[name] = property.Value.Clone();
        }
        return extras is null
            ? FrozenDictionary<string, JsonElement>.Empty
            : extras.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static bool Has(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) &&
        value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    public static string RequiredString(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        return GetString(value);
    }

    public static string? OptionalString(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            return null;
        return GetString(value);
    }

    public static bool RequiredBool(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return value.GetBoolean();
    }

    public static bool? OptionalBool(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return value.GetBoolean();
    }

    public static ulong RequiredUInt64(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        return GetUInt64(value);
    }

    public static int RequiredProtocol(Dictionary<string, JsonElement> fields, string name)
    {
        var value = RequiredUInt64(fields, name);
        if (value > int.MaxValue)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return (int)value;
    }

    public static ushort RequiredUInt16(Dictionary<string, JsonElement> fields, string name)
    {
        var value = RequiredUInt64(fields, name);
        if (value > ushort.MaxValue)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return (ushort)value;
    }

    public static double RequiredFiniteNumber(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
            !double.IsFinite(number))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return number;
    }

    public static JsonElement RequiredArray(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        if (value.ValueKind != JsonValueKind.Array)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return value;
    }

    public static JsonElement RequiredObjectField(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        RequireObject(value);
        return value;
    }

    public static JsonElement? OptionalObject(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            return null;
        RequireObject(value);
        return value;
    }

    public static FrozenDictionary<string, string>? OptionalStringMap(
        Dictionary<string, JsonElement> fields,
        string name)
    {
        if (!fields.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Undefined
            or JsonValueKind.Null)
            return null;
        RequireObject(value);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var key = GetName(property);
            if (map.Count >= 32)
                throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
            map[key] = GetString(property.Value);
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static SchemaValue<TKnown> RequiredEnum<TKnown>(
        Dictionary<string, JsonElement> fields,
        string name,
        FrozenDictionary<string, TKnown> map)
        where TKnown : struct, Enum
    {
        var raw = RequiredString(fields, name);
        map.TryGetValue(raw, out var known);
        TKnown? boxed = map.ContainsKey(raw) ? known : null;
        return new SchemaValue<TKnown>(raw, boxed);
    }

    public static string GetString(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        try
        {
            return element.GetString() ?? throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        }
        catch (InvalidOperationException)
        {
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        }
    }

    public static string GetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        }
    }

    private static ulong GetUInt64(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        if (element.TryGetDouble(out var number) && !double.IsFinite(number))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        if (!element.TryGetUInt64(out var value))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        return value;
    }
}
