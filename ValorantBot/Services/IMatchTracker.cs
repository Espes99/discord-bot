namespace ValorantBot.Services;

/// <summary>
/// Tracks last-seen match IDs per player to detect new matches.
/// </summary>
public interface IMatchTracker
{
    /// <summary>
    /// Returns true if the given match has not been seen before for this player.
    /// </summary>
    bool IsNewMatch(string playerKey, string matchId);

    /// <summary>
    /// Records a match as seen for the given player and persists to disk.
    /// </summary>
    void SetLastMatch(string playerKey, string matchId);
}
