using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Client for the HenrikDev Valorant API v4.
/// </summary>
public interface IHenrikDevClient
{
    /// <summary>
    /// Fetches the most recent matches for a player.
    /// </summary>
    /// <param name="name">Player display name.</param>
    /// <param name="tag">Player tag (e.g. "1234").</param>
    /// <param name="region">Region code (eu, na, kr, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of recent match entries, or an empty list if the player is not found.</returns>
    Task<List<MatchListEntry>> GetRecentMatchesAsync(string name, string tag, string region, CancellationToken ct = default);

    /// <summary>
    /// Fetches full details for a specific match.
    /// </summary>
    /// <param name="matchId">The match identifier.</param>
    /// <param name="region">Region code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Match detail data, or null if the match was not found.</returns>
    Task<MatchDetailData?> GetMatchDetailsAsync(string matchId, string region, CancellationToken ct = default);
}
