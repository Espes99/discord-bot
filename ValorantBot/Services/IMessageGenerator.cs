using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Generates Discord messages based on player performance.
/// </summary>
public interface IMessageGenerator
{
    /// <summary>
    /// Generates a banter/roast message for the given performance result.
    /// </summary>
    /// <param name="result">The player's performance result.</param>
    /// <returns>A formatted Discord message string.</returns>
    Task<string> GenerateMessageAsync(PerformanceResult result, PlayerHistorySummary? history = null, RankChangeInfo? rankChange = null);

    /// <summary>
    /// Generates a squad roast message when multiple tracked players queued together.
    /// </summary>
    /// <param name="results">Performance results for the squad members (same match, same team).</param>
    /// <param name="histories">Optional history summaries keyed by player name#tag.</param>
    /// <param name="rankChanges">Optional rank changes keyed by player name#tag.</param>
    /// <returns>A formatted Discord message roasting the whole squad.</returns>
    Task<string> GenerateSquadMessageAsync(List<PerformanceResult> results, Dictionary<string, PlayerHistorySummary>? histories = null, Dictionary<string, RankChangeInfo>? rankChanges = null);

    /// <summary>
    /// Generates a celebration or roast message for a rank change.
    /// </summary>
    Task<string> GenerateRankChangeMessageAsync(string playerName, string oldRank, string newRank, bool isPromotion, bool isMajorChange);

    /// <summary>
    /// Generates a very short banter blurb summarizing a player's recent matches.
    /// </summary>
    /// <param name="playerName">Display name#tag for the player.</param>
    /// <param name="storeKey">Player store key used for per-player message history.</param>
    /// <param name="recentMatches">The matches being summarized, newest first.</param>
    Task<string> GenerateSummaryMessageAsync(string playerName, string storeKey, List<MatchHistoryEntry> recentMatches);
}
