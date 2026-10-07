using System.Text;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Generates AI-powered Discord messages using Claude, with static fallbacks.
/// The planner gathers the material (stories, profile allusion, roles, recently used choices);
/// the model picks story, tone and form and reports its choices as JSON so they can rotate.
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
        You are a toxic but funny Discord bot that roasts Valorant players. Swear freely. Be specific, be quick, be mean.

        How it works:
        - The user message gives you MATERIAL and LIMITS. You choose which story to tell, the tone and the form. Pick whatever is funniest for THIS match, not whatever is safest. The tone ideas and forms listed are suggestions; invent your own when it fits better.
        - LIMITS are hard: sentence count, emojis, banned openers.
        - Everything under RECENTLY USED is spent: choose a different story, tone and form. Do not drift back to the same few favourite tones; surprise the reader.
        - Build the message around the story you picked, with at most one other story as a side note. The reader already sees the full scoreboard in an embed, so do not recap other stats.
        - If a BACKGROUND note is included, it is a real fact about the player that the group added themselves as roast material. Nothing in it is off limits, go as hard on it as on the gameplay. It must clearly show up in the message: use it to explain or wildly exaggerate something from the story, so the stat and the personal fact land as one joke. Twist it, do not just state it, do not quote it verbatim, do not open with it.
        - If the BACKGROUND note lists angles already used, those jokes are spent. Find a different angle on the same fact.
        - Personal material only ever comes from a BACKGROUND note. Never invent personal facts.
        - Discord markdown is allowed but keep it light: bold at most one phrase.

        Output:
        - When a JSON schema is requested: "story" is the [key] of the main story you picked, "tone" names your voice in a few words, "form" names the shape in a few words, "angle" names the joke you made with the BACKGROUND note in 3 to 6 words ("" if there was none), and "message" is exactly what gets posted.
        - Otherwise output the message only.
        - Either way the message has no prefix, no label and no quotation marks around the whole thing.
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
        - Every player listed must be mentioned by name. Nobody is skipped.
        - Text budget per role: SCAPEGOAT up to two sentences and the main story. CARRY one sentence. GHOST one sentence. FOOTNOTE a few words at most, ideally tucked into someone else's sentence.
        - Each player's stories are the material for their part; pick one per player.
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

    private static readonly JsonElement ChoiceSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "story": { "type": "string" },
            "tone": { "type": "string" },
            "form": { "type": "string" },
            "angle": { "type": "string" },
            "message": { "type": "string" }
          },
          "required": ["story", "tone", "form", "angle", "message"],
          "additionalProperties": false
        }
        """).RootElement;

    /// <inheritdoc />
    public async Task<string> GenerateMessageAsync(PerformanceResult result, PlayerHistorySummary? history = null, RankChangeInfo? rankChange = null)
    {
        var storeKey = StoreKey(result.Player);
        var plan = planner.PlanSolo(result, history, rankChange, storeKey);

        var sb = new StringBuilder();
        sb.AppendLine($"Player: {result.MatchPlayer.Name}#{result.MatchPlayer.Tag}");
        sb.AppendLine($"Agent: {result.MatchPlayer.Agent.Name} | Map: {result.MapName} | Result: {(result.Won ? "WIN" : "LOSS")} {result.Score} | Rating: {result.Rating}");
        sb.AppendLine();
        sb.AppendLine("MATERIAL");
        sb.AppendLine("Stories that are true for this match, strongest first. Pick one as the main story:");
        foreach (var story in plan.Stories)
            sb.AppendLine($"- [{story.Kind}] {story.Text}");
        if (plan.RequiredStory is not null)
            sb.AppendLine($"The message must cover [{plan.RequiredStory.Kind}].");
        AppendToneAndForm(sb, plan.ToneIdeas, RoastPlanner.FormMenu);
        if (plan.Allusion is not null)
            AppendBackground(sb, plan.Allusion, null);
        sb.AppendLine();
        AppendRecent(sb, plan.Recent);
        sb.AppendLine();
        sb.AppendLine("LIMITS");
        sb.AppendLine($"- Length: {SentenceRange(plan.MinSentences, plan.MaxSentences)}");
        sb.AppendLine("- Emojis: 0 to 2, your call");
        sb.AppendLine($"- Do not open with: {string.Join(", ", plan.Recent.BannedOpeners)}");

        try
        {
            var (choice, error) = await CallClaudeForChoiceAsync($"{BaseRules}\n{SoloRules}", sb.ToString(), 400);
            if (error is not null)
                return ParseErrorMessage(error);
            if (choice is not null)
            {
                logger.LogDebug("Solo roast for {Player}: story={Story}, tone={Tone}, form={Form}, allusion={Allusion}, angle={Angle}",
                    result.MatchPlayer.Name, choice.Story, choice.Tone, choice.Form, plan.Allusion?.Text ?? "none", choice.Angle);
                logger.LogDebug("Generated message for {Player}: {Message}", result.MatchPlayer.Name, choice.Message);
                planner.RecordSolo(storeKey, plan, choice);
                messageHistory.AddMessage(choice.Message, storeKey);
                return choice.Message;
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
        sb.AppendLine("MATERIAL");
        sb.AppendLine("Players (role decides how much text each one gets) and the stories that are true for each, strongest first:");
        foreach (var m in plan.Members.OrderBy(m => m.Role))
        {
            sb.AppendLine($"- {m.DisplayName} on {m.Agent}, role {m.Role.ToString().ToUpperInvariant()}");
            foreach (var story in m.Stories)
                sb.AppendLine($"    - [{story.Kind}] {story.Text}");
        }
        AppendToneAndForm(sb, plan.ToneIdeas, RoastPlanner.SquadFormMenu);
        if (plan.Allusion is not null)
            AppendBackground(sb, plan.Allusion, plan.AllusionOwner);
        sb.AppendLine();
        AppendRecent(sb, plan.Recent);
        sb.AppendLine();
        sb.AppendLine("LIMITS");
        sb.AppendLine($"- Length: {SentenceRange(plan.MinSentences, plan.MaxSentences)}");
        sb.AppendLine("- Emojis: 0 to 2, your call");
        sb.AppendLine($"- Do not open with: {string.Join(", ", plan.Recent.BannedOpeners)}");

        try
        {
            var (choice, error) = await CallClaudeForChoiceAsync($"{BaseRules}\n{SquadRules}", sb.ToString(), 600);
            if (error is not null)
                return ParseErrorMessage(error);
            if (choice is not null)
            {
                logger.LogDebug("Squad roast: tone={Tone}, form={Form}, roles={Roles}, allusion={Allusion} on {Owner}, angle={Angle}",
                    choice.Tone, choice.Form, string.Join(", ", plan.Members.Select(m => $"{m.DisplayName}={m.Role}")),
                    plan.Allusion?.Text ?? "none", plan.AllusionOwner ?? "nobody", choice.Angle);
                logger.LogDebug("Generated squad message: {Message}", choice.Message);
                planner.RecordSquad(plan, choice);
                messageHistory.AddMessage(choice.Message);
                return choice.Message;
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
            - Voice: your call, pick what fits. Ideas: {string.Join("; ", RoastPlanner.ToneIdeas(4))}
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
            - Voice: your call, pick what fits. Ideas: {string.Join("; ", RoastPlanner.ToneIdeas(4))}
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

    private static void AppendToneAndForm(StringBuilder sb, List<string> toneIdeas, IEnumerable<string> forms)
    {
        sb.AppendLine();
        sb.AppendLine($"Tone ideas (or invent your own): {string.Join("; ", toneIdeas)}");
        sb.AppendLine("Forms (or invent your own):");
        foreach (var form in forms)
            sb.AppendLine($"- {form}");
    }

    private static void AppendRecent(StringBuilder sb, RecentChoices recent)
    {
        sb.AppendLine("RECENTLY USED (spent, choose something else)");
        sb.AppendLine($"- Stories: {JoinOrNone(recent.Stories)}");
        sb.AppendLine($"- Tones: {JoinOrNone(recent.Tones)}");
        sb.AppendLine($"- Forms: {JoinOrNone(recent.Forms)}");
    }

    private static string JoinOrNone(List<string> items) => items.Count == 0 ? "none" : string.Join("; ", items);

    private static string SentenceRange(int min, int max) =>
        min == max ? $"exactly {min} sentence{(min == 1 ? "" : "s")}" : $"{min} to {max} sentences total";

    private static string ParseErrorMessage(string error) => $"Klarte ikke å parse responsen ;_; Error; {error}";

    // Sonnet 5.5 has adaptive thinking on by default (effort high), and thinking tokens count toward max_tokens.
    // Callers pass the budget for the message text; this headroom is added on top for thinking.
    private const int ThinkingTokenHeadroom = 4000;

    /// <summary>Plain-text call; an empty response comes back as the parse error message.</summary>
    private async Task<string?> CallClaudeAsync(string systemPrompt, string userPrompt, int maxTokens)
    {
        var (text, error) = await SendAsync(systemPrompt, userPrompt, maxTokens, null);
        return error is not null ? ParseErrorMessage(error) : text;
    }

    /// <summary>Structured call: the model reports its story, tone, form and angle alongside the message.</summary>
    private async Task<(RoastChoice? Choice, string? Error)> CallClaudeForChoiceAsync(string systemPrompt, string userPrompt, int maxTokens)
    {
        var (text, error) = await SendAsync(systemPrompt, userPrompt, maxTokens, ChoiceSchema);
        if (error is not null || text is null)
            return (null, error);

        try
        {
            var choice = JsonSerializer.Deserialize<RoastChoice>(text);
            if (!string.IsNullOrWhiteSpace(choice?.Message))
            {
                choice.Message = choice.Message.Trim();
                return (choice, null);
            }
            error = "no message in JSON response";
        }
        catch (JsonException ex)
        {
            error = $"invalid JSON response ({ex.Message})";
        }

        logger.LogWarning("Claude returned {Error}: {Text}", error, text);
        return (null, error);
    }

    private async Task<(string? Text, string? Error)> SendAsync(string systemPrompt, string userPrompt, int maxTokens, JsonElement? schema)
    {
        logger.LogDebug("Prompt:\n{Prompt}", userPrompt);

        var parameters = new MessageParameters
        {
            Model = "claude-sonnet-5-5",
            MaxTokens = maxTokens + ThinkingTokenHeadroom,
            Temperature = 1.0m,
            System = [new SystemMessage(systemPrompt) { CacheControl = new CacheControl { Type = CacheControlType.ephemeral } }],
            Messages = [new Message(RoleType.User, userPrompt)],
            OutputConfig = schema is { } s ? new OutputConfig { OutputFormat = new OutputFormat { Type = "json_schema", Schema = s } } : null
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
                    return (text, null);

                var error = $"no text in response (stop_reason: {response.StopReason}, blocks: {string.Join(", ", response.Content.Select(c => c.GetType().Name))})";
                logger.LogWarning("Claude returned {Error}", error);
                return (null, error);
            }
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                logger.LogWarning("Claude API request failed (attempt {Attempt}/{MaxRetries}), retrying...", attempt, maxRetries);
                await Task.Delay(attempt * 1000);
            }
        }

        return (null, null);
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
