using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Gathers the material for a roast in code: which stories are true for the match, which bio line or trait
/// to use, who plays which squad role, and what was used recently. The model chooses story, tone and form
/// from that material; the recent lists are how it avoids repeating itself.
/// </summary>
public class RoastPlanner(IRoastPlanStore planStore, IPlayerProfileStore profileStore)
{
    private static readonly Random Rng = Random.Shared;

    private const int RecentFormWindow = 3;
    private const int RecentVoiceWindow = 4;
    private const int RecentFocusWindow = 2;
    private const int RecentOpenerWindow = 3;

    private const int SoloStories = 6;
    private const int SquadMemberStories = 3;
    private const int ToneIdeaCount = 6;

    private const double SoloAllusionChance = 0.6;
    private const double SquadAllusionChance = 0.7;
    // A small group with few traits plays several games a day, so cooldown is measured in time, not messages
    private static readonly TimeSpan TraitCooldown = TimeSpan.FromDays(3);
    private const int PreviousAnglesShown = 5;

    // Inspiration only: a few are shown per message and the model may pick one or invent its own
    private static readonly string[] Voices =
    [
        "disappointed coach at a post-game press conference",
        "nature documentary narrator observing the player in their natural habitat",
        "passive-aggressive mom who is not mad, just disappointed",
        "sports commentator doing play-by-play",
        "brutally honest Reddit match review",
        "drill sergeant addressing a recruit after a failed exercise",
        "detective filing a crime report",
        "overly enthusiastic hype man, even when they played badly",
        "therapist processing what they just witnessed",
        "judge delivering a verdict",
        "ancient Greek philosopher",
        "sarcastic best friend who watched the whole thing",
        "airline pilot making a calm announcement about the situation",
        "wildlife rescue volunteer describing an injured animal",
        "customer support agent reading from a script",
        "weather forecaster reporting the outlook",
        "IKEA assembly manual, step by step",
        "movie trailer voice-over",
        "tired night-shift nurse",
        "conspiracy theorist who has connected the dots",
        "bored museum tour guide",
        "auctioneer trying to sell the performance",
        "obituary writer",
        "kindergarten teacher explaining to the class what happened",
    ];

    private static readonly Dictionary<RoastForm, string> FormDirectives = new()
    {
        [RoastForm.OneLiner] = "one single sentence, no setup, straight to the punch",
        [RoastForm.SetupAndPunchline] = "one sentence of setup, then one sentence of punchline",
        [RoastForm.FakeQuote] = "a fake quote the player supposedly said during the match, in quotation marks, then one sentence of reaction",
        [RoastForm.QuestionToPlayer] = "a direct question to the player that they cannot answer well, optionally followed by one short sentence",
        [RoastForm.UnrelatedComparison] = "compare the performance to something completely unrelated to gaming (cooking, traffic, weather, furniture, taxes) and commit to the comparison",
        [RoastForm.ListOfThree] = "three short items, comma separated or as a tiny list, escalating in cruelty",
        [RoastForm.Announcement] = "a public announcement or notice, as if pinned on a wall",
        [RoastForm.SecondPerson] = "speak directly to the player as 'you', no name in the first sentence",
        [RoastForm.ThirdPerson] = "narrate the player in third person, as if they are not in the room",
    };

    private static readonly Dictionary<SquadForm, string> SquadFormDirectives = new()
    {
        [SquadForm.GroupChatLeak] = "a leaked group chat: a few short chat lines between the players, names as senders, then one closing line from you",
        [SquadForm.PostMatchInterview] = "a post-match interview where you ask the scapegoat one question and they answer badly; the other players are referenced in the question or the answer",
        [SquadForm.AwardCeremony] = "an award ceremony: every player gets one award, worst award last, scapegoat gets the most words",
        [SquadForm.IncidentReport] = "an incident report with a one-line summary and a short findings section naming everyone",
        [SquadForm.OneLinePerPlayer] = "exactly one line per player, scapegoat's line is the longest, footnotes get a handful of words",
        [SquadForm.BlameParagraph] = "one paragraph that blames the scapegoat and name-drops everyone else as accomplices",
        [SquadForm.TwoWayComparison] = "compare the two most different players in the squad, mention the rest as footnotes",
    };

