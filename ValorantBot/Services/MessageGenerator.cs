using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Generates AI-powered Discord messages using Claude, with static fallbacks.
/// </summary>
public class MessageGenerator(AnthropicClient client, IMessageHistoryStore messageHistory, IPlayerProfileStore profileStore, ILogger<MessageGenerator> logger) : IMessageGenerator
{
    private static readonly Random Rng = new();

    private static string StoreKey(TrackedPlayer player) =>
        !string.IsNullOrEmpty(player.Puuid) ? player.Puuid : MatchTracker.PlayerKey(player.Name, player.Tag);

    private static readonly string[] StyleModifiers =
    [
        "Voice: disappointed coach giving a post-game press conference",
        "Voice: nature documentary narrator observing the player in their natural habitat",
        "Voice: passive-aggressive mom who's 'not mad, just disappointed'",
        "Voice: sports commentator doing play-by-play highlights",
        "Voice: a brutally honest Reddit match review",
        "Voice: drill sergeant addressing a recruit after a failed exercise",
        "Voice: detective filing a crime report about the match",
        "Voice: an overly enthusiastic hype man (even when they played badly)",
        "Voice: a therapist trying to process what they just witnessed",
        "Voice: a judge delivering a verdict in a courtroom",
        "Voice: an ancient Greek philosopher reflecting on the match",
        "Voice: a sarcastic best friend who's been watching the whole time",
    ];

    private const string BaseRules = """
        You are a toxic but funny Discord bot that roasts Valorant players. Be creative with insults and use swear words and foul language to really drive the point home.

        Shared rules:
        - Use Discord markdown (**bold**, etc.) and emojis
        - If RANK CHANGES are included, weave them naturally into the roast. Don't treat them as a separate topic.
          - For MAJOR tier changes (e.g. Silver to Gold, Plat to Diamond): make it dramatic and over the top
          - For minor rank changes (within same tier): a quick mention is enough
        - If player history is provided, reference trends to make roasts more personal (streaks, declining stats, map weaknesses, etc.)
        - If a PLAYER PROFILE is provided, use it to personalize your roast. Reference their known personality, habits, and tendencies. This is what makes your roasts recognizable and personal to the player.
        - If weapon context is provided, factor it into your HS% commentary. Don't mock low HS% if the player mainly used shotguns, snipers, or LMGs. Mock them for weapon choice instead if anything.
        - Never be mean-spirited about real personal things, keep it about the game
        - Do NOT use any prefix or label. Just output the message directly.
        """;

    private const string SoloRules = """
        Context: roasting a single player's match performance.

        Additional rules:
        - Keep messages short (1-3 sentences max, or up to 4 if there's a rank change to address)
        - Be savage when they play badly, really go for it
        - Reference specific stats (K/D/A, Combat Score, HS%, agent, map) to make the roast personal
        - For terrible/bad performance: be toxic and funny, mock them relentlessly
        - For average performance: be dismissive or backhanded
        - For promotions: throw shade ("finally", "boosted?") while acknowledging it
        - For demotions: pile on extra, they played badly AND lost rank
        """;

    private const string SquadRules = """
        Context: roasting a squad of players who queued together.

        Additional rules:
        - Write a medium-length message (3-6 sentences) roasting the entire squad
        - Call out individual players by name, blame the worst performer, mock the carried players, etc.
        - Compare players against each other (e.g. "while X was busy dying, Y was actually trying")
        - Mock the fact that they queued together and still played like this
        - Reference specific stats (K/D/A, Combat Score, HS%, agent) to make roasts personal
        - If they lost, make it extra savage, they stacked and STILL lost
        - If they won, find the weak link who got carried
        """;

    private const string RankChangeRules = """
        Context: reacting to a Valorant rank change (no match stats).

        Additional rules:
        - Keep messages short (1-3 sentences max)
        - For promotions: be funny and celebratory, but still throw shade ("finally", "took you long enough", "boosted?")
        - For demotions: be absolutely savage. Mock them, question their life choices, suggest they uninstall
        - Reference the specific ranks involved (old rank and new rank)
        - For MAJOR promotions, act like they just won Worlds. For MAJOR demotions, treat it like a tragedy of epic proportions.
        """;

