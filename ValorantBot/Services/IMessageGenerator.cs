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
    Task<string> GenerateMessageAsync(PerformanceResult result);

    /// <summary>
    /// Generates a squad roast message when multiple tracked players queued together.
    /// </summary>
    /// <param name="results">Performance results for the squad members (same match, same team).</param>
    /// <returns>A formatted Discord message roasting the whole squad.</returns>
    Task<string> GenerateSquadMessageAsync(List<PerformanceResult> results);
}
