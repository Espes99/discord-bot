using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Orchestrates match lookup and performance analysis for a tracked player.
/// </summary>
public class MatchService(
    IHenrikDevClient henrikDev,
    IPerformanceAnalyzer performanceAnalyzer,
    ILogger<MatchService> logger) : IMatchService
{
    /// <inheritdoc />
    public async Task<PerformanceResult?> GetLatestPerformanceAsync(TrackedPlayer player, CancellationToken ct = default)
    {
        var playerKey = $"{player.Name}#{player.Tag}";

        var matches = await henrikDev.GetRecentMatchesAsync(player.Name, player.Tag, player.Region, ct);
        if (matches.Count == 0)
        {
            logger.LogDebug("No matches found for {Key}", playerKey);
            return null;
        }

        var latest = matches
            .Where(m => m.Metadata.IsCompleted)
            .OrderByDescending(m => m.Metadata.StartedAt)
            .FirstOrDefault();

        if (latest is null)
            return null;

        var matchId = latest.Metadata.MatchId;
        logger.LogInformation("Latest match for {Key}: {MatchId}", playerKey, matchId);

        var details = await henrikDev.GetMatchDetailsAsync(matchId, player.Region, ct);
        if (details is null)
        {
            logger.LogWarning("Could not fetch details for match {MatchId}", matchId);
            return null;
        }

        var matchPlayer = details.Players
            .FirstOrDefault(p =>
                p.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase) &&
                p.Tag.Equals(player.Tag, StringComparison.OrdinalIgnoreCase));

        if (matchPlayer is null)
        {
            logger.LogWarning("Player {Key} not found in match details", playerKey);
            return null;
        }

        var result = performanceAnalyzer.Analyze(player, matchPlayer, details);

        logger.LogInformation("{Key} performance: {Rating} — K/D/A: {K}/{D}/{A}, ACS: {Acs:F0}",
            playerKey, result.Rating,
            matchPlayer.Stats.Kills, matchPlayer.Stats.Deaths, matchPlayer.Stats.Assists,
            result.Acs);

        return result;
    }
}
