using System.Net;
using System.Text.Json;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Typed HTTP client for the HenrikDev Valorant API v4.
/// </summary>
public class HenrikDevClient(HttpClient httpClient, ILogger<HenrikDevClient> logger) : IHenrikDevClient
{
    /// <inheritdoc />
    public async Task<List<MatchListEntry>> GetRecentMatchesAsync(
        string name, string tag, string region, CancellationToken ct = default)
    {
        var url = $"v4/matches/{region}/pc/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}?size=5";
        logger.LogDebug("Fetching match list: {Url}", url);

        var response = await httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Player {Name}#{Tag} not found", name, tag);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Match list response ({StatusCode}): {Body}", response.StatusCode, body);

        var result = JsonSerializer.Deserialize<MatchListResponse>(body);
        logger.LogDebug("Deserialized {Count} match(es) for {Name}#{Tag}",
            result?.Data?.Count ?? 0, name, tag);

        return result?.Data ?? [];
    }

    /// <inheritdoc />
    public async Task<MatchDetailData?> GetMatchDetailsAsync(
        string matchId, string region, CancellationToken ct = default)
    {
        var url = $"v4/match/{region}/{matchId}";
        logger.LogDebug("Fetching match details: {Url}", url);

        var response = await httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Match {MatchId} not found", matchId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogDebug("Match detail response ({StatusCode}): {Body}", response.StatusCode, body);

        var result = JsonSerializer.Deserialize<MatchDetailResponse>(body);
        logger.LogDebug("Deserialized match {MatchId}: {PlayerCount} players, {TeamCount} teams",
            matchId, result?.Data?.Players?.Count ?? 0, result?.Data?.Teams?.Count ?? 0);

        return result?.Data;
    }
}
