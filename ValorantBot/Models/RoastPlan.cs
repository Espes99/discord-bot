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

public class RoastPlan
{
    public required string Voice { get; init; }
    public required RoastForm Form { get; init; }
    public required int Sentences { get; init; }
    public required int EmojiBudget { get; init; }
    public required RoastFocus MainFocus { get; init; }
    public RoastFocus? SideFocus { get; init; }
    public string? Allusion { get; init; }
    public required List<string> BannedOpeners { get; init; }
}

public class SquadMemberPlan
{
    public required string StoreKey { get; init; }
    public required string DisplayName { get; init; }
    public required string Agent { get; init; }
    public required SquadRole Role { get; init; }
    public RoastFocus? Focus { get; init; }
}

public class SquadRoastPlan
{
    public required string Voice { get; init; }
    public required SquadForm Form { get; init; }
    public required int MinSentences { get; init; }
    public required int MaxSentences { get; init; }
    public required int EmojiBudget { get; init; }
    public required List<SquadMemberPlan> Members { get; init; }
    public string? Allusion { get; init; }
    public string? AllusionOwner { get; init; }
    public required List<string> BannedOpeners { get; init; }
}

/// <summary>Persisted trace of a solo plan, used to rotate voice, form, focus and allusions per player.</summary>
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
    public string? Opener { get; init; }
}
