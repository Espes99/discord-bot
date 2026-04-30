using System.Net;
using System.Text.Json;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Typed HTTP client for the HenrikDev Valorant API v4.
/// </summary>
public class HenrikDevClient(HttpClient httpClient, ILogger<HenrikDevClient> logger) : IHenrikDevClient
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<List<MatchListEntry>> GetRecentMatchesAsync(
        string name, string tag, string region, CancellationToken ct = default)
    {
        var url = $"v4/matches/{region}/pc/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}?size=5&mode=competitive";
        logger.LogDebug("Fetching match list: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return [];

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Player {Name}#{Tag} not found", name, tag);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
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

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return null;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Match {MatchId} not found", matchId);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MatchDetailResponse>(body);
        logger.LogInformation("Deserialized match {MatchId}: {PlayerCount} players, {TeamCount} teams, {KillCount} kills",
            matchId, result?.Data?.Players?.Count ?? 0, result?.Data?.Teams?.Count ?? 0, result?.Data?.Kills?.Count ?? 0);

        return result?.Data;
    }

    /// <inheritdoc />
    public async Task<MmrData?> GetPlayerMmrAsync(
        string name, string tag, string region, CancellationToken ct = default)
    {
        var url = $"v3/mmr/{region}/pc/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}";
        logger.LogDebug("Fetching MMR: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return null;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("MMR not found for {Name}#{Tag}", name, tag);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MmrResponse>(body);
        return result?.Data;
    }

    /// <inheritdoc />
    public async Task<AccountData?> GetAccountAsync(
        string name, string tag, CancellationToken ct = default)
    {
        var url = $"v1/account/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}";
        logger.LogDebug("Fetching account: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return null;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Account {Name}#{Tag} not found", name, tag);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<AccountResponse>(body);
        return result?.Data;
    }

    /// <inheritdoc />
    public async Task<AccountData?> GetAccountByPuuidAsync(
        string puuid, CancellationToken ct = default)
    {
        var url = $"v1/by-puuid/account/{Uri.EscapeDataString(puuid)}";
        logger.LogDebug("Fetching account by puuid: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return null;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Account not found for puuid {Puuid}", puuid);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<AccountResponse>(body);
        return result?.Data;
    }

    /// <inheritdoc />
    public async Task<MmrData?> GetPlayerMmrByPuuidAsync(
        string puuid, string region, CancellationToken ct = default)
    {
        var url = $"v3/by-puuid/mmr/{region}/pc/{Uri.EscapeDataString(puuid)}";
        logger.LogDebug("Fetching MMR by puuid: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return null;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("MMR not found for puuid {Puuid}", puuid);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MmrResponse>(body);
        return result?.Data;
    }

    /// <inheritdoc />
    public async Task<List<MatchListEntry>> GetRecentMatchesByPuuidAsync(
        string puuid, string region, CancellationToken ct = default)
    {
        var url = $"v4/by-puuid/matches/{region}/pc/{Uri.EscapeDataString(puuid)}?size=5&mode=competitive";
        logger.LogDebug("Fetching match list by puuid: {Url}", url);

        var response = await SendWithRetryAsync(url, ct);
        if (response is null)
            return [];

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("No matches found for puuid {Puuid}", puuid);
            return [];
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MatchListResponse>(body);
        logger.LogDebug("Deserialized {Count} match(es) for puuid {Puuid}",
            result?.Data?.Count ?? 0, puuid);

        return result?.Data ?? [];
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await httpClient.GetAsync(url, ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt == MaxRetries)
                    {
                        logger.LogError("Rate limited on all {MaxRetries} attempts to {Url}, skipping", MaxRetries, url);
                        return null;
                    }

                    var retryAfter = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(RetryDelay.TotalSeconds * attempt);
                    logger.LogWarning("Rate limited on {Url} (attempt {Attempt}/{MaxRetries}), retrying after {Delay}s",
                        url, attempt, MaxRetries, retryAfter.TotalSeconds);
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Request to {Url} failed (attempt {Attempt}/{MaxRetries})", url, attempt, MaxRetries);
                if (attempt == MaxRetries)
                {
                    logger.LogError("All {MaxRetries} attempts to {Url} failed, skipping", MaxRetries, url);
                    return null;
                }
                await Task.Delay(RetryDelay, ct);
            }
        }

        return null;
    }
}
