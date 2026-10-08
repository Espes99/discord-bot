using ValorantBot.Models;

using S = ValorantBot.Models.BehaviorSignals;

namespace ValorantBot.Services;

public static class ProfileTraitDeriver
{
    private const int MaxTraits = 6;
    private const int MinMatchesForStatTraits = 10;
    private const int SignalWindow = 10;
    private const int MinSignalMatches = 5;
    // Traits already held stay until they clearly fade, so the list does not flicker between matches
    private const double HoldRatioEase = 0.85;
    private const double HoldConsistencyEase = 0.75;

    private static readonly Dictionary<string, string> AgentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Duelists
        ["Jett"] = "Duelist",
        ["Phoenix"] = "Duelist",
        ["Reyna"] = "Duelist",
        ["Raze"] = "Duelist",
        ["Yoru"] = "Duelist",
        ["Neon"] = "Duelist",
        ["Iso"] = "Duelist",
        ["Waylay"] = "Duelist",

        // Initiators
        ["Sova"] = "Initiator",
        ["Breach"] = "Initiator",
        ["Skye"] = "Initiator",
        ["KAY/O"] = "Initiator",
        ["Fade"] = "Initiator",
        ["Gekko"] = "Initiator",
        ["Tejo"] = "Initiator",

        // Controllers
        ["Brimstone"] = "Controller",
        ["Omen"] = "Controller",
        ["Viper"] = "Controller",
        ["Astra"] = "Controller",
        ["Harbor"] = "Controller",
        ["Clove"] = "Controller",
        ["Miks"] = "Controller",