    /// <inheritdoc />
    public async Task<string> GenerateMessageAsync(PerformanceResult result, PlayerHistorySummary? history = null, RankChangeInfo? rankChange = null)
    {
        var stats = result.MatchPlayer.Stats;
        var historyBlock = history is not null ? $"\n{HistorySummarizer.FormatForPrompt(history)}\n" : "";
        var storeKey = StoreKey(result.Player);
        var profileBlock = FormatProfileForPrompt(profileStore.GetProfile(storeKey));
        var rankBlock = rankChange is not null
            ? $"\nRANK CHANGE: {(rankChange.IsPromotion ? "PROMOTED" : "DEMOTED")} from {rankChange.OldRank} to {rankChange.NewRank} ({(rankChange.IsMajor ? "MAJOR tier change" : "minor change")})\n"
            : "";
        var weaponBlock = FormatWeaponContext(result.WeaponContext);
        var prompt = $"""
            Player: {result.MatchPlayer.Name}#{result.MatchPlayer.Tag}
            Agent: {result.MatchPlayer.Agent.Name}
            Map: {result.MapName}
            Result: {(result.Won ? "WIN" : "LOSS")} ({result.Score})
            K/D/A: {stats.Kills}/{stats.Deaths}/{stats.Assists}
            Combat Score: {result.Acs:F0}
            KDA Ratio: {stats.Kda:F2}
            Headshot %: {stats.HeadshotPercentage:F1}%{weaponBlock}
            Performance Rating: {result.Rating}
            {historyBlock}{profileBlock}{rankBlock}
            Generate a single Discord message for this player's performance.
            """;

        try
        {
            var systemPrompt = BuildSystemPrompt($"{BaseRules}\n{SoloRules}");
            var text = await CallClaudeAsync(systemPrompt, prompt, 600);

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated message for {Player}: {Message}", result.MatchPlayer.Name, text);
                messageHistory.AddMessage(text);
                return text;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate AI message, using fallback");
        }

        return GetFallbackMessage(result);
    }

    /// <inheritdoc />
    public async Task<string> GenerateSquadMessageAsync(List<PerformanceResult> results, Dictionary<string, PlayerHistorySummary>? histories = null, Dictionary<string, RankChangeInfo>? rankChanges = null)
    {
        var first = results[0];
        var playerStats = string.Join("\n", results.Select(r =>
        {
            var s = r.MatchPlayer.Stats;
            var displayKey = $"{r.MatchPlayer.Name}#{r.MatchPlayer.Tag}";
            var storeK = StoreKey(r.Player);
            var historyLine = histories is not null && histories.TryGetValue(storeK, out var h)
                ? $"\n    History: WR {h.WinRate:F0}%, Avg ACS {h.AverageAcs:F0}, Avg KDA {h.AverageKda:F2}, {(h.CurrentLossStreak > 1 ? $"{h.CurrentLossStreak} loss streak" : h.CurrentWinStreak > 1 ? $"{h.CurrentWinStreak} win streak" : "no streak")}"
                : "";
            var rankLine = rankChanges is not null && rankChanges.TryGetValue(storeK, out var rc)
                ? $"\n    RANK CHANGE: {(rc.IsPromotion ? "PROMOTED" : "DEMOTED")} from {rc.OldRank} to {rc.NewRank} ({(rc.IsMajor ? "MAJOR tier change" : "minor change")})"
                : "";
            var weaponLine = FormatWeaponContext(r.WeaponContext);
            var profileLine = FormatProfileLineForSquad(profileStore.GetProfile(storeK));
            return $"""
                - {displayKey} | Agent: {r.MatchPlayer.Agent.Name} | K/D/A: {s.Kills}/{s.Deaths}/{s.Assists} | ACS: {r.Acs:F0} | KDA: {s.Kda:F2} | HS%: {s.HeadshotPercentage:F1}%{weaponLine} | Rating: {r.Rating}{historyLine}{rankLine}{profileLine}
                """;
        }));

        var prompt = $"""
            Squad match on **{first.MapName}** — Result: {(first.Won ? "WIN" : "LOSS")} ({first.Score})

            Players in the stack:
            {playerStats}

            Generate a single Discord message roasting this squad for queueing together. Blame individuals by name based on their stats.
            """;

        try
        {
            var systemPrompt = BuildSystemPrompt($"{BaseRules}\n{SquadRules}");
            var text = await CallClaudeAsync(systemPrompt, prompt, 600);

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated squad message: {Message}", text);
                messageHistory.AddMessage(text);
                return text;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate AI squad message, using fallback");
        }

        return GetSquadFallbackMessage(results);
    }

    /// <inheritdoc />
    public async Task<string> GenerateRankChangeMessageAsync(string playerName, string oldRank, string newRank, bool isPromotion, bool isMajorChange)
    {
        var direction = isPromotion ? "PROMOTED" : "DEMOTED";
        var majorLabel = isMajorChange ? "MAJOR TIER CHANGE (crossed a full tier boundary, e.g. Silver to Gold)" : "Minor rank change (within same tier)";
        var prompt = $"""
            Player: {playerName}
            Rank change: {direction}
            Old rank: {oldRank}
            New rank: {newRank}
            Significance: {majorLabel}

            Generate a single Discord message reacting to this rank change.
            """;

        try
        {
            var systemPrompt = BuildSystemPrompt($"{BaseRules}\n{RankChangeRules}");
            var text = await CallClaudeAsync(systemPrompt, prompt, 600);

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated rank change message for {Player}: {Message}", playerName, text);
                messageHistory.AddMessage(text);
                return text;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate AI rank change message, using fallback");
        }

        var emoji = isPromotion ? "📈" : "📉";
        return $"{emoji} **{playerName}** went from **{oldRank}** to **{newRank}**. {(isPromotion ? "Let's go!" : "Yikes.")}";
    }