    private static readonly string[] AlwaysBannedOpeners =
    [
        "the player's name", "Alright", "Ladies and gentlemen", "Case file", "Well well", "Ah", "Oh", "So"
    ];

    private static readonly string[] NorwegianBannedOpeners = ["Altså", "Jaja", "Nei vel", "Herregud", "Oi", "Nå"];

    public RoastPlan PlanSolo(PerformanceResult result, PlayerHistorySummary? history, RankChangeInfo? rankChange, string storeKey)
    {
        var records = planStore.GetPlayerRecords(storeKey);
        var solo = records.Where(r => r.Source == "solo").ToList();
        var stories = FocusCandidates.Build(result, history, rankChange)
            .OrderByDescending(c => c.Weight)
            .Take(SoloStories)
            .ToList();
        var recentTones = solo.TakeLast(RecentVoiceWindow).Select(r => r.Voice).ToList();

        return new RoastPlan
        {
            Stories = stories,
            RequiredStory = rankChange is not null ? stories.First(c => c.Kind == "RankChange") : null,
            ToneIdeas = ToneIdeas(ToneIdeaCount, recentTones),
            MinSentences = rankChange is not null ? 2 : 1,
            MaxSentences = 3,
            Allusion = Rng.NextDouble() < SoloAllusionChance ? PickAllusion(storeKey) : null,
            Recent = new RecentChoices
            {
                Stories = Distinct(records.TakeLast(RecentFocusWindow).Select(r => r.FocusKind)),
                Tones = Distinct(recentTones),
                Forms = Distinct(solo.TakeLast(RecentFormWindow).Select(r => r.Form)),
                BannedOpeners = BannedOpeners(records.Select(r => r.Opener), result.MatchPlayer.Name)
            }
        };
    }

    public SquadRoastPlan PlanSquad(
        List<PerformanceResult> results,
        Dictionary<string, PlayerHistorySummary>? histories,
        Dictionary<string, RankChangeInfo>? rankChanges,
        Func<TrackedPlayer, string> storeKeyOf)
    {
        var keys = results.Select(r => storeKeyOf(r.Player)).ToList();
        var squadKey = RoastPlanStore.SquadKey(keys);
        var records = planStore.GetSquadRecords(squadKey);
        var roles = AssignRoles(results, keys, records);

        var members = results.Select((r, i) =>
        {
            var key = keys[i];
            PlayerHistorySummary? h = null;
            histories?.TryGetValue(key, out h);
            RankChangeInfo? rc = null;
            rankChanges?.TryGetValue(key, out rc);
            var role = roles[key];
            // Footnotes get a few words, so one story is plenty
            var stories = FocusCandidates.Build(r, h, rc)
                .OrderByDescending(c => c.Weight)
                .Take(role == SquadRole.Footnote ? 1 : SquadMemberStories)
                .ToList();
            return new SquadMemberPlan
            {
                StoreKey = key,
                DisplayName = $"{r.MatchPlayer.Name}#{r.MatchPlayer.Tag}",
                Agent = r.MatchPlayer.Agent.Name,
                Role = role,
                Stories = stories
            };
        }).ToList();

        RoastAllusion? allusion = null;
        SquadMemberPlan? owner = null;
        var present = results.Select(r => r.MatchPlayer.Puuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Rng.NextDouble() < SquadAllusionChance)
        {
            // Spread trait jokes around the squad, favouring the roles that get the most text
            var lastOwner = records.LastOrDefault()?.AllusionOwnerKey;
            var pool = members.Where(m => m.StoreKey != lastOwner).ToList();
            if (pool.Count == 0)
                pool = [.. members];
            while (pool.Count > 0 && allusion is null)
            {
                owner = PickWeighted(pool, m => AllusionRoleWeight(m.Role));
                pool.Remove(owner);
                allusion = PickAllusion(owner.StoreKey, present);
            }
            if (allusion is null)
                owner = null;
        }

        var (min, max) = results.Count switch
        {
            2 => (2, 4),
            3 => (3, 5),
            _ => (4, 6)
        };
        var recentTones = records.TakeLast(RecentVoiceWindow).Select(r => r.Voice).ToList();

        return new SquadRoastPlan
        {
            Members = members,
            ToneIdeas = ToneIdeas(ToneIdeaCount, recentTones),
            MinSentences = min,
            MaxSentences = max,
            Allusion = allusion,
            AllusionOwner = owner?.DisplayName,
            AllusionOwnerKey = owner?.StoreKey,
            Recent = new RecentChoices
            {
                Tones = Distinct(recentTones),
                Forms = Distinct(records.TakeLast(RecentFormWindow).Select(r => r.Form)),
                BannedOpeners = BannedOpeners(records.Select(r => r.Opener), null)
            }
        };
    }

