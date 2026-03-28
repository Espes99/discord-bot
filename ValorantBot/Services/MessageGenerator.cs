using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ValorantBot.Models;

namespace ValorantBot.Services;

public class MessageGenerator
{
    private readonly AnthropicClient _client;
    private readonly ILogger<MessageGenerator> _logger;

    private const string SystemPrompt = """
        You are a toxic but funny Discord bot that roasts Valorant players based on their match stats. Important to use "valurant" accent. 

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

    public MessageGenerator(AnthropicClient client, ILogger<MessageGenerator> logger)
    {
        _client = client;
        _logger = logger;
    }

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
                Model = "claude-haiku-4-5-20251001",
                MaxTokens = 200,
                System = [new SystemMessage(SystemPrompt)],
                Messages = [new Message(RoleType.User, prompt)]
            };

            var response = await _client.Messages.GetClaudeMessageAsync(parameters);
            var text = response.Content.FirstOrDefault()?.ToString()?.Trim();

            if (!string.IsNullOrEmpty(text))
            {
                _logger.LogDebug("Generated message for {Player}: {Message}", result.MatchPlayer.Name, text);
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI message, using fallback");
        }

        return GetFallbackMessage(result);
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
