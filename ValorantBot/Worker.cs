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

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var player = new TrackedPlayer { Name = name, Tag = tag, Region = "eu" };

        try
        {
            var result = await CheckPlayerAsync(player);
            if (result is null)
            {
                await command.FollowupAsync($"No completed matches found for {name}#{tag}.");
                return;
            }

            await _discord.SendPerformanceMessageAsync(result);
            await command.FollowupAsync($"Latest match for {name}#{tag} posted.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API request failed for {Name}#{Tag}", name, tag);
            await command.FollowupAsync($"Failed to fetch data for {name}#{tag}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking {Name}#{Tag}", name, tag);
            await command.FollowupAsync($"Something went wrong checking {name}#{tag}.");
        }
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
