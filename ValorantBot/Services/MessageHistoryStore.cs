using System.Text.Json;

namespace ValorantBot.Services;

/// <summary>
/// Persists recent AI-generated messages (global + per-player) to avoid repetitive output.
/// </summary>
public class MessageHistoryStore : IMessageHistoryStore
{
    private const int MaxMessages = 15;
    private const int MaxPlayerMessages = 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly string _playerFilePath;
    private readonly ILogger<MessageHistoryStore> _logger;
    private readonly object _lock = new();
    private List<string> _messages = [];
    private Dictionary<string, List<string>> _playerMessages = new();

    public MessageHistoryStore(ILogger<MessageHistoryStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "message_history.json");
        _playerFilePath = Path.Combine(dataDir, "player_message_history.json");
        Load();
    }

    public void AddMessage(string message, string? playerKey = null)
    {
        lock (_lock)
        {
            _messages.Add(message);
            if (_messages.Count > MaxMessages)
                _messages = _messages[^MaxMessages..];

            if (playerKey is not null)
            {
                if (!_playerMessages.TryGetValue(playerKey, out var list))
                {
                    list = [];
                    _playerMessages[playerKey] = list;
                }
                list.Add(message);
                if (list.Count > MaxPlayerMessages)
                    _playerMessages[playerKey] = list[^MaxPlayerMessages..];
            }

            Save();
        }
    }

    public List<string> GetRecentMessages(int count = 8)
    {
        lock (_lock)
        {
            return _messages.TakeLast(count).ToList();
        }
    }

    public List<string> GetRecentPlayerMessages(string playerKey, int count = 5)
    {
        lock (_lock)
        {
            if (_playerMessages.TryGetValue(playerKey, out var list))
                return list.TakeLast(count).ToList();
            return [];
        }
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                _messages = JsonSerializer.Deserialize<List<string>>(json) ?? [];
                _logger.LogInformation("Loaded {Count} recent messages from disk", _messages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load message history, starting fresh");
                _messages = [];
            }
        }

        if (File.Exists(_playerFilePath))
        {
            try
            {
                var json = File.ReadAllText(_playerFilePath);
                _playerMessages = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
                _logger.LogInformation("Loaded player message history for {Count} players", _playerMessages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load player message history, starting fresh");
                _playerMessages = new();
            }
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_messages, JsonOptions));
            File.WriteAllText(_playerFilePath, JsonSerializer.Serialize(_playerMessages, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist message history");
        }
    }
}