        // Sentinels
        ["Sage"] = "Sentinel",
        ["Cypher"] = "Sentinel",
        ["Killjoy"] = "Sentinel",
        ["Chamber"] = "Sentinel",
        ["Deadlock"] = "Sentinel",
        ["Vyse"] = "Sentinel",
    };

    public static string? RoleOf(string agent) =>
        AgentRoles.TryGetValue(agent, out var role) ? role : null;

    public static List<AutoTrait> DeriveTraits(List<MatchHistoryEntry> history, PlayerHistorySummary? summary, IReadOnlyCollection<string>? currentLabels = null)
    {
        if (history.Count == 0)
            return [];

        var held = (currentLabels ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<Candidate>();
        DeriveSignalTraits(history, held, candidates);
        DeriveAgentTraits(summary, candidates);
        DerivePerformanceTraits(history, summary, candidates);
        DeriveStreakTraits(summary, candidates);
        DeriveMapTraits(summary, candidates);
        DeriveRankTraits(history, candidates);

        return candidates
            .OrderByDescending(c => c.Strength)
            .DistinctBy(c => c.Category)
            .Take(MaxTraits)
            .Select(c => c.Trait)
            .ToList();
    }

    private sealed record Candidate(AutoTrait Trait, string Category, double Strength);

    /// <summary>A rate: numerator over denominator, unjudgeable below MinDen.</summary>
    private sealed record Metric(Func<S, int> Num, Func<S, int> Den, int MinDen)
    {
        public double? Rate(S s) => Den(s) < MinDen ? null : (double)Num(s) / Den(s);
    }

    /// <summary>Higher: player rate at least Ratio times the lobby's. Lower: at most Ratio times.</summary>
    private sealed record Condition(Metric Metric, double Ratio, bool Higher)
    {
        public bool Holds(S player, S lobby, bool eased)
        {
            var (p, l) = (Metric.Rate(player), Metric.Rate(lobby));
            if (p is null || l is null) return false;
            var ratio = eased ? (Higher ? Ratio * HoldRatioEase : Ratio / HoldRatioEase) : Ratio;
            return Higher ? p >= l * ratio && p > 0 : l > 0 && p <= l * ratio;
        }

        public double Strength(S player, S lobby)
        {
            const double eps = 0.01;
            var (p, l) = (Metric.Rate(player) ?? 0, Metric.Rate(lobby) ?? 0);
            return Higher ? (p + eps) / (l + eps) : (l + eps) / (p + eps);
        }
    }

    /// <summary>
    /// Primary decides per-match consistency; Also and Guard are checked on the summed window,
    /// since secondary rates are too sparse to judge match by match.
    /// </summary>
    private sealed record SignalRule(
        string Label,
        string Category,
        Condition Primary,
        Func<S, S, int, string> Evidence,
        Condition[]? Also = null,
        Func<S, bool>? Guard = null,
        double MinConsistency = 0.6,
        Func<List<MatchHistoryEntry>, double>? Boost = null);

    private static readonly Metric LastAliveRate = new(s => s.LastAlive, s => s.Rounds, 10);
    private static readonly Metric TradeRate = new(s => s.TradesMade, s => s.NearbyTeammateDeaths, 5);
    private static readonly Metric IsolatedRate = new(s => s.IsolatedDeaths, s => s.DeathsWithTeammateAlive, 3);
    private static readonly Metric ClutchRate = new(s => s.Clutches, s => s.Rounds, 10);
    private static readonly Metric FirstDeathRate = new(s => s.FirstDeaths, s => s.Rounds, 10);
    private static readonly Metric TradedRate = new(s => s.DeathsTraded, s => s.Deaths, 5);
    private static readonly Metric FirstBloodRate = new(s => s.FirstBloods, s => s.Rounds, 10);
    private static readonly Metric ExitRate = new(s => s.SurvivedLostRounds, s => s.LostRounds, 4);
    private static readonly Metric ZeroDamageRate = new(s => s.ZeroDamageRounds, s => s.Rounds, 10);
    private static readonly Metric CastRate = new(s => s.AbilityCasts, s => s.Rounds, 10);
    private static readonly Metric PlantRate = new(s => s.Plants, s => s.Rounds, 10);
    private static readonly Metric EcoRifleRate = new(s => s.EcoRifleBuys, s => s.Rounds, 10);

    private static readonly SignalRule[] SignalRules =
    [
        new("baiter", "Positioning",
            new(LastAliveRate, 1.6, Higher: true),
            (p, l, n) => $"last one alive in {Pct(p.LastAlive, p.Rounds)} of rounds over {n} matches (lobby {Pct(l.LastAlive, l.Rounds)}), lost {p.LastAliveLost} of {p.LastAlive} of those; traded only {p.TradesMade} of {p.NearbyTeammateDeaths} teammates who died within ~15 m of them (lobby {Pct(l.TradesMade, l.NearbyTeammateDeaths)})",
            Also: [new(TradeRate, 0.6, Higher: false)],
            Guard: p => p.LastAliveLost > p.Clutches * 2,
            Boost: RiotTradesDown),
        new("lurker who dies alone", "Positioning",
            new(IsolatedRate, 1.5, Higher: true),
            (p, l, n) => $"no teammate within ~25 m in {Pct(p.IsolatedDeaths, p.DeathsWithTeammateAlive)} of their deaths over {n} matches (lobby {Pct(l.IsolatedDeaths, l.DeathsWithTeammateAlive)})"),
        new("clutch merchant", "Clutch",
            new(ClutchRate, 2.0, Higher: true),
            (p, l, n) => $"{p.Clutches} clutches (1v2 or worse) over {n} matches, {Ratio(ClutchRate, p, l)}x the lobby rate",
            Guard: p => p.Clutches >= 3,
            MinConsistency: 0.4),
        new("first to die and never traded", "Entry",
            new(FirstDeathRate, 1.5, Higher: true),
            (p, l, n) => $"died first in {Pct(p.FirstDeaths, p.Rounds)} of rounds over {n} matches (lobby {Pct(l.FirstDeaths, l.Rounds)}); team traded only {Pct(p.DeathsTraded, p.Deaths)} of their deaths (lobby {Pct(l.DeathsTraded, l.Deaths)})",
            Also: [new(TradedRate, 0.7, Higher: false)]),
        new("entry fragger who actually gets the first kill", "Entry",
            new(FirstBloodRate, 1.6, Higher: true),
            (p, l, n) => $"first blood in {Pct(p.FirstBloods, p.Rounds)} of rounds over {n} matches (lobby {Pct(l.FirstBloods, l.Rounds)})"),
        new("exit-frager who saves every lost round", "Survival",
            new(ExitRate, 1.8, Higher: true),
            (p, l, n) => $"survived {Pct(p.SurvivedLostRounds, p.LostRounds)} of lost rounds over {n} matches (lobby {Pct(l.SurvivedLostRounds, l.LostRounds)})"),
        new("zero-damage tourist", "Impact",
            new(ZeroDamageRate, 1.5, Higher: true),
            (p, l, n) => $"zero damage in {Pct(p.ZeroDamageRounds, p.Rounds)} of rounds over {n} matches (lobby {Pct(l.ZeroDamageRounds, l.Rounds)})"),
        // ponytail: lobby baseline mixes roles, so a duelist main reads as a hoarder more easily; compare per role if that misfires
        new("utility hoarder", "Utility",
            new(CastRate, 0.6, Higher: false),
            (p, l, n) => $"{(double)p.AbilityCasts / p.Rounds:F1} ability casts per round over {n} matches (lobby {(double)l.AbilityCasts / l.Rounds:F1})"),
        new("designated spike carrier", "Objective",
            new(PlantRate, 2.0, Higher: true),
            (p, l, n) => $"planted {p.Plants} spikes over {n} matches, {Ratio(PlantRate, p, l)}x the lobby rate",
            Guard: p => p.Plants >= 4,
            MinConsistency: 0.4),
        new("buys a rifle on the team's eco", "Economy",
            new(EcoRifleRate, 2.0, Higher: true),
            (p, l, n) => $"bought a rifle on {p.EcoRifleBuys} team eco rounds over {n} matches",
            Guard: p => p.EcoRifleBuys >= 3,
            MinConsistency: 0.4),
    ];

    private static void DeriveSignalTraits(List<MatchHistoryEntry> history, HashSet<string> held, List<Candidate> candidates)
    {
        var window = history
            .Where(h => h.Signals is not null && h.LobbySignals is not null)
            .OrderByDescending(h => h.PlayedAt)
            .Take(SignalWindow)
            .ToList();
        if (window.Count < MinSignalMatches)
            return;

        var player = S.Sum(window.Select(h => h.Signals!));
        var lobby = S.Sum(window.Select(h => h.LobbySignals!));

        foreach (var rule in SignalRules)
        {
            var eased = held.Contains(rule.Label);
            var judged = window.Where(h => rule.Primary.Metric.Rate(h.Signals!) is not null && rule.Primary.Metric.Rate(h.LobbySignals!) is not null).ToList();
            if (judged.Count < MinSignalMatches)
                continue;

            var consistency = (double)judged.Count(h => rule.Primary.Holds(h.Signals!, h.LobbySignals!, eased)) / judged.Count;
            var minConsistency = eased ? rule.MinConsistency * HoldConsistencyEase : rule.MinConsistency;
            if (consistency < minConsistency
                || !rule.Primary.Holds(player, lobby, eased)
                || rule.Also?.Any(c => !c.Holds(player, lobby, eased)) == true
                || rule.Guard?.Invoke(player) == false)
                continue;

            var strength = rule.Primary.Strength(player, lobby) * consistency * (rule.Boost?.Invoke(window) ?? 1);
            candidates.Add(new Candidate(new AutoTrait(rule.Label, rule.Evidence(player, lobby, window.Count)), rule.Category, strength));
        }
    }

    // Riot's own trade rating, when present, backs up our position-based read
    private static double RiotTradesDown(List<MatchHistoryEntry> window)
    {
        var rated = window.Where(h => h.RiotTradesTrend is not null).ToList();
        if (rated.Count < 3)
            return 1;
        var down = rated.Count(h => h.RiotTradesTrend!.Contains("down", StringComparison.OrdinalIgnoreCase));
        return down * 2 > rated.Count ? 1.25 : 1;
    }

    private static string Pct(int num, int den) => den == 0 ? "n/a" : $"{100.0 * num / den:F0}%";

    private static string Ratio(Metric metric, S player, S lobby) =>
        metric.Rate(lobby) is > 0 and var l ? $"{metric.Rate(player) / l:F1}" : "many";

    private static void DeriveAgentTraits(PlayerHistorySummary? summary, List<Candidate> candidates)
    {
        if (summary?.AgentStats is not { Count: > 0 })
            return;

        var topAgent = summary.AgentStats[0];
        var totalGames = summary.TotalMatches;

        if (totalGames > 0 && (double)topAgent.Games / totalGames >= 0.6)
        {
            candidates.Add(new Candidate(new AutoTrait($"one-trick {topAgent.Agent}", $"{topAgent.Agent} in {topAgent.Games} of the last {totalGames} matches"), "Agent", 1.5));
            return;
        }

        // Role lock: top 2 agents share a role
        if (summary.AgentStats.Count >= 2)
        {
            var top2 = summary.AgentStats.Take(2).ToList();
            if (AgentRoles.TryGetValue(top2[0].Agent, out var role1) &&
                AgentRoles.TryGetValue(top2[1].Agent, out var role2) &&
                role1 == role2)
            {
                candidates.Add(new Candidate(new AutoTrait($"{role1.ToLowerInvariant()} instalock", $"most played: {top2[0].Agent} ({top2[0].Games}) and {top2[1].Agent} ({top2[1].Games})"), "Agent", 1.3));
            }
        }
    }

    private static void DerivePerformanceTraits(List<MatchHistoryEntry> history, PlayerHistorySummary? summary, List<Candidate> candidates)
    {
        if (summary is null || history.Count < MinMatchesForStatTraits)
            return;

        if (summary.AverageAcs < 150)
            candidates.Add(new Candidate(new AutoTrait("career bottom-fragger", $"average ACS {summary.AverageAcs:F0} over {summary.TotalMatches} matches"), "Results", 1.5));
    }

    private static void DeriveStreakTraits(PlayerHistorySummary? summary, List<Candidate> candidates)
    {
        if (summary is null)
            return;

        if (summary.CurrentLossStreak > 4)
            candidates.Add(new Candidate(new AutoTrait("currently tilted off the face of the earth", $"{summary.CurrentLossStreak} losses in a row"), "Streak", 2.0));
        else if (summary.CurrentWinStreak > 4)
            candidates.Add(new Candidate(new AutoTrait("on a hot streak (probably getting carried)", $"{summary.CurrentWinStreak} wins in a row"), "Streak", 2.0));
    }

    private static void DeriveMapTraits(PlayerHistorySummary? summary, List<Candidate> candidates)
    {
        var cursed = summary?.MapStats.FirstOrDefault(m => m.Wins == 0 && m.Losses >= 3);
        if (cursed is not null)
            candidates.Add(new Candidate(new AutoTrait($"cursed on {cursed.Map}", $"0 wins and {cursed.Losses} losses on {cursed.Map}"), "Map", 1.5));
    }

    private static void DeriveRankTraits(List<MatchHistoryEntry> history, List<Candidate> candidates)
    {
        var rankedEntries = history
            .Where(h => !string.IsNullOrEmpty(h.Rank))
            .OrderByDescending(h => h.PlayedAt)
            .Take(10)
            .ToList();

        if (rankedEntries.Count < 10)
            return;

        var tiers = rankedEntries
            .Select(h => h.Rank!.Split(' ')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tiers.Count == 1)
            candidates.Add(new Candidate(new AutoTrait($"hardstuck {tiers[0]}", $"{tiers[0]} in each of the last 10 ranked matches"), "Rank", 1.3));
    }
}
