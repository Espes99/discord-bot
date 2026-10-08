using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Pulls per-player stories out of the v4 match payload (rounds and kills) so every match gives the
/// roast something new. Every text is final English and safe to drop straight into a prompt.
/// </summary>
public static class HighlightExtractor
{
    public static MatchHighlights Extract(MatchDetailData match, MatchPlayer player, BehaviorSignals? signals)
    {
        var items = new List<Highlight>();
        var puuid = player.Puuid;
        var team = player.TeamId;
        var agent = player.Agent.Name;
        var rounds = match.Rounds ?? [];
        var kills = match.Kills ?? [];
        var totalRounds = rounds.Count > 0 ? rounds.Count : match.Teams.Sum(t => t.Rounds.Won + t.Rounds.Lost) / 2;
        if (totalRounds == 0)
            return MatchHighlights.Empty;

        var teammates = match.Players.Where(p => p.TeamId == team).ToList();
        var lost = match.Teams.FirstOrDefault(t => t.TeamId == team)?.Won == false;

        AddKillTimeline(items, kills, puuid, agent, totalRounds);
        AddRoundEvents(items, rounds, puuid, team, totalRounds);
        if (signals is not null)
            AddSignals(items, signals, totalRounds);
        AddBehavior(items, player, totalRounds);
        AddDamage(items, player, teammates, totalRounds);
        AddEconomy(items, player, teammates);
        AddAbilityUse(items, player, agent, totalRounds);
        AddMatchShape(items, match, rounds, team, lost, totalRounds);
        AddStanding(items, match, player, teammates);
        AddPenalties(items, match, player);

        return new MatchHighlights { Items = items };
    }

