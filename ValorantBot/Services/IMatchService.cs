using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Orchestrates match lookup and performance analysis for a tracked player.
/// </summary>
public interface IMatchService
{
    /// <summary>
    /// Fetches the latest completed match for a player and analyzes their performance.
    /// </summary>
    /// <param name="player">The player to check.</param>
    /// <param name="detailsCache">Optional per-cycle cache of match details keyed by match ID, to avoid refetching the same match for each stack member.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A performance result, or null if no completed match was found.</returns>
    Task<PerformanceResult?> GetLatestPerformanceAsync(
        TrackedPlayer player,
        IDictionary<string, MatchDetailData>? detailsCache = null,
        CancellationToken ct = default);

    /// <summary>
    /// Locates the player in already-fetched match details and analyzes their performance.
    /// </summary>
    /// <returns>A performance result, or null if the player did not take part in the match.</returns>
    PerformanceResult? BuildPerformance(TrackedPlayer player, MatchDetailData details);
}
