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

    [JsonPropertyName("rounds")]
    public List<MatchRound>? Rounds { get; set; }

    [JsonPropertyName("kills")]
    public List<MatchKill>? Kills { get; set; }
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

    [JsonPropertyName("game_length_in_ms")]
    public long GameLengthInMs { get; set; }

    [JsonPropertyName("party_rr_penaltys")]
    public List<PartyRrPenalty>? PartyRrPenaltys { get; set; }
}

public class PartyRrPenalty
{
    [JsonPropertyName("party_id")]
    public string PartyId { get; set; } = string.Empty;

    // Spec says integer, API sends 0.0
    [JsonPropertyName("penalty")]
    public double Penalty { get; set; }
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

    [JsonPropertyName("party_id")]
    public string PartyId { get; set; } = string.Empty;

    [JsonPropertyName("account_level")]
    public int AccountLevel { get; set; }

    [JsonPropertyName("ability_casts")]
    public AbilityCasts? AbilityCasts { get; set; }

    [JsonPropertyName("behavior")]
    public PlayerBehavior? Behavior { get; set; }

    [JsonPropertyName("economy")]
    public PlayerEconomy? Economy { get; set; }

    [JsonPropertyName("performance")]
    public PlayerPerformance? Performance { get; set; }
}

// Riot's own evaluation; every level is nullable upstream
public class PlayerPerformance
{
    [JsonPropertyName("ratings")]
    public PerformanceRatings? Ratings { get; set; }
}

public class PerformanceRatings
{
    [JsonPropertyName("combat")]
    public CombatRatings? Combat { get; set; }
}

public class CombatRatings
{
    // Trend: double_down, down, neutral, up, double_up
    [JsonPropertyName("trades")]
    public string? Trades { get; set; }
}

public class AbilityCasts
{
    [JsonPropertyName("grenade")]
    public int? Grenade { get; set; }

    // Spec says ability_1, API sends ability1; accept both
    [JsonPropertyName("ability1")]
    public int? Ability1 { get; set; }

    [JsonPropertyName("ability_1")]
    public int? Ability1Alt { get; set; }

    [JsonPropertyName("ability2")]
    public int? Ability2 { get; set; }

    [JsonPropertyName("ability_2")]
    public int? Ability2Alt { get; set; }

    [JsonPropertyName("ultimate")]
    public int? Ultimate { get; set; }

    public int Total => (Grenade ?? 0) + (Ability1 ?? Ability1Alt ?? 0) + (Ability2 ?? Ability2Alt ?? 0) + (Ultimate ?? 0);
}

public class PlayerBehavior
{
    [JsonPropertyName("afk_rounds")]
    public double AfkRounds { get; set; }

    [JsonPropertyName("friendly_fire")]
    public FriendlyFire? FriendlyFire { get; set; }

    [JsonPropertyName("rounds_in_spawn")]
    public double RoundsInSpawn { get; set; }
}

public class FriendlyFire
{
    [JsonPropertyName("incoming")]
    public double Incoming { get; set; }

    [JsonPropertyName("outgoing")]
    public double Outgoing { get; set; }
}

public class PlayerEconomy
{
    [JsonPropertyName("spent")]
    public EconomyTotals? Spent { get; set; }

    [JsonPropertyName("loadout_value")]
    public EconomyTotals? LoadoutValue { get; set; }
}

public class EconomyTotals
{
    [JsonPropertyName("overall")]
    public int Overall { get; set; }

    [JsonPropertyName("average")]
    public double Average { get; set; }
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

    [JsonPropertyName("damage")]
    public DamageTotals? Damage { get; set; }

    public double Kda => Deaths == 0 ? Kills + Assists : (double)(Kills + Assists) / Deaths;

    public int TotalShots => Headshots + Bodyshots + Legshots;

    public double HeadshotPercentage => TotalShots == 0 ? 0 : (double)Headshots / TotalShots * 100;
}

public class DamageTotals
{
    [JsonPropertyName("dealt")]
    public int Dealt { get; set; }

    [JsonPropertyName("received")]
    public int Received { get; set; }
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

// --- Round / Kill Detail ---

public class MatchRound
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("ceremony")]
    public string? Ceremony { get; set; }

    [JsonPropertyName("winning_team")]
    public string? WinningTeam { get; set; }

    [JsonPropertyName("plant")]
    public RoundSiteEvent? Plant { get; set; }

    [JsonPropertyName("defuse")]
    public RoundSiteEvent? Defuse { get; set; }

    [JsonPropertyName("stats")]
    public List<RoundPlayerStats>? Stats { get; set; }
}

public class RoundSiteEvent
{
    [JsonPropertyName("round_time_in_ms")]
    public int RoundTimeInMs { get; set; }

    [JsonPropertyName("site")]
    public string? Site { get; set; }

    [JsonPropertyName("player")]
    public RoundPlayer? Player { get; set; }
}

public class RoundPlayerStats
{
    [JsonPropertyName("player")]
    public RoundPlayer? Player { get; set; }

    [JsonPropertyName("stats")]
    public RoundStatTotals? Stats { get; set; }

    [JsonPropertyName("damage_events")]
    public List<RoundDamageEvent>? DamageEvents { get; set; }

    [JsonPropertyName("economy")]
    public RoundEconomy? Economy { get; set; }

    public int DamageDealt => DamageEvents?.Sum(e => e.Damage) ?? 0;

    [JsonPropertyName("was_afk")]
    public bool WasAfk { get; set; }

    [JsonPropertyName("received_penalty")]
    public bool ReceivedPenalty { get; set; }

    [JsonPropertyName("stayed_in_spawn")]
    public bool StayedInSpawn { get; set; }
}

public class RoundStatTotals
{
    [JsonPropertyName("kills")]
    public int Kills { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public class RoundDamageEvent
{
    [JsonPropertyName("player")]
    public RoundPlayer? Player { get; set; }

    [JsonPropertyName("damage")]
    public int Damage { get; set; }
}

public class RoundEconomy
{
    [JsonPropertyName("loadout_value")]
    public int LoadoutValue { get; set; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; set; }

    [JsonPropertyName("weapon")]
    public KillWeapon? Weapon { get; set; }
}

public class RoundPlayer
{
    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("team")]
    public string? Team { get; set; }
}

// Top-level kill entries under data.kills[]
public class MatchKill
{
    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("time_in_round_in_ms")]
    public int TimeInRoundInMs { get; set; }

    [JsonPropertyName("killer")]
    public KillPlayer? Killer { get; set; }

    [JsonPropertyName("victim")]
    public KillPlayer? Victim { get; set; }

    [JsonPropertyName("assistants")]
    public List<KillPlayer>? Assistants { get; set; }

    [JsonPropertyName("weapon")]
    public KillWeapon? Weapon { get; set; }

    // Where the victim died
    [JsonPropertyName("location")]
    public MapLocation? Location { get; set; }

    // Everyone still alive at the moment of the kill
    [JsonPropertyName("player_locations")]
    public List<PlayerLocation>? PlayerLocations { get; set; }
}

public class MapLocation
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    public double DistanceTo(MapLocation other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

public class PlayerLocation
{
    [JsonPropertyName("player")]
    public KillPlayer? Player { get; set; }

    [JsonPropertyName("location")]
    public MapLocation? Location { get; set; }
}

public class KillPlayer
{
    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("team")]
    public string? Team { get; set; }
}

public class KillWeapon
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
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

// --- Account Lookup (v1) ---

public class AccountResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public AccountData Data { get; set; } = new();
}

public class AccountData
{
    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("account_level")]
    public int AccountLevel { get; set; }
}
