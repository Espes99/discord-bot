using ValorantBot.Models;

namespace ValorantBot.Services;

public static class ProfileTraitDeriver
{
    private const int MaxTraits = 6;
    private const int MinMatchesForStatTraits = 10;

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

    public static List<string> DeriveTraits(List<MatchHistoryEntry> history, PlayerHistorySummary? summary)
    {
        var traits = new List<string>();

        if (history.Count == 0)
            return traits;

        DeriveAgentTraits(history, summary, traits);
        DerivePerformanceTraits(history, summary, traits);
        DeriveStreakTraits(summary, traits);
        DeriveMapTraits(summary, traits);
        DeriveRankTraits(history, traits);

        return traits.Take(MaxTraits).ToList();
    }

    private static void DeriveAgentTraits(List<MatchHistoryEntry> history, PlayerHistorySummary? summary, List<string> traits)
    {
        if (summary?.AgentStats is not { Count: > 0 })
            return;

        var topAgent = summary.AgentStats[0];
        var totalGames = summary.TotalMatches;

        // One-trick detection
        if (totalGames > 0 && (double)topAgent.Games / totalGames >= 0.6)
        {
            traits.Add($"one-trick {topAgent.Agent}");
            return;
        }

        // Role lock detection: top 2 agents same role
        if (summary.AgentStats.Count >= 2)
        {
            var top2 = summary.AgentStats.Take(2).ToList();
            if (AgentRoles.TryGetValue(top2[0].Agent, out var role1) &&
                AgentRoles.TryGetValue(top2[1].Agent, out var role2) &&
                role1 == role2)
            {
                traits.Add($"{role1.ToLowerInvariant()} instalock");
            }
        }
    }

    private static void DerivePerformanceTraits(List<MatchHistoryEntry> history, PlayerHistorySummary? summary, List<string> traits)
    {
        if (summary is null || history.Count < MinMatchesForStatTraits)
            return;

        // Bottom-frag tendency
        if (summary.AverageAcs < 150)
            traits.Add("career bottom-fragger");
        
    }

    private static void DeriveStreakTraits(PlayerHistorySummary? summary, List<string> traits)
    {
        if (summary is null)
            return;

        if (summary.CurrentLossStreak > 4)
            traits.Add("currently tilted off the face of the earth");
        else if (summary.CurrentWinStreak > 4)
            traits.Add("on a hot streak (probably getting carried)");
    }

    private static void DeriveMapTraits(PlayerHistorySummary? summary, List<string> traits)
    {
        if (summary?.MapStats is null)
            return;

        foreach (var map in summary.MapStats)
        {
            if (map.Wins == 0 && map.Losses >= 3)
            {
                traits.Add($"cursed on {map.Map}");
                break; // Only one map curse
            }
        }
    }

    private static void DeriveRankTraits(List<MatchHistoryEntry> history, List<string> traits)
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
            traits.Add($"hardstuck {tiers[0]}");
    }
}
