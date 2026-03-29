using System.Text.Json.Serialization;

namespace ValorantBot.Models;

public class MatchHistoryEntry
{
    public string MatchId { get; init; } = string.Empty;
    public DateTime PlayedAt { get; init; }
    public string Map { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public bool Won { get; init; }
    public string Score { get; init; } = string.Empty;
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public double Acs { get; init; }
    public double Kda { get; init; }
    public double HeadshotPercent { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PerformanceRating Rating { get; init; }

    public static MatchHistoryEntry FromPerformanceResult(PerformanceResult result) => new()
    {
        MatchId = result.MatchData.Metadata.MatchId,
        PlayedAt = result.MatchData.Metadata.StartedAt,
        Map = result.MapName,
        Agent = result.MatchPlayer.Agent.Name,
        Won = result.Won,
        Score = result.Score,
        Kills = result.MatchPlayer.Stats.Kills,
        Deaths = result.MatchPlayer.Stats.Deaths,
        Assists = result.MatchPlayer.Stats.Assists,
        Acs = result.Acs,
        Kda = result.MatchPlayer.Stats.Kda,
        HeadshotPercent = result.MatchPlayer.Stats.HeadshotPercentage,
        Rating = result.Rating
    };
}
