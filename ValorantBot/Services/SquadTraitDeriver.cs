using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Derives traits about how a player plays with the other tracked players. Pair behavior is judged against how the
/// same player treats the randoms in the same matches, so a trait says something about the relationship, not the
/// player alone. Same lifecycle as auto traits: enter at 3 hits in the latest shared matches, drop at 1 or fewer.
/// </summary>
public static class SquadTraitDeriver
{
    private const int Window = 5;
    private const int RareWindow = 8;
    private const int EnterHits = 3;
    private const int DropAtHits = 1;
    private const int MaxTraits = 8;
    private const double HoldEase = 0.85;
    private const int QueueWindow = 10;
    private const int MinWinRateMatches = 5;

    private delegate bool? Hit(MatchHistoryEntry match, bool held);

    /// <param name="owner">The player's puuid.</param>
    /// <param name="tracked">Every tracked player's puuid and display name.</param>
    /// <param name="heldIds">Ids of the squad traits the player has now, for hysteresis.</param>
    public static List<SquadTrait> Derive(string owner, List<MatchHistoryEntry> history, IReadOnlyDictionary<string, string> tracked, IReadOnlyCollection<string>? heldIds = null)
    {
        var held = (heldIds ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trackedSet = tracked.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = history
            .Where(h => h.Teammates is not null)
            .OrderByDescending(h => h.PlayedAt)
            .ToList();
        var traits = new List<SquadTrait>();

        foreach (var (mate, name) in tracked)
        {
            if (Is(mate, owner)) continue;
            var shared = ordered.Where(h => Mate(h, mate) is not null).ToList();
            if (shared.Count == 0) continue;
            DerivePairTraits(traits, held, owner, mate, name, ordered, shared, trackedSet);
        }

        DeriveStackTraits(traits, held, ordered, trackedSet, owner);
        return traits.Take(MaxTraits).ToList();
    }

    private static void DerivePairTraits(List<SquadTrait> traits, HashSet<string> held, string owner, string mate, string name,
        List<MatchHistoryEntry> ordered, List<MatchHistoryEntry> shared, HashSet<string> tracked)
    {
        string[] mates = [mate];
        PairSignals P(MatchHistoryEntry h) => Mate(h, mate)!;

        var tradeRate = Rate(s => s.Trades, s => s.MateNearbyDeaths);
        HitRule(traits, held, $"leaves:{mate}", $"leaves {name} to die", shared, Window, mates,
            (h, eased) => Compare(tradeRate, P(h), Randoms(h, tracked), 2, 0.5, higher: false, eased),
            judged => $"traded {Sum(judged, mate).Trades} of {Sum(judged, mate).MateNearbyDeaths} times {name} died next to them over {judged.Count} matches; traded {Pct(RandomSum(judged, tracked).Trades, RandomSum(judged, tracked).MateNearbyDeaths)} of randoms");

        HitRule(traits, held, $"bodyguard:{mate}", $"{name}'s bodyguard", shared, Window, mates,
            (h, eased) => Compare(tradeRate, P(h), Randoms(h, tracked), 2, 1.6, higher: true, eased),
            judged => $"traded {Pct(Sum(judged, mate).Trades, Sum(judged, mate).MateNearbyDeaths)} of {name}'s nearby deaths over {judged.Count} matches, randoms only {Pct(RandomSum(judged, tracked).Trades, RandomSum(judged, tracked).MateNearbyDeaths)}");

        HitRule(traits, held, $"steals:{mate}", $"steals {name}'s kills", shared, RareWindow, mates,
            (h, eased) => Compare(Rate(s => s.KillsOnMateDamaged, s => s.OwnKills), P(h), Randoms(h, tracked), 5, 1.6, higher: true, eased),
            judged => $"{Sum(judged, mate).KillsOnMateDamaged} kills on enemies {name} had already damaged over {judged.Count} matches, {Ratio(Sum(judged, mate).KillsOnMateDamaged, Sum(judged, mate).OwnKills, RandomSum(judged, tracked).KillsOnMateDamaged, RandomSum(judged, tracked).OwnKills)}x what they finish for a random");

        HitRule(traits, held, $"assists:{mate}", $"{name}'s assist servant", shared, Window, mates,
            (h, eased) => Compare(Rate(s => s.AssistsOnMateKills, s => s.MateKills), P(h), Randoms(h, tracked), 5, 1.6, higher: true, eased),
            judged => $"assisted {Pct(Sum(judged, mate).AssistsOnMateKills, Sum(judged, mate).MateKills)} of {name}'s kills over {judged.Count} matches, randoms' kills only {Pct(RandomSum(judged, tracked).AssistsOnMateKills, RandomSum(judged, tracked).MateKills)}");

        HitRule(traits, held, $"carries:{mate}", $"carries {name}", shared, Window, mates,
            (h, eased) => h.Acs >= P(h).MateAcs * (eased ? 1.3 * HoldEase : 1.3),
            judged => $"average ACS {judged.Average(h => h.Acs):F0} against {name}'s {judged.Average(h => P(h).MateAcs):F0} over {judged.Count} shared matches");

        HitRule(traits, held, $"teamkill:{mate}", $"teamkilled {name}", shared, RareWindow, mates,
            (h, _) => P(h).Teamkills > 0,
            judged => $"killed {name} {Sum(judged, mate).Teamkills} time(s) in the last {judged.Count} shared matches",
            enterHits: 1, dropAtHits: 0);

        QueueRule(traits, held, mate, name, ordered);

        // Symmetric traits live on one side of the pair only, so the squad roast cannot use them twice
        if (string.CompareOrdinal(owner, mate) > 0)
            return;

        HitRule(traits, held, $"together:{mate}", $"dies together with {name}", shared, Window, mates,
            (h, eased) => P(h).DiedTogether > 0 ? Compare(Rate(s => s.DiedTogether, s => s.Rounds), P(h), Randoms(h, tracked), 10, 1.8, higher: true, eased) : Randoms(h, tracked) is null ? null : false,
            judged => $"died within 3 s and ~15 m of {name} {Sum(judged, mate).DiedTogether} times over {judged.Count} matches, {Ratio(Sum(judged, mate).DiedTogether, Sum(judged, mate).Rounds, RandomSum(judged, tracked).DiedTogether, RandomSum(judged, tracked).Rounds)}x as often as with randoms");

        WinRateRule(traits, held, mate, name, ordered, shared);
    }

    private static void QueueRule(List<SquadTrait> traits, HashSet<string> held, string mate, string name, List<MatchHistoryEntry> ordered)
    {
        var recent = ordered.Take(QueueWindow).ToList();
        if (recent.Count < Window) return;
        var id = $"queue:{mate}";
        var share = (double)recent.Count(h => Mate(h, mate) is not null) / recent.Count;
        if (held.Contains(id) ? share >= 0.6 : share >= 0.8)
            traits.Add(new SquadTrait(id, $"never queues without {name}", $"{name} was in {share * 100:F0}% of their last {recent.Count} matches", [mate]));
    }

    private static void WinRateRule(List<SquadTrait> traits, HashSet<string> held, string mate, string name, List<MatchHistoryEntry> ordered, List<MatchHistoryEntry> shared)
    {
        var together = shared.Take(QueueWindow).ToList();
        var apart = ordered.Where(h => Mate(h, mate) is null).Take(QueueWindow).ToList();
        if (together.Count < MinWinRateMatches || apart.Count < MinWinRateMatches) return;

        var wrTogether = (double)together.Count(h => h.Won) / together.Count;
        var wrApart = (double)apart.Count(h => h.Won) / apart.Count;
        var diff = wrTogether - wrApart;
        var evidence = $"won {wrTogether * 100:F0}% of {together.Count} matches with {name}, {wrApart * 100:F0}% of {apart.Count} without";

        var cursed = $"cursed:{mate}";
        if (diff <= (held.Contains(cursed) ? -0.10 : -0.25))
            traits.Add(new SquadTrait(cursed, $"cursed duo with {name}", evidence, [mate]));
        var lucky = $"lucky:{mate}";
        if (diff >= (held.Contains(lucky) ? 0.10 : 0.25))
            traits.Add(new SquadTrait(lucky, $"lucky charm duo with {name}", evidence, [mate]));
    }

    private static void DeriveStackTraits(List<SquadTrait> traits, HashSet<string> held, List<MatchHistoryEntry> ordered, HashSet<string> tracked, string owner)
    {
        List<PairSignals> TrackedMates(MatchHistoryEntry h) =>
            h.Teammates!.Where(kv => tracked.Contains(kv.Key) && !Is(kv.Key, owner)).Select(kv => kv.Value).ToList();

        var stack = ordered.Where(h => TrackedMates(h).Count > 0).ToList();
        if (stack.Count == 0) return;

        // ponytail: pairwise death order, so in a 3+ stack "first" means first against most tracked mates, not strictly first
        HitRule(traits, held, "stack:first", "always the first to die in the stack", stack, Window, [],
            (h, eased) =>
            {
                var s = PairSignals.Sum(TrackedMates(h));
                var den = s.DiedFirst + s.MateDiedFirst;
                return den < 5 ? null : (double)s.DiedFirst / den >= (eased ? 0.58 : 0.65);
            },
            judged =>
            {
                var s = PairSignals.Sum(judged.SelectMany(TrackedMates));
                return $"died before their tracked teammates in {Pct(s.DiedFirst, s.DiedFirst + s.MateDiedFirst)} of rounds over {judged.Count} stack matches";
            });

        HitRule(traits, held, "stack:anchor", "the reason the stack eats an RR penalty", stack, Window, [],
            (h, _) =>
            {
                var mateTiers = TrackedMates(h).Select(p => p.MateTierId).Where(t => t > 0).ToList();
                if (h.PartyRrPenalty <= 0 || h.TierId <= 0 || mateTiers.Count == 0) return null;
                return h.TierId < mateTiers.Min();
            },
            judged => $"lowest rank in the stack in {judged.Count(h => h.TierId < TrackedMates(h).Select(p => p.MateTierId).Where(t => t > 0).DefaultIfEmpty(0).Min())} of {judged.Count} matches where the stack took an RR penalty (up to {judged.Max(h => h.PartyRrPenalty) * 100:F0}%)");

        var stackAcs = stack.Take(Window).ToList();
        var soloAcs = ordered.Where(h => TrackedMates(h).Count == 0).Take(Window).ToList();
        if (stackAcs.Count >= EnterHits && soloAcs.Count >= EnterHits)
        {
            var ratio = stackAcs.Average(h => h.Acs) / Math.Max(soloAcs.Average(h => h.Acs), 1);
            const string id = "stack:better-solo";
            if (ratio <= (held.Contains(id) ? 0.9 : 0.8))
                traits.Add(new SquadTrait(id, "plays better without the stack",
                    $"average ACS {stackAcs.Average(h => h.Acs):F0} in the stack, {soloAcs.Average(h => h.Acs):F0} without, over the last {stackAcs.Count} and {soloAcs.Count} matches", []));
        }
    }

    /// <summary>Judges the latest window of matches the rule can judge; a held trait stays until hits fall to dropAtHits.</summary>
    private static void HitRule(List<SquadTrait> traits, HashSet<string> held, string id, string label, List<MatchHistoryEntry> matches, int window,
        string[] mates, Hit hit, Func<List<MatchHistoryEntry>, string> evidence, int enterHits = EnterHits, int dropAtHits = DropAtHits)
    {
        var isHeld = held.Contains(id);
        var judged = matches.Where(h => hit(h, isHeld) is not null).Take(window).ToList();
        if (judged.Count < enterHits) return;

        var hits = judged.Count(h => hit(h, isHeld) == true);
        if (isHeld ? hits > dropAtHits : hits >= enterHits)
            traits.Add(new SquadTrait(id, label, evidence(judged), [.. mates]));
    }

    private static Func<PairSignals, int, double?> Rate(Func<PairSignals, int> num, Func<PairSignals, int> den) =>
        (s, minDen) => den(s) < minDen ? null : (double)num(s) / den(s);

    /// <summary>Owner's rate towards the mate against their rate towards randoms; null when either is too thin to judge.</summary>
    private static bool? Compare(Func<PairSignals, int, double?> rate, PairSignals mate, PairSignals? randoms, int minDen, double ratio, bool higher, bool eased)
    {
        if (randoms is null) return null;
        var (m, r) = (rate(mate, minDen), rate(randoms, minDen + 1));
        if (m is null || r is null) return null;
        var threshold = eased ? (higher ? ratio * HoldEase : ratio / HoldEase) : ratio;
        return higher ? m > 0 && m >= r * threshold : r > 0 && m <= r * threshold;
    }

    private static PairSignals? Mate(MatchHistoryEntry h, string mate) =>
        h.Teammates?.FirstOrDefault(kv => Is(kv.Key, mate)).Value;

    private static PairSignals? Randoms(MatchHistoryEntry h, HashSet<string> tracked)
    {
        var randoms = h.Teammates!.Where(kv => !tracked.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        return randoms.Count == 0 ? null : PairSignals.Sum(randoms);
    }

    private static PairSignals Sum(List<MatchHistoryEntry> matches, string mate) => PairSignals.Sum(matches.Select(h => Mate(h, mate)!));

    private static PairSignals RandomSum(List<MatchHistoryEntry> matches, HashSet<string> tracked) =>
        PairSignals.Sum(matches.Select(h => Randoms(h, tracked)).OfType<PairSignals>());

    private static string Pct(int num, int den) => den == 0 ? "n/a" : $"{100.0 * num / den:F0}%";

    private static string Ratio(int num, int den, int baseNum, int baseDen)
    {
        if (den == 0 || baseDen == 0 || baseNum == 0) return "many";
        return $"{(double)num / den / ((double)baseNum / baseDen):F1}";
    }

    private static bool Is(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