    public void RecordSolo(string storeKey, RoastPlan plan, RoastChoice choice)
    {
        planStore.AddPlayerRecord(storeKey, new RoastPlanRecord
        {
            At = DateTime.UtcNow,
            Source = "solo",
            Voice = choice.Tone,
            Form = choice.Form,
            FocusKind = choice.Story,
            Allusion = plan.Allusion?.Text,
            Opener = Opener(choice.Message)
        });

        if (plan.Allusion is not null)
            planStore.AddTraitUse(storeKey, new TraitUseRecord { At = DateTime.UtcNow, Trait = plan.Allusion.Text, Angle = NullIfBlank(choice.Angle) });
    }

    public void RecordSquad(SquadRoastPlan plan, RoastChoice choice)
    {
        var squadKey = RoastPlanStore.SquadKey(plan.Members.Select(m => m.StoreKey));
        planStore.AddSquadRecord(squadKey, new SquadPlanRecord
        {
            At = DateTime.UtcNow,
            Voice = choice.Tone,
            Form = choice.Form,
            Roles = plan.Members.ToDictionary(m => m.StoreKey, m => m.Role.ToString()),
            AllusionOwnerKey = plan.AllusionOwnerKey,
            Opener = Opener(choice.Message)
        });

        foreach (var member in plan.Members)
        {
            var usedAllusion = plan.AllusionOwnerKey == member.StoreKey ? plan.Allusion?.Text : null;
            planStore.AddPlayerRecord(member.StoreKey, new RoastPlanRecord
            {
                At = DateTime.UtcNow,
                Source = "squad",
                Allusion = usedAllusion
            });
        }

        if (plan.Allusion is not null && plan.AllusionOwnerKey is not null)
            planStore.AddTraitUse(plan.AllusionOwnerKey, new TraitUseRecord { At = DateTime.UtcNow, Trait = plan.Allusion.Text, Angle = NullIfBlank(choice.Angle) });
    }

