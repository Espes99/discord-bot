using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValorantBot.Models;

public class PlayerProfile
{
    public string? Bio { get; set; }
    public List<string> ManualTraits { get; set; } = [];
    public List<AutoTrait> AutoTraits { get; set; } = [];
    public List<SquadTrait> SquadTraits { get; set; } = [];
    public DateTime LastAutoTraitUpdate { get; set; }
}

/// <summary>A trait derived from match history; Evidence is the numbers behind it, shown to the model.</summary>
[JsonConverter(typeof(AutoTraitConverter))]
public record AutoTrait(string Label, string? Evidence = null);

/// <summary>
/// A trait about how the owner plays with other tracked players. Only usable in a squad roast when every
/// puuid in MatePuuids is in the stack; empty means any stack. Id is stable across name changes, Label is not.
/// </summary>
public record SquadTrait(string Id, string Label, string Evidence, List<string> MatePuuids);

// Profiles saved before evidence existed hold auto traits as plain strings
public class AutoTraitConverter : JsonConverter<AutoTrait>
{
    public override AutoTrait Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new AutoTrait(reader.GetString()!);

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var evidence = root.TryGetProperty("Evidence", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return new AutoTrait(root.GetProperty("Label").GetString()!, evidence);
    }

    public override void Write(Utf8JsonWriter writer, AutoTrait value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Label", value.Label);
        if (value.Evidence is not null)
            writer.WriteString("Evidence", value.Evidence);
        writer.WriteEndObject();
    }
}

public enum BotLanguage
{
    English,
    Norwegian
}
