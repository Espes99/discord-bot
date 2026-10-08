using System.Text.Json;
using ValorantBot.Models;
using ValorantBot.Services;

namespace ValorantBot.Tests;

public class AutoTraitTests
{
    private static MatchPlayer P(string id, string team) => new() { Puuid = id, TeamId = team };

    private static MatchKill Kill(int round, int ms, string killer, string killerTeam, string victim, string victimTeam, params (string Id, int X)[] alive) => new()
    {
        Round = round,
        TimeInRoundInMs = ms,
        Killer = new KillPlayer { Puuid = killer, Team = killerTeam },
        Victim = new KillPlayer { Puuid = victim, Team = victimTeam },
        Location = new MapLocation { X = 0, Y = 0 },
        PlayerLocations = alive.Select(a => new PlayerLocation { Player = new KillPlayer { Puuid = a.Id }, Location = new MapLocation { X = a.X, Y = 0 } }).ToList()
    };

    [Fact]
    public void Signals_count_bait_and_trade()
    {
        var match = new MatchDetailData
        {
            Players = [P("a1", "Red"), P("a2", "Red"), P("a3", "Red"), P("a4", "Red"), P("a5", "Red"), P("b1", "Blue"), P("b2", "Blue"), P("b3", "Blue"), P("b4", "Blue"), P("b5", "Blue")],
            Rounds = [new MatchRound { Id = 0, WinningTeam = "Blue" }, new MatchRound { Id = 1, WinningTeam = "Red" }],
            Kills =
            [
                // Round 0: a2 dies next to a1, a1 never trades, the rest die, a1 is left alone and the round is lost
                Kill(0, 1000, "b1", "Blue", "a2", "Red", ("a1", 500), ("a3", 5000), ("a4", 5000), ("a5", 5000)),
                Kill(0, 2000, "b1", "Blue", "a3", "Red", ("a1", 5000), ("a4", 5000), ("a5", 5000)),
                Kill(0, 3000, "b1", "Blue", "a4", "Red", ("a1", 5000), ("a5", 5000)),
                Kill(0, 4000, "b1", "Blue", "a5", "Red", ("a1", 5000)),
                // Round 1: a2 dies next to a1, a1 trades within the window
                Kill(1, 1000, "b1", "Blue", "a2", "Red", ("a1", 500)),
                Kill(1, 2500, "a1", "Red", "b1", "Blue"),
            ]
        };

        var s = BehaviorSignalExtractor.ComputeAll(match);

        Assert.Equal(1, s["a1"].LastAlive);
        Assert.Equal(1, s["a1"].LastAliveLost);
        Assert.Equal(2, s["a1"].NearbyTeammateDeaths);
        Assert.Equal(1, s["a1"].TradesMade);
        Assert.Equal(1, s["a1"].SurvivedLostRounds);
        Assert.Equal(2, s["a2"].FirstDeaths);
        Assert.Equal(1, s["a2"].DeathsTraded);
        Assert.Equal(1, s["a5"].IsolatedDeaths);
        Assert.Equal(2, s["b1"].FirstBloods);
    }

    [Fact]
    public void Baiter_is_derived_from_consistent_history()
    {
        var history = Enumerable.Range(0, 6).Select(i => new MatchHistoryEntry
        {
            MatchId = $"m{i}",
            PlayedAt = DateTime.UtcNow.AddHours(-i),
            Signals = new BehaviorSignals { Rounds = 20, LostRounds = 10, LastAlive = 8, LastAliveLost = 7, NearbyTeammateDeaths = 8, TradesMade = 0, AbilityCasts = 30 },
            LobbySignals = new BehaviorSignals { Rounds = 180, LostRounds = 90, LastAlive = 30, LastAliveLost = 15, NearbyTeammateDeaths = 60, TradesMade = 24, AbilityCasts = 270 }
        }).ToList();

        var traits = ProfileTraitDeriver.DeriveTraits(history, HistorySummarizer.Summarize(history));

        var baiter = Assert.Single(traits, t => t.Label == "baiter");
        Assert.Contains("40%", baiter.Evidence);
        Assert.DoesNotContain(traits, t => t.Label == "utility hoarder");
    }

    [Fact]
    public void Legacy_string_traits_still_load()
    {
        var traits = JsonSerializer.Deserialize<List<AutoTrait>>("""["hardstuck Gold", {"Label":"baiter","Evidence":"x"}]""")!;

        Assert.Equal(new AutoTrait("hardstuck Gold"), traits[0]);
        Assert.Equal(new AutoTrait("baiter", "x"), traits[1]);
    }
}
