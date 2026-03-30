using System.Text.Json;
using System.Text.Json.Serialization;
using ValorantBot.Models;

namespace ValorantBot.Services;

public class MatchHistoryStore : IMatchHistoryStore
{
    private const int MaxEntriesPerPlayer = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger<MatchHistoryStore> _logger;
    private readonly object _lock = new();
    private Dictionary<string, List<MatchHistoryEntry>> _history = new();

    public MatchHistoryStore(ILogger<MatchHistoryStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "match_history.json");
        Load();
    }

    public List<MatchHistoryEntry> GetHistory(string playerKey)
    {
        lock (_lock)
        {
            return _history.TryGetValue(playerKey, out var entries)
                ? entries.ToList()
                : [];
        }
    }

    public void AddMatch(string playerKey, MatchHistoryEntry entry)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(playerKey, out var entries))
            {
                entries = [];
                _history[playerKey] = entries;
            }

            // Avoid duplicates
            if (entries.Any(e => e.MatchId == entry.MatchId))
                return;

            entries.Add(entry);

            // Trim to most recent N entries
            if (entries.Count > MaxEntriesPerPlayer)
            {
                _history[playerKey] = entries
                    .OrderByDescending(e => e.PlayedAt)
                    .Take(MaxEntriesPerPlayer)
                    .ToList();
            }

            Save();
            _logger.LogDebug("Saved match history for {Player}, now {Count} entries",
                playerKey, _history[playerKey].Count);
        }
    }

    public string? GetLastRank(string playerKey)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(playerKey, out var entries) || entries.Count == 0)
                return null;

            return entries
                .OrderByDescending(e => e.PlayedAt)
                .Select(e => e.Rank)
                .FirstOrDefault(r => !string.IsNullOrEmpty(r));
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing match history file found, starting fresh");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _history = JsonSerializer.Deserialize<Dictionary<string, List<MatchHistoryEntry>>>(json, JsonOptions) ?? new();
            _logger.LogInformation("Loaded match history for {Count} player(s)", _history.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load match history, starting fresh");
            _history = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist match history");
        }
    }
}
