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
        var displayKey = MatchTracker.PlayerKey(player.Name, player.Tag);

        // Prefer puuid-based match list if available (survives name changes)
        var matches = !string.IsNullOrEmpty(player.Puuid)
            ? await henrikDev.GetRecentMatchesByPuuidAsync(player.Puuid, player.Region, ct)
            : await henrikDev.GetRecentMatchesAsync(player.Name, player.Tag, player.Region, ct);

        if (matches.Count == 0)
        {
            logger.LogDebug("No matches found for {Key}", displayKey);
            return null;
        }

        var latest = matches
            .Where(m => m.Metadata.IsCompleted
                && m.Metadata.Queue.Name.Equals("Competitive", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Metadata.StartedAt)
            .FirstOrDefault();

        if (latest is null)
            return null;

        var matchId = latest.Metadata.MatchId;
        logger.LogInformation("Latest match for {Key}: {MatchId}", displayKey, matchId);

        // Pace requests to avoid HenrikDev API rate limits
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var details = await henrikDev.GetMatchDetailsAsync(matchId, player.Region, ct);
        if (details is null)
        {
            logger.LogWarning("Could not fetch details for match {MatchId}", matchId);
            return null;
        }

        // Match player by puuid first (stable), fall back to name+tag
        MatchPlayer? matchPlayer = null;
        if (!string.IsNullOrEmpty(player.Puuid))
        {
            matchPlayer = details.Players
                .FirstOrDefault(p => string.Equals(p.Puuid, player.Puuid, StringComparison.OrdinalIgnoreCase));
        }

        matchPlayer ??= details.Players
            .FirstOrDefault(p =>
                p.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase) &&
                p.Tag.Equals(player.Tag, StringComparison.OrdinalIgnoreCase));

        if (matchPlayer is null)
        {
            logger.LogWarning("Player {Key} not found in match details", displayKey);
            return null;
        }

        if (string.IsNullOrWhiteSpace(matchPlayer.Name) && !string.IsNullOrWhiteSpace(player.Name))
            matchPlayer.Name = player.Name;
        if (string.IsNullOrWhiteSpace(matchPlayer.Tag) && !string.IsNullOrWhiteSpace(player.Tag))
            matchPlayer.Tag = player.Tag;

        var result = performanceAnalyzer.Analyze(player, matchPlayer, details);

        logger.LogInformation("{Key} performance: {Rating} -- K/D/A: {K}/{D}/{A}, ACS: {Acs:F0}",
            displayKey, result.Rating,
            matchPlayer.Stats.Kills, matchPlayer.Stats.Deaths, matchPlayer.Stats.Assists,
            result.Acs);

        return result;
    }
}
