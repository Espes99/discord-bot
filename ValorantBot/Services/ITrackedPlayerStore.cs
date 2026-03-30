using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Persisted, thread-safe store of dynamically tracked Valorant players.
/// </summary>
public interface ITrackedPlayerStore
{
    /// <summary>
    /// Returns all currently tracked players.
    /// </summary>
    List<TrackedPlayer> GetAll();

    /// <summary>
    /// Adds a player. Returns false if the player is already tracked (name+tag, case-insensitive).
    /// </summary>
    bool Add(TrackedPlayer player);

    /// <summary>
    /// Removes a player by name and tag. Returns false if the player was not found.
    /// </summary>
    bool Remove(string name, string tag);

    /// <summary>
    /// Returns true if the player (name+tag) is currently tracked.
    /// </summary>
    bool Contains(string name, string tag);
}