    /// <summary>A shuffled handful of voices, skipping recently used ones, as inspiration for the model.</summary>
    public static List<string> ToneIdeas(int count, IEnumerable<string?>? recent = null)
    {
        var exclude = (recent ?? []).Where(r => r is not null).Select(r => r!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Voices.Where(v => !exclude.Contains(v)).OrderBy(_ => Rng.Next()).Take(count).ToList();
    }

    public static IEnumerable<string> FormMenu => FormDirectives.Select(kv => $"{kv.Key}: {kv.Value}");
    public static IEnumerable<string> SquadFormMenu => SquadFormDirectives.Select(kv => $"{kv.Key}: {kv.Value}");

    private Dictionary<string, SquadRole> AssignRoles(List<PerformanceResult> results, List<string> keys, List<SquadPlanRecord> records)
    {
        var byAcs = results.Select((r, i) => (key: keys[i], r)).OrderBy(x => x.r.Acs).ToList();
        var lastRoles = records.LastOrDefault()?.Roles ?? new Dictionary<string, string>();

        // Rotate the scapegoat unless the worst player is worst by a wide margin.
        var scapegoat = byAcs[0];
        if (byAcs.Count > 1
            && lastRoles.TryGetValue(scapegoat.key, out var lastRole)
            && lastRole == nameof(SquadRole.Scapegoat)
            && byAcs[1].r.Acs - scapegoat.r.Acs < 40
            && scapegoat.r.Acs >= 100)
        {
            scapegoat = byAcs[1];
        }

        var roles = keys.ToDictionary(k => k, _ => SquadRole.Footnote);
        roles[scapegoat.key] = SquadRole.Scapegoat;

        var carry = byAcs.Last(x => x.key != scapegoat.key);
        roles[carry.key] = SquadRole.Carry;

        var rest = results.Select((r, i) => (key: keys[i], r))
            .Where(x => roles[x.key] == SquadRole.Footnote)
            .OrderBy(x => x.r.Acs)
            .ToList();
        if (rest.Count > 0)
            roles[rest[0].key] = SquadRole.Ghost;

        return roles;
    }

    /// <summary>
    /// Bio, manual traits and auto traits share one pool; each item cools down for TraitCooldown after use.
    /// In a squad roast, squad traits join the pool when every mate they name is in the stack.
    /// Less-used items are favoured, and the angles already used for the pick go along so the joke changes.
    /// </summary>
    private RoastAllusion? PickAllusion(string storeKey, IReadOnlySet<string>? stack = null)
    {
        var profile = profileStore.GetProfile(storeKey);
        if (profile is null)
            return null;

        var uses = planStore.GetTraitUses(storeKey);
        var cutoff = DateTime.UtcNow - TraitCooldown;
        var cooling = uses.Where(u => u.At > cutoff).Select(u => u.Trait).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var evidence = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.Bio))
            evidence.TryAdd(profile.Bio.Trim(), null);
        foreach (var trait in profile.ManualTraits)
            evidence.TryAdd(trait, null);
        foreach (var trait in profile.AutoTraits)
            evidence.TryAdd(trait.Label, trait.Evidence);
        if (stack is not null)
        {
            foreach (var trait in profile.SquadTraits.Where(t => t.MatePuuids.All(stack.Contains)))
                evidence.TryAdd(trait.Label, trait.Evidence);
        }

        var pool = evidence.Keys.Where(t => !cooling.Contains(t)).ToList();
        if (pool.Count == 0)
            return null;

        var useCounts = uses
            .GroupBy(u => u.Trait, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var picked = PickWeighted(pool, t => 1.0 / (1 + useCounts.GetValueOrDefault(t, 0)));

        var angles = uses
            .Where(u => u.Trait.Equals(picked, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(u.Angle))
            .Select(u => u.Angle!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(PreviousAnglesShown)
            .ToList();
        return new RoastAllusion(picked, angles, evidence[picked]);
    }

    private static double AllusionRoleWeight(SquadRole role) => role switch
    {
        SquadRole.Scapegoat => 3,
        SquadRole.Carry => 2,
        SquadRole.Ghost => 1.5,
        _ => 0.5
    };

    private List<string> BannedOpeners(IEnumerable<string?> recentOpeners, string? playerName)
    {
        var banned = AlwaysBannedOpeners.Select(o => $"\"{o}\"").ToList();
        if (playerName is not null)
            banned[0] = $"\"{playerName}\"";
        else
            banned[0] = AlwaysBannedOpeners[0];
        if (profileStore.Language == BotLanguage.Norwegian)
            banned.AddRange(NorwegianBannedOpeners.Select(o => $"\"{o}\""));
        banned.AddRange(recentOpeners.Where(o => !string.IsNullOrWhiteSpace(o)).TakeLast(RecentOpenerWindow).Select(o => $"\"{o}\""));
        return banned;
    }

    private static string Opener(string message)
    {
        var words = message
            .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .Select(w => w.Trim('*', '_', '"', '“', '”'));
        return string.Join(' ', words);
    }

    private static List<string> Distinct(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T PickWeighted<T>(IReadOnlyList<T> options, Func<T, double> weightOf)
    {
        var weights = options.Select(o => Math.Max(weightOf(o), 0.01)).ToList();
        var roll = Rng.NextDouble() * weights.Sum();
        for (var i = 0; i < options.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0)
                return options[i];
        }
        return options[^1];
    }
}
