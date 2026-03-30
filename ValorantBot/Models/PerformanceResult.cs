namespace ValorantBot.Models;

public enum PerformanceRating
{
    Terrible,
    Bad,
    Average,
    Good,
    Excellent
}

public class PerformanceResult
{
    public required TrackedPlayer Player { get; init; }
    public required MatchPlayer MatchPlayer { get; init; }
    public required MatchDetailData MatchData { get; init; }
    public required PerformanceRating Rating { get; init; }
    public required bool Won { get; init; }
    public required string MapName { get; init; }
    public required string Score { get; init; }

    public required double Acs { get; init; }

    public WeaponContext? WeaponContext { get; init; }
}
