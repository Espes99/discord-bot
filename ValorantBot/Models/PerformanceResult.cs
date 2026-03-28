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

    public double Acs
    {
        get
        {
            var totalRounds = MatchData.Teams.Sum(t => t.Rounds.Won + t.Rounds.Lost) / 2;
            return totalRounds == 0 ? 0 : (double)MatchPlayer.Stats.Score / totalRounds;
        }
    }
}