    private async Task<string?> CallClaudeAsync(string systemPrompt, string userPrompt, int maxTokens)
    {
        var parameters = new MessageParameters
        {
            Model = "claude-sonnet-4-6",
            MaxTokens = maxTokens,
            System = [new SystemMessage(systemPrompt)],
            Messages = [new Message(RoleType.User, userPrompt)]
        };

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await client.Messages.GetClaudeMessageAsync(parameters);
                return response.Content.FirstOrDefault()?.ToString()?.Trim();
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                logger.LogWarning("Claude API request failed (attempt {Attempt}/{MaxRetries}), retrying...", attempt, maxRetries);
                await Task.Delay(attempt * 1000);
            }
        }

        return null;
    }

    private string BuildSystemPrompt(string basePrompt)
    {
        var style = StyleModifiers[Rng.Next(StyleModifiers.Length)];
        var recentMessages = messageHistory.GetRecentMessages();

        var prompt = $"{basePrompt}\n\nStyle for this message: {style}";

        if (recentMessages.Count > 0)
        {
            var recentBlock = string.Join("\n---\n", recentMessages);
            prompt += $"""

                IMPORTANT — Here are your recent messages. You MUST vary your style, sentence structure, vocabulary, and opening words. Do NOT repeat phrases, patterns, or formats from these:

                {recentBlock}
                """;
        }

        return prompt;
    }

    private static string GetSquadFallbackMessage(List<PerformanceResult> results)
    {
        var first = results[0];
        var names = string.Join(", ", results.Select(r => $"**{r.MatchPlayer.Name}**"));
        var outcome = first.Won ? "won" : "lost";
        return $"👥 {names} stacked on {first.MapName} and {outcome} {first.Score}. Yikes.";
    }

    private static string FormatProfileForPrompt(PlayerProfile? profile)
    {
        if (profile is null)
            return "";

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Bio))
            lines.Add($"- Bio: \"{profile.Bio}\"");

        var allTraits = profile.ManualTraits.Concat(profile.AutoTraits).ToList();
        if (allTraits.Count > 0)
            lines.Add($"- Known traits: {string.Join(", ", allTraits)}");

        if (lines.Count == 0)
            return "";

        return $"\nPlayer Profile:\n{string.Join("\n", lines)}\n";
    }

    private static string FormatProfileLineForSquad(PlayerProfile? profile)
    {
        if (profile is null)
            return "";

        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile.Bio))
            parts.Add($"\"{profile.Bio}\"");

        var allTraits = profile.ManualTraits.Concat(profile.AutoTraits).ToList();
        if (allTraits.Count > 0)
            parts.Add(string.Join(", ", allTraits));

        if (parts.Count == 0)
            return "";

        return $"\n    Profile: {string.Join(" | ", parts)}";
    }

    private static string FormatWeaponContext(WeaponContext? ctx)
    {
        if (ctx is not { HasData: true }) return "";

        var commentary = ctx.LowHsExpected
            ? "Low HS% is expected — player used mostly shotguns/snipers/LMGs."
            : "Player used mostly rifles/pistols — HS% is a fair metric.";

        var mostUsed = ctx.MostUsedWeapon is not null ? $" Most used: {ctx.MostUsedWeapon}." : "";

        return $"\n            Weapon Context: {ctx.PrecisionKills}/{ctx.TotalWeaponKills} kills with precision weapons ({ctx.PrecisionKillPercent:F0}%).{mostUsed} {commentary}";
    }

    private static string GetFallbackMessage(PerformanceResult result)
    {
        var stats = result.MatchPlayer.Stats;
        var name = result.MatchPlayer.Name;

        return result.Rating switch
        {
            PerformanceRating.Terrible or PerformanceRating.Bad =>
                $"💀 **{name}** went {stats.Kills}/{stats.Deaths}/{stats.Assists} on {result.MapName}. Rough.",
            PerformanceRating.Excellent or PerformanceRating.Good =>
                $"🔥 **{name}** went {stats.Kills}/{stats.Deaths}/{stats.Assists} on {result.MapName}. Nice one!",
            _ =>
                $"😐 **{name}** went {stats.Kills}/{stats.Deaths}/{stats.Assists} on {result.MapName}."
        };
    }
}
