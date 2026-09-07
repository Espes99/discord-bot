using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Turns a match result into a list of things worth roasting, with weights. Only candidates that are
/// actually true for this match are produced, so the planner never picks a non-story.
/// </summary>
public static class FocusCandidates
{
    public static List<RoastFocus> Build(PerformanceResult result, PlayerHistorySummary? history, RankChangeInfo? rankChange)
    {
        var s = result.MatchPlayer.Stats;
        var agent = result.MatchPlayer.Agent.Name;
        var map = result.MapName;
        var outcome = result.Won ? "WIN" : "LOSS";
        var list = new List<RoastFocus>();

        if (rankChange is not null)
        {
            var dir = rankChange.IsPromotion ? "PROMOTED" : "DEMOTED";
            var size = rankChange.IsMajor ? "a MAJOR tier change" : "a minor step";
            list.Add(new RoastFocus("RankChange", $"{dir} from {rankChange.OldRank} to {rankChange.NewRank} ({size}) after this match", 100));
        }

        list.Add(new RoastFocus("ScoreLine", $"{outcome} {result.Score} on {map}", ScoreWeight(result)));

        if (s.Deaths >= 15 || s.Deaths >= s.Kills + 4)
            list.Add(new RoastFocus("Deaths", $"{s.Deaths} deaths on {agent} ({s.Kills}/{s.Deaths}/{s.Assists})", 6 + Math.Max(0, s.Deaths - s.Kills)));

        var role = ProfileTraitDeriver.RoleOf(agent);
        if (s.Assists <= 2 && role is "Initiator" or "Controller" or "Sentinel")
            list.Add(new RoastFocus("Assists", $"{s.Assists} assist(s) as {agent}, a {role!.ToLowerInvariant()} whose whole job is helping the team", 7));

        var wc = result.WeaponContext;
        if (wc is { HasData: true, LowHsExpected: true })
            list.Add(new RoastFocus("WeaponChoice", $"{100 - wc.PrecisionKillPercent:F0}% of kills with non-rifle weapons, mostly {wc.MostUsedWeapon}", 5));
        else if (wc is { HasData: true } && s.HeadshotPercentage < 15)
            list.Add(new RoastFocus("HsPercent", $"{s.HeadshotPercentage:F1}% headshots while using {wc.MostUsedWeapon ?? "rifles"}", 6));
        else if (s.HeadshotPercentage > 30 && result.Rating <= PerformanceRating.Average)
            list.Add(new RoastFocus("HsPercent", $"{s.HeadshotPercentage:F1}% headshots and still only {s.Kills} kills, aim was there, brain was not", 5));

        if (result.Rating >= PerformanceRating.Good)
            list.Add(new RoastFocus("Carry", $"{s.Kills}/{s.Deaths}/{s.Assists}, {result.Acs:F0} ACS, a {result.Rating} game in a {outcome}", result.Won ? 6 : 9));
        else if (result.Rating == PerformanceRating.Average)
            list.Add(new RoastFocus("Mediocrity", $"{result.Acs:F0} ACS, an exactly Average game, nothing to praise and nothing to remember", 4));
        else
            list.Add(new RoastFocus("Acs", $"{result.Acs:F0} ACS in a {outcome}, rated {result.Rating}", 6));

        list.Add(new RoastFocus("AgentPick", $"picked {agent} on {map}", 2));

        if (history is not null)
        {
            if (history.CurrentLossStreak >= 2)
                list.Add(new RoastFocus("Streak", $"{history.CurrentLossStreak} losses in a row now", 5 + history.CurrentLossStreak));
            else if (history.CurrentWinStreak >= 3)
                list.Add(new RoastFocus("Streak", $"{history.CurrentWinStreak} wins in a row, suspicious", 5));

            if (history.AcsTrend == TrendDirection.Declining)
                list.Add(new RoastFocus("Trend", $"ACS trending down over the last {history.TotalMatches} matches (avg {history.AverageAcs:F0})", 5));
            else if (history.AcsTrend == TrendDirection.Improving && result.Rating <= PerformanceRating.Bad)
                list.Add(new RoastFocus("Trend", $"was on an upward trend, then produced this", 5));

            var mapStat = history.MapStats.FirstOrDefault(m => m.Map.Equals(map, StringComparison.OrdinalIgnoreCase));
            if (mapStat is { Wins: 0, Losses: >= 2 } && !result.Won)
                list.Add(new RoastFocus("MapCurse", $"{mapStat.Losses + 1} straight losses on {map}, never won there", 7));

            if (history.MatchesSinceLastGoodGame is >= 5)
                list.Add(new RoastFocus("Drought", $"{history.MatchesSinceLastGoodGame} matches since the last Good game", 6));

            var top = history.AgentStats.FirstOrDefault();
            if (top is not null && top.Agent.Equals(agent, StringComparison.OrdinalIgnoreCase)
                && history.TotalMatches >= 5 && (double)top.Games / history.TotalMatches >= 0.6
                && result.Rating <= PerformanceRating.Bad)
                list.Add(new RoastFocus("OneTrick", $"{agent} in {top.Games} of the last {history.TotalMatches} matches and still plays it like this", 6));
        }

        foreach (var h in result.Highlights.Items)
            list.Add(new RoastFocus($"Highlight:{h.Kind}", h.Text, h.Severity * 4));

        return list;
    }

    private static int ScoreWeight(PerformanceResult result)
    {
        var parts = result.Score.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b))
            return 3;
        var diff = Math.Abs(a - b);
        if (diff >= 8) return 6;
        if (a + b >= 24) return 6;
        return 3;
    }
}
