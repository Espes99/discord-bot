using System.Text.Json;

namespace ValorantBot.Services;

/// <summary>
/// Persists recent AI-generated messages to avoid repetitive output.
/// </summary>
public class MessageHistoryStore : IMessageHistoryStore
{
    private const int MaxMessages = 15;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<MessageHistoryStore> _logger;
    private readonly object _lock = new();
    private List<string> _messages = [];

    public MessageHistoryStore(ILogger<MessageHistoryStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "message_history.json");
        Load();
    }

    public void AddMessage(string message)
    {
        lock (_lock)
        {
            _messages.Add(message);
            if (_messages.Count > MaxMessages)
                _messages = _messages[^MaxMessages..];
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

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

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

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_messages, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist message history");
        }
    }
}
