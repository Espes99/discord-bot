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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A performance result, or null if no completed match was found.</returns>
    Task<PerformanceResult?> GetLatestPerformanceAsync(TrackedPlayer player, CancellationToken ct = default);
}
