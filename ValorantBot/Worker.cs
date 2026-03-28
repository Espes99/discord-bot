using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ValorantBot.Models;
using ValorantBot.Services;

namespace ValorantBot;

/// <summary>
/// Background service that connects the Discord bot, routes slash commands,
/// and polls tracked players for new matches.
/// </summary>
public class Worker(
    IDiscordNotifier discord,
    IMatchTracker matchTracker,
    IServiceScopeFactory scopeFactory,
    IOptions<List<TrackedPlayer>> trackedPlayersOptions,
    IOptions<PollingSettings> pollingOptions,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Valorant Bot");

        discord.OnLatestCommand += HandleLatestCommandAsync;
        await discord.StartAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(pollingOptions.Value.IntervalSeconds);
        logger.LogInformation("Polling {Count} tracked player(s) every {Interval}s",
            trackedPlayersOptions.Value.Count, interval.TotalSeconds);

        // Check immediately on startup, then on each timer tick
        await PollAllPlayersAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAllPlayersAsync(stoppingToken);
        }
    }

    private async Task PollAllPlayersAsync(CancellationToken ct)
    {
        var players = trackedPlayersOptions.Value;
        logger.LogDebug("Polling {Count} player(s) for new matches", players.Count);

        foreach (var player in players)
        {
            try
            {
                await CheckPlayerForNewMatchAsync(player, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to poll {Name}#{Tag}, will retry next cycle",
                    player.Name, player.Tag);
            }

            // Pace requests to avoid HenrikDev API rate limits
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }

    private async Task CheckPlayerForNewMatchAsync(TrackedPlayer player, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var matchService = scope.ServiceProvider.GetRequiredService<IMatchService>();

        var result = await matchService.GetLatestPerformanceAsync(player, ct);
        if (result is null)
            return;

        var playerKey = MatchTracker.PlayerKey(player.Name, player.Tag);
        var matchId = result.MatchData.Metadata.MatchId;

        if (!matchTracker.IsNewMatch(playerKey, matchId))
        {
            logger.LogDebug("No new match for {Key}", playerKey);
            return;
        }

        logger.LogInformation("New match detected for {Key}: {MatchId}", playerKey, matchId);
        await discord.SendPerformanceMessageAsync(result);
        matchTracker.SetLastMatch(playerKey, matchId);
    }

    private async Task HandleLatestCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var player = new TrackedPlayer { Name = name, Tag = tag, Region = "eu" };

        try
        {
            using var scope = scopeFactory.CreateScope();
            var matchService = scope.ServiceProvider.GetRequiredService<IMatchService>();

            var result = await matchService.GetLatestPerformanceAsync(player);
            if (result is null)
            {
                await command.FollowupAsync($"No completed matches found for {name}#{tag}.");
                return;
            }

            await discord.SendPerformanceMessageAsync(result);
            await command.FollowupAsync($"Latest match for {name}#{tag} posted.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "API request failed for {Name}#{Tag}", name, tag);
            await command.FollowupAsync($"Failed to fetch data for {name}#{tag}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error checking {Name}#{Tag}", name, tag);
            await command.FollowupAsync($"Something went wrong checking {name}#{tag}.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Valorant Bot...");
        await discord.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
