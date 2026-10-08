using ValorantBot.Models;

namespace ValorantBot.Services;

public record PlayerSignals(BehaviorSignals Player, BehaviorSignals Lobby, Dictionary<string, PairSignals> Teammates);

/// <summary>Walks every round of a match once and counts behavior signals for all ten players and every teammate pair.</summary>
public static class BehaviorSignalExtractor
{
    public const int TradeWindowMs = 3000;
    // Coordinates are game units (~100 per metre) on every map, so one threshold fits all maps
    public const double NearbyUnits = 1500;
    public const double IsolatedUnits = 2500;

    private const int EcoTeamAverageLoadout = 2000;
    private const int RifleLoadout = 2900;

    private sealed class Pairs(Dictionary<string, string> teamOf)
    {
        public readonly Dictionary<(string Player, string Mate), PairSignals> All = new(PairComparer.Instance);

        public PairSignals Of(string player, string mate)
        {
            if (!All.TryGetValue((player, mate), out var pair))
                All[(player, mate)] = pair = new PairSignals();
            return pair;
        }

        public IEnumerable<string> MatesOf(string player) =>
            teamOf.Where(kv => kv.Value == teamOf[player] && !Is(kv.Key, player)).Select(kv => kv.Key);
    }

    private sealed class PairComparer : IEqualityComparer<(string, string)>
    {
        public static readonly PairComparer Instance = new();
        public bool Equals((string, string) a, (string, string) b) => Is(a.Item1, b.Item1) && Is(a.Item2, b.Item2);
        public int GetHashCode((string, string) p) => HashCode.Combine(p.Item1.ToLowerInvariant(), p.Item2.ToLowerInvariant());
    }

    private sealed record Death(string Puuid, int TimeMs, MapLocation? Location);

    public static (Dictionary<string, BehaviorSignals> Signals, Dictionary<(string Player, string Mate), PairSignals> Pairs) ComputeAll(MatchDetailData match)
    {
        var signals = match.Players.ToDictionary(
            p => p.Puuid,
            p => new BehaviorSignals { AbilityCasts = p.AbilityCasts?.Total ?? 0 },
            StringComparer.OrdinalIgnoreCase);
        var teamOf = match.Players.ToDictionary(p => p.Puuid, p => p.TeamId, StringComparer.OrdinalIgnoreCase);
        var pairs = new Pairs(teamOf);
        var killsByRound = (match.Kills ?? [])
            .GroupBy(k => k.Round)
            .ToDictionary(g => g.Key, g => g.OrderBy(k => k.TimeInRoundInMs).ToList());

        foreach (var round in match.Rounds ?? [])
        {
            var deaths = WalkKills(round, killsByRound.GetValueOrDefault(round.Id) ?? [], signals, teamOf, pairs);
            CountRoundStats(round, signals, teamOf, deaths.Select(d => d.Puuid).ToHashSet(StringComparer.OrdinalIgnoreCase));
            CountDeathOrder(deaths, teamOf, pairs);
        }

        var rounds = (match.Rounds ?? []).Count;
        foreach (var mate in match.Players)
        {
            foreach (var player in pairs.MatesOf(mate.Puuid))
            {
                var pair = pairs.Of(player, mate.Puuid);
                pair.Rounds = rounds;
                pair.MateAcs = rounds == 0 ? 0 : (double)mate.Stats.Score / rounds;
                pair.MateTierId = mate.Tier?.Id ?? 0;
                pair.OwnKills = match.Players.FirstOrDefault(p => Is(p.Puuid, player))?.Stats.Kills ?? 0;
                pair.MateKills = mate.Stats.Kills;
            }
        }

        return (signals, pairs.All);
    }

