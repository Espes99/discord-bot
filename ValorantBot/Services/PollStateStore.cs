using System.Text.Json;

namespace ValorantBot.Services;

public class PollStateStore : IPollStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<PollStateStore> _logger;
    private readonly object _lock = new();
    private DateTimeOffset? _lastPollAt;

    public PollStateStore(ILogger<PollStateStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "poll_state.json");
        Load();
    }

    public DateTimeOffset? GetLastPollAt()
    {
        lock (_lock)
        {
            return _lastPollAt;
        }
    }

    public void SetLastPollAt(DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            _lastPollAt = timestamp;
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing poll state file found, will poll immediately on startup");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<PollState>(json);
            if (state?.LastPollAt is not null)
            {
                _lastPollAt = state.LastPollAt;
                _logger.LogInformation("Loaded last poll timestamp from disk: {LastPollAt}", _lastPollAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load poll state, will poll immediately on startup");
            _lastPollAt = null;
        }
    }

    private void Save()
    {
        try
        {
            var state = new PollState { LastPollAt = _lastPollAt };
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist poll state");
        }
    }

    private sealed class PollState
    {
        public DateTimeOffset? LastPollAt { get; set; }
    }
}
