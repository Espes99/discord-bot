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
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "last_matches.json");
        logger.LogInformation("Match tracker using path: {Path} (BaseDirectory: {BaseDir})", _filePath, AppContext.BaseDirectory);
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
        _logger.LogInformation("Match tracker file path: {Path}", _filePath);

        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing match tracker file found, starting fresh");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _lastMatchIds = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            _logger.LogInformation("Loaded {Count} tracked match IDs from disk", _lastMatchIds.Count);
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
