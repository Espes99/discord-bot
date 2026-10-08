using ValorantBot.Models;
using ValorantBot.Services;

namespace ValorantBot.Tests;

public class SquadTraitTests
{
    private static readonly Dictionary<string, string> Tracked = new() { ["owner"] = "Owner#1", ["mate"] = "Mate#2" };

    // Owner trades half of the randoms who die next to them; trades of the tracked mate vary per match
    private static MatchHistoryEntry Match(int hoursAgo, int mateTrades) => new()
    {
        MatchId = $"m{hoursAgo}",
        PlayedAt = DateTime.UtcNow.AddHours(-hoursAgo),
        Acs = 200,
        Teammates = new()
        {
            ["mate"] = new PairSignals { Rounds = 20, MateAcs = 200, MateNearbyDeaths = 4, Trades = mateTrades },
            ["r1"] = new PairSignals { Rounds = 20, MateAcs = 200, MateNearbyDeaths = 2, Trades = 1 },
            ["r2"] = new PairSignals { Rounds = 20, MateAcs = 200, MateNearbyDeaths = 2, Trades = 1 },
            ["r3"] = new PairSignals { Rounds = 20, MateAcs = 200, MateNearbyDeaths = 2, Trades = 1 },
        }
    };

    private static List<string> Ids(List<MatchHistoryEntry> history, params string[] held) =>
        SquadTraitDeriver.Derive("owner", history, Tracked, held).Select(t => t.Id).ToList();

    [Fact]
    public void Leaves_mate_to_die_enters_after_three_and_fades_after_five_clean()
    {
        var history = new List<MatchHistoryEntry> { Match(8, 0), Match(7, 0), Match(6, 0) };
        var traits = SquadTraitDeriver.Derive("owner", history, Tracked);
        var leaves = Assert.Single(traits, t => t.Id == "leaves:mate");
        Assert.Equal("leaves Mate#2 to die", leaves.Label);
        Assert.Equal(["mate"], leaves.MatePuuids);
        Assert.DoesNotContain("carries:mate", Ids(history));

        history.AddRange([Match(5, 2), Match(4, 2), Match(3, 2), Match(2, 2), Match(1, 2)]);
        Assert.DoesNotContain("leaves:mate", Ids(history, "leaves:mate"));
    }

    [Fact]
    public void Matches_without_randoms_are_not_judged()
    {
        var full = Match(3, 0);
        full.Teammates!.Remove("r1");
        full.Teammates.Remove("r2");
        full.Teammates.Remove("r3");

        Assert.DoesNotContain("leaves:mate", Ids([full, Match(2, 0), Match(1, 0)]));
    }

    [Fact]
    public void Only_symmetric_traits_have_a_mirror()
    {
        Assert.Equal("together:owner", SquadTraitDeriver.MirrorId("together:mate", "owner"));
        Assert.Equal("cursed:owner", SquadTraitDeriver.MirrorId("cursed:mate", "owner"));
        Assert.Null(SquadTraitDeriver.MirrorId("leaves:mate", "owner"));
        Assert.Null(SquadTraitDeriver.MirrorId("stack:first", "owner"));
        Assert.Equal("mate", SquadTraitDeriver.MateOf("together:mate"));
        Assert.Null(SquadTraitDeriver.MateOf("stack:anchor"));
    }
}
