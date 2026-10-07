using System.Text.Json;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Remembers what each player and squad was roasted with (voice, form, focus, allusion, opener),
/// so the planner can rotate instead of feeding old messages back to the model.
/// </summary>
public class RoastPlanStore : IRoastPlanStore
{
    private const int MaxRecordsPerKey = 12;
    // Trait uses back the multi-day cooldown and the "angles already used" list, so they outlive plan records
    private const int MaxTraitUsesPerPlayer = 60;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<RoastPlanStore> _logger;
    private readonly object _lock = new();
    private StoreData _data = new();

    public RoastPlanStore(ILogger<RoastPlanStore> logger)
    {
        _logger = logger;
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR")
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "roast_plans.json");
        Load();
    }

    public static string SquadKey(IEnumerable<string> playerKeys) =>
        string.Join("|", playerKeys.Select(k => k.ToLowerInvariant()).OrderBy(k => k, StringComparer.Ordinal));

    public List<RoastPlanRecord> GetPlayerRecords(string playerKey)
    {
        lock (_lock)
        {
            return _data.Players.TryGetValue(playerKey.ToLowerInvariant(), out var list) ? [.. list] : [];
        }
    }

    public void AddPlayerRecord(string playerKey, RoastPlanRecord record)
    {
        lock (_lock)
        {
            Append(_data.Players, playerKey.ToLowerInvariant(), record);
            Save();
        }
    }

    public List<SquadPlanRecord> GetSquadRecords(string squadKey)
    {
        lock (_lock)
        {
            return _data.Squads.TryGetValue(squadKey, out var list) ? [.. list] : [];
        }
    }

    public void AddSquadRecord(string squadKey, SquadPlanRecord record)
    {
        lock (_lock)
        {
            Append(_data.Squads, squadKey, record);
            Save();
        }
    }

    public List<TraitUseRecord> GetTraitUses(string playerKey)
    {
        lock (_lock)
        {
            return _data.TraitUses.TryGetValue(playerKey.ToLowerInvariant(), out var list) ? [.. list] : [];
        }
    }

    public void AddTraitUse(string playerKey, TraitUseRecord use)
    {
        lock (_lock)
        {
            Append(_data.TraitUses, playerKey.ToLowerInvariant(), use, MaxTraitUsesPerPlayer);
            Save();
        }
    }

    private static void Append<T>(Dictionary<string, List<T>> map, string key, T record, int max = MaxRecordsPerKey)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(record);
        if (list.Count > max)
            map[key] = list[^max..];
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
            return;

        try
        {
            _data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_filePath), JsonOptions) ?? new StoreData();
            _logger.LogInformation("Loaded roast plans for {Players} player(s) and {Squads} squad(s)",
                _data.Players.Count, _data.Squads.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load roast plans, starting fresh");
            _data = new StoreData();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist roast plans");
        }
    }

    private class StoreData
    {
        public Dictionary<string, List<RoastPlanRecord>> Players { get; set; } = new();
        public Dictionary<string, List<SquadPlanRecord>> Squads { get; set; } = new();
        public Dictionary<string, List<TraitUseRecord>> TraitUses { get; set; } = new();
    }
}