    /// <summary>Player's own signals, the summed signals of everyone else in the lobby, and the player's view of each teammate.</summary>
    public static PlayerSignals? ForPlayer(MatchDetailData match, string puuid)
    {
        var (all, pairs) = ComputeAll(match);
        if (!all.TryGetValue(puuid, out var mine) || mine.Rounds == 0)
            return null;
        var lobby = BehaviorSignals.Sum(all.Where(kv => !Is(kv.Key, puuid)).Select(kv => kv.Value));
        var teammates = pairs
            .Where(kv => Is(kv.Key.Player, puuid))
            .ToDictionary(kv => kv.Key.Mate, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return new PlayerSignals(mine, lobby, teammates);
    }

    private static List<Death> WalkKills(MatchRound round, List<MatchKill> kills, Dictionary<string, BehaviorSignals> signals, Dictionary<string, string> teamOf, Pairs pairs)
    {
        var alive = teamOf.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deaths = new List<Death>();
        var lastAlive = new Dictionary<string, (string Puuid, int EnemiesLeft)>();
        var damaged = DamagePairs(round);
        var first = true;

        for (var i = 0; i < kills.Count; i++)
        {
            var kill = kills[i];
            var victim = kill.Victim?.Puuid;
            if (victim is null || !alive.Remove(victim) || !teamOf.TryGetValue(victim, out var team))
                continue;

            CountDiedTogether(victim, kill, deaths, teamOf, pairs);
            deaths.Add(new Death(victim, kill.TimeInRoundInMs, kill.Location));

            var victimSignals = signals[victim];
            victimSignals.Deaths++;
            var killer = kill.Killer?.Puuid;
            var killerKnown = killer is not null && teamOf.ContainsKey(killer);
            var enemyKill = killerKnown && teamOf[killer!] != team;
            if (killerKnown && !enemyKill && !Is(killer, victim))
                pairs.Of(killer!, victim).Teamkills++;
            if (enemyKill)
                CountKillCredit(killer!, victim, kill, damaged, pairs);

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
            CountProximity(kill, victim, mates, trader, victimSignals, signals, pairs);

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

        return deaths;
    }

    private static HashSet<(string Attacker, string Target)> DamagePairs(MatchRound round)
    {
        var set = new HashSet<(string, string)>(PairComparer.Instance);
        foreach (var stats in round.Stats ?? [])
        {
            if (stats.Player?.Puuid is not { } attacker) continue;
            foreach (var e in stats.DamageEvents ?? [])
            {
                if (e.Player?.Puuid is { } target && e.Damage > 0)
                    set.Add((attacker, target));
            }
        }
        return set;
    }

    private static void CountKillCredit(string killer, string victim, MatchKill kill, HashSet<(string, string)> damaged, Pairs pairs)
    {
        foreach (var mate in pairs.MatesOf(killer))
        {
            if (damaged.Contains((mate, victim)))
                pairs.Of(killer, mate).KillsOnMateDamaged++;
            if (kill.Assistants?.Any(a => Is(a.Puuid, mate)) == true)
                pairs.Of(mate, killer).AssistsOnMateKills++;
        }
    }

    private static void CountDiedTogether(string victim, MatchKill kill, List<Death> earlier, Dictionary<string, string> teamOf, Pairs pairs)
    {
        if (kill.Location is null) return;
        foreach (var d in earlier)
        {
            if (d.Location is null || teamOf[d.Puuid] != teamOf[victim]
                || kill.TimeInRoundInMs - d.TimeMs > TradeWindowMs
                || d.Location.DistanceTo(kill.Location) > NearbyUnits)
                continue;
            pairs.Of(victim, d.Puuid).DiedTogether++;
            pairs.Of(d.Puuid, victim).DiedTogether++;
        }
    }

    // Survivors count as dying after everyone who died
    private static void CountDeathOrder(List<Death> deaths, Dictionary<string, string> teamOf, Pairs pairs)
    {
        var order = deaths.Select((d, i) => (d.Puuid, i)).ToDictionary(x => x.Puuid, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var d in deaths)
        {
            foreach (var mate in pairs.MatesOf(d.Puuid))
            {
                if (order.TryGetValue(mate, out var mateIndex) && mateIndex < order[d.Puuid])
                    continue;
                pairs.Of(d.Puuid, mate).DiedFirst++;
                pairs.Of(mate, d.Puuid).MateDiedFirst++;
            }
        }
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

    private static void CountProximity(MatchKill kill, string victim, List<string> mates, string? trader, BehaviorSignals victimSignals, Dictionary<string, BehaviorSignals> signals, Pairs pairs)
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
            var pair = pairs.Of(mate, victim);
            pair.MateNearbyDeaths++;
            if (Is(mate, trader))
            {
                signals[mate].TradesMade++;
                pair.Trades++;
            }
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
                && (mine.Economy.LoadoutValue >= RifleLoadout || GameContent.IsRifle(mine.Economy.Weapon?.Name)))
                s.EcoRifleBuys++;
        }
    }

    private static bool Is(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
