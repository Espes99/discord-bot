using System.Net;
using System.Text.Json;
using ValorantBot.Models;

namespace ValorantBot.Services;

public class HenrikDevClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HenrikDevClient> _logger;

    public HenrikDevClient(HttpClient httpClient, ILogger<HenrikDevClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<MatchListEntry>> GetRecentMatchesAsync(
        string name, string tag, string region, CancellationToken ct = default)
    {
        var url = $"v4/matches/{region}/pc/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}?size=5";
        _logger.LogDebug("Fetching match list: {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Player {Name}#{Tag} not found", name, tag);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Match list response ({StatusCode}): {Body}", response.StatusCode, body);

        var result = JsonSerializer.Deserialize<MatchListResponse>(body);
        _logger.LogDebug("Deserialized {Count} match(es) for {Name}#{Tag}",
            result?.Data?.Count ?? 0, name, tag);

        return result?.Data ?? [];
    }

    public async Task<MatchDetailData?> GetMatchDetailsAsync(
        string matchId, string region, CancellationToken ct = default)
    {
        var url = $"v4/match/{region}/{matchId}";
        _logger.LogDebug("Fetching match details: {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Match {MatchId} not found", matchId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Match detail response ({StatusCode}): {Body}", response.StatusCode, body);

        var result = JsonSerializer.Deserialize<MatchDetailResponse>(body);
        _logger.LogDebug("Deserialized match {MatchId}: {PlayerCount} players, {TeamCount} teams",
            matchId, result?.Data?.Players?.Count ?? 0, result?.Data?.Teams?.Count ?? 0);

        return result?.Data;
    }
}
