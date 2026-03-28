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
}
