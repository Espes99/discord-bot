using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>Walks every round of a match once and counts behavior signals for all ten players.</summary>
public static class BehaviorSignalExtractor
{
    public const int TradeWindowMs = 3000;
    // Coordinates are game units (~100 per metre) on every map, so one threshold fits all maps
    public const double NearbyUnits = 1500;
    public const double IsolatedUnits = 2500;

    private static readonly HashSet<string> Rifles = new(StringComparer.OrdinalIgnoreCase) { "Vandal", "Phantom", "Guardian", "Bulldog" };
    private const int EcoTeamAverageLoadout = 2000;
    private const int RifleLoadout = 2900;

    public static Dictionary<string, BehaviorSignals> ComputeAll(MatchDetailData match)
    {
        var signals = match.Players.ToDictionary(
            p => p.Puuid,
            p => new BehaviorSignals { AbilityCasts = p.AbilityCasts?.Total ?? 0 },
            StringComparer.OrdinalIgnoreCase);
        var teamOf = match.Players.ToDictionary(p => p.Puuid, p => p.TeamId, StringComparer.OrdinalIgnoreCase);
        var killsByRound = (match.Kills ?? [])
            .GroupBy(k => k.Round)
            .ToDictionary(g => g.Key, g => g.OrderBy(k => k.TimeInRoundInMs).ToList());

        foreach (var round in match.Rounds ?? [])
        {
            var died = WalkKills(round, killsByRound.GetValueOrDefault(round.Id) ?? [], signals, teamOf);
            CountRoundStats(round, signals, teamOf, died);
        }

        return signals;
    }

    /// <summary>Player's own signals plus the summed signals of everyone else in the lobby.</summary>
    public static (BehaviorSignals Player, BehaviorSignals Lobby)? ForPlayer(MatchDetailData match, string puuid)
    {
        var all = ComputeAll(match);
        if (!all.TryGetValue(puuid, out var mine) || mine.Rounds == 0)
            return null;
        return (mine, BehaviorSignals.Sum(all.Where(kv => !Is(kv.Key, puuid)).Select(kv => kv.Value)));
    }

    private static HashSet<string> WalkKills(MatchRound round, List<MatchKill> kills, Dictionary<string, BehaviorSignals> signals, Dictionary<string, string> teamOf)
    {
        var alive = teamOf.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var died = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastAlive = new Dictionary<string, (string Puuid, int EnemiesLeft)>();
        var first = true;

        for (var i = 0; i < kills.Count; i++)
        {
            var kill = kills[i];
            var victim = kill.Victim?.Puuid;
            if (victim is null || !alive.Remove(victim) || !teamOf.TryGetValue(victim, out var team))
                continue;
            died.Add(victim);

            var victimSignals = signals[victim];
            victimSignals.Deaths++;
            var killer = kill.Killer?.Puuid;
            var enemyKill = killer is not null && teamOf.TryGetValue(killer, out var killerTeam) && killerTeam != team;
            if (first)
            {
                victimSignals.FirstDeaths++;
                if (enemyKill)
                    signals[killer!].FirstBloods++;
                first = false;
            }

            var trader = enemyKill ? Trader(kills, i, killer!, team, teamOf) : null;
            if (trader is not null)
                victimSignals.DeathsTraded++;

            var mates = alive.Where(p => teamOf[p] == team).ToList();
            CountProximity(kill, mates, trader, victimSignals, signals);

            if (mates.Count == 1 && !lastAlive.ContainsKey(team))
                lastAlive[team] = (mates[0], alive.Count(p => teamOf[p] != team));
        }

        foreach (var (team, (puuid, enemiesLeft)) in lastAlive)
        {
            var s = signals[puuid];
            s.LastAlive++;
            if (!Is(round.WinningTeam, team))
                s.LastAliveLost++;
            else if (enemiesLeft >= 2)
                s.Clutches++;
        }

        return died;
    }

    private static string? Trader(List<MatchKill> kills, int index, string killer, string team, Dictionary<string, string> teamOf)
    {
        var at = kills[index].TimeInRoundInMs;
        return kills.Skip(index + 1)
            .TakeWhile(k => k.TimeInRoundInMs - at <= TradeWindowMs)
            .FirstOrDefault(k => Is(k.Victim?.Puuid, killer)
                && k.Killer?.Puuid is { } p && teamOf.TryGetValue(p, out var t) && t == team)
            ?.Killer!.Puuid;
    }

    private static void CountProximity(MatchKill kill, List<string> mates, string? trader, BehaviorSignals victimSignals, Dictionary<string, BehaviorSignals> signals)
    {
        if (mates.Count == 0 || kill.Location is null || kill.PlayerLocations is not { Count: > 0 })
            return;

        var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pl in kill.PlayerLocations)
        {
            if (pl.Player?.Puuid is { } p && pl.Location is not null && mates.Contains(p, StringComparer.OrdinalIgnoreCase))
                distances[p] = pl.Location.DistanceTo(kill.Location);
        }
        if (distances.Count == 0)
            return;

        victimSignals.DeathsWithTeammateAlive++;
        if (distances.Values.Min() > IsolatedUnits)
            victimSignals.IsolatedDeaths++;

        foreach (var (mate, distance) in distances)
        {
            if (distance > NearbyUnits) continue;
            signals[mate].NearbyTeammateDeaths++;
            if (Is(mate, trader))
                signals[mate].TradesMade++;
        }
    }

    private static void CountRoundStats(MatchRound round, Dictionary<string, BehaviorSignals> signals, Dictionary<string, string> teamOf, HashSet<string> died)
    {
        foreach (var (puuid, s) in signals)
        {
            s.Rounds++;
            if (!Is(round.WinningTeam, teamOf[puuid]))
            {
                s.LostRounds++;
                if (!died.Contains(puuid))
                    s.SurvivedLostRounds++;
            }
        }

        if (round.Plant?.Player?.Puuid is { } planter && signals.TryGetValue(planter, out var plantSignals))
            plantSignals.Plants++;

        if (round.Stats is null) return;
        foreach (var mine in round.Stats)
        {
            if (mine.Player?.Puuid is not { } puuid || !signals.TryGetValue(puuid, out var s))
                continue;

            if (mine.DamageEvents is not null && mine.DamageDealt == 0)
                s.ZeroDamageRounds++;

            var teamLoadouts = round.Stats
                .Where(o => Is(o.Player?.Team, mine.Player.Team) && !Is(o.Player?.Puuid, puuid) && o.Economy is not null)
                .Select(o => o.Economy!.LoadoutValue)
                .ToList();
            if (teamLoadouts.Count >= 3 && teamLoadouts.Average() < EcoTeamAverageLoadout
                && mine.Economy is not null
                && (mine.Economy.LoadoutValue >= RifleLoadout || Rifles.Contains(mine.Economy.Weapon?.Name ?? "")))
                s.EcoRifleBuys++;
        }
    }

    private static bool Is(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
