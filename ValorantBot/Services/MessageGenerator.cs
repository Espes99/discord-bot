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
        - Keep messages short (1-3 sentences max)
        - Use Discord markdown (**bold**, etc.) and emojis
        - Be savage when they play badly — really go for it
        - Reference specific stats (K/D/A, Combat Score, HS%, agent, map) to make the roast personal
        - For terrible/bad performance: be toxic and funny, mock them relentlessly
        - For average performance: be dismissive or backhanded
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
        - Never be mean-spirited about real personal things — keep it about the game
        - Do NOT use any prefix or label. Just output the message directly.
        """;

    /// <inheritdoc />
    public async Task<string> GenerateMessageAsync(PerformanceResult result)
    {
        var stats = result.MatchPlayer.Stats;
        var prompt = $"""
            Player: {result.MatchPlayer.Name}#{result.MatchPlayer.Tag}
            Agent: {result.MatchPlayer.Agent.Name}
            Map: {result.MapName}
            Result: {(result.Won ? "WIN" : "LOSS")} ({result.Score})
            K/D/A: {stats.Kills}/{stats.Deaths}/{stats.Assists}
            Combat Score: {result.Acs:F0}
            KDA Ratio: {stats.Kda:F2}
            Headshot %: {stats.HeadshotPercentage:F1}%
            Performance Rating: {result.Rating}

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
    public async Task<string> GenerateSquadMessageAsync(List<PerformanceResult> results)
    {
        var first = results[0];
        var playerStats = string.Join("\n", results.Select(r =>
        {
            var s = r.MatchPlayer.Stats;
            return $"""
                - {r.MatchPlayer.Name}#{r.MatchPlayer.Tag} | Agent: {r.MatchPlayer.Agent.Name} | K/D/A: {s.Kills}/{s.Deaths}/{s.Assists} | ACS: {r.Acs:F0} | KDA: {s.Kda:F2} | HS%: {s.HeadshotPercentage:F1}% | Rating: {r.Rating}
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
