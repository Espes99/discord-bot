using System.Text;
using ValorantBot.Models;

namespace ValorantBot.Services;

public static class HistorySummarizer
{
    public static PlayerHistorySummary? Summarize(List<MatchHistoryEntry> history)
    {
        if (history.Count == 0)
            return null;

        var ordered = history.OrderByDescending(h => h.PlayedAt).ToList();

        return new PlayerHistorySummary
        {
            TotalMatches = ordered.Count,
            WinRate = (double)ordered.Count(h => h.Won) / ordered.Count * 100,
            CurrentWinStreak = CountStreak(ordered, won: true),
            CurrentLossStreak = CountStreak(ordered, won: false),
            AverageAcs = ordered.Average(h => h.Acs),
            AverageKda = ordered.Average(h => h.Kda),
            AverageHsPercent = ordered.Average(h => h.HeadshotPercent),
            AcsTrend = ComputeTrend(ordered, h => h.Acs),
            HsPercentTrend = ComputeTrend(ordered, h => h.HeadshotPercent),
            MapStats = ComputeMapStats(ordered),
            AgentStats = ordered
                .GroupBy(h => h.Agent)
                .Select(g => new AgentStat { Agent = g.Key, Games = g.Count() })
                .OrderByDescending(a => a.Games)
                .Take(3)
                .ToList(),
            RatingDistribution = ordered
                .GroupBy(h => h.Rating)
                .ToDictionary(g => g.Key, g => g.Count()),
            MatchesSinceLastGoodGame = ComputeMatchesSinceLastGoodGame(ordered)
        };
    }

    public static string FormatForPrompt(PlayerHistorySummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Recent History (last {summary.TotalMatches} matches):");

        var wins = (int)Math.Round(summary.WinRate / 100 * summary.TotalMatches);
        var losses = summary.TotalMatches - wins;
        sb.AppendLine($"- Win rate: {summary.WinRate:F0}% ({wins}W {losses}L)");

        if (summary.CurrentWinStreak > 1)
            sb.AppendLine($"- Current streak: {summary.CurrentWinStreak} wins in a row");
        else if (summary.CurrentLossStreak > 1)
            sb.AppendLine($"- Current streak: {summary.CurrentLossStreak} losses in a row");

        sb.AppendLine($"- Average ACS: {summary.AverageAcs:F0}, Average KDA: {summary.AverageKda:F2}, Average HS%: {summary.AverageHsPercent:F1}%");
        sb.AppendLine($"- ACS trend: {summary.AcsTrend}, HS% trend: {summary.HsPercentTrend}");

        if (summary.MapStats.Count > 0)
        {
            var mapParts = summary.MapStats.Select(m =>
                $"{m.Map} {m.Wins}W/{m.Losses}L (avg ACS {m.AverageAcs:F0})");
            sb.AppendLine($"- Map performance: {string.Join(", ", mapParts)}");
        }

        if (summary.AgentStats.Count > 0)
        {
            var agentParts = summary.AgentStats.Select(a => $"{a.Agent} ({a.Games})");
            sb.AppendLine($"- Most played agents: {string.Join(", ", agentParts)}");
        }

        var ratingParts = Enum.GetValues<PerformanceRating>()
            .Select(r => $"{summary.RatingDistribution.GetValueOrDefault(r, 0)} {r}");
        sb.AppendLine($"- Rating history: {string.Join(", ", ratingParts)}");

        if (summary.MatchesSinceLastGoodGame.HasValue)
            sb.AppendLine($"- Last good game: {summary.MatchesSinceLastGoodGame.Value} matches ago");

        return sb.ToString().TrimEnd();
    }

    private static int CountStreak(List<MatchHistoryEntry> ordered, bool won)
    {
        var count = 0;
        foreach (var entry in ordered)
        {
            if (entry.Won == won) count++;
            else break;
        }
        return count;
    }

    private static TrendDirection ComputeTrend(List<MatchHistoryEntry> ordered, Func<MatchHistoryEntry, double> selector)
    {
        if (ordered.Count < 4)
            return TrendDirection.Stable;

        var recentCount = Math.Min(ordered.Count / 2, 5);
        var recent = ordered.Take(recentCount).Average(selector);
        var older = ordered.Skip(recentCount).Take(recentCount).Average(selector);

        var change = older == 0 ? 0 : (recent - older) / older;
        return change switch
        {
            > 0.10 => TrendDirection.Improving,
            < -0.10 => TrendDirection.Declining,
            _ => TrendDirection.Stable
        };
    }

    private static List<MapStat> ComputeMapStats(List<MatchHistoryEntry> ordered)
    {
        return ordered
            .GroupBy(h => h.Map)
            .Where(g => g.Count() >= 2)
            .Select(g => new MapStat
            {
                Map = g.Key,
                Wins = g.Count(h => h.Won),
                Losses = g.Count(h => !h.Won),
                AverageAcs = g.Average(h => h.Acs)
            })
            .OrderByDescending(m => m.Wins + m.Losses)
            .ToList();
    }

    private static int? ComputeMatchesSinceLastGoodGame(List<MatchHistoryEntry> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Rating is PerformanceRating.Good or PerformanceRating.Excellent)
                return i == 0 ? null : i;
        }

        return ordered.Count;
    }
}
