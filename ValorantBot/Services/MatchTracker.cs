using System.Text.Json;

namespace ValorantBot.Services;

public class MatchTracker : IMatchTracker
{
    private readonly string _filePath;
    private readonly ILogger<MatchTracker> _logger;
    private Dictionary<string, string> _lastMatchIds = new();

    public MatchTracker(ILogger<MatchTracker> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(AppContext.BaseDirectory, "last_matches.json");
        Load();
    }

    public bool IsNewMatch(string playerKey, string matchId)
    {
        if (_lastMatchIds.TryGetValue(playerKey, out var lastId) && lastId == matchId)
            return false;

        return true;
    }

    public void SetLastMatch(string playerKey, string matchId)
    {
        _lastMatchIds[playerKey] = matchId;
        Save();
    }

    public static string PlayerKey(string name, string tag) => $"{name}#{tag}";

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            _lastMatchIds = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            _logger.LogDebug("Loaded {Count} tracked match IDs", _lastMatchIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load match tracker state, starting fresh");
            _lastMatchIds = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_lastMatchIds, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist match tracker state");
        }
    }
}
