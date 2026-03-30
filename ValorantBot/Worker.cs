using Discord;
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
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastPollAt;
    private DateTimeOffset? _nextPollAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Valorant Bot");

        discord.OnLatestCommand += HandleLatestCommandAsync;
        discord.OnStatusCommand += HandleStatusCommandAsync;
        await discord.StartAsync(stoppingToken);
        await discord.WaitUntilReadyAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(pollingOptions.Value.IntervalSeconds);
        logger.LogInformation("Polling {Count} tracked player(s) every {Interval}s",
            trackedPlayersOptions.Value.Count, interval.TotalSeconds);

        // Check immediately on startup, then on each timer tick
        await PollAllPlayersAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        _nextPollAt = DateTimeOffset.UtcNow + interval;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PollAllPlayersAsync(stoppingToken);
            _nextPollAt = DateTimeOffset.UtcNow + interval;
        }
    }

    private async Task PollAllPlayersAsync(CancellationToken ct)
    {
        _lastPollAt = DateTimeOffset.UtcNow;
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
                var previousRank = matchHistoryStore.GetLastRank(playerKey);
                matchTracker.SetLastMatch(playerKey, result.MatchData.Metadata.MatchId);
                matchHistoryStore.AddMatch(playerKey, MatchHistoryEntry.FromPerformanceResult(result));
                await CheckAndAnnounceRankChangeAsync(result, playerKey, previousRank);
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
                    var previousRank = matchHistoryStore.GetLastRank(playerKey);
                    matchTracker.SetLastMatch(playerKey, result.MatchData.Metadata.MatchId);
                    matchHistoryStore.AddMatch(playerKey, MatchHistoryEntry.FromPerformanceResult(result));
                    await CheckAndAnnounceRankChangeAsync(result, playerKey, previousRank);
                }
            }
        }
    }

    private async Task CheckAndAnnounceRankChangeAsync(PerformanceResult result, string playerKey, string? previousRank)
    {
        var currentRank = result.MatchPlayer.Tier?.Name;
        if (string.IsNullOrEmpty(currentRank) || string.IsNullOrEmpty(previousRank))
        {
            if (!string.IsNullOrEmpty(currentRank) && string.IsNullOrEmpty(previousRank))
                logger.LogInformation("Seeding initial rank for {Key}: {Rank}", playerKey, currentRank);
            return;
        }

        if (string.Equals(currentRank, previousRank, StringComparison.OrdinalIgnoreCase))
            return;

        var isPromotion = IsPromotion(previousRank, currentRank);
        var isMajor = IsMajorRankChange(previousRank, currentRank);
        logger.LogInformation("Rank change for {Key}: {Old} -> {New} ({Direction}, {Severity})",
            playerKey, previousRank, currentRank,
            isPromotion ? "promotion" : "demotion",
            isMajor ? "major" : "minor");

        try
        {
            await discord.SendRankChangeMessageAsync(
                $"{result.MatchPlayer.Name}#{result.MatchPlayer.Tag}",
                previousRank, currentRank, isPromotion, isMajor);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send rank change message for {Key}", playerKey);
        }
    }

    private static bool IsPromotion(string oldRank, string newRank)
    {
        var rankOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iron 1"] = 1, ["Iron 2"] = 2, ["Iron 3"] = 3,
            ["Bronze 1"] = 4, ["Bronze 2"] = 5, ["Bronze 3"] = 6,
            ["Silver 1"] = 7, ["Silver 2"] = 8, ["Silver 3"] = 9,
            ["Gold 1"] = 10, ["Gold 2"] = 11, ["Gold 3"] = 12,
            ["Platinum 1"] = 13, ["Platinum 2"] = 14, ["Platinum 3"] = 15,
            ["Diamond 1"] = 16, ["Diamond 2"] = 17, ["Diamond 3"] = 18,
            ["Ascendant 1"] = 19, ["Ascendant 2"] = 20, ["Ascendant 3"] = 21,
            ["Immortal 1"] = 22, ["Immortal 2"] = 23, ["Immortal 3"] = 24,
            ["Radiant"] = 25
        };

        var oldOrder = rankOrder.GetValueOrDefault(oldRank, 0);
        var newOrder = rankOrder.GetValueOrDefault(newRank, 0);
        return newOrder > oldOrder;
    }

    private static bool IsMajorRankChange(string oldRank, string newRank)
    {
        var tierOf = (string rank) => rank.Split(' ')[0];
        return !string.Equals(tierOf(oldRank), tierOf(newRank), StringComparison.OrdinalIgnoreCase);
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

    private async Task HandleStatusCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        try
        {
            var players = trackedPlayersOptions.Value;
            var uptime = DateTimeOffset.UtcNow - _startedAt;
            var interval = pollingOptions.Value.IntervalSeconds;

            var embed = new EmbedBuilder()
                .WithTitle("Valorant Bot Status")
                .WithColor(Color.Blue)
                .WithTimestamp(DateTimeOffset.UtcNow);

            // Uptime & polling info
            var uptimeText = FormatUptime(uptime);
            var pollText = _lastPollAt.HasValue
                ? FormatRelativeTime(DateTimeOffset.UtcNow - _lastPollAt.Value) + " ago"
                : "Not yet";

            var nextPollText = _nextPollAt.HasValue
                ? "in " + FormatRelativeTime(_nextPollAt.Value - DateTimeOffset.UtcNow)
                : "Pending";

            embed.AddField("Uptime", uptimeText, inline: true);
            embed.AddField("Poll interval", $"{interval}s", inline: true);
            embed.AddField("Last poll", pollText, inline: true);
            embed.AddField("Next poll", nextPollText, inline: true);

            // Tracked players
            if (players.Count == 0)
            {
                embed.AddField("Tracked players", "None configured");
            }
            else
            {
                var playerLines = new List<string>();
                foreach (var player in players)
                {
                    var key = MatchTracker.PlayerKey(player.Name, player.Tag);
                    var lastMatchId = matchTracker.GetLastMatchId(key);
                    var history = matchHistoryStore.GetHistory(key);
                    var lastEntry = history
                        .OrderByDescending(h => h.PlayedAt)
                        .FirstOrDefault();

                    var line = $"**{player.Name}#{player.Tag}** ({player.Region})";

                    if (lastEntry is not null)
                    {
                        var won = lastEntry.Won ? "W" : "L";
                        var timestamp = new DateTimeOffset(lastEntry.PlayedAt, TimeSpan.Zero).ToUnixTimeSeconds();
                        line += $"\n> Last: {lastEntry.Map} ({won} {lastEntry.Score}) " +
                                $"{lastEntry.Kills}/{lastEntry.Deaths}/{lastEntry.Assists} " +
                                $"as {lastEntry.Agent} - <t:{timestamp}:R>";
                    }
                    else if (lastMatchId is not null)
                    {
                        line += "\n> Tracking active, no history stored yet";
                    }
                    else
                    {
                        line += "\n> No matches seen yet";
                    }

                    var matchCount = history.Count;
                    if (matchCount > 0)
                    {
                        var wins = history.Count(h => h.Won);
                        var losses = matchCount - wins;
                        var avgAcs = history.Average(h => h.Acs);
                        line += $"\n> History: {matchCount} matches, {wins}W/{losses}L, avg ACS {avgAcs:F0}";
                    }

                    playerLines.Add(line);
                }

                embed.AddField($"Tracked players ({players.Count})", string.Join("\n\n", playerLines));
            }

            embed.WithFooter("Valorant Bot");
            await command.FollowupAsync(embed: embed.Build());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle /status command");
            await command.FollowupAsync("Failed to retrieve bot status.");
        }
    }

    private static string FormatRelativeTime(TimeSpan duration)
    {
        var total = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        var minutes = (int)total.TotalMinutes;
        var seconds = total.Seconds;

        if (minutes > 0)
            return $"{minutes} minutes and {seconds} seconds";
        return $"{seconds} seconds";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalMinutes < 1)
            return $"{uptime.Seconds}s";
        if (uptime.TotalHours < 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalDays < 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Valorant Bot...");
        await discord.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
