using Microsoft.Extensions.Logging;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Analyzes a player's match performance based on KDA, ACS, and headshot percentage.
/// </summary>
public class PerformanceAnalyzer(ILogger<PerformanceAnalyzer> logger) : IPerformanceAnalyzer
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
        var weaponContext = WeaponClassifier.ExtractForPlayer(matchData, matchPlayer.Puuid);

        if (weaponContext.HasData)
            logger.LogDebug(
                "[WeaponContext] {Player}: {Total} tracked kills — {Precision} precision, {NonPrecision} non-precision ({PrecisionPct:F0}%). Most used: {MostUsed}. LowHsExpected={LowHsExpected}",
                $"{matchPlayer.Name}#{matchPlayer.Tag}",
                weaponContext.TotalWeaponKills,
                weaponContext.PrecisionKills,
                weaponContext.NonPrecisionKills,
                weaponContext.PrecisionKillPercent,
                weaponContext.MostUsedWeapon ?? "unknown",
                weaponContext.LowHsExpected);
        else
            logger.LogDebug(
                "[WeaponContext] {Player}: no kill data available, weapon context inactive",
                $"{matchPlayer.Name}#{matchPlayer.Tag}");

        var rating = Evaluate(stats.Kda, acs, stats.HeadshotPercentage, weaponContext, matchPlayer, logger);

        return new PerformanceResult
        {
            Player = player,
            MatchPlayer = matchPlayer,
            MatchData = matchData,
            Rating = rating,
            Won = won,
            MapName = matchData.Metadata.Map.Name,
            Score = score,
            Acs = acs,
            WeaponContext = weaponContext
        };
    }

    private static double CalculateAcs(MatchDetailData matchData, PlayerStats stats)
    {
        var totalRounds = matchData.Teams.Sum(t => t.Rounds.Won + t.Rounds.Lost) / 2;
        return totalRounds == 0 ? 0 : (double)stats.Score / totalRounds;
    }

    private static PerformanceRating Evaluate(double kda, double acs, double hsPercent, WeaponContext? weaponContext, MatchPlayer matchPlayer, ILogger logger)
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

        // Headshot % scoring — skip penalty if player used mostly non-precision weapons
        var skipHsPenalty = weaponContext is { LowHsExpected: true };
        if (!skipHsPenalty && hsPercent < 12)
        {
            points -= 1;
            logger.LogInformation(
                "[WeaponContext] {Player}: HS% penalty applied ({HsPct:F1}% < 12%)",
                $"{matchPlayer.Name}#{matchPlayer.Tag}", hsPercent);
        }
        else if (skipHsPenalty && hsPercent < 12)
        {
            logger.LogInformation(
                "[WeaponContext] {Player}: HS% penalty SKIPPED ({HsPct:F1}% < 12%) — LowHsExpected due to {MostUsed}",
                $"{matchPlayer.Name}#{matchPlayer.Tag}", hsPercent, weaponContext!.MostUsedWeapon ?? "non-precision weapon");
        }
        else if (hsPercent > 28)
        {
            points += 1;
        }

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
