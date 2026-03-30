using ValorantBot.Models;

namespace ValorantBot.Messages;

public static class MessageTemplates
{
    private static readonly string[] TerribleMessages =
    [
        "💀 **{name}** went {kills}/{deaths}/{assists} on {map}. Genuinely concerning.",
        "📉 **{name}** dropped {acs} ACS. The spike had more impact than you.",
        "🪦 **{name}** just gave the enemy team a free win. {kills}/{deaths}/{assists}. RIP.",
        "🤡 **{name}** played {agent} and got {kills} kills. My nan could do better blindfolded.",
        "🚨 Performance alert: **{name}** is officially boosted. {acs} ACS on {map}.",
        "😭 **{name}** went {kills}/{deaths}/{assists}. Please uninstall or at least switch to spike rush."
    ];

    private static readonly string[] BadMessages =
    [
        "😬 **{name}** had a rough one — {kills}/{deaths}/{assists} on {map}.",
        "👎 **{name}** with {acs} ACS. Not your best work.",
        "🥴 **{name}** went {kills}/{deaths}/{assists}. Let's just move on.",
        "📉 **{name}** played {agent} and... yeah. {kills}/{deaths}/{assists}."
    ];

    private static readonly string[] AverageMessages =
    [
        "😐 **{name}** went {kills}/{deaths}/{assists} on {map}. Perfectly mid.",
        "🤷 **{name}** did... fine? {acs} ACS. Not great, not terrible.",
        "⚖️ **{name}** played an average game on {map}. {kills}/{deaths}/{assists}."
    ];

    private static readonly string[] GoodMessages =
    [
        "💪 **{name}** went {kills}/{deaths}/{assists} on {map}. Solid game!",
        "🔥 **{name}** dropped {acs} ACS on {agent}. Nice one!",
        "👏 **{name}** had a great game — {kills}/{deaths}/{assists}. Keep it up!",
        "⭐ **{name}** popping off with {hs}% headshots on {map}!"
    ];

    private static readonly string[] ExcellentMessages =
    [
        "🐐 **{name}** just dropped {kills} kills with {hs}% HS rate. Absolutely unhinged.",
        "👑 **{name}** went {kills}/{deaths}/{assists} with {acs} ACS. Carry harder please.",
        "🏆 **{name}** played {agent} and completely dominated. {kills}/{deaths}/{assists}.",
        "💎 **{name}** is GAMING. {acs} ACS and {hs}% headshots on {map}. Radiant loading...",
        "🔱 **{name}** said GG before the game even started. {kills}/{deaths}/{assists}. Unstoppable."
    ];

    public static string GetMessage(PerformanceResult result)
    {
        var templates = result.Rating switch
        {
            PerformanceRating.Terrible => TerribleMessages,
            PerformanceRating.Bad => BadMessages,
            PerformanceRating.Average => AverageMessages,
            PerformanceRating.Good => GoodMessages,
            PerformanceRating.Excellent => ExcellentMessages,
            _ => AverageMessages
        };

        var template = templates[Random.Shared.Next(templates.Length)];
        return FormatMessage(template, result);
    }

    private static string FormatMessage(string template, PerformanceResult result)
    {
        var stats = result.MatchPlayer.Stats;

        return template
            .Replace("{name}", result.MatchPlayer.Name)
            .Replace("{kills}", stats.Kills.ToString())
            .Replace("{deaths}", stats.Deaths.ToString())
            .Replace("{assists}", stats.Assists.ToString())
            .Replace("{acs}", $"{result.Acs:F0}")
            .Replace("{hs}", $"{stats.HeadshotPercentage:F1}")
            .Replace("{map}", result.MapName)
            .Replace("{agent}", result.MatchPlayer.Agent.Name);
    }
}
