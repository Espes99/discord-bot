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
    IMatchHistoryStore matchHistoryStore,
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
        await discord.WaitUntilReadyAsync(stoppingToken);

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

        // Collect all new results first so we can detect stacks
        var newResults = new List<PerformanceResult>();

        foreach (var player in players)
        {
            try
            {
                var result = await GetNewMatchResultAsync(player, ct);
                if (result is not null)
                    newResults.Add(result);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to poll {Name}#{Tag}, will retry next cycle",
                    player.Name, player.Tag);
            }

            // Pace requests to avoid HenrikDev API rate limits
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        if (newResults.Count == 0)
            return;

        // Group by match + team to detect stacks
        var squads = newResults
            .GroupBy(r => (MatchId: r.MatchData.Metadata.MatchId, TeamId: r.MatchPlayer.TeamId))
            .ToList();

        var squadResults = new HashSet<PerformanceResult>(
            squads.Where(g => g.Count() >= 2).SelectMany(g => g));

        // Send individual messages first, before squad messages
        foreach (var result in newResults.Where(r => !squadResults.Contains(r)))
        {
            var sent = await discord.SendPerformanceMessageAsync(result);
            if (sent)
            {
                var playerKey = MatchTracker.PlayerKey(result.Player.Name, result.Player.Tag);
                matchTracker.SetLastMatch(playerKey, result.MatchData.Metadata.MatchId);
                matchHistoryStore.AddMatch(playerKey, MatchHistoryEntry.FromPerformanceResult(result));
            }
        }

        // Then send squad messages for stacked players
        foreach (var squad in squads.Where(g => g.Count() >= 2))
        {
            var members = squad.ToList();
            var names = string.Join(", ", members.Select(r => r.Player.Name));
            logger.LogInformation("Stack detected! [{Players}] on same team in match {MatchId}",
                names, squad.Key.MatchId);

            var sent = await discord.SendSquadMessageAsync(members);
            if (sent)
            {
                foreach (var result in members)
                {
                    var playerKey = MatchTracker.PlayerKey(result.Player.Name, result.Player.Tag);
                    matchTracker.SetLastMatch(playerKey, result.MatchData.Metadata.MatchId);
                    matchHistoryStore.AddMatch(playerKey, MatchHistoryEntry.FromPerformanceResult(result));
                }
            }
        }
    }

    private async Task<PerformanceResult?> GetNewMatchResultAsync(TrackedPlayer player, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var matchService = scope.ServiceProvider.GetRequiredService<IMatchService>();

        var result = await matchService.GetLatestPerformanceAsync(player, ct);
        if (result is null)
            return null;

        var playerKey = MatchTracker.PlayerKey(player.Name, player.Tag);
        var matchId = result.MatchData.Metadata.MatchId;

        if (!matchTracker.IsNewMatch(playerKey, matchId))
        {
            logger.LogInformation("Already seen match {MatchId} for {Key}, skipping", matchId, playerKey);
            return null;
        }

        logger.LogInformation("New match detected for {Key}: {MatchId}", playerKey, matchId);
        return result;
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
