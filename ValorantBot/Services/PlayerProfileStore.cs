using System.Text.Json;
using System.Text.Json.Serialization;
using ValorantBot.Models;

namespace ValorantBot.Services;

public class PlayerProfileStore : IPlayerProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger<PlayerProfileStore> _logger;
    private readonly object _lock = new();
    private Dictionary<string, PlayerProfile> _profiles = new();

    public PlayerProfileStore(ILogger<PlayerProfileStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "player_profiles.json");
        Load();
    }

    public PlayerProfile? GetProfile(string playerKey)
    {
        lock (_lock)
        {
            return _profiles.GetValueOrDefault(playerKey.ToLowerInvariant());
        }
    }

    public void SetBio(string playerKey, string bio)
    {
        lock (_lock)
        {
            var profile = GetOrCreateProfile(playerKey);
            profile.Bio = bio;
            Save();
        }
    }

    public void AddManualTrait(string playerKey, string trait)
    {
        lock (_lock)
        {
            var profile = GetOrCreateProfile(playerKey);
            if (!profile.ManualTraits.Contains(trait, StringComparer.OrdinalIgnoreCase))
            {
                profile.ManualTraits.Add(trait);
                Save();
            }
        }
    }

    public void RemoveManualTrait(string playerKey, string trait)
    {
        lock (_lock)
        {
            var key = playerKey.ToLowerInvariant();
            if (!_profiles.TryGetValue(key, out var profile))
                return;

            var index = profile.ManualTraits.FindIndex(t => t.Equals(trait, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                profile.ManualTraits.RemoveAt(index);
                Save();
            }
        }
    }

    public void UpdateAutoTraits(string playerKey, List<string> traits)
    {
        lock (_lock)
        {
            var profile = GetOrCreateProfile(playerKey);
            profile.AutoTraits = traits;
            profile.LastAutoTraitUpdate = DateTime.UtcNow;
            Save();
        }
    }

    private PlayerProfile GetOrCreateProfile(string playerKey)
    {
        var key = playerKey.ToLowerInvariant();
        if (!_profiles.TryGetValue(key, out var profile))
        {
            profile = new PlayerProfile();
            _profiles[key] = profile;
        }
        return profile;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("No existing player profiles file found, starting fresh");
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _profiles = JsonSerializer.Deserialize<Dictionary<string, PlayerProfile>>(json, JsonOptions) ?? new();
            _logger.LogInformation("Loaded profiles for {Count} player(s)", _profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load player profiles, starting fresh");
            _profiles = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_profiles, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist player profiles");
        }
    }
}
