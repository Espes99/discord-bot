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
    /// Adds a player. Returns false if the player is already tracked (by puuid, or name+tag if no puuid).
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

    /// <summary>
    /// Finds a tracked player by puuid.
    /// </summary>
    TrackedPlayer? FindByPuuid(string puuid);

    /// <summary>
    /// Finds a tracked player by name and tag (case-insensitive).
    /// </summary>
    TrackedPlayer? FindByNameTag(string name, string tag);

    /// <summary>
    /// Updates the name, tag, and/or puuid for a tracked player and persists.
    /// </summary>
    void UpdatePlayer(TrackedPlayer player);
}
