using System.Net.Http.Json;

namespace ValorantBot.Services;

public static class GameContent
{
    // Body shots kill or one-shot with these, so a low HS% is expected
    private static readonly HashSet<string> NonPrecisionClasses = new(StringComparer.OrdinalIgnoreCase) { "Shotgun", "Sniper", "Heavy", "Melee" };

    // Fallback until the first fetch succeeds; API data is merged on top so aliases like "Tactical Knife" survive
    private static Dictionary<string, string> _agentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jett"] = "Duelist", ["Phoenix"] = "Duelist", ["Reyna"] = "Duelist", ["Raze"] = "Duelist",
        ["Yoru"] = "Duelist", ["Neon"] = "Duelist", ["Iso"] = "Duelist", ["Waylay"] = "Duelist",
        ["Sova"] = "Initiator", ["Breach"] = "Initiator", ["Skye"] = "Initiator", ["KAY/O"] = "Initiator",
        ["Fade"] = "Initiator", ["Gekko"] = "Initiator", ["Tejo"] = "Initiator",
        ["Brimstone"] = "Controller", ["Omen"] = "Controller", ["Viper"] = "Controller", ["Astra"] = "Controller",
        ["Harbor"] = "Controller", ["Clove"] = "Controller", ["Miks"] = "Controller",
        ["Sage"] = "Sentinel", ["Cypher"] = "Sentinel", ["Killjoy"] = "Sentinel", ["Chamber"] = "Sentinel",
        ["Deadlock"] = "Sentinel", ["Vyse"] = "Sentinel", ["Veto"] = "Sentinel",
    };

    private static Dictionary<string, string> _weaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Classic"] = "Sidearm", ["Shorty"] = "Sidearm", ["Frenzy"] = "Sidearm", ["Ghost"] = "Sidearm",
        ["Bandit"] = "Sidearm", ["Sheriff"] = "Sidearm",
        ["Stinger"] = "SMG", ["Spectre"] = "SMG",
        ["Bucky"] = "Shotgun", ["Judge"] = "Shotgun",
        ["Bulldog"] = "Rifle", ["Guardian"] = "Rifle", ["Phantom"] = "Rifle", ["Vandal"] = "Rifle", ["Warden"] = "Rifle",
        ["Marshal"] = "Sniper", ["Outlaw"] = "Sniper", ["Operator"] = "Sniper",
        ["Ares"] = "Heavy", ["Odin"] = "Heavy",
        ["Melee"] = "Melee", ["Knife"] = "Melee", ["Tactical Knife"] = "Melee",
    };

    public static string? RoleOf(string? agent) =>
        agent is not null && _agentRoles.TryGetValue(agent, out var role) ? role : null;

    public static string? WeaponClassOf(string? weapon) =>
        weapon is not null && _weaponClasses.TryGetValue(weapon, out var cls) ? cls : null;

    public static bool IsNonPrecision(string? weapon) => WeaponClassOf(weapon) is { } cls && NonPrecisionClasses.Contains(cls);

    public static bool IsRifle(string? weapon) => WeaponClassOf(weapon) == "Rifle";

    public static void Apply(IEnumerable<(string Agent, string Role)> agents, IEnumerable<(string Weapon, string Class)> weapons)
    {
        var roles = new Dictionary<string, string>(_agentRoles, StringComparer.OrdinalIgnoreCase);
        foreach (var (agent, role) in agents) roles[agent] = role;
        var classes = new Dictionary<string, string>(_weaponClasses, StringComparer.OrdinalIgnoreCase);
        foreach (var (weapon, cls) in weapons) classes[weapon] = cls;
        _agentRoles = roles;
        _weaponClasses = classes;
    }
}

public class GameContentRefresher(IHttpClientFactory httpFactory, ILogger<GameContentRefresher> logger) : BackgroundService
{
    private const string BaseUrl = "https://valorant-api.com/v1/";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private record Envelope<T>(List<T> Data);
    private record Named(string DisplayName);
    private record Agent(string DisplayName, Named? Role);
    private record Weapon(string DisplayName, string Category);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        do await RefreshAsync(ct);
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient();
            var agents = await http.GetFromJsonAsync<Envelope<Agent>>(BaseUrl + "agents?isPlayableCharacter=true", ct);
            var weapons = await http.GetFromJsonAsync<Envelope<Weapon>>(BaseUrl + "weapons", ct);
            if (agents is null || weapons is null) return;

            GameContent.Apply(
                agents.Data.Where(a => a.Role is not null).Select(a => (a.DisplayName, a.Role!.DisplayName)),
                weapons.Data.Select(w => (w.DisplayName, w.Category.Replace("EEquippableCategory::", ""))));
            logger.LogInformation("Loaded {Agents} agents and {Weapons} weapons from valorant-api.com", agents.Data.Count, weapons.Data.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "valorant-api.com refresh failed, keeping current agent and weapon data");
        }
    }
}
