using System.Text.Json;

namespace ValorantBot.Services;

public class MatchTracker : IMatchTracker
{
    // A set rather than a single id: a player can get two results in one poll cycle
    // (an older stack match expanded from details plus a newer solo match), and the
    // send order must not decide which one is re-posted next cycle.
    private const int MaxSeenPerPlayer = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<MatchTracker> _logger;
    private readonly object _lock = new();
    private Dictionary<string, List<string>> _seenMatchIds = new();

    public MatchTracker(ILogger<MatchTracker> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "last_matches.json");
        logger.LogInformation("Match tracker using path: {Path} (BaseDirectory: {BaseDir})", _filePath, AppContext.BaseDirectory);
        Load();
    }

    public bool IsNewMatch(string playerKey, string matchId)
    {
        lock (_lock)
        {
            return !_seenMatchIds.TryGetValue(playerKey, out var seen) || !seen.Contains(matchId);
        }
    }

    public void SetLastMatch(string playerKey, string matchId)
    {
        lock (_lock)
        {
            if (!_seenMatchIds.TryGetValue(playerKey, out var seen))
            {
                seen = [];
                _seenMatchIds[playerKey] = seen;
            }

            if (seen.Contains(matchId))
                return;

            seen.Add(matchId);
            if (seen.Count > MaxSeenPerPlayer)
                seen.RemoveRange(0, seen.Count - MaxSeenPerPlayer);

            Save();
        }
    }

    public string? GetLastMatchId(string playerKey)
    {
        lock (_lock)
        {
            return _seenMatchIds.TryGetValue(playerKey, out var seen) && seen.Count > 0
                ? seen[^1]
                : null;
        }
    }

    public static string PlayerKey(string name, string tag) => $"{name}#{tag}";

    /// <summary>
    /// Migrates data stored under an old key to a new key.
    /// Returns true if a migration was performed.
    /// </summary>
    public bool MigrateKey(string oldKey, string newKey)
    {
        lock (_lock)
        {
            if (!_seenMatchIds.TryGetValue(oldKey, out var seen))
                return false;

            if (oldKey == newKey)
                return false;

            if (_seenMatchIds.TryGetValue(newKey, out var existing))
            {
                var merged = existing.Concat(seen).Distinct().ToList();
                if (merged.Count > MaxSeenPerPlayer)
                    merged.RemoveRange(0, merged.Count - MaxSeenPerPlayer);
                _seenMatchIds[newKey] = merged;
            }
            else
            {
                _seenMatchIds[newKey] = seen;
            }

            _seenMatchIds.Remove(oldKey);
            Save();
            return true;
        }
    }

    private void Load()
    {
        _logger.LogInformation("Match tracker file path: {Path}", _filePath);

        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing match tracker file found, starting fresh");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _seenMatchIds = Deserialize(json);
            _logger.LogInformation("Loaded seen match IDs for {Count} player(s) from disk", _seenMatchIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load match tracker state, starting fresh");
            _seenMatchIds = new();
        }
    }

    private Dictionary<string, List<string>> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
        }
        catch (JsonException)
        {
            // Pre-seen-set files stored a single id per player.
            var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            _logger.LogInformation("Converting legacy match tracker format ({Count} player(s))", legacy.Count);
            return legacy.ToDictionary(kv => kv.Key, kv => new List<string> { kv.Value });
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_seenMatchIds, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist match tracker state");
        }
    }
}
