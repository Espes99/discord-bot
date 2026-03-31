using System.Text.Json;
using System.Text.Json.Serialization;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// File-backed, thread-safe store of dynamically tracked Valorant players.
/// Persists to {DATA_DIR}/tracked_players.json.
/// </summary>
public class TrackedPlayerStore : ITrackedPlayerStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger<TrackedPlayerStore> _logger;
    private readonly object _lock = new();
    private List<TrackedPlayer> _players = [];

    public TrackedPlayerStore(ILogger<TrackedPlayerStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "tracked_players.json");
        Load();
    }

    public List<TrackedPlayer> GetAll()
    {
        lock (_lock)
            return _players.ToList();
    }

    public bool Add(TrackedPlayer player)
    {
        lock (_lock)
        {
            // Check by puuid first if available, then by name+tag
            if (player.Puuid is not null && FindByPuuid_Locked(player.Puuid) is not null)
                return false;

            if (Contains_Locked(player.Name, player.Tag))
                return false;

            _players.Add(player);
            Save();
            _logger.LogInformation("Now tracking {Name}#{Tag} ({Region}, puuid: {Puuid})",
                player.Name, player.Tag, player.Region, player.Puuid ?? "pending");
            return true;
        }
    }

    public bool Remove(string name, string tag)
    {
        lock (_lock)
        {
            var existing = Find_Locked(name, tag);
            if (existing is null)
                return false;

            _players.Remove(existing);
            Save();
            _logger.LogInformation("Stopped tracking {Name}#{Tag}", name, tag);
            return true;
        }
    }

    public bool Contains(string name, string tag)
    {
        lock (_lock)
            return Contains_Locked(name, tag);
    }

    public TrackedPlayer? FindByPuuid(string puuid)
    {
        lock (_lock)
            return FindByPuuid_Locked(puuid);
    }

    public TrackedPlayer? FindByNameTag(string name, string tag)
    {
        lock (_lock)
            return Find_Locked(name, tag);
    }

    public void UpdatePlayer(TrackedPlayer player)
    {
        lock (_lock)
            Save();
    }

    private bool Contains_Locked(string name, string tag) =>
        Find_Locked(name, tag) is not null;

    private TrackedPlayer? FindByPuuid_Locked(string puuid) =>
        _players.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.Puuid) &&
            string.Equals(p.Puuid, puuid, StringComparison.OrdinalIgnoreCase));

    private TrackedPlayer? Find_Locked(string name, string tag) =>
        _players.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Tag, tag, StringComparison.OrdinalIgnoreCase));

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No tracked_players.json found, starting with empty list");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _players = JsonSerializer.Deserialize<List<TrackedPlayer>>(json, JsonOptions) ?? [];
            _logger.LogInformation("Loaded {Count} tracked player(s) from store", _players.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tracked_players.json, starting fresh");
            _players = [];
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_players, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist tracked_players.json");
        }
    }
}
