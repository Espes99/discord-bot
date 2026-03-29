namespace ValorantBot.Models;

public class PlayerHistorySummary
{
    public int TotalMatches { get; init; }
    public double WinRate { get; init; }
    public int CurrentWinStreak { get; init; }
    public int CurrentLossStreak { get; init; }
    public double AverageAcs { get; init; }
    public double AverageKda { get; init; }
    public double AverageHsPercent { get; init; }
    public TrendDirection AcsTrend { get; init; }
    public TrendDirection HsPercentTrend { get; init; }
    public List<MapStat> MapStats { get; init; } = [];
    public List<AgentStat> AgentStats { get; init; } = [];
    public Dictionary<PerformanceRating, int> RatingDistribution { get; init; } = new();
    public int? MatchesSinceLastGoodGame { get; init; }
}

public enum TrendDirection { Declining, Stable, Improving }

public class MapStat
{
    public string Map { get; init; } = string.Empty;
    public int Wins { get; init; }
    public int Losses { get; init; }
    public double AverageAcs { get; init; }
}

public class AgentStat
{
    public string Agent { get; init; } = string.Empty;
    public int Games { get; init; }
}
