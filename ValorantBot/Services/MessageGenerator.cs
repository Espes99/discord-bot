using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Generates AI-powered Discord messages using Claude Haiku, with static fallbacks.
/// </summary>
public class MessageGenerator(AnthropicClient client, ILogger<MessageGenerator> logger) : IMessageGenerator
{
    private const string SystemPrompt = """
        You are a toxic but funny Discord bot that roasts Valorant players based on their match stats. Important to be creative with insults and use swear words and foul language to really drive the point home.

        Rules:
        - Keep messages short (1-3 sentences max, or up to 4 if there's a rank change to address)
        - Use Discord markdown (**bold**, etc.) and emojis
        - Be savage when they play badly — really go for it
        - Reference specific stats (K/D/A, Combat Score, HS%, agent, map) to make the roast personal
        - For terrible/bad performance: be toxic and funny, mock them relentlessly
        - For average performance: be dismissive or backhanded
        - If a RANK CHANGE is included, weave it naturally into the match roast. Don't treat it as a separate topic.
          - For promotions: throw shade ("finally", "boosted?") while acknowledging it
          - For demotions: pile on extra, they played badly AND lost rank
          - For MAJOR tier changes (e.g. Silver to Gold, Plat to Diamond): make it dramatic and over the top
          - For minor rank changes (within same tier): a quick mention is enough
        - If player history is provided, reference trends to make roasts more personal (streaks, declining stats, map weaknesses, etc.)
        - If weapon context is provided, factor it into your HS% commentary. Don't mock low HS% if the player mainly used shotguns, snipers, or LMGs — that's expected. Mock them for weapon choice instead if anything.
        - Never be mean-spirited about real personal things — keep it about the game
        - Do NOT use any prefix or label. Just output the message directly.
        """;

    private const string SquadSystemPrompt = """
        You are a toxic but funny Discord bot that roasts a squad of Valorant players who queued together. Important to be creative with insults and use swear words and foul language to really drive the point home.

        Rules:
        - Write a medium-length message (3-6 sentences) roasting the entire squad
        - Call out individual players by name — blame the worst performer, mock the carried players, etc.
        - Compare players against each other (e.g. "while X was busy dying, Y was actually trying")
        - Use Discord markdown (**bold**, etc.) and emojis
        - Mock the fact that they queued together and still played like this
        - Reference specific stats (K/D/A, Combat Score, HS%, agent) to make roasts personal
        - If they lost, make it extra savage — they stacked and STILL lost
        - If they won, find the weak link who got carried
        - If RANK CHANGES are included for any players, weave them naturally into the squad roast. Don't list them separately.
          - For MAJOR tier changes: make it dramatic. Call out the player by name.
          - For minor changes: a quick mention is enough
        - If player history is provided, reference trends to make roasts more personal (streaks, declining stats, etc.)
        - If weapon context is provided, factor it into your HS% commentary. Don't mock low HS% if the player mainly used shotguns, snipers, or LMGs — that's expected.
        - Never be mean-spirited about real personal things — keep it about the game
        - Do NOT use any prefix or label. Just output the message directly.
        """;

    private const string RankChangeSystemPrompt = """
        You are a toxic but funny Discord bot that reacts to Valorant rank changes. Important to be creative with insults and use swear words and foul language to really drive the point home.

        Rules:
        - Keep messages short (1-3 sentences max)
        - Use Discord markdown (**bold**, etc.) and emojis
        - For promotions: be funny and celebratory, but still throw in some shade (e.g. "finally", "took you long enough", "boosted?")
        - For demotions: be absolutely savage. Mock them, question their life choices, suggest they uninstall
        - Reference the specific ranks involved (old rank and new rank)
        - For MAJOR tier changes (crossing a full tier boundary like Silver to Gold, Plat to Diamond): go all out. Make it dramatic and over the top. For promotions, act like they just won Worlds. For demotions, treat it like a tragedy of epic proportions.
        - For minor rank changes (within the same tier, like Gold 1 to Gold 2): keep it chill, a quick quip is enough
        - Never be mean-spirited about real personal things — keep it about the game
        - Do NOT use any prefix or label. Just output the message directly.
        """;

    /// <inheritdoc />
    public async Task<string> GenerateMessageAsync(PerformanceResult result, PlayerHistorySummary? history = null, RankChangeInfo? rankChange = null)
    {
        var stats = result.MatchPlayer.Stats;
        var historyBlock = history is not null ? $"\n{HistorySummarizer.FormatForPrompt(history)}\n" : "";
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
            {historyBlock}{rankBlock}
            Generate a single Discord message for this player's performance.
            """;

        try
        {
            var parameters = new MessageParameters
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 300,
                System = [new SystemMessage(SystemPrompt)],
                Messages = [new Message(RoleType.User, prompt)]
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters);
            var text = response.Content.FirstOrDefault()?.ToString()?.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated message for {Player}: {Message}", result.MatchPlayer.Name, text);
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
            var key = $"{r.MatchPlayer.Name}#{r.MatchPlayer.Tag}";
            var historyLine = histories is not null && histories.TryGetValue(key, out var h)
                ? $"\n    History: WR {h.WinRate:F0}%, Avg ACS {h.AverageAcs:F0}, Avg KDA {h.AverageKda:F2}, {(h.CurrentLossStreak > 1 ? $"{h.CurrentLossStreak} loss streak" : h.CurrentWinStreak > 1 ? $"{h.CurrentWinStreak} win streak" : "no streak")}"
                : "";
            var playerKey = MatchTracker.PlayerKey(r.Player.Name, r.Player.Tag);
            var rankLine = rankChanges is not null && rankChanges.TryGetValue(playerKey, out var rc)
                ? $"\n    RANK CHANGE: {(rc.IsPromotion ? "PROMOTED" : "DEMOTED")} from {rc.OldRank} to {rc.NewRank} ({(rc.IsMajor ? "MAJOR tier change" : "minor change")})"
                : "";
            var weaponLine = FormatWeaponContext(r.WeaponContext);
            return $"""
                - {key} | Agent: {r.MatchPlayer.Agent.Name} | K/D/A: {s.Kills}/{s.Deaths}/{s.Assists} | ACS: {r.Acs:F0} | KDA: {s.Kda:F2} | HS%: {s.HeadshotPercentage:F1}%{weaponLine} | Rating: {r.Rating}{historyLine}{rankLine}
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
            var parameters = new MessageParameters
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 500,
                System = [new SystemMessage(SquadSystemPrompt)],
                Messages = [new Message(RoleType.User, prompt)]
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters);
            var text = response.Content.FirstOrDefault()?.ToString()?.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated squad message: {Message}", text);
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
            var parameters = new MessageParameters
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 300,
                System = [new SystemMessage(RankChangeSystemPrompt)],
                Messages = [new Message(RoleType.User, prompt)]
            };

            var response = await client.Messages.GetClaudeMessageAsync(parameters);
            var text = response.Content.FirstOrDefault()?.ToString()?.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                logger.LogDebug("Generated rank change message for {Player}: {Message}", playerName, text);
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

    private static string GetSquadFallbackMessage(List<PerformanceResult> results)
    {
        var first = results[0];
        var names = string.Join(", ", results.Select(r => $"**{r.MatchPlayer.Name}**"));
        var outcome = first.Won ? "won" : "lost";
        return $"👥 {names} stacked on {first.MapName} and {outcome} {first.Score}. Yikes.";
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
