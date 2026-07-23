using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dust.OnlineServer.Protocol;

internal sealed record ClientEnvelope(
    string Type,
    string? RequestId,
    JsonElement Payload);

internal sealed class ProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32
    };

    public static string RequiredString(JsonElement payload, string name, int maxLength)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' must be a string.");
        }

        var result = value.GetString() ?? string.Empty;
        if (result.Length > maxLength)
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' is too long.");

        return result;
    }

    public static string? OptionalString(JsonElement payload, string name, int maxLength)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (value.ValueKind != JsonValueKind.String)
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' must be a string.");

        var result = value.GetString();
        if (result?.Length > maxLength)
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' is too long.");

        return result;
    }

    public static long RequiredNonNegativeInt64(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            !value.TryGetInt64(out var result) ||
            result < 0)
        {
            throw new ProtocolException(
                "INVALID_REQUEST",
                $"'{name}' must be a non-negative integer.");
        }

        return result;
    }

    public static bool RequiredBoolean(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' must be a boolean.");
        }

        return value.GetBoolean();
    }

    public static bool OptionalBoolean(JsonElement payload, string name, bool fallback = false)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' must be a boolean.");
        return value.GetBoolean();
    }

    public static JsonElement RequiredObject(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolException("INVALID_REQUEST", $"'{name}' must be an object.");
        }

        return value;
    }
}
