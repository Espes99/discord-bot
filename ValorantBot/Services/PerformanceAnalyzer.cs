using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Analyzes a player's match performance based on KDA, ACS, and headshot percentage.
/// </summary>
public class PerformanceAnalyzer : IPerformanceAnalyzer
{
    /// <inheritdoc />
    public PerformanceResult Analyze(
        TrackedPlayer player, MatchPlayer matchPlayer, MatchDetailData matchData)
    {
        var stats = matchPlayer.Stats;

        var team = matchData.Teams.FirstOrDefault(t => t.TeamId == matchPlayer.TeamId);
        var won = team?.Won ?? false;
        var score = team is not null
            ? $"{team.Rounds.Won} - {team.Rounds.Lost}"
            : "N/A";

        var acs = CalculateAcs(matchData, stats);
        var rating = Evaluate(stats.Kda, acs, stats.HeadshotPercentage);

        return new PerformanceResult
        {
            Player = player,
            MatchPlayer = matchPlayer,
            MatchData = matchData,
            Rating = rating,
            Won = won,
            MapName = matchData.Metadata.Map.Name,
            Score = score,
            Acs = acs
        };
    }

    private static double CalculateAcs(MatchDetailData matchData, PlayerStats stats)
    {
        var totalRounds = matchData.Teams.Sum(t => t.Rounds.Won + t.Rounds.Lost) / 2;
        return totalRounds == 0 ? 0 : (double)stats.Score / totalRounds;
    }

    private static PerformanceRating Evaluate(double kda, double acs, double hsPercent)
    {
        var points = 0;

        // KDA scoring
        if (kda < 0.7) points -= 2;
        else if (kda < 1.0) points -= 1;
        else if (kda > 2.0) points += 2;
        else if (kda > 1.5) points += 1;

        // ACS scoring
        if (acs < 130) points -= 2;
        else if (acs < 170) points -= 1;
        else if (acs > 270) points += 2;
        else if (acs > 220) points += 1;

        // Headshot % scoring
        if (hsPercent < 12) points -= 1;
        else if (hsPercent > 28) points += 1;

        return points switch
        {
            <= -3 => PerformanceRating.Terrible,
            -2 or -1 => PerformanceRating.Bad,
            0 or 1 => PerformanceRating.Average,
            2 or 3 => PerformanceRating.Good,
            _ => PerformanceRating.Excellent
        };
    }
}