    private static bool Is(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void AddKillTimeline(List<Highlight> items, List<MatchKill> kills, string puuid, string agent, int totalRounds)
    {
        if (kills.Count == 0) return;

        var byRound = kills.GroupBy(k => k.Round).ToDictionary(g => g.Key, g => g.OrderBy(k => k.TimeInRoundInMs).ToList());

        var firstBloods = byRound.Values.Count(r => Is(r[0].Killer?.Puuid, puuid));
        var firstDeaths = byRound.Values.Count(r => Is(r[0].Victim?.Puuid, puuid));
        var role = ProfileTraitDeriver.RoleOf(agent);

        if (firstDeaths >= Math.Max(4, totalRounds / 4))
            items.Add(new Highlight("FirstDeaths", $"died first in {firstDeaths} of {totalRounds} rounds", firstDeaths >= totalRounds / 3 ? 3 : 2));
        if (firstBloods == 0 && role == "Duelist")
            items.Add(new Highlight("NoFirstBloods", $"0 first bloods in {totalRounds} rounds as {agent}, a duelist", 2));
        else if (firstBloods >= Math.Max(4, totalRounds / 4))
            items.Add(new Highlight("FirstBloods", $"{firstBloods} first bloods in {totalRounds} rounds", 2));

        var multis = byRound.Values
            .Select(r => r.Count(k => Is(k.Killer?.Puuid, puuid)))
            .Where(c => c >= 3)
            .ToList();
        var aces = multis.Count(c => c >= 5);
        if (aces > 0)
            items.Add(new Highlight("Ace", aces == 1 ? "got an ace" : $"got {aces} aces", 3));
        else if (multis.Count >= 2 || (multis.Count > 0 && multis.Max() >= 4))
            items.Add(new Highlight("Multikill", $"{multis.Count} round(s) with 3+ kills (best: {multis.Max()}k)", 2));

        var knife = kills.Count(k => Is(k.Killer?.Puuid, puuid) && (Is(k.Weapon?.Name, "Melee") || Is(k.Weapon?.Name, "Knife")));
        if (knife > 0)
            items.Add(new Highlight("KnifeKill", knife == 1 ? "got a knife kill" : $"got {knife} knife kills", 2));
        var knifed = kills.Count(k => Is(k.Victim?.Puuid, puuid) && (Is(k.Weapon?.Name, "Melee") || Is(k.Weapon?.Name, "Knife")));
        if (knifed > 0)
            items.Add(new Highlight("GotKnifed", knifed == 1 ? "got knifed" : $"got knifed {knifed} times", 3));

        var classicKills = kills.Count(k => Is(k.Killer?.Puuid, puuid) && Is(k.Weapon?.Name, "Classic"));
        if (classicKills >= 3)
            items.Add(new Highlight("ClassicKills", $"{classicKills} kills with the Classic pistol", 1));

        var assists = kills.Count(k => k.Assistants?.Any(a => Is(a.Puuid, puuid)) == true);
        var ownKills = kills.Count(k => Is(k.Killer?.Puuid, puuid));
        if (assists >= ownKills + 5 && assists >= 8)
            items.Add(new Highlight("AssistMerchant", $"{assists} assists to {ownKills} kills, did the damage and let others take the kill", 2));
    }

    private static void AddSignals(List<Highlight> items, BehaviorSignals s, int totalRounds)
    {
        if (s.Clutches > 0)
            items.Add(new Highlight("Clutch", s.Clutches == 1 ? "clutched a round alone" : $"clutched {s.Clutches} rounds alone", 3));
        if (s.LastAliveLost >= 5)
            items.Add(new Highlight("LastAliveLost", $"was the last one alive in {s.LastAliveLost} rounds and lost every one of them", 1));
        if (s.EcoRifleBuys >= 2)
            items.Add(new Highlight("EcoRifle", $"bought a rifle on {s.EcoRifleBuys} of the team's eco rounds", 2));
        if (s.ZeroDamageRounds >= totalRounds * 0.45)
            items.Add(new Highlight("ZeroDamageRounds", $"did zero damage in {s.ZeroDamageRounds} of {totalRounds} rounds", 3));
    }

    private static void AddRoundEvents(List<Highlight> items, List<MatchRound> rounds, string puuid, string team, int totalRounds)
    {
        if (rounds.Count == 0) return;

        var plants = rounds.Count(r => Is(r.Plant?.Player?.Puuid, puuid));
        var defuses = rounds.Count(r => Is(r.Defuse?.Player?.Puuid, puuid));
        var teamPlants = rounds.Count(r => Is(r.Plant?.Player?.Team, team));
        if (plants >= 4 && plants >= teamPlants * 0.6)
            items.Add(new Highlight("Planter", $"planted {plants} of the team's {teamPlants} spikes, the designated spike carrier", 1));
        if (defuses >= 2)
            items.Add(new Highlight("Defuser", $"defused {defuses} spikes", 1));

        var afk = rounds.Count(r => r.Stats?.Any(s => Is(s.Player?.Puuid, puuid) && s.WasAfk) == true);
        if (afk > 0)
            items.Add(new Highlight("Afk", $"AFK for {afk} round(s)", 3));
        var spawn = rounds.Count(r => r.Stats?.Any(s => Is(s.Player?.Puuid, puuid) && s.StayedInSpawn) == true);
        if (spawn >= 2)
            items.Add(new Highlight("Spawn", $"stayed in spawn for {spawn} whole rounds", 3));
    }

    private static void AddBehavior(List<Highlight> items, MatchPlayer player, int totalRounds)
    {
        var b = player.Behavior;
        if (b is null) return;
        if (b.AfkRounds >= 1 && items.All(i => i.Kind != "Afk"))
            items.Add(new Highlight("Afk", $"AFK for {b.AfkRounds:F0} round(s)", 3));
        if (b.RoundsInSpawn >= 2 && items.All(i => i.Kind != "Spawn"))
            items.Add(new Highlight("Spawn", $"stayed in spawn for {b.RoundsInSpawn:F0} rounds", 3));
        if (b.FriendlyFire is { Outgoing: >= 150 })
            items.Add(new Highlight("TeamDamage", $"did {b.FriendlyFire.Outgoing:F0} damage to their own team", 3));
    }

    private static void AddDamage(List<Highlight> items, MatchPlayer player, List<MatchPlayer> teammates, int totalRounds)
    {
        var dmg = player.Stats.Damage;
        if (dmg is null || dmg.Dealt == 0) return;
        var adr = (double)dmg.Dealt / totalRounds;
        var kills = player.Stats.Kills;
        var expectedKillsFromDamage = dmg.Dealt / 150.0;

        if (kills >= expectedKillsFromDamage * 1.4 && kills >= 12)
            items.Add(new Highlight("KillStealer", $"{kills} kills on only {adr:F0} ADR, teammates did the damage", 2));
        else if (kills <= expectedKillsFromDamage * 0.6 && adr >= 130)
            items.Add(new Highlight("DamageNoKills", $"{adr:F0} ADR but only {kills} kills, hit everything and finished nothing", 2));
        else if (adr < 90)
            items.Add(new Highlight("LowAdr", $"{adr:F0} damage per round", 2));

        var received = dmg.Received;
        if (received > dmg.Dealt * 1.5 && received > 2000)
            items.Add(new Highlight("Punchingbag", $"took {received} damage and dealt {dmg.Dealt}", 2));
    }

    private static void AddEconomy(List<Highlight> items, MatchPlayer player, List<MatchPlayer> teammates)
    {
        var eco = player.Economy;
        if (eco?.Spent is null || teammates.Count < 3) return;

        var spentRank = teammates.OrderByDescending(p => p.Economy?.Spent?.Overall ?? 0).ToList().FindIndex(p => Is(p.Puuid, player.Puuid));
        var acsRank = teammates.OrderByDescending(p => p.Stats.Score).ToList().FindIndex(p => Is(p.Puuid, player.Puuid));
        if (spentRank == 0 && acsRank >= teammates.Count - 2)
            items.Add(new Highlight("ExpensiveBottom", $"spent the most credits on the team ({eco.Spent.Overall}) for the least output", 2));

        var loadoutRank = teammates.OrderBy(p => p.Economy?.LoadoutValue?.Average ?? double.MaxValue).ToList().FindIndex(p => Is(p.Puuid, player.Puuid));
        if (loadoutRank == 0 && eco.LoadoutValue is not null && eco.LoadoutValue.Average < 2500)
            items.Add(new Highlight("Cheapskate", $"lowest average loadout on the team ({eco.LoadoutValue.Average:F0} credits), never buys", 1));
    }

    private static void AddAbilityUse(List<Highlight> items, MatchPlayer player, string agent, int totalRounds)
    {
        var casts = player.AbilityCasts;
        if (casts is null || totalRounds < 10) return;
        var role = ProfileTraitDeriver.RoleOf(agent);
        var perRound = (double)casts.Total / totalRounds;
        var expected = role switch
        {
            "Initiator" => 1.6,
            "Controller" => 1.4,
            "Sentinel" => 1.2,
            _ => 1.0
        };
        if (perRound < expected * 0.5)
            items.Add(new Highlight("NoUtility", $"cast {casts.Total} abilities in {totalRounds} rounds as {agent}, barely used the kit", role == "Duelist" ? 1 : 2));
        if (casts.Ultimate is >= 5)
            items.Add(new Highlight("UltSpam", $"used ultimate {casts.Ultimate} times", 1));
    }

    private static void AddMatchShape(List<Highlight> items, MatchDetailData match, List<MatchRound> rounds, string team, bool lost, int totalRounds)
    {
        var minutes = match.Metadata.GameLengthInMs / 60000;
        if (minutes >= 45)
            items.Add(new Highlight("LongMatch", $"a {minutes}-minute {(lost ? "loss" : "win")}", 2));

        if (totalRounds > 24)
            items.Add(new Highlight("Overtime", $"went to overtime ({totalRounds} rounds) and {(lost ? "lost" : "won")}", lost ? 2 : 1));

        if (rounds.Count >= 12)
        {
            var firstHalfWon = rounds.Take(12).Count(r => Is(r.WinningTeam, team));
            var firstHalfLost = 12 - firstHalfWon;
            if (lost && firstHalfWon >= 8)
                items.Add(new Highlight("ThrownLead", $"led {firstHalfWon}-{firstHalfLost} at half and lost", 3));
            else if (!lost && firstHalfWon <= 4)
                items.Add(new Highlight("Comeback", $"came back from {firstHalfWon}-{firstHalfLost} at half", 2));

            var longestLosingRun = 0;
            var run = 0;
            foreach (var r in rounds)
            {
                run = Is(r.WinningTeam, team) ? 0 : run + 1;
                longestLosingRun = Math.Max(longestLosingRun, run);
            }
            if (longestLosingRun >= 7)
                items.Add(new Highlight("RoundSlide", $"the team lost {longestLosingRun} rounds in a row at one point", 2));
        }

        var flawlessLost = rounds.Count(r => !Is(r.WinningTeam, team) && (r.Ceremony?.Contains("Flawless", StringComparison.OrdinalIgnoreCase) ?? false));
        if (flawlessLost >= 3)
            items.Add(new Highlight("FlawlessLost", $"the team got flawlessed {flawlessLost} times", 2));
        var thriftyLost = rounds.Count(r => !Is(r.WinningTeam, team) && (r.Ceremony?.Contains("Thrifty", StringComparison.OrdinalIgnoreCase) ?? false));
        if (thriftyLost >= 2)
            items.Add(new Highlight("LostToEco", $"lost {thriftyLost} rounds to enemy ecos", 2));
    }

    private static void AddStanding(List<Highlight> items, MatchDetailData match, MatchPlayer player, List<MatchPlayer> teammates)
    {
        if (match.Players.Count < 6) return;
        var serverRank = match.Players.OrderByDescending(p => p.Stats.Score).ToList().FindIndex(p => Is(p.Puuid, player.Puuid));
        var teamRank = teammates.OrderByDescending(p => p.Stats.Score).ToList().FindIndex(p => Is(p.Puuid, player.Puuid));

        if (serverRank == match.Players.Count - 1)
            items.Add(new Highlight("ServerBottom", "bottom-fragged the entire server, both teams", 3));
        else if (serverRank == 0)
            items.Add(new Highlight("ServerTop", "top-fragged the entire server", 2));
        else if (teamRank == teammates.Count - 1)
            items.Add(new Highlight("TeamBottom", "bottom-fragged the team", 2));

        var level = player.AccountLevel;
        if (level > 0 && level < 40)
            items.Add(new Highlight("SmurfLevel", $"account level {level}, on an alt or brand new", 1));
    }

    private static void AddPenalties(List<Highlight> items, MatchDetailData match, MatchPlayer player)
    {
        // Penalty is a fraction (0.25 = 25 % less RR) for rank disparity in the party
        var penalty = match.Metadata.PartyRrPenaltys?.FirstOrDefault(p => Is(p.PartyId, player.PartyId) && p.Penalty > 0);
        if (penalty is not null)
            items.Add(new Highlight("PartyPenalty", $"the stack is eating a {penalty.Penalty * 100:F0}% RR penalty for the rank gap between them", 1));
    }
}
