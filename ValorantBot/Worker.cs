using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ValorantBot.Models;
using ValorantBot.Services;

namespace ValorantBot;

public class Worker : BackgroundService
{
    private readonly HenrikDevClient _henrikDev;
    private readonly DiscordNotifier _discord;
    private readonly List<TrackedPlayer> _players;
    private readonly ILogger<Worker> _logger;

    public Worker(
        HenrikDevClient henrikDev,
        DiscordNotifier discord,
        IOptions<List<TrackedPlayer>> players,
        ILogger<Worker> logger)
    {
        _henrikDev = henrikDev;
        _discord = discord;
        _players = players.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Valorant Bot — tracking {Count} player(s)", _players.Count);

        _discord.OnLatestCommand += HandleLatestCommandAsync;
        await _discord.StartAsync(stoppingToken);

        // Keep the service alive
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleLatestCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var results = new List<PerformanceResult>();
        var errors = new List<string>();

        foreach (var player in _players)
        {
            try
            {
                var result = await CheckPlayerAsync(player);
                if (result is not null)
                    results.Add(result);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "API request failed for {Name}#{Tag}", player.Name, player.Tag);
                errors.Add($"Failed to fetch data for {player.Name}#{player.Tag}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error checking {Name}#{Tag}", player.Name, player.Tag);
                errors.Add($"Error checking {player.Name}#{player.Tag}");
            }
        }

        if (results.Count == 0 && errors.Count == 0)
        {
            await command.FollowupAsync("No new matches found for any tracked players.");
            return;
        }

        foreach (var result in results)
        {
            await _discord.SendPerformanceMessageAsync(result);
        }

        var summary = results.Count > 0
            ? $"Found {results.Count} new match(es)."
            : "No new matches found.";

        if (errors.Count > 0)
            summary += $"\n{errors.Count} error(s): {string.Join(", ", errors)}";

        await command.FollowupAsync(summary);
    }

    private async Task<PerformanceResult?> CheckPlayerAsync(TrackedPlayer player)
    {
        var playerKey = $"{player.Name}#{player.Tag}";

        var matches = await _henrikDev.GetRecentMatchesAsync(player.Name, player.Tag, player.Region);
        if (matches.Count == 0)
        {
            _logger.LogDebug("No matches found for {Key}", playerKey);
            return null;
        }

        var latest = matches
            .Where(m => m.Metadata.IsCompleted)
            .OrderByDescending(m => m.Metadata.StartedAt)
            .FirstOrDefault();

        if (latest is null)
            return null;

        var matchId = latest.Metadata.MatchId;

        _logger.LogInformation("Latest match for {Key}: {MatchId}", playerKey, matchId);

        var details = await _henrikDev.GetMatchDetailsAsync(matchId, player.Region);
        if (details is null)
        {
            _logger.LogWarning("Could not fetch details for match {MatchId}", matchId);

            return null;
        }

        var matchPlayer = details.Players
            .FirstOrDefault(p =>
                p.Name.Equals(player.Name, StringComparison.OrdinalIgnoreCase) &&
                p.Tag.Equals(player.Tag, StringComparison.OrdinalIgnoreCase));

        if (matchPlayer is null)
        {
            _logger.LogWarning("Player {Key} not found in match details", playerKey);

            return null;
        }

        var result = PerformanceAnalyzer.Analyze(player, matchPlayer, details);

        _logger.LogInformation("{Key} performance: {Rating} — K/D/A: {K}/{D}/{A}, ACS: {Acs:F0}",
            playerKey, result.Rating,
            matchPlayer.Stats.Kills, matchPlayer.Stats.Deaths, matchPlayer.Stats.Assists,
            result.Acs);

        return result;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Valorant Bot...");
        await _discord.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
