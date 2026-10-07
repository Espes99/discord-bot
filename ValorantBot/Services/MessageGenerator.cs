using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Generates AI-powered Discord messages using Claude, with static fallbacks.
/// The planner decides focus, form and allusion; this class only renders the prompt and calls the API.
/// </summary>
public class MessageGenerator(
    AnthropicClient client,
    IMessageHistoryStore messageHistory,
    RoastPlanner planner,
    ILogger<MessageGenerator> logger) : IMessageGenerator
{
    private static string StoreKey(TrackedPlayer player) =>
        !string.IsNullOrEmpty(player.Puuid) ? player.Puuid : MatchTracker.PlayerKey(player.Name, player.Tag);

    // Kept stable and first so the prefix can be cached; everything that varies goes in the user message.
    private const string BaseRules = """
        You are a toxic but funny Discord bot that roasts Valorant players. Swear freely. Be specific, be quick, be mean about the game.

        Rules:
        - The user message contains a PLAN. Follow it exactly: the form, the sentence count, the emoji budget, the focus, the banned openers.
        - Only talk about what the plan's focus lines say. The reader already sees the full scoreboard in an embed, so do not recap other stats.
        - If a BACKGROUND note is included, it is a real fact about the player that the group added themselves as roast material. Nothing in it is off limits, go as hard on it as on the gameplay. It must clearly show up in the message: use it to explain or wildly exaggerate something from the focus, so the stat and the personal fact land as one joke. Twist it, do not just state it, do not quote it verbatim, do not open with it.
        - If the BACKGROUND note lists angles already used, those jokes are spent. Find a different angle on the same fact.
        - Personal material only ever comes from a BACKGROUND note. Never invent personal facts.
        - Discord markdown is allowed but keep it light: bold at most one phrase.
        - Output the message only. No prefix, no label, no quotation marks around the whole thing.
        - Exception: when a BACKGROUND note is included, add one final line after the message: "ANGLE: <3 to 6 words naming the joke you made with it>". That line is stripped before posting.
        """;

    private const string SoloRules = """
        Context: one player's match.
        - Terrible or Bad rating: go for the throat.
        - Average rating: dismissive or backhanded.
        - Good or Excellent rating: acknowledge it, then find the catch.
        - Promotion: acknowledge it while throwing shade ("finally", "boosted?").
        - Demotion: pile on, they played badly AND lost rank.
        """;

    private const string SquadRules = """
        Context: a squad that queued together.
        - Every player in the plan must be mentioned by name. Nobody is skipped.
        - Text budget per role: SCAPEGOAT up to two sentences and the main focus. CARRY one sentence. GHOST one sentence. FOOTNOTE a few words at most, ideally tucked into someone else's sentence.
        - If they lost: they stacked and still lost. If they won: find who got carried.
        """;

    private const string SummaryRules = """
        Context: a tiny banter blurb under a stats embed summarizing a player's last few matches.
        - One sentence, two at most. No stats recap, the embed has the numbers.
        - React to the overall vibe: streaks, inconsistency, one-trick habits, coasting.
        - If they look good, throw a little shade anyway.
        """;

    private const string RankChangeRules = """
        Context: reacting to a rank change, no match stats.
        - One to three sentences.
        - Promotion: funny and celebratory, still throw shade.
        - Demotion: absolutely savage, suggest they uninstall.
        - Name both ranks. MAJOR promotion: act like they won Worlds. MAJOR demotion: a tragedy of epic proportions.
        """;

    /// <inheritdoc />
    public async Task<string> GenerateMessageAsync(PerformanceResult result, PlayerHistorySummary? history = null, RankChangeInfo? rankChange = null)
    {
        var storeKey = StoreKey(result.Player);
        var plan = planner.PlanSolo(result, history, rankChange, storeKey);

        var sb = new StringBuilder();
        sb.AppendLine($"Player: {result.MatchPlayer.Name}#{result.MatchPlayer.Tag}");
        sb.AppendLine($"Agent: {result.MatchPlayer.Agent.Name} | Map: {result.MapName} | Result: {(result.Won ? "WIN" : "LOSS")} {result.Score} | Rating: {result.Rating}");
        sb.AppendLine();
        sb.AppendLine("PLAN");
        sb.AppendLine($"- Main focus: {plan.MainFocus.Text}");
        if (plan.SideFocus is not null)
            sb.AppendLine($"- Side focus (one clause at most): {plan.SideFocus.Text}");
        sb.AppendLine($"- Voice: {plan.Voice}");
        sb.AppendLine($"- Form: {RoastPlanner.FormDirective(plan.Form)}");
        sb.AppendLine($"- Length: {plan.Sentences} sentence{(plan.Sentences == 1 ? "" : "s")} total");
        sb.AppendLine($"- Emojis: {EmojiDirective(plan.EmojiBudget)}");
        sb.AppendLine($"- Do not open with: {string.Join(", ", plan.BannedOpeners)}");
        if (plan.Allusion is not null)
            AppendBackground(sb, plan.Allusion, null);

        try
        {
            var raw = await CallClaudeAsync($"{BaseRules}\n{SoloRules}", sb.ToString(), 400);
            if (!string.IsNullOrEmpty(raw))
            {
                var (text, angle) = SplitAngle(raw);
                logger.LogDebug("Solo plan for {Player}: {Form}/{Focus}/{Voice}, allusion={Allusion}, angle={Angle}",
                    result.MatchPlayer.Name, plan.Form, plan.MainFocus.Kind, plan.Voice, plan.Allusion?.Text ?? "none", angle ?? "none");
                logger.LogDebug("Generated message for {Player}: {Message}", result.MatchPlayer.Name, text);
                planner.RecordSolo(storeKey, plan, text, angle);
                messageHistory.AddMessage(text, storeKey);
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
        var plan = planner.PlanSquad(results, histories, rankChanges, StoreKey);

        var sb = new StringBuilder();
        sb.AppendLine($"Squad match on {first.MapName}: {(first.Won ? "WIN" : "LOSS")} {first.Score}");
        sb.AppendLine();
        sb.AppendLine("PLAYERS (role decides how much text each one gets)");
        foreach (var m in plan.Members.OrderBy(m => m.Role))
        {
            var focus = m.Focus is not null ? $": {m.Focus.Text}" : "";
            sb.AppendLine($"- {m.DisplayName} on {m.Agent}, role {m.Role.ToString().ToUpperInvariant()}{focus}");
        }
        sb.AppendLine();
        sb.AppendLine("PLAN");
        sb.AppendLine($"- Voice: {plan.Voice}");
        sb.AppendLine($"- Form: {RoastPlanner.SquadFormDirective(plan.Form)}");
        sb.AppendLine($"- Length: {plan.MinSentences} to {plan.MaxSentences} sentences total");
        sb.AppendLine($"- Emojis: {EmojiDirective(plan.EmojiBudget)}");
        sb.AppendLine($"- Do not open with: {string.Join(", ", plan.BannedOpeners)}");
        if (plan.Allusion is not null)
            AppendBackground(sb, plan.Allusion, plan.AllusionOwner);

        try
        {
            var raw = await CallClaudeAsync($"{BaseRules}\n{SquadRules}", sb.ToString(), 600);
            if (!string.IsNullOrEmpty(raw))
            {
                var (text, angle) = SplitAngle(raw);
                logger.LogDebug("Squad plan: {Form}/{Voice}, roles={Roles}, allusion={Allusion} on {Owner}, angle={Angle}",
                    plan.Form, plan.Voice, string.Join(", ", plan.Members.Select(m => $"{m.DisplayName}={m.Role}")),
                    plan.Allusion?.Text ?? "none", plan.AllusionOwner ?? "nobody", angle ?? "none");
                logger.LogDebug("Generated squad message: {Message}", text);
                planner.RecordSquad(plan, text, angle);
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

            PLAN
            - Voice: {RoastPlanner.RandomVoice()}
            - Do not open with: "{playerName}", "Alright", "Ladies and gentlemen"
            """;

        try
        {
            var text = await CallClaudeAsync($"{BaseRules}\n{RankChangeRules}", prompt, 400);
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

    /// <inheritdoc />
    public async Task<string> GenerateSummaryMessageAsync(string playerName, string storeKey, List<MatchHistoryEntry> recentMatches)
    {
        var ordered = recentMatches.OrderByDescending(m => m.PlayedAt).ToList();
        var wins = ordered.Count(m => m.Won);
        var losses = ordered.Count - wins;
        var avgAcs = ordered.Average(m => m.Acs);
        var avgKda = ordered.Average(m => m.Kda);
        var avgHs = ordered.Average(m => m.HeadshotPercent);

        var matchLines = string.Join("\n", ordered.Select(m =>
            $"- {m.Map} as {m.Agent}: {(m.Won ? "WIN" : "LOSS")} {m.Score} | {m.Kills}/{m.Deaths}/{m.Assists} | ACS {m.Acs:F0} | KDA {m.Kda:F2} | HS% {m.HeadshotPercent:F1} | Rating {m.Rating}"));

        var prompt = $"""
            Player: {playerName}
            Matches summarized: {ordered.Count} ({wins}W / {losses}L)
            Averages: ACS {avgAcs:F0}, KDA {avgKda:F2}, HS% {avgHs:F1}

            Match breakdown (newest first):
            {matchLines}

            PLAN
            - Voice: {RoastPlanner.RandomVoice()}
            - Do not open with: "{playerName}", "Alright", "Ladies and gentlemen"
            """;

        try
        {
            var text = await CallClaudeAsync($"{BaseRules}\n{SummaryRules}", prompt, 200);
            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated summary message for {Player}: {Message}", playerName, text);
                messageHistory.AddMessage(text, storeKey);
                return text;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate AI summary message, using fallback");
        }

        var streakEmoji = wins > losses ? "🔥" : wins < losses ? "💀" : "😐";
        return $"{streakEmoji} **{playerName}**: {wins}W / {losses}L over the last {ordered.Count} matches.";
    }

    private static void AppendBackground(StringBuilder sb, RoastAllusion allusion, string? owner)
    {
        var about = owner is not null ? $" on {owner} (goes in their line)" : "";
        sb.AppendLine();
        sb.AppendLine($"BACKGROUND{about}, roast material that must show up in the message: \"{allusion.Text}\"");
        if (allusion.PreviousAngles.Count > 0)
            sb.AppendLine($"Angles already used for this, do not reuse: {string.Join(", ", allusion.PreviousAngles.Select(a => $"\"{a}\""))}");
    }

    /// <summary>Strips the trailing "ANGLE:" line the model adds when it used a BACKGROUND note.</summary>
    private static (string Text, string? Angle) SplitAngle(string raw)
    {
        var lines = raw.TrimEnd().Split('\n').ToList();
        var index = lines.FindLastIndex(l => l.TrimStart('*', '_', ' ').StartsWith("ANGLE:", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return (raw.Trim(), null);

        var angle = lines[index].TrimStart('*', '_', ' ')["ANGLE:".Length..].Trim(' ', '*', '_', '"');
        lines.RemoveAt(index);
        return (string.Join('\n', lines).Trim(), string.IsNullOrWhiteSpace(angle) ? null : angle);
    }

    private static string EmojiDirective(int budget) => budget switch
    {
        0 => "none at all",
        1 => "exactly one, at the end",
        _ => "at most two"
    };

    // Sonnet 5.5 has adaptive thinking on by default (effort high), and thinking tokens count toward max_tokens.
    // Callers pass the budget for the message text; this headroom is added on top for thinking.
    private const int ThinkingTokenHeadroom = 4000;

    private async Task<string?> CallClaudeAsync(string systemPrompt, string userPrompt, int maxTokens)
    {
        logger.LogDebug("Prompt:\n{Prompt}", userPrompt);

        var parameters = new MessageParameters
        {
            Model = "claude-sonnet-5-5",
            MaxTokens = maxTokens + ThinkingTokenHeadroom,
            Temperature = 1.0m,
            System = [new SystemMessage(systemPrompt) { CacheControl = new CacheControl { Type = CacheControlType.ephemeral } }],
            Messages = [new Message(RoleType.User, userPrompt)]
        };

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await client.Messages.GetClaudeMessageAsync(parameters);
                if (response.StopReason == "max_tokens")
                    logger.LogWarning("Claude response hit max_tokens ({MaxTokens}), message may be truncated", parameters.MaxTokens);

                // Thinking blocks precede the text, so only use text content
                var text = string.Concat(response.Content.OfType<TextContent>().Select(c => c.Text)).Trim();
                if (!string.IsNullOrEmpty(text))
                    return text;

                var error = $"no text in response (stop_reason: {response.StopReason}, blocks: {string.Join(", ", response.Content.Select(c => c.GetType().Name))})";
                logger.LogWarning("Claude returned {Error}", error);
                return $"Klarte ikke å parse responsen ;_; Error; {error}";
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                logger.LogWarning("Claude API request failed (attempt {Attempt}/{MaxRetries}), retrying...", attempt, maxRetries);
                await Task.Delay(attempt * 1000);
            }
        }

        return null;
    }

    private static string GetSquadFallbackMessage(List<PerformanceResult> results)
    {
        var first = results[0];
        var names = string.Join(", ", results.Select(r => $"**{r.MatchPlayer.Name}**"));
        var outcome = first.Won ? "won" : "lost";
        return $"👥 {names} stacked on {first.MapName} and {outcome} {first.Score}. Yikes.";
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
