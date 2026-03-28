using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Analyzes a player's match performance and produces a rating.
/// </summary>
public interface IPerformanceAnalyzer
{
    /// <summary>
    /// Evaluates a player's performance in a specific match.
    /// </summary>
    /// <param name="player">The tracked player configuration.</param>
    /// <param name="matchPlayer">The player's data from the match.</param>
    /// <param name="matchData">The full match detail data.</param>
    /// <returns>A performance result with rating, stats, and context.</returns>
    PerformanceResult Analyze(TrackedPlayer player, MatchPlayer matchPlayer, MatchDetailData matchData);
}
