using System.Text.Json;
using Dust.OnlineServer.Protocol;

namespace Dust.OnlineServer.Lobbies;

internal sealed record RunSettings(
    string MapSize,
    string MazeStrictness,
    string HollowAmount,
    IReadOnlyList<string> HollowTypes,
    bool DifficultyScaling)
{
    public static RunSettings Default { get; } = new(
        "medium",
        "normal",
        "normal",
        ["square", "diamond", "hex", "sentry", "triangle", "camera", "star"],
        true);

    public static RunSettings Parse(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return Default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ProtocolException(
                "INVALID_SETTINGS",
                "Run settings must be an object.");
        }

        var input = element.Deserialize<SettingsInput>(ProtocolJson.Options)
            ?? new SettingsInput();
        var mapSize = Normalize(
            input.MapSize ?? Default.MapSize,
            "mapSize",
            ["small", "medium", "large"]);
        var strictness = Normalize(
            input.MazeStrictness ?? Default.MazeStrictness,
            "mazeStrictness",
            ["strict", "normal", "loose"]);
        var hollowAmount = Normalize(
            input.HollowAmount ?? Default.HollowAmount,
            "hollowAmount",
            ["none", "small", "normal", "large"]);

        var sourceTypes = input.HollowTypes ?? Default.HollowTypes;
        var hollowTypes = sourceTypes
            .Select(type => Normalize(
                type,
                "hollowTypes",
                ["square", "diamond", "hex", "sentry", "triangle", "camera", "star"]))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (hollowTypes.Length == 0 && hollowAmount != "none")
        {
            throw new ProtocolException(
                "INVALID_SETTINGS",
                "At least one hollow type is required unless hollowAmount is 'none'.");
        }

        return new RunSettings(
            mapSize,
            strictness,
            hollowAmount,
            hollowTypes,
            input.DifficultyScaling ?? Default.DifficultyScaling);
    }

    private static string Normalize(
        string value,
        string field,
        IReadOnlyCollection<string> allowed)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!allowed.Contains(normalized))
        {
            throw new ProtocolException(
                "INVALID_SETTINGS",
                $"'{field}' has an unsupported value.");
        }

        return normalized;
    }

    private sealed class SettingsInput
    {
        public string? MapSize { get; set; }
        public string? MazeStrictness { get; set; }
        public string? HollowAmount { get; set; }
        public string[]? HollowTypes { get; set; }
        public bool? DifficultyScaling { get; set; }
    }
}
