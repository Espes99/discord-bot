using System.Text.Json.Serialization;

namespace ValorantBot.Models;

// --- Match List (v4) ---

public class MatchListResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public List<MatchListEntry> Data { get; set; } = [];
}

public class MatchListEntry
{
    [JsonPropertyName("metadata")]
    public MatchListMetadata Metadata { get; set; } = new();
}

public class MatchListMetadata
{
    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = string.Empty;

    [JsonPropertyName("map")]
    public MatchListMap Map { get; set; } = new();

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("queue")]
    public MatchListQueue Queue { get; set; } = new();
}

public class MatchListMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class MatchListQueue
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

// --- Match Detail (v4) ---

public class MatchDetailResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public MatchDetailData Data { get; set; } = new();
}

public class MatchDetailData
{
    [JsonPropertyName("metadata")]
    public MatchDetailMetadata Metadata { get; set; } = new();

    [JsonPropertyName("players")]
    public List<MatchPlayer> Players { get; set; } = [];

    [JsonPropertyName("teams")]
    public List<MatchTeam> Teams { get; set; } = [];
}

public class MatchDetailMetadata
{
    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = string.Empty;

    [JsonPropertyName("map")]
    public MatchDetailMap Map { get; set; } = new();

    [JsonPropertyName("queue")]
    public MatchDetailQueue Queue { get; set; } = new();

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }
}

public class MatchDetailMap
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class MatchDetailQueue
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class MatchPlayer
{
    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("team_id")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("agent")]
    public AgentInfo Agent { get; set; } = new();

    [JsonPropertyName("stats")]
    public PlayerStats Stats { get; set; } = new();

    [JsonPropertyName("tier")]
    public TierInfo Tier { get; set; } = new();
}

public class AgentInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class TierInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class PlayerStats
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("kills")]
    public int Kills { get; set; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("headshots")]
    public int Headshots { get; set; }

    [JsonPropertyName("bodyshots")]
    public int Bodyshots { get; set; }

    [JsonPropertyName("legshots")]
    public int Legshots { get; set; }

    public double Kda => Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;

    public int TotalShots => Headshots + Bodyshots + Legshots;

    public double HeadshotPercentage => TotalShots == 0 ? 0 : (double)Headshots / TotalShots * 100;
}

public class MatchTeam
{
    [JsonPropertyName("team_id")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("rounds")]
    public TeamRounds Rounds { get; set; } = new();

    [JsonPropertyName("won")]
    public bool Won { get; set; }
}

public class TeamRounds
{
    [JsonPropertyName("won")]
    public int Won { get; set; }

    [JsonPropertyName("lost")]
    public int Lost { get; set; }
}

// --- MMR / Rank (v3) ---

public class MmrResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public MmrData Data { get; set; } = new();
}

public class MmrData
{
    [JsonPropertyName("current")]
    public MmrCurrent Current { get; set; } = new();
}

public class MmrCurrent
{
    [JsonPropertyName("tier")]
    public TierInfo Tier { get; set; } = new();

    [JsonPropertyName("rr")]
    public int Rr { get; set; }

    [JsonPropertyName("last_change")]
    public int LastChange { get; set; }
}
