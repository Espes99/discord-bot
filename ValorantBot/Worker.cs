using System.Net;
using Discord;
using Discord.Net;
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
    IPlayerProfileStore playerProfileStore,
    IPollStateStore pollStateStore,
    IServiceScopeFactory scopeFactory,
    IOptions<PollingSettings> pollingOptions,
    IOptions<BotAdminSettings> botAdminOptions,
    ITrackedPlayerStore trackedPlayerStore,
    IDataMigrator dataMigrator,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastPollAt;
    private DateTimeOffset? _nextPollAt;

    /// <summary>
    /// Returns the store key for a tracked player: puuid if available, otherwise name#tag.
    /// </summary>
    private static string StoreKey(TrackedPlayer player) =>
        !string.IsNullOrEmpty(player.Puuid) ? player.Puuid : MatchTracker.PlayerKey(player.Name, player.Tag);

    /// <summary>
    /// Resolves a tracked player from name+tag input, checking the tracked store first.
    /// </summary>
    private TrackedPlayer? ResolveTrackedPlayer(string name, string tag) =>
        trackedPlayerStore.FindByNameTag(name, tag);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting Valorant Bot");

        discord.OnLatestMatchCommand += HandleLatestMatchCommandAsync;
        discord.OnStatusCommand += HandleStatusCommandAsync;
        discord.OnRanksCommand += HandleRanksCommandAsync;
        discord.OnTrackCommand += HandleTrackCommandAsync;
        discord.OnUntrackCommand += HandleUntrackCommandAsync;
        discord.OnSetBioCommand += HandleSetBioCommandAsync;
        discord.OnAddTraitCommand += HandleAddTraitCommandAsync;
        discord.OnProfileCommand += HandleProfileCommandAsync;
        discord.OnToggleProfileCommand += HandleToggleProfileCommandAsync;
        discord.OnSummaryCommand += HandleSummaryCommandAsync;
        await discord.StartAsync(stoppingToken);
        await discord.WaitUntilReadyAsync(stoppingToken);

        // Migrate legacy data (name#tag keys -> puuid) for tracked players missing puuid
        try
        {
            await dataMigrator.MigrateAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Data migration encountered an error, continuing with startup");
        }

        // Repair any tracked players with empty name/tag (resolve from puuid)
        try
        {
            using var repairScope = scopeFactory.CreateScope();
            var henrikClient = repairScope.ServiceProvider.GetRequiredService<IHenrikDevClient>();
            await trackedPlayerStore.RepairEmptyNamesAsync(henrikClient, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Player name repair encountered an error, continuing with startup");
        }

        var interval = TimeSpan.FromSeconds(pollingOptions.Value.IntervalSeconds);
        logger.LogInformation("Polling {Count} tracked player(s) every {Interval}s",
            trackedPlayerStore.GetAll().Count, interval.TotalSeconds);

        // Check if enough time has passed since the last persisted poll
        var lastPersistedPoll = pollStateStore.GetLastPollAt();
        if (lastPersistedPoll is not null)
        {
            _lastPollAt = lastPersistedPoll.Value;
            _nextPollAt = lastPersistedPoll.Value + interval;

            var elapsed = DateTimeOffset.UtcNow - lastPersistedPoll.Value;
            if (elapsed < interval)
            {
                var remaining = interval - elapsed;
                logger.LogInformation(
                    "Last poll was {Elapsed}s ago, waiting {Remaining}s before first poll",
                    (int)elapsed.TotalSeconds, (int)remaining.TotalSeconds);
                await Task.Delay(remaining, stoppingToken);
            }
            else
            {
                logger.LogInformation(
                    "Last poll was {Elapsed}s ago (>= interval), polling immediately",
                    (int)elapsed.TotalSeconds);
            }
        }
        else
        {
            logger.LogInformation("No previous poll state found, polling immediately");
        }

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
        pollStateStore.SetLastPollAt(_lastPollAt.Value);
        var players = trackedPlayerStore.GetAll();
        logger.LogDebug("Polling {Count} player(s) for new matches", players.Count);

        // Collect all new results first so we can detect stacks
        var newResults = new List<PerformanceResult>();

        foreach (var player in players)
        {
            if (string.IsNullOrWhiteSpace(player.Name) || string.IsNullOrWhiteSpace(player.Tag))
            {
                logger.LogWarning("Skipping player with empty name/tag (puuid={Puuid}), needs repair", player.Puuid);
                continue;
            }

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
            var key = StoreKey(result.Player);
            DetectAndApplyNameChange(result);

            var matchStartRank = result.MatchPlayer.Tier?.Name;
            string? currentRank = null;
            RankChangeInfo? rankChange = null;
            try
            {
                var mmr = await GetPlayerMmrDataAsync(result.Player, ct);
                currentRank = mmr?.Current.Tier?.Name;
                var displayKey = MatchTracker.PlayerKey(result.Player.Name, result.Player.Tag);
                rankChange = DetectRankChange(matchStartRank, currentRank, displayKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MMR lookup failed for {Key}, skipping rank change detection", key);
            }

            var sent = await discord.SendPerformanceMessageAsync(result, rankChange);
            if (sent)
            {
                matchTracker.SetLastMatch(key, result.MatchData.Metadata.MatchId);
                matchHistoryStore.AddMatch(key, MatchHistoryEntry.FromPerformanceResult(result, currentRank));
                UpdateAutoTraits(key);
            }
        }

        // Then send squad messages for stacked players
        foreach (var squad in squads.Where(g => g.Count() >= 2))
        {
            var members = squad.ToList();
            var names = string.Join(", ", members.Select(r => r.Player.Name));
            logger.LogInformation("Stack detected! [{Players}] on same team in match {MatchId}",
                names, squad.Key.MatchId);

            // Detect rank changes for all squad members before sending
            var rankChanges = new Dictionary<string, RankChangeInfo>();
            var currentRanks = new Dictionary<string, string?>();
            foreach (var result in members)
            {
                var key = StoreKey(result.Player);
                DetectAndApplyNameChange(result);
                var matchStartRank = result.MatchPlayer.Tier?.Name;
                try
                {
                    var mmr = await GetPlayerMmrDataAsync(result.Player, ct);
                    var currentRank = mmr?.Current.Tier?.Name;
                    currentRanks[key] = currentRank;
                    var displayKey = MatchTracker.PlayerKey(result.Player.Name, result.Player.Tag);
                    var rankChange = DetectRankChange(matchStartRank, currentRank, displayKey);
                    if (rankChange is not null)
                        rankChanges[key] = rankChange;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "MMR lookup failed for {Key}, skipping rank change detection", key);
                }
            }

            var sent = await discord.SendSquadMessageAsync(members, rankChanges.Count > 0 ? rankChanges : null);
            if (sent)
            {
                foreach (var result in members)
                {
                    var key = StoreKey(result.Player);
                    matchTracker.SetLastMatch(key, result.MatchData.Metadata.MatchId);
                    var currentRank = currentRanks.GetValueOrDefault(key);
                    matchHistoryStore.AddMatch(key, MatchHistoryEntry.FromPerformanceResult(result, currentRank));
                    UpdateAutoTraits(key);
                }
            }
        }
    }

    private RankChangeInfo? DetectRankChange(string? matchStartRank, string? currentRank, string displayKey)
    {
        if (string.IsNullOrEmpty(currentRank) || string.IsNullOrEmpty(matchStartRank))
        {
            if (!string.IsNullOrEmpty(currentRank) && string.IsNullOrEmpty(matchStartRank))
                logger.LogInformation("Seeding initial rank for {Key}: {Rank}", displayKey, currentRank);
            return null;
        }

        if (string.Equals(currentRank, matchStartRank, StringComparison.OrdinalIgnoreCase))
            return null;

        var isPromotion = IsPromotion(matchStartRank, currentRank);
        var isMajor = IsMajorRankChange(matchStartRank, currentRank);
        logger.LogInformation("Rank change for {Key}: {Old} -> {New} ({Direction}, {Severity})",
            displayKey, matchStartRank, currentRank,
            isPromotion ? "promotion" : "demotion",
            isMajor ? "major" : "minor");

        return new RankChangeInfo
        {
            OldRank = matchStartRank,
            NewRank = currentRank,
            IsPromotion = isPromotion,
            IsMajor = isMajor
        };
    }

    private async Task<MmrData?> GetPlayerMmrDataAsync(TrackedPlayer player, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var henrikClient = scope.ServiceProvider.GetRequiredService<IHenrikDevClient>();

        if (!string.IsNullOrEmpty(player.Puuid))
            return await henrikClient.GetPlayerMmrByPuuidAsync(player.Puuid, player.Region, ct);

        return await henrikClient.GetPlayerMmrAsync(player.Name, player.Tag, player.Region, ct);
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

        var key = StoreKey(player);
        var matchId = result.MatchData.Metadata.MatchId;

        if (!matchTracker.IsNewMatch(key, matchId))
        {
            logger.LogInformation("Already seen match {MatchId} for {Key}, skipping",
                matchId, MatchTracker.PlayerKey(player.Name, player.Tag));
            return null;
        }

        logger.LogInformation("New match detected for {Key}: {MatchId}",
            MatchTracker.PlayerKey(player.Name, player.Tag), matchId);
        return result;
    }

    private async Task HandleLatestMatchCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var player = trackedPlayerStore.FindByNameTag(name, tag)
            ?? new TrackedPlayer { Name = name, Tag = tag, Region = "eu" };

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

    private bool IsAuthorized(SocketSlashCommand command) =>
        botAdminOptions.Value.AllowedUserIds.Contains(command.User.Id);

    private async Task HandleTrackCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!IsAuthorized(command))
        {
            await command.FollowupAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var regionOption = command.Data.Options.FirstOrDefault(o => o.Name == "region");
        var region = regionOption?.Value?.ToString() ?? "eu";

        // Resolve puuid from the HenrikDev account API
        string? puuid = null;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var henrikClient = scope.ServiceProvider.GetRequiredService<IHenrikDevClient>();
            var account = await henrikClient.GetAccountAsync(name, tag);
            if (account is not null)
            {
                puuid = account.Puuid;
                // Use the canonical name/tag from the API (only if non-empty)
                if (!string.IsNullOrEmpty(account.Name))
                    name = account.Name;
                if (!string.IsNullOrEmpty(account.Tag))
                    tag = account.Tag;
            }
            else
            {
                logger.LogWarning("Could not resolve puuid for {Name}#{Tag}, tracking without puuid", name, tag);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve puuid for {Name}#{Tag}, tracking without puuid", name, tag);
        }

        var player = new TrackedPlayer { Puuid = puuid, Name = name, Tag = tag, Region = region };
        var added = trackedPlayerStore.Add(player);

        if (!added)
        {
            await command.FollowupAsync($"**{name}#{tag}** is already being tracked.", ephemeral: true);
            return;
        }

        var puuidInfo = puuid is not null ? $", puuid: {puuid[..8]}..." : ", puuid pending";
        logger.LogInformation("{User} added tracked player {Name}#{Tag} ({Region}{Puuid})",
            command.User.Username, name, tag, region, puuidInfo);
        await command.FollowupAsync($"Now tracking **{name}#{tag}** ({region}).", ephemeral: true);
    }

    private async Task HandleUntrackCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!IsAuthorized(command))
        {
            await command.FollowupAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;

        var removed = trackedPlayerStore.Remove(name, tag);

        if (!removed)
        {
            await command.FollowupAsync($"**{name}#{tag}** is not currently tracked.", ephemeral: true);
            return;
        }

        logger.LogInformation("{User} removed tracked player {Name}#{Tag}",
            command.User.Username, name, tag);
        await command.FollowupAsync($"Stopped tracking **{name}#{tag}**.", ephemeral: true);
    }

    private async Task HandleStatusCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        try
        {
            var players = trackedPlayerStore.GetAll();
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
                    var key = StoreKey(player);
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

    private async Task HandleRanksCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        // Delete previous /ranks messages from the bot in this channel
        if (command.Channel is ITextChannel textChannel)
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
                var ranksMessages = new List<IMessage>();

                var batch = (await textChannel.GetMessagesAsync(100).FlattenAsync()).ToList();
                while (batch.Count > 0)
                {
                    foreach (var msg in batch)
                    {
                        // Bot messages with the ranks-specific footer
                        var isRanksMessage = msg.Author.IsBot
                            && msg.Embeds.Any(e => e.Footer?.Text == "Valorant Bot • Ranks");

                        // Discord "pinned a message" system notifications
                        var isPinNotification = msg.Type == MessageType.ChannelPinnedMessage;

                        if (isRanksMessage || isPinNotification)
                            ranksMessages.Add(msg);
                    }

                    var oldest = batch.MinBy(m => m.CreatedAt)!;
                    batch = (await textChannel.GetMessagesAsync(oldest.Id, Direction.Before, 100).FlattenAsync()).ToList();
                }

                var recentMessages = ranksMessages.Where(m => m.CreatedAt > cutoff).ToList();
                var oldMessages = ranksMessages.Where(m => m.CreatedAt <= cutoff).ToList();

                if (recentMessages.Count >= 2)
                    await textChannel.DeleteMessagesAsync(recentMessages);
                else if (recentMessages.Count == 1)
                    await recentMessages[0].DeleteAsync();

                foreach (var msg in oldMessages)
                {
                    await msg.DeleteAsync();
                    await Task.Delay(500);
                }
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                logger.LogWarning("Bot lacks ManageMessages permission — skipping channel cleanup");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete previous ranks messages in channel");
            }
        }

        try
        {
            var players = trackedPlayerStore.GetAll();
            if (players.Count == 0)
            {
                await command.FollowupAsync("No tracked players configured.");
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var henrikClient = scope.ServiceProvider.GetRequiredService<IHenrikDevClient>();

            var rankEntries = new List<(TrackedPlayer Player, string Rank, int Rr, int RankOrder)>();

            foreach (var player in players)
            {
                try
                {
                    var mmr = !string.IsNullOrEmpty(player.Puuid)
                        ? await henrikClient.GetPlayerMmrByPuuidAsync(player.Puuid, player.Region)
                        : await henrikClient.GetPlayerMmrAsync(player.Name, player.Tag, player.Region);
                    if (mmr is not null && !string.IsNullOrEmpty(mmr.Current.Tier.Name))
                    {
                        var order = GetRankOrder(mmr.Current.Tier.Name);
                        rankEntries.Add((player, mmr.Current.Tier.Name, mmr.Current.Rr, order));
                    }
                    else
                    {
                        rankEntries.Add((player, "Unranked", 0, 0));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch MMR for {Name}#{Tag}", player.Name, player.Tag);
                    rankEntries.Add((player, "Unknown", 0, -1));
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            var sorted = rankEntries.OrderByDescending(e => e.RankOrder).ThenByDescending(e => e.Rr).ToList();

            var embeds = new List<Embed>();
            for (var i = 0; i < sorted.Count && i < 10; i++)
            {
                var entry = sorted[i];
                var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"#{i + 1}" };
                var rrText = entry.RankOrder > 0 ? $"{entry.Rr} RR" : "";
                var indicator = "";
                if (entry.RankOrder > 0)
                {
                    if (entry.Rr >= 90)
                        indicator = " 🟢";
                    else if (entry.Rr <= 10)
                        indicator = " 🚨";
                }

                var color = i switch { 0 => Color.Gold, 1 => new Color(192, 192, 192), 2 => new Color(205, 127, 50), _ => Color.LightGrey };
                var description = entry.RankOrder > 0
                    ? $"{entry.Rank} - {rrText}{indicator}"
                    : entry.Rank;

                var iconUrl = GetRankIconUrl(entry.Rank);
                var embedBuilder = new EmbedBuilder()
                    .WithAuthor($"{medal} {entry.Player.Name}#{entry.Player.Tag}", iconUrl: iconUrl)
                    .WithDescription(description)
                    .WithColor(color);

                if (i == sorted.Count - 1 || i == 9)
                    embedBuilder.WithFooter("Valorant Bot • Ranks").WithTimestamp(DateTimeOffset.UtcNow);

                embeds.Add(embedBuilder.Build());
            }

            var ranksMessage = await command.FollowupAsync(embeds: embeds.ToArray());

            try
            {
                await ranksMessage.PinAsync();
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                logger.LogWarning("Bot lacks ManageMessages permission — could not pin ranks message");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle /ranks command");
            await command.FollowupAsync("Failed to retrieve rank data.");
        }
    }

    private static int GetRankOrder(string rank)
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

        return rankOrder.GetValueOrDefault(rank, 0);
    }

    private static string? GetRankIconUrl(string rank)
    {
        const string baseUrl = "https://media.valorant-api.com/competitivetiers/03621f52-342b-cf4e-4f86-9350a49c6d04";

        var tierMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iron 1"] = 3, ["Iron 2"] = 4, ["Iron 3"] = 5,
            ["Bronze 1"] = 6, ["Bronze 2"] = 7, ["Bronze 3"] = 8,
            ["Silver 1"] = 9, ["Silver 2"] = 10, ["Silver 3"] = 11,
            ["Gold 1"] = 12, ["Gold 2"] = 13, ["Gold 3"] = 14,
            ["Platinum 1"] = 15, ["Platinum 2"] = 16, ["Platinum 3"] = 17,
            ["Diamond 1"] = 18, ["Diamond 2"] = 19, ["Diamond 3"] = 20,
            ["Ascendant 1"] = 21, ["Ascendant 2"] = 22, ["Ascendant 3"] = 23,
            ["Immortal 1"] = 24, ["Immortal 2"] = 25, ["Immortal 3"] = 26,
            ["Radiant"] = 27
        };

        if (!tierMap.TryGetValue(rank, out var tierId))
            return null;

        return $"{baseUrl}/{tierId}/smallicon.png";
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

    private async Task HandleSetBioCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!IsAuthorized(command))
        {
            await command.FollowupAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var bio = command.Data.Options.First(o => o.Name == "bio").Value.ToString()!;

        var tracked = ResolveTrackedPlayer(name, tag);
        var key = tracked is not null ? StoreKey(tracked) : MatchTracker.PlayerKey(name, tag);
        playerProfileStore.SetBio(key, bio);

        logger.LogInformation("{User} set bio for {Name}#{Tag}: {Bio}",
            command.User.Username, name, tag, bio);
        await command.FollowupAsync($"Bio set for **{name}#{tag}**: \"{bio}\"", ephemeral: true);
    }

    private async Task HandleAddTraitCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!IsAuthorized(command))
        {
            await command.FollowupAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var trait = command.Data.Options.First(o => o.Name == "trait").Value.ToString()!;

        var tracked = ResolveTrackedPlayer(name, tag);
        var key = tracked is not null ? StoreKey(tracked) : MatchTracker.PlayerKey(name, tag);
        playerProfileStore.AddManualTrait(key, trait);

        logger.LogInformation("{User} added trait for {Name}#{Tag}: {Trait}",
            command.User.Username, name, tag, trait);
        await command.FollowupAsync($"Trait added for **{name}#{tag}**: \"{trait}\"", ephemeral: true);
    }

    private async Task HandleProfileCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!playerProfileStore.IsProfileCommandPublic && !IsAuthorized(command))
        {
            await command.FollowupAsync("The /profile command is currently disabled.", ephemeral: true);
            return;
        }

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;

        var tracked = ResolveTrackedPlayer(name, tag);
        var key = tracked is not null ? StoreKey(tracked) : MatchTracker.PlayerKey(name, tag);

        // Generate auto traits on-demand if the player has history but no profile yet
        var profile = playerProfileStore.GetProfile(key);
        if (profile is null)
        {
            var history = matchHistoryStore.GetHistory(key);
            if (history.Count > 0)
            {
                UpdateAutoTraits(key);
                profile = playerProfileStore.GetProfile(key);
            }
        }

        if (profile is null)
        {
            await command.FollowupAsync($"No profile found for **{name}#{tag}**. Use `/set-bio` or `/add-trait` to create one, or wait for a match to generate auto traits.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"Profile: {name}#{tag}")
            .WithColor(Color.Purple)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter("Valorant Bot");

        if (!string.IsNullOrWhiteSpace(profile.Bio))
            embed.AddField("Bio", profile.Bio);

        if (profile.ManualTraits.Count > 0)
            embed.AddField("Manual Traits", string.Join("\n", profile.ManualTraits.Select(t => $"- {t}")));

        if (profile.AutoTraits.Count > 0)
            embed.AddField("Auto Traits", string.Join("\n", profile.AutoTraits.Select(t => $"- {t}")));

        if (string.IsNullOrWhiteSpace(profile.Bio) && profile.ManualTraits.Count == 0 && profile.AutoTraits.Count == 0)
            embed.WithDescription("Profile exists but has no bio, traits, or auto traits yet.");

        await command.FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    private async Task HandleSummaryCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        var name = command.Data.Options.First(o => o.Name == "name").Value.ToString()!;
        var tag = command.Data.Options.First(o => o.Name == "tag").Value.ToString()!;
        var countOption = command.Data.Options.FirstOrDefault(o => o.Name == "count");
        var count = countOption?.Value is long rawCount ? (int)rawCount : 12;
        count = Math.Clamp(count, 1, 20);

        var tracked = ResolveTrackedPlayer(name, tag);
        var key = tracked is not null ? StoreKey(tracked) : MatchTracker.PlayerKey(name, tag);

        var history = matchHistoryStore.GetHistory(key)
            .OrderByDescending(h => h.PlayedAt)
            .Take(count)
            .ToList();

        if (history.Count == 0)
        {
            await command.FollowupAsync($"No match history stored for **{name}#{tag}**.");
            return;
        }

        var displayName = $"{name}#{tag}";
        var embed = BuildSummaryEmbed(displayName, history);
        await command.FollowupAsync(embed: embed);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IMessageGenerator>();
            var blurb = await generator.GenerateSummaryMessageAsync(displayName, key, history);
            if (!string.IsNullOrWhiteSpace(blurb))
                await command.FollowupAsync(blurb);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate summary blurb for {Name}#{Tag}", name, tag);
        }
    }

    private static Embed BuildSummaryEmbed(string displayName, List<MatchHistoryEntry> history)
    {
        var wins = history.Count(h => h.Won);
        var losses = history.Count - wins;
        var avgAcs = history.Average(h => h.Acs);
        var avgKda = history.Average(h => h.Kda);
        var avgHs = history.Average(h => h.HeadshotPercent);

        var color = wins > losses ? Color.Green : wins < losses ? Color.Red : Color.LightGrey;

        var matchLines = history.Select(h =>
        {
            var outcome = h.Won ? "✅" : "❌";
            var ts = new DateTimeOffset(h.PlayedAt, TimeSpan.Zero).ToUnixTimeSeconds();
            return $"{outcome} **{h.Map}** as {h.Agent} ({h.Score}) | {h.Kills}/{h.Deaths}/{h.Assists} | ACS {h.Acs:F0} | <t:{ts}:R>";
        });

        var ratingCounts = history
            .GroupBy(h => h.Rating)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key}");

        var embed = new EmbedBuilder()
            .WithTitle($"Summary: {displayName}")
            .WithColor(color)
            .WithDescription($"Last {history.Count} matches: **{wins}W / {losses}L**")
            .AddField("Avg ACS", $"{avgAcs:F0}", inline: true)
            .AddField("Avg KDA", $"{avgKda:F2}", inline: true)
            .AddField("Avg HS%", $"{avgHs:F1}%", inline: true)
            .AddField("Matches", string.Join("\n", matchLines))
            .AddField("Ratings", string.Join(", ", ratingCounts))
            .WithFooter("Valorant Bot")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        return embed;
    }

    private async Task HandleToggleProfileCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);

        if (!IsAuthorized(command))
        {
            await command.FollowupAsync("You don't have permission to use this command.", ephemeral: true);
            return;
        }

        var newState = !playerProfileStore.IsProfileCommandPublic;
        playerProfileStore.SetProfileCommandPublic(newState);

        var stateText = newState ? "enabled" : "disabled";
        logger.LogInformation("{User} toggled /profile command to {State}", command.User.Username, stateText);
        await command.FollowupAsync($"/profile command is now **{stateText}** for non-admins.", ephemeral: true);
    }

    private void UpdateAutoTraits(string playerKey)
    {
        var history = matchHistoryStore.GetHistory(playerKey);
        var summary = HistorySummarizer.Summarize(history);
        var autoTraits = ProfileTraitDeriver.DeriveTraits(history, summary);
        playerProfileStore.UpdateAutoTraits(playerKey, autoTraits);
    }

    /// <summary>
    /// Checks if the API returned a different name/tag than we have stored.
    /// If so, silently updates the tracked player. Also backfills puuid if missing.
    /// </summary>
    private void DetectAndApplyNameChange(PerformanceResult result)
    {
        var player = result.Player;
        var apiName = result.MatchPlayer.Name;
        var apiTag = result.MatchPlayer.Tag;
        var apiPuuid = result.MatchPlayer.Puuid;

        var changed = false;

        // Backfill puuid if we did not have it
        if (string.IsNullOrEmpty(player.Puuid) && !string.IsNullOrEmpty(apiPuuid))
        {
            logger.LogInformation("Resolved puuid for {Name}#{Tag}: {Puuid}",
                player.Name, player.Tag, apiPuuid);
            player.Puuid = apiPuuid;
            changed = true;
        }

        // Detect name/tag change (only apply if API returned non-empty values)
        if (!string.IsNullOrEmpty(apiName) && !string.IsNullOrEmpty(apiTag) &&
            (!string.Equals(player.Name, apiName, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(player.Tag, apiTag, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("Name change detected: {OldName}#{OldTag} -> {NewName}#{NewTag}",
                player.Name, player.Tag, apiName, apiTag);
            player.Name = apiName;
            player.Tag = apiTag;
            changed = true;
        }

        if (changed)
            trackedPlayerStore.UpdatePlayer(player);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping Valorant Bot...");
        await discord.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
