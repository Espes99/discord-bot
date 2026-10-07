using System.Text.Json.Serialization;

namespace ValorantBot.Models;

public enum RoastForm
{
    OneLiner,
    SetupAndPunchline,
    FakeQuote,
    QuestionToPlayer,
    UnrelatedComparison,
    ListOfThree,
    Announcement,
    SecondPerson,
    ThirdPerson
}

public enum SquadForm
{
    GroupChatLeak,
    PostMatchInterview,
    AwardCeremony,
    IncidentReport,
    OneLinePerPlayer,
    BlameParagraph,
    TwoWayComparison
}

public enum SquadRole
{
    Scapegoat,
    Carry,
    Ghost,
    Footnote
}

/// <summary>One thing the message is allowed to be about. Kind is used for rotation, Text goes into the prompt.</summary>
public record RoastFocus(string Kind, string Text, int Weight);

/// <summary>A bio line or trait picked as roast material, with the joke angles it was already used for.</summary>
public record RoastAllusion(string Text, List<string> PreviousAngles);

/// <summary>
/// Material and limits for a solo roast. Code decides what is true and what was used recently;
/// the model picks the story, tone and form from it.
/// </summary>
public class RoastPlan
{
    /// <summary>Stories that are true for this match, strongest first.</summary>
    public required List<RoastFocus> Stories { get; init; }
    /// <summary>A story the message must cover, e.g. a rank change.</summary>
    public RoastFocus? RequiredStory { get; init; }
    public required List<string> ToneIdeas { get; init; }
    public required int MinSentences { get; init; }
    public required int MaxSentences { get; init; }
    public RoastAllusion? Allusion { get; init; }
    public required RecentChoices Recent { get; init; }
}

public class SquadMemberPlan
{
    public required string StoreKey { get; init; }
    public required string DisplayName { get; init; }
    public required string Agent { get; init; }
    public required SquadRole Role { get; init; }
    public required List<RoastFocus> Stories { get; init; }
}

public class SquadRoastPlan
{
    public required List<SquadMemberPlan> Members { get; init; }
    public required List<string> ToneIdeas { get; init; }
    public required int MinSentences { get; init; }
    public required int MaxSentences { get; init; }
    public RoastAllusion? Allusion { get; init; }
    public string? AllusionOwner { get; init; }
    public string? AllusionOwnerKey { get; init; }
    public required RecentChoices Recent { get; init; }
}

/// <summary>What was used lately, shown to the model as spent so it rotates on its own.</summary>
public class RecentChoices
{
    public List<string> Stories { get; init; } = [];
    public List<string> Tones { get; init; } = [];
    public List<string> Forms { get; init; } = [];
    public required List<string> BannedOpeners { get; init; }
}

/// <summary>The model's structured reply: what it chose, and the message itself.</summary>
public class RoastChoice
{
    [JsonPropertyName("story")] public string Story { get; set; } = "";
    [JsonPropertyName("tone")] public string Tone { get; set; } = "";
    [JsonPropertyName("form")] public string Form { get; set; } = "";
    [JsonPropertyName("angle")] public string Angle { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>Persisted trace of a solo roast, used to tell the model what is spent. Voice holds the tone the model chose.</summary>
public class RoastPlanRecord
{
    public DateTime At { get; init; }
    public string Source { get; init; } = "solo";
    public string? Voice { get; init; }
    public string? Form { get; init; }
    public string? FocusKind { get; init; }
    public string? Allusion { get; init; }
    public string? Opener { get; init; }
}

public class SquadPlanRecord
{
    public DateTime At { get; init; }
    public string? Voice { get; init; }
    public string? Form { get; init; }
    public Dictionary<string, string> Roles { get; init; } = new();
    public string? AllusionOwnerKey { get; init; }
    public string? Opener { get; init; }
}

/// <summary>One time a bio line or trait made it into a message. Kept longer than plan records so cooldowns can span days.</summary>
public class TraitUseRecord
{
    public DateTime At { get; init; }
    public required string Trait { get; init; }
    public string? Angle { get; init; }
}
